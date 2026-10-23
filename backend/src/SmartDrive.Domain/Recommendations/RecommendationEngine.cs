using System.Globalization;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Domain.Recommendations;

/// <summary>
/// Explainable, rule-based recommendation engine.
///
/// <para><b>1. Eligibility (hard constraints).</b> A vehicle can only be a match if it is genuinely
/// available for the requested period, seats at least the requested passengers, holds at least the
/// requested luggage, has the required transmission (when marked as required) and fits the daily and
/// total budgets. A high score can never override these.</para>
///
/// <para><b>2. Scoring (soft preferences).</b> Each eligible vehicle gets a 0–100 score: a weighted
/// average of the components the customer actually expressed, plus value-for-money (always) and
/// customer rating (when there are at least three reviews). Weights are in <see cref="Weights"/>.</para>
///
/// <para><b>3. Explanations.</b> Every reason and trade-off is generated from the vehicle's own data.</para>
///
/// <para><b>4. Alternatives.</b> When nothing satisfies every hard constraint, the engine explains which
/// requirement failed and offers the closest options with their trade-offs stated plainly. Vehicles
/// that are unavailable are offered only with their next available date, never as available.</para>
/// </summary>
public static class RecommendationEngine
{
    public static class Weights
    {
        public const double Budget = 25;
        public const double Category = 20;
        public const double Capacity = 15;
        public const double Luggage = 10;
        public const double Transmission = 10;
        public const double Fuel = 5;
        public const double Journey = 10;
        public const double Comfort = 10;
        public const double FuelEfficiency = 10;
        public const double Space = 10;
        public const double Value = 10;
        public const double Rating = 5;
    }

    public const int MinimumReviewsForRating = 3;

    private static readonly Dictionary<(string, string), double> RelatedCategories = new()
    {
        [("economy", "hatchback")] = 0.6,
        [("hatchback", "economy")] = 0.6,
        [("hatchback", "sedan")] = 0.3,
        [("sedan", "hatchback")] = 0.3,
        [("sedan", "luxury")] = 0.6,
        [("luxury", "sedan")] = 0.6,
        [("suv", "minivan")] = 0.5,
        [("minivan", "suv")] = 0.5,
        [("suv", "bakkie")] = 0.4,
        [("bakkie", "suv")] = 0.4,
    };

    public static RecommendationOutcome Recommend(
        RecommendationPreferences prefs,
        IReadOnlyList<VehicleCandidate> fleet,
        int maxResults = 6)
    {
        var capacityOk = fleet.Where(v => SeatsOk(v, prefs) && LuggageOk(v, prefs)).ToList();
        var eligible = capacityOk.Where(v => v.IsAvailable && TransmissionOk(v, prefs)).ToList();
        var withinBudget = eligible.Where(v => BudgetRatio(v, prefs) <= 1.0).ToList();

        var context = new ScoringContext(prefs, eligible.Count > 0 ? eligible : capacityOk, fleet);

        var matches = withinBudget
            .Select(v => Score(v, context, isAlternative: false))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Vehicle.Quote.Total)
            .Take(maxResults)
            .ToList();

        var unmet = new List<string>();
        var suggestions = new List<string>();
        var alternatives = new List<ScoredVehicle>();

        if (matches.Count == 0)
        {
            ExplainUnmet(prefs, fleet, capacityOk, eligible, unmet, suggestions);
            alternatives = BuildAlternatives(prefs, capacityOk, eligible, context, maxResults: 3);
        }

