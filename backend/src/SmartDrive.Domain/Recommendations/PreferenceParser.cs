using System.Globalization;
using System.Text.RegularExpressions;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Domain.Recommendations;

/// <summary>
/// Preferences extracted from a free-text request. Facts about vehicles (availability, prices,
/// specifications) never come from here: these values only feed the normal recommendation engine.
/// </summary>
public sealed record ParsedPreferences
{
    public int? Passengers { get; init; }
    public int? LuggageBags { get; init; }
    public int? DurationDays { get; init; }
    public decimal? MaxDailyBudget { get; init; }
    public decimal? MaxTotalBudget { get; init; }
    public string? CategorySlug { get; init; }
    public TransmissionType? Transmission { get; init; }
    public bool TransmissionRequired { get; init; }
    public FuelType? Fuel { get; init; }
    public JourneyType? Journey { get; init; }
    public bool PrioritiseComfort { get; init; }
    public bool PrioritiseFuelEfficiency { get; init; }
    public bool PrioritiseSpace { get; init; }

    /// <summary>Plain-language list of what was understood, shown back to the customer to confirm.</summary>
    public IReadOnlyList<string> Understood { get; init; } = [];

    /// <summary>Assumptions made and things that could not be worked out.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public static class Limits
    {
        public const int MaxPassengers = 15;
        public const int MaxLuggage = 15;
        public const int MaxDays = 60;
        public const decimal MinBudget = 50m;
        public const decimal MaxBudget = 1_000_000m;
    }

    /// <summary>Drops any value outside sensible bounds (used for both rule-based and language-model output).</summary>
    public ParsedPreferences Sanitised(out List<string> rejected)
    {
        rejected = [];
        var result = this;
        if (Passengers is { } p && (p < 1 || p > Limits.MaxPassengers))
        {
            rejected.Add($"Ignored a passenger count of {p} (must be 1–{Limits.MaxPassengers}).");
            result = result with { Passengers = null };
        }

        if (LuggageBags is { } b && (b < 0 || b > Limits.MaxLuggage))
        {
            rejected.Add($"Ignored a luggage count of {b}.");
            result = result with { LuggageBags = null };
        }

        if (DurationDays is { } d && (d < 1 || d > Limits.MaxDays))
        {
            rejected.Add($"Ignored a rental length of {d} days (must be 1–{Limits.MaxDays}).");
            result = result with { DurationDays = null };
        }

        if (MaxDailyBudget is { } daily && (daily < Limits.MinBudget || daily > Limits.MaxBudget))
        {
            rejected.Add($"Ignored a daily budget of {Money.Format(daily)}.");
            result = result with { MaxDailyBudget = null };
        }

        if (MaxTotalBudget is { } total && (total < Limits.MinBudget || total > Limits.MaxBudget))
        {
            rejected.Add($"Ignored a total budget of {Money.Format(total)}.");
            result = result with { MaxTotalBudget = null };
        }

        return result;
    }
}

