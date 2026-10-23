using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Ai;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Recommendations;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Services;

/// <summary>
/// Builds the facts for every rentable vehicle (real availability, server-side price, ratings, next
/// free date) with the same services the booking flow uses, then runs the explainable
/// <see cref="RecommendationEngine"/>.
/// </summary>
public sealed class RecommendationService(
    SmartDriveDbContext db,
    CatalogService catalog,
    AvailabilityService availability,
    RentalPolicy policy,
    IPreferenceInterpreter interpreter,
    TimeProvider clock)
{
    public const string AlgorithmDescription =
        "Explainable rule-based ranking: vehicles must be available and meet passenger, luggage, required-transmission and budget limits; " +
        "eligible vehicles are scored 0–100 on the preferences you gave plus value for money and customer rating.";

    public async Task<RecommendationResponseDto> RecommendAsync(RecommendationRequest req, Guid? userId, CancellationToken ct)
    {
        var pickup = req.PickupAt.UtcDateTime;
        var ret = req.ReturnAt.UtcDateTime;
        var problems = RentalPeriod.Validate(pickup, ret, clock.GetUtcNow().UtcDateTime, policy);
        if (problems.Count > 0)
        {
            throw new DomainException("rental.invalid_period", string.Join(" ", problems));
        }

        ValidateRequest(req);

        var prefs = new RecommendationPreferences
        {
            PickupUtc = pickup,
            ReturnUtc = ret,
            Passengers = req.Passengers,
            LuggageBags = req.LuggageBags,
            MaxDailyBudget = req.MaxDailyBudget,
            MaxTotalBudget = req.MaxTotalBudget,
            PreferredCategory = string.IsNullOrWhiteSpace(req.PreferredCategory) || req.PreferredCategory == "any" ? null : req.PreferredCategory.Trim().ToLowerInvariant(),
            Transmission = Parse.OptionalEnum<TransmissionType>(req.Transmission, "transmission"),
            TransmissionRequired = req.TransmissionRequired,
            Fuel = Parse.OptionalEnum<FuelType>(req.Fuel, "fuel type"),
            Journey = Parse.Journey(req.Journey),
            PrioritiseComfort = req.PrioritiseComfort,
            PrioritiseFuelEfficiency = req.PrioritiseFuelEfficiency,
            PrioritiseSpace = req.PrioritiseSpace,
        };

        // Pricing needs branches; default to the first active branch when none was chosen.
        var locations = await db.Locations.AsNoTracking().Where(l => l.IsActive).OrderBy(l => l.Id).ToListAsync(ct);
        var pickupLocation = locations.FirstOrDefault(l => l.Id == req.PickupLocationId) ?? locations.FirstOrDefault()
                             ?? throw new DomainException("location.none", "No branches are configured.");
        var returnLocation = locations.FirstOrDefault(l => l.Id == req.ReturnLocationId) ?? pickupLocation;

        var vehicles = await catalog.Rentable().Include(v => v.Category).AsNoTracking().ToListAsync(ct);
        var ids = vehicles.Select(v => v.Id).ToList();
        var ratings = await catalog.RatingsAsync(ids, ct);
        var images = await catalog.PrimaryImagesAsync(ids, ct);
        var states = await availability.CurrentStatesAsync(vehicles, ct);

        // One query covers the requested period and the following 31 days (for "next available" suggestions).
        var slots = await availability.LoadSlotsAsync(ids, pickup, ret.AddDays(31), ct);

        var candidates = new List<VehicleCandidate>();
        foreach (var v in vehicles)
        {
            var result = availability.Evaluate(v, pickup, ret, slots);
            var quote = PricingCalculator.Quote(v.DailyRate, v.DepositAmount, pickup, ret, pickupLocation.City, returnLocation.City, policy);
            DateTime? next = result.IsAvailable ? null : availability.NextAvailable(v, pickup, ret, slots);
            var rating = ratings.TryGetValue(v.Id, out var r) ? r : ((double, int)?)null;

            candidates.Add(new VehicleCandidate(
                v.Id, v.DisplayName, v.Category!.Slug, v.Category.Name, v.Seats, v.LuggageCapacity, v.Transmission, v.FuelType,
                v.FuelEconomyLitresPer100Km, v.ComfortRating, v.DailyRate,
                rating?.Item1, rating?.Item2 ?? 0, result.IsAvailable, result.Reasons, next, quote, images.GetValueOrDefault(v.Id)?.Url));
        }

        var outcome = RecommendationEngine.Recommend(prefs, candidates);
        var byId = vehicles.ToDictionary(v => v.Id);

        RecommendedVehicleDto Map(ScoredVehicle s)
        {
            var v = byId[s.Vehicle.VehicleId];
            var summary = v.ToSummary(images.GetValueOrDefault(v.Id), ratings.TryGetValue(v.Id, out var rr) ? rr : null, states[v.Id], s.Vehicle.IsAvailable);
            return new RecommendedVehicleDto(
                summary, s.Score,
                s.Components.Select(c => new ScoreComponentDto(c.Name, c.Weight, Math.Round(c.Score, 3))).ToList(),
                s.Reasons, s.TradeOffs, s.Summary, s.Vehicle.IsAvailable, Mapping.Local(s.Vehicle.NextAvailablePickupUtc),
                s.Vehicle.Quote.RentalDays, s.Vehicle.Quote.Total, s.Vehicle.Quote.EffectiveDailyRate, s.IsAlternative);
        }

        db.RecommendationLogs.Add(new RecommendationLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PreferencesJson = JsonSerializer.Serialize(new
            {
                days = (ret - pickup).TotalDays,
                req.Passengers, req.LuggageBags, req.MaxDailyBudget, req.MaxTotalBudget, req.PreferredCategory,
                req.Transmission, req.TransmissionRequired, req.Fuel, req.Journey,
                req.PrioritiseComfort, req.PrioritiseFuelEfficiency, req.PrioritiseSpace,
            }),
            MatchCount = outcome.Matches.Count,
            AlternativeCount = outcome.Alternatives.Count,
            Source = "wizard",
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);

        return new RecommendationResponseDto(
            outcome.Matches.Select(Map).ToList(),
            outcome.Alternatives.Select(Map).ToList(),
            outcome.UnmetRequirements,
            outcome.Suggestions,
            outcome.FleetSize,
            outcome.EligibleCount,
            AlgorithmDescription);
    }

    public async Task<InterpretedPreferencesDto> InterpretAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
        {
            throw new DomainException("interpret.text", "Please describe your trip in up to 1,000 characters.");
        }

        var (p, source) = await interpreter.InterpretAsync(text, ct);
        return new InterpretedPreferencesDto(
            p.Passengers, p.LuggageBags, p.DurationDays, p.MaxDailyBudget, p.MaxTotalBudget, p.CategorySlug,
            p.Transmission?.ToString(), p.TransmissionRequired, p.Fuel?.ToString(),
            p.Journey is { } j ? Mapping.JourneyName(j) : null,
            p.PrioritiseComfort, p.PrioritiseFuelEfficiency, p.PrioritiseSpace, p.Understood, p.Notes, source);
    }

    private static void ValidateRequest(RecommendationRequest req)
    {
        if (req.Passengers is < 1 or > ParsedPreferences.Limits.MaxPassengers)
        {
            throw new DomainException("recommend.passengers", $"Passengers must be between 1 and {ParsedPreferences.Limits.MaxPassengers}.");
        }

        if (req.LuggageBags is < 0 or > ParsedPreferences.Limits.MaxLuggage)
        {
            throw new DomainException("recommend.luggage", $"Luggage must be between 0 and {ParsedPreferences.Limits.MaxLuggage} bags.");
        }

        if (req.MaxDailyBudget is <= 0m || req.MaxTotalBudget is <= 0m)
        {
            throw new DomainException("recommend.budget", "Budgets must be greater than zero.");
        }
    }
}
