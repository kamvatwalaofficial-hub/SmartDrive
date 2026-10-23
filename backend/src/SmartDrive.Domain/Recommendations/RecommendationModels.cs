using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Domain.Recommendations;

/// <summary>What the customer asked for. Only the rental period is required; everything else may be skipped.</summary>
public sealed record RecommendationPreferences
{
    public DateTime PickupUtc { get; init; }
    public DateTime ReturnUtc { get; init; }
    public int? Passengers { get; init; }
    public int? LuggageBags { get; init; }
    public decimal? MaxDailyBudget { get; init; }
    public decimal? MaxTotalBudget { get; init; }

    /// <summary>Category slug, e.g. "suv".</summary>
    public string? PreferredCategory { get; init; }

    public TransmissionType? Transmission { get; init; }

    /// <summary>When true the transmission is a hard requirement rather than a preference.</summary>
    public bool TransmissionRequired { get; init; }

    public FuelType? Fuel { get; init; }
    public JourneyType? Journey { get; init; }
    public bool PrioritiseComfort { get; init; }
    public bool PrioritiseFuelEfficiency { get; init; }
    public bool PrioritiseSpace { get; init; }

    public bool HasBudget => MaxDailyBudget is not null || MaxTotalBudget is not null;
}

/// <summary>
/// One fleet vehicle with the facts the engine needs. Availability and price are computed by the
/// same services the booking flow uses, so the engine can never contradict a real booking.
/// </summary>
public sealed record VehicleCandidate(
    Guid VehicleId,
    string DisplayName,
    string CategorySlug,
    string CategoryName,
    int Seats,
    int LuggageCapacity,
    TransmissionType Transmission,
    FuelType Fuel,
    decimal? FuelEconomyLitresPer100Km,
    int ComfortRating,
    decimal DailyRate,
    double? AverageRating,
    int ReviewCount,
    bool IsAvailable,
    IReadOnlyList<UnavailableReason> UnavailableReasons,
    DateTime? NextAvailablePickupUtc,
    PriceQuote Quote,
    string? ImageUrl);

/// <summary>One weighted part of the score, kept so the UI can show how a score was reached.</summary>
public sealed record ScoreComponent(string Name, double Weight, double Score);

public sealed record ScoredVehicle(
    VehicleCandidate Vehicle,
    int Score,
    IReadOnlyList<ScoreComponent> Components,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> TradeOffs,
    string Summary,
    bool IsAlternative);

public sealed record RecommendationOutcome(
    IReadOnlyList<ScoredVehicle> Matches,
    IReadOnlyList<ScoredVehicle> Alternatives,
    IReadOnlyList<string> UnmetRequirements,
    IReadOnlyList<string> Suggestions,
    int EligibleCount,
    int FleetSize);