/// <summary>
/// A transparent, rule-based interpreter for requests such as
/// "I need a comfortable car for four adults and luggage for a five-day trip, and I want to spend no
/// more than R600 per day". It needs no external service, so the feature always works; a
/// language-model interpreter can be layered on top and is validated the same way.
/// </summary>
public static partial class PreferenceParser
{
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
        ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["twenty"] = 20,
        ["thirty"] = 30, ["couple"] = 2,
    };

    private const string Words = "one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|twenty|thirty";

    // \b stops digits inside amounts (e.g. "R600") being read as counts.
    private const string Num = @"\b(\d{1,3}|a|an|" + Words + ")";

    // Day counts ignore "a"/"an" so "R600 a day" is not read as a one-day rental.
    private const string NumNoArticle = @"\b(\d{1,3}|" + Words + ")";

    public static ParsedPreferences Parse(string? text)
    {
        var understood = new List<string>();
        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedPreferences { Notes = ["Please describe your trip."] };
        }

        var s = Normalise(text);

        // ---- passengers
        int? passengers = null;
        var people = PeopleRegex().Matches(s);
        if (people.Count > 0)
        {
            passengers = people.Sum(m => ToInt(m.Groups[1].Value));
        }
        else if (FamilyOfRegex().Match(s) is { Success: true } family)
        {
            passengers = ToInt(family.Groups[1].Value);
        }
        else if (SoloRegex().IsMatch(s))
        {
            passengers = 1;
        }
        else if (CoupleRegex().IsMatch(s))
        {
            passengers = 2;
        }

        if (passengers is not null)
        {
            understood.Add($"{passengers} passenger{(passengers == 1 ? "" : "s")}");
        }

        // ---- luggage
        int? bags = null;
        if (BagsRegex().Match(s) is { Success: true } bagMatch)
        {
            bags = ToInt(bagMatch.Groups[1].Value);
            understood.Add($"{bags} large bag{(bags == 1 ? "" : "s")}");
        }
        else if (LuggageWordRegex().IsMatch(s))
        {
            bags = Math.Max(1, passengers ?? 1);
            understood.Add($"luggage for the group (assumed {bags} large bag{(bags == 1 ? "" : "s")})");
            notes.Add("I assumed one large bag per passenger; adjust it if you are travelling light or heavy.");
        }

        // ---- rental length
        int? duration = null;
        if (DaysRegex().Match(s) is { Success: true } dayMatch)
        {
            duration = ToInt(dayMatch.Groups[1].Value);
            if (dayMatch.Groups[2].Value.StartsWith("night", StringComparison.Ordinal))
            {
                duration = Math.Max(1, duration.Value);
            }
        }
        else if (WeeksRegex().Match(s) is { Success: true } weekMatch)
        {
            duration = 7 * ToInt(weekMatch.Groups[1].Value);
        }
        else if (s.Contains("fortnight", StringComparison.Ordinal))
        {
            duration = 14;
        }
        else if (s.Contains("weekend", StringComparison.Ordinal))
        {
            duration = 2;
            notes.Add("I took \"weekend\" as a 2-day rental.");
        }

        if (duration is not null)
        {
            understood.Add($"{duration} day{(duration == 1 ? "" : "s")}");
        }

        // ---- budget
        decimal? daily = null, total = null;
        if (DailyBudgetRegex().Match(s) is { Success: true } dailyMatch)
        {
            daily = ParseAmount(dailyMatch.Groups["amount"].Value, dailyMatch.Groups["k"].Success);
            understood.Add($"up to {Money.Format(daily.Value)} per day");
        }

        if (TotalBudgetRegex().Match(s) is { Success: true } totalMatch)
        {
            total = ParseAmount(totalMatch.Groups["amount"].Value, totalMatch.Groups["k"].Success);
            understood.Add($"up to {Money.Format(total.Value)} in total");
        }
        else if (daily is null && AnyAmountRegex().Match(s) is { Success: true } anyAmount)
        {
            total = ParseAmount(anyAmount.Groups["amount"].Value, anyAmount.Groups["k"].Success);
            understood.Add($"up to {Money.Format(total.Value)} in total");
            notes.Add($"I took {Money.Format(total.Value)} as your budget for the whole rental. Say \"per day\" if you meant a daily budget.");
        }

        // ---- transmission and fuel
        TransmissionType? transmission = null;
        var transmissionRequired = false;
        if (AutomaticRegex().IsMatch(s))
        {
            transmission = TransmissionType.Automatic;
        }
        else if (ManualRegex().IsMatch(s))
        {
            transmission = TransmissionType.Manual;
        }

        if (transmission is not null)
        {
            transmissionRequired = RequiredRegex().IsMatch(s);
            understood.Add($"{transmission.ToString()!.ToLowerInvariant()} transmission{(transmissionRequired ? " (required)" : " (preferred)")}");
        }

        FuelType? fuel = s switch
        {
            _ when ElectricRegex().IsMatch(s) => FuelType.Electric,
            _ when s.Contains("hybrid", StringComparison.Ordinal) => FuelType.Hybrid,
            _ when s.Contains("diesel", StringComparison.Ordinal) => FuelType.Diesel,
            _ when s.Contains("petrol", StringComparison.Ordinal) => FuelType.Petrol,
            _ => null,
        };
        if (fuel is not null)
        {
            understood.Add($"{fuel.ToString()!.ToLowerInvariant()} preferred");
        }

        // ---- category
        string? category = s switch
        {
            _ when BakkieRegex().IsMatch(s) => "bakkie",
            _ when MinivanRegex().IsMatch(s) => "minivan",
            _ when SuvRegex().IsMatch(s) => "suv",
            _ when LuxuryRegex().IsMatch(s) => "luxury",
            _ when SedanRegex().IsMatch(s) => "sedan",
            _ when HatchRegex().IsMatch(s) => "hatchback",
            _ when EconomyRegex().IsMatch(s) => "economy",
            _ => null,
        };
        if (category is not null)
        {
            understood.Add($"{category} category");
        }

        // ---- journey type
        JourneyType? journey = s switch
        {
            _ when BusinessRegex().IsMatch(s) => JourneyType.Business,
            _ when RoadTripRegex().IsMatch(s) => JourneyType.RoadTrip,
            _ when FamilyRegex().IsMatch(s) => JourneyType.Family,
            _ when CityRegex().IsMatch(s) => JourneyType.City,
            _ => null,
        };
        if (journey is not null)
        {
            understood.Add(RecommendationEngine.JourneyLabel(journey.Value));
        }

        // ---- priorities
        var comfort = ComfortRegex().IsMatch(s);
        var efficiency = EfficiencyRegex().IsMatch(s);
        var space = SpaceRegex().IsMatch(s);
        if (comfort) understood.Add("comfort is a priority");
        if (efficiency) understood.Add("fuel efficiency is a priority");
        if (space) understood.Add("extra space is a priority");

        if (understood.Count == 0)
        {
            notes.Add("I couldn't pick out any preferences. Try mentioning passengers, budget, trip length or car type.");
        }

        var parsed = new ParsedPreferences
        {
            Passengers = passengers,
            LuggageBags = bags,
            DurationDays = duration,
            MaxDailyBudget = daily,
            MaxTotalBudget = total,
            CategorySlug = category,
            Transmission = transmission,
            TransmissionRequired = transmissionRequired,
            Fuel = fuel,
            Journey = journey,
            PrioritiseComfort = comfort,
            PrioritiseFuelEfficiency = efficiency,
            PrioritiseSpace = space,
        };

        var sanitised = parsed.Sanitised(out var rejected);
        notes.AddRange(rejected);
        return sanitised with { Understood = understood, Notes = notes };
    }

    private static string Normalise(string text) =>
        MultiSpaceRegex().Replace(text.ToLowerInvariant().Replace('–', '-').Replace('—', '-').Replace('’', '\''), " ").Trim();

    private static int ToInt(string token) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : NumberWords.GetValueOrDefault(token, 0);

    private static decimal ParseAmount(string raw, bool thousands)
    {
        var cleaned = raw.Replace(" ", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        var value = decimal.Parse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture);
        return Money.Round(thousands ? value * 1000 : value);
    }

    private const string Amount = @"(?:r\s?(?<amount>\d[\d ,]*(?:\.\d+)?)(?<k>k)?|(?<amount>\d[\d ,]*(?:\.\d+)?)(?<k>k)?\s?(?:rand|zar))";

    [GeneratedRegex(@"\s+")] private static partial Regex MultiSpaceRegex();
    [GeneratedRegex(Num + @"\s+(?:adults?|people|persons?|passengers?|travell?ers?|kids?|children|of us|pax)\b")] private static partial Regex PeopleRegex();
    [GeneratedRegex(@"family of\s+" + Num + @"\b")] private static partial Regex FamilyOfRegex();
    [GeneratedRegex(@"\b(?:just me|only me|myself|solo|alone|on my own)\b")] private static partial Regex SoloRegex();
    [GeneratedRegex(@"\b(?:my (?:wife|husband|partner|girlfriend|boyfriend)|the two of us|a couple)\b")] private static partial Regex CoupleRegex();
    [GeneratedRegex(Num + @"\s+(?:large\s+|big\s+)?(?:bags?|suitcases?|cases?)\b")] private static partial Regex BagsRegex();
    [GeneratedRegex(@"\b(?:luggage|suitcases?|bags|baggage)\b")] private static partial Regex LuggageWordRegex();
    [GeneratedRegex(NumNoArticle + @"[\s-]*(days?|nights?)\b")] private static partial Regex DaysRegex();
    [GeneratedRegex(Num + @"[\s-]*weeks?\b")] private static partial Regex WeeksRegex();
    [GeneratedRegex(Amount + @"\s*(?:per|a|/|each)\s*day")] private static partial Regex DailyBudgetRegex();
    [GeneratedRegex(Amount + @"\s*(?:in total|total|altogether|for the (?:whole|entire|full) (?:trip|rental|period))|(?:total budget|budget overall|overall budget)\s*(?:of|is)?\s*" + Amount)] private static partial Regex TotalBudgetRegex();
    [GeneratedRegex(Amount)] private static partial Regex AnyAmountRegex();
    [GeneratedRegex(@"\b(?:automatic|auto)\b")] private static partial Regex AutomaticRegex();
    [GeneratedRegex(@"\b(?:manual|stick shift|gear ?stick)\b")] private static partial Regex ManualRegex();
    [GeneratedRegex(@"\b(?:must|has to|have to|needs? to|only|required?|can't drive|cannot drive)\b")] private static partial Regex RequiredRegex();
    [GeneratedRegex(@"\b(?:electric|ev)\b")] private static partial Regex ElectricRegex();
    [GeneratedRegex(@"\b(?:bakkie|pick-?up|double cab|single cab)\b")] private static partial Regex BakkieRegex();
    [GeneratedRegex(@"\b(?:minivan|mpv|people carrier|minibus|kombi|van|7[- ]seater)\b")] private static partial Regex MinivanRegex();
    [GeneratedRegex(@"\b(?:suv|4x4|crossover)\b")] private static partial Regex SuvRegex();
    [GeneratedRegex(@"\b(?:luxury|premium|executive)\b")] private static partial Regex LuxuryRegex();
    [GeneratedRegex(@"\b(?:sedan|saloon)\b")] private static partial Regex SedanRegex();
    [GeneratedRegex(@"\bhatch(?:back)?\b")] private static partial Regex HatchRegex();
    [GeneratedRegex(@"\b(?:economy|small car|budget car|cheap car|compact)\b")] private static partial Regex EconomyRegex();
    [GeneratedRegex(@"\b(?:business|work trip|meetings?|conference|client)\b")] private static partial Regex BusinessRegex();
    [GeneratedRegex(@"\b(?:road ?trip|long drive|long trip|holiday|vacation|garden route|cross[- ]country|tour)\b")] private static partial Regex RoadTripRegex();
    [GeneratedRegex(@"\b(?:family|kids|children)\b")] private static partial Regex FamilyRegex();
    [GeneratedRegex(@"\b(?:city|town|errands|around (?:the )?(?:city|town)|short trips?)\b")] private static partial Regex CityRegex();
    [GeneratedRegex(@"\b(?:comfort|comfortable|smooth ride|luxurious|relaxing)\b")] private static partial Regex ComfortRegex();
    [GeneratedRegex(@"\b(?:fuel[- ]efficient|fuel efficiency|economical|save on fuel|low fuel|cheap to run|fuel economy|good on fuel)\b")] private static partial Regex EfficiencyRegex();
    [GeneratedRegex(@"\b(?:spacious|roomy|lots of (?:room|space)|extra space|more space|big boot|plenty of (?:room|space))\b")] private static partial Regex SpaceRegex();
}