        return new RecommendationOutcome(matches, alternatives, unmet, suggestions, withinBudget.Count, fleet.Count);
    }

    // ------------------------------------------------------------------ hard constraints

    public static bool SeatsOk(VehicleCandidate v, RecommendationPreferences p) => p.Passengers is null || v.Seats >= p.Passengers;

    public static bool LuggageOk(VehicleCandidate v, RecommendationPreferences p) => p.LuggageBags is null || v.LuggageCapacity >= p.LuggageBags;

    public static bool TransmissionOk(VehicleCandidate v, RecommendationPreferences p) =>
        !p.TransmissionRequired || p.Transmission is null || v.Transmission == p.Transmission;

    /// <summary>
    /// How much of the budget the rental uses: the larger of (effective daily cost ÷ daily budget) and
    /// (total ÷ total budget). 1.0 means exactly on budget; 0 when no budget was given.
    /// </summary>
    public static double BudgetRatio(VehicleCandidate v, RecommendationPreferences p)
    {
        var ratio = 0.0;
        if (p.MaxDailyBudget is { } daily && daily > 0)
        {
            ratio = Math.Max(ratio, (double)(v.Quote.EffectiveDailyRate / daily));
        }

        if (p.MaxTotalBudget is { } total && total > 0)
        {
            ratio = Math.Max(ratio, (double)(v.Quote.Total / total));
        }

        return ratio;
    }

    // ------------------------------------------------------------------ scoring

    private sealed class ScoringContext(RecommendationPreferences prefs, IReadOnlyList<VehicleCandidate> pool, IReadOnlyList<VehicleCandidate> fleet)
    {
        public RecommendationPreferences Prefs { get; } = prefs;
        public decimal MinTotal { get; } = pool.Count == 0 ? 0 : pool.Min(v => v.Quote.Total);
        public decimal MaxTotal { get; } = pool.Count == 0 ? 0 : pool.Max(v => v.Quote.Total);
        public int MaxLuggage { get; } = Math.Max(1, pool.Count == 0 ? 1 : pool.Max(v => v.LuggageCapacity));
        public int MaxSeats { get; } = Math.Max(1, pool.Count == 0 ? 1 : pool.Max(v => v.Seats));
        public decimal? MinEconomy { get; } = pool.Where(v => v.FuelEconomyLitresPer100Km is not null).Select(v => v.FuelEconomyLitresPer100Km).Min();
        public decimal? MaxEconomy { get; } = pool.Where(v => v.FuelEconomyLitresPer100Km is not null).Select(v => v.FuelEconomyLitresPer100Km).Max();
        public string? PreferredCategoryName { get; } = prefs.PreferredCategory is null ? null
            : fleet.FirstOrDefault(v => v.CategorySlug == prefs.PreferredCategory)?.CategoryName ?? prefs.PreferredCategory;
    }

    private static ScoredVehicle Score(VehicleCandidate v, ScoringContext c, bool isAlternative)
    {
        var p = c.Prefs;
        var components = new List<ScoreComponent>();
        var reasons = new List<string>();
        var tradeOffs = new List<string>();
        var days = v.Quote.RentalDays;

        if (v.IsAvailable)
        {
            reasons.Add("Available for your selected dates");
        }
        else
        {
            var next = v.NextAvailablePickupUtc is { } n
                ? $" — next available pickup {SouthAfricanTime.ToLocal(n).ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}"
                : "";
            tradeOffs.Add($"Not available for your selected dates{next}");
        }

        // Budget
        if (p.HasBudget)
        {
            var ratio = BudgetRatio(v, p);
            var score = ratio <= 1 ? 1 - (0.5 * ratio) : Math.Max(0, 0.5 - (ratio - 1));
            components.Add(new("Budget fit", Weights.Budget, score));
            if (p.MaxTotalBudget is { } total)
            {
                if (v.Quote.Total <= total)
                {
                    reasons.Add($"Estimated {Money.Format(v.Quote.Total)} for {Days(days)} — within your {Money.Format(total)} total budget");
                }
                else
                {
                    tradeOffs.Add($"Estimated {Money.Format(v.Quote.Total)} for {Days(days)} — {Money.Format(v.Quote.Total - total)} over your total budget");
                }
            }

            if (p.MaxDailyBudget is { } daily)
            {
                var perDay = v.Quote.EffectiveDailyRate;
                if (perDay <= daily)
                {
                    reasons.Add($"{Money.Format(perDay)} per day — within your {Money.Format(daily)} daily budget");
                }
                else
                {
                    tradeOffs.Add($"{Money.Format(perDay)} per day — {Money.Format(perDay - daily)} above your daily budget");
                }
            }
        }

        // Category
        if (p.PreferredCategory is { } wanted)
        {
            var score = v.CategorySlug == wanted ? 1.0 : RelatedCategories.GetValueOrDefault((wanted, v.CategorySlug), 0.0);
            components.Add(new("Category", Weights.Category, score));
            if (score == 1.0)
            {
                reasons.Add($"Matches your {v.CategoryName} preference");
            }
            else
            {
                tradeOffs.Add($"{v.CategoryName} rather than your preferred {c.PreferredCategoryName}");
            }
        }

        // Passenger capacity
        if (p.Passengers is { } pax)
        {
            var score = v.Seats >= pax ? 0.6 + (0.4 * pax / v.Seats) : 0;
            components.Add(new("Passenger fit", Weights.Capacity, score));
            if (v.Seats >= pax)
            {
                reasons.Add($"Seats {v.Seats} — room for your {pax} passenger{(pax == 1 ? "" : "s")}");
            }
        }

        // Luggage
        if (p.LuggageBags is { } bags && bags > 0)
        {
            var score = v.LuggageCapacity >= bags ? 0.6 + (0.4 * bags / v.LuggageCapacity) : 0;
            components.Add(new("Luggage fit", Weights.Luggage, score));
            if (v.LuggageCapacity >= bags)
            {
                reasons.Add($"Boot takes {v.LuggageCapacity} large bag{(v.LuggageCapacity == 1 ? "" : "s")}; you need {bags}");
            }
        }

        // Transmission (preference)
        if (p.Transmission is { } transmission)
        {
            var match = v.Transmission == transmission;
            components.Add(new("Transmission", Weights.Transmission, match ? 1 : 0));
            if (match)
            {
                reasons.Add($"{transmission} transmission, as preferred");
            }
            else
            {
                tradeOffs.Add($"{v.Transmission} transmission (you preferred {transmission.ToString().ToLowerInvariant()})");
            }
        }

        // Fuel
        if (p.Fuel is { } fuel)
        {
            var match = v.Fuel == fuel;
            components.Add(new("Fuel type", Weights.Fuel, match ? 1 : 0));
            if (match)
            {
                reasons.Add($"{fuel} engine, as preferred");
            }
            else
            {
                tradeOffs.Add($"{v.Fuel} rather than {fuel.ToString().ToLowerInvariant()}");
            }
        }

        // Journey type
        if (p.Journey is { } journey)
        {
            var score = JourneyScore(v, journey, c);
            components.Add(new("Journey suitability", Weights.Journey, score));
            if (score >= 0.75)
            {
                reasons.Add($"Well suited to {JourneyLabel(journey)}");
            }
            else if (score < 0.45)
            {
                tradeOffs.Add($"Less suited to {JourneyLabel(journey)}");
            }
        }

        // Priorities
        if (p.PrioritiseComfort)
        {
            components.Add(new("Comfort", Weights.Comfort, v.ComfortRating / 5.0));
            if (v.ComfortRating >= 4)
            {
                reasons.Add($"High comfort rating ({v.ComfortRating}/5)");
            }
            else if (v.ComfortRating <= 2)
            {
                tradeOffs.Add($"Basic comfort level ({v.ComfortRating}/5)");
            }
        }

        if (p.PrioritiseFuelEfficiency)
        {
            var score = EfficiencyScore(v, c);
            components.Add(new("Fuel efficiency", Weights.FuelEfficiency, score));
            if (v.Fuel == FuelType.Electric)
            {
                reasons.Add("Electric — no fuel costs");
            }
            else if (v.FuelEconomyLitresPer100Km is { } economy)
            {
                var text = $"Uses about {economy.ToString("0.0", CultureInfo.InvariantCulture)} L/100 km";
                if (score >= 0.6)
                {
                    reasons.Add(text);
                }
                else
                {
                    tradeOffs.Add($"{text} — thirstier than other options");
                }
            }
            else
            {
                tradeOffs.Add("Fuel consumption figure not recorded for this vehicle");
            }
        }

        if (p.PrioritiseSpace)
        {
            var score = (0.6 * v.LuggageCapacity / c.MaxLuggage) + (0.4 * v.Seats / c.MaxSeats);
            components.Add(new("Space", Weights.Space, Math.Min(1, score)));
            if (score >= 0.7)
            {
                reasons.Add("One of the roomiest options for your dates");
            }
        }

        // Value for money: always considered, relative to the other suitable vehicles.
        var range = c.MaxTotal - c.MinTotal;
        var value = range <= 0 ? 1.0 : 1 - (double)((v.Quote.Total - c.MinTotal) / range);
        components.Add(new("Value for money", Weights.Value, Math.Clamp(value, 0, 1)));

        // Customer rating, only with enough reviews to mean something.
        if (v.AverageRating is { } rating && v.ReviewCount >= MinimumReviewsForRating)
        {
            components.Add(new("Customer rating", Weights.Rating, rating / 5.0));
            if (rating >= 4.0)
            {
                reasons.Add($"Rated {rating.ToString("0.0", CultureInfo.InvariantCulture)}/5 by {v.ReviewCount} customers");
            }
        }

        var totalWeight = components.Sum(x => x.Weight);
        var finalScore = totalWeight == 0 ? 0 : (int)Math.Round(100 * components.Sum(x => x.Weight * x.Score) / totalWeight);

        return new ScoredVehicle(v, finalScore, components, reasons, tradeOffs, Summarise(v, p, isAlternative, tradeOffs), isAlternative);
    }

    private static double JourneyScore(VehicleCandidate v, JourneyType journey, ScoringContext c)
    {
        double Category(params (string Slug, double Score)[] table) =>
            table.FirstOrDefault(t => t.Slug == v.CategorySlug).Score;

        var comfort = v.ComfortRating / 5.0;
        var luggage = Math.Min(1.0, v.LuggageCapacity / 3.0);
        return journey switch
        {
            JourneyType.City => (0.7 * Category(("economy", 1), ("hatchback", 1), ("sedan", 0.6), ("luxury", 0.5), ("suv", 0.4), ("bakkie", 0.2), ("minivan", 0.2)))
                                + (0.3 * EfficiencyScore(v, c)),
            JourneyType.Business => (0.6 * Category(("luxury", 1), ("sedan", 0.9), ("suv", 0.7), ("hatchback", 0.5), ("economy", 0.4), ("bakkie", 0.3), ("minivan", 0.3)))
                                    + (0.4 * comfort),
            JourneyType.Family => (0.6 * Category(("minivan", 1), ("suv", 0.9), ("sedan", 0.5), ("luxury", 0.5), ("bakkie", 0.4), ("hatchback", 0.3), ("economy", 0.2)))
                                  + (0.2 * luggage) + (0.2 * Math.Min(1.0, v.Seats / 7.0)),
            JourneyType.RoadTrip => (0.6 * Category(("suv", 0.9), ("luxury", 0.9), ("sedan", 0.8), ("minivan", 0.7), ("bakkie", 0.7), ("hatchback", 0.5), ("economy", 0.4)))
                                    + (0.2 * comfort) + (0.2 * luggage),
            _ => 0.5,
        };
    }

    /// <summary>1 for the most economical vehicle in the pool, 0 for the thirstiest. Unknown figures score a neutral 0.5.</summary>
    private static double EfficiencyScore(VehicleCandidate v, ScoringContext c)
    {
        if (v.Fuel == FuelType.Electric)
        {
            return 1;
        }

        if (v.FuelEconomyLitresPer100Km is not { } economy || c.MinEconomy is not { } min || c.MaxEconomy is not { } max)
        {
            return 0.5;
        }

        return max == min ? 1 : (double)((max - economy) / (max - min));
    }

    private static string Summarise(VehicleCandidate v, RecommendationPreferences p, bool isAlternative, List<string> tradeOffs)
    {
        if (isAlternative)
        {
            return tradeOffs.Count > 0
                ? $"Closest alternative: {Lower(tradeOffs[0])}."
                : "Closest alternative to your requirements.";
        }

        // Only claims that the data supports: seats, budget and availability were all verified as hard constraints.
        var parts = new List<string>();
        if (p.Passengers is { } pax)
        {
            parts.Add($"accommodates {pax} passenger{(pax == 1 ? "" : "s")}");
        }

        if (p.HasBudget)
        {
            parts.Add("fits your estimated rental budget");
        }

        if (p.PreferredCategory is not null && v.CategorySlug == p.PreferredCategory)
        {
            parts.Add($"is the {v.CategoryName.ToLowerInvariant()} you asked for");
        }

        parts.Add("is available for your selected dates");
        return $"This vehicle is recommended because it {JoinNatural(parts)}.";
    }

    // ------------------------------------------------------------------ when nothing fits

    private static void ExplainUnmet(
        RecommendationPreferences p,
        IReadOnlyList<VehicleCandidate> fleet,
        List<VehicleCandidate> capacityOk,
        List<VehicleCandidate> eligible,
        List<string> unmet,
        List<string> suggestions)
    {
        if (fleet.Count == 0)
        {
            unmet.Add("There are no vehicles in the fleet yet.");
            return;
        }

        if (p.Passengers is { } pax && fleet.All(v => v.Seats < pax))
        {
            unmet.Add($"No vehicle in our fleet seats {pax} people; the largest seats {fleet.Max(v => v.Seats)}.");
            suggestions.Add("Consider splitting your group across two vehicles.");
            return;
        }

        if (p.LuggageBags is { } bags && fleet.All(v => v.LuggageCapacity < bags))
        {
            unmet.Add($"No vehicle in our fleet holds {bags} large bags; the largest boot takes {fleet.Max(v => v.LuggageCapacity)}.");
            return;
        }

        if (capacityOk.Count == 0)
        {
            unmet.Add("No single vehicle meets both your passenger and luggage requirements.");
            suggestions.Add("Try reducing the luggage requirement, or consider two vehicles.");
            return;
        }

        var available = capacityOk.Where(v => v.IsAvailable).ToList();
        if (available.Count == 0)
        {
            unmet.Add($"{capacityOk.Count} vehicle{(capacityOk.Count == 1 ? " fits" : "s fit")} your group, but none {(capacityOk.Count == 1 ? "is" : "are")} available for your selected dates.");
            var next = capacityOk.Where(v => v.NextAvailablePickupUtc is not null).Min(v => v.NextAvailablePickupUtc);
            if (next is { } n)
            {
                suggestions.Add($"Try a pickup from {SouthAfricanTime.ToLocal(n).ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)}, when a suitable vehicle becomes free.");
            }
            else
            {
                suggestions.Add("Try different dates.");
            }

            return;
        }

        if (eligible.Count == 0 && p.TransmissionRequired && p.Transmission is { } t)
        {
            unmet.Add($"No available vehicle with {t.ToString().ToLowerInvariant()} transmission fits your group for these dates.");
            suggestions.Add($"Allow a {(t == TransmissionType.Automatic ? "manual" : "automatic")} vehicle, or try different dates.");
            return;
        }

        if (p.HasBudget && eligible.Count > 0)
        {
            var cheapest = eligible.OrderBy(v => v.Quote.Total).First();
            unmet.Add($"The most affordable suitable vehicle ({cheapest.DisplayName}) costs {Money.Format(cheapest.Quote.EffectiveDailyRate)} per day ({Money.Format(cheapest.Quote.Total)} in total), which is above your budget.");
            if (p.MaxDailyBudget is not null)
            {
                suggestions.Add($"A daily budget of {Money.Format(Math.Ceiling(cheapest.Quote.EffectiveDailyRate))} would open up {cheapest.DisplayName}.");
            }

            if (p.MaxTotalBudget is not null)
            {
                suggestions.Add($"A total budget of {Money.Format(Math.Ceiling(cheapest.Quote.Total))} would open up {cheapest.DisplayName}.");
            }

            if (p.PreferredCategory is not null and not "economy")
            {
                suggestions.Add("Economy vehicles usually offer the lowest daily rates.");
            }

            if (cheapest.Quote.RentalDays > 1)
            {
                suggestions.Add("A shorter rental would lower the total cost.");
            }
        }
    }

    private static List<ScoredVehicle> BuildAlternatives(
        RecommendationPreferences p,
        List<VehicleCandidate> capacityOk,
        List<VehicleCandidate> eligible,
        ScoringContext context,
        int maxResults)
    {
        // 1. Available and big enough, but over budget: least over budget first.
        var overBudget = eligible
            .OrderBy(v => BudgetRatio(v, p))
            .Select(v => Score(v, context, isAlternative: true));

        // 2. Available and big enough, but with the other transmission (only when it was required).
        var otherTransmission = capacityOk
            .Where(v => v.IsAvailable && !TransmissionOk(v, p))
            .OrderBy(v => v.Quote.Total)
            .Select(v => Score(v, context, isAlternative: true));

        // 3. Big enough but not free on these dates: offered with their next available date only.
        var later = capacityOk
            .Where(v => !v.IsAvailable && v.NextAvailablePickupUtc is not null)
            .OrderBy(v => v.NextAvailablePickupUtc)
            .Select(v => Score(v, context, isAlternative: true));

        return overBudget.Concat(otherTransmission).Concat(later)
            .DistinctBy(s => s.Vehicle.VehicleId)
            .Take(maxResults)
            .ToList();
    }

    // ------------------------------------------------------------------ helpers

    public static string JourneyLabel(JourneyType journey) => journey switch
    {
        JourneyType.City => "city driving",
        JourneyType.Business => "business travel",
        JourneyType.Family => "family travel",
        JourneyType.RoadTrip => "a longer road trip",
        _ => "your journey",
    };

    private static string Days(int days) => $"{days} day{(days == 1 ? "" : "s")}";

    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static string JoinNatural(List<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };
}
