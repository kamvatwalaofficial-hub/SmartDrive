using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Recommendations;
using SmartDrive.Domain.Rules;

namespace SmartDrive.UnitTests.Domain;

public sealed class RecommendationEngineTests : TestBase
{
    private static readonly RentalPolicy Policy = new();
    private static readonly DateTime Pickup = At(3);
    private static readonly DateTime Return = At(8);   // five days

    private static VehicleCandidate Car(string name, string category = "hatchback", int seats = 5, int luggage = 2, decimal rate = 600m,
        bool available = true, int comfort = 3, TransmissionType transmission = TransmissionType.Automatic, double? rating = null, int reviews = 0) =>
        new(Guid.NewGuid(), name, category, category, seats, luggage, transmission, FuelType.Petrol, 6.0m, comfort, rate, rating, reviews,
            available, available ? [] : [UnavailableReason.ConflictingBooking], available ? null : At(20),
            PricingCalculator.Quote(rate, 3000m, Pickup, Return, "Johannesburg", "Johannesburg", Policy), null);

    private static RecommendationPreferences Prefs(int? passengers = null, decimal? daily = null, decimal? total = null, string? category = null,
        int? luggage = null, bool comfort = false) => new()
    {
        PickupUtc = Pickup,
        ReturnUtc = Return,
        Passengers = passengers,
        MaxDailyBudget = daily,
        MaxTotalBudget = total,
        PreferredCategory = category,
        LuggageBags = luggage,
        PrioritiseComfort = comfort,
    };

    [Fact]
    public void An_unavailable_vehicle_is_never_a_match_however_well_it_scores()
    {
        var perfect = Car("Perfect but booked", "suv", seats: 7, comfort: 5, rate: 500m, available: false, rating: 5, reviews: 30);
        var ordinary = Car("Ordinary", "hatchback");

        var outcome = RecommendationEngine.Recommend(Prefs(passengers: 4, category: "suv", comfort: true), [perfect, ordinary]);

        Assert.DoesNotContain(outcome.Matches, m => m.Vehicle.VehicleId == perfect.VehicleId);
        Assert.Contains(outcome.Matches, m => m.Vehicle.VehicleId == ordinary.VehicleId);
    }

    [Fact]
    public void Passenger_and_luggage_minimums_are_hard_constraints()
    {
        var small = Car("Two-seater", seats: 2, rate: 300m);
        var noBoot = Car("Tiny boot", seats: 5, luggage: 1, rate: 350m);
        var family = Car("Family SUV", "suv", seats: 7, luggage: 4, rate: 1200m);

        var outcome = RecommendationEngine.Recommend(Prefs(passengers: 5, luggage: 3), [small, noBoot, family]);

        Assert.Single(outcome.Matches);
        Assert.Equal(family.VehicleId, outcome.Matches[0].Vehicle.VehicleId);
        Assert.DoesNotContain(outcome.Alternatives, a => a.Vehicle.Seats < 5 || a.Vehicle.LuggageCapacity < 3);
    }

    [Fact]
    public void Matches_respect_both_the_daily_and_the_total_budget()
    {
        var cheap = Car("Cheap", rate: 450m);
        var mid = Car("Mid", rate: 650m);
        var dear = Car("Dear", rate: 1500m);

        var daily = RecommendationEngine.Recommend(Prefs(daily: 700m), [cheap, mid, dear]);
        Assert.Equal(2, daily.Matches.Count);
        Assert.All(daily.Matches, m => Assert.True(m.Vehicle.Quote.EffectiveDailyRate <= 700m));

        var total = RecommendationEngine.Recommend(Prefs(total: 2500m), [cheap, mid, dear]);   // 5 days
        Assert.Single(total.Matches);
        Assert.Equal(cheap.VehicleId, total.Matches[0].Vehicle.VehicleId);
    }

    [Fact]
    public void Preferred_category_ranks_first_when_everything_else_is_equal()
    {
        var hatch = Car("Hatch", "hatchback", rate: 700m);
        var suv = Car("SUV", "suv", rate: 700m);

        var outcome = RecommendationEngine.Recommend(Prefs(category: "suv"), [hatch, suv]);

        Assert.Equal(suv.VehicleId, outcome.Matches[0].Vehicle.VehicleId);
        Assert.Contains(outcome.Matches[0].Reasons, r => r.StartsWith("Matches your suv preference"));
        Assert.Contains(outcome.Matches.Single(m => m.Vehicle.VehicleId == hatch.VehicleId).TradeOffs, t => t.Contains("rather than your preferred"));
    }

    [Fact]
    public void Explanations_only_state_facts_the_data_supports()
    {
        var car = Car("Honest", seats: 5, rate: 550m);

        var match = RecommendationEngine.Recommend(Prefs(passengers: 4, daily: 600m), [car]).Matches.Single();

        Assert.Contains("Available for your selected dates", match.Reasons);
        Assert.Contains(match.Reasons, r => r == "Seats 5 — room for your 4 passengers");
        Assert.Contains(match.Reasons, r => r.Contains("within your R600.00 daily budget"));
        Assert.InRange(match.Score, 0, 100);
        Assert.NotEmpty(match.Components);
    }

    [Fact]
    public void When_nothing_fits_the_budget_it_explains_why_and_offers_honest_alternatives()
    {
        var a = Car("A", rate: 900m);
        var b = Car("B", rate: 1100m, available: false);

        var outcome = RecommendationEngine.Recommend(Prefs(daily: 500m), [a, b]);

        Assert.Empty(outcome.Matches);
        Assert.NotEmpty(outcome.UnmetRequirements);
        Assert.Contains(outcome.Suggestions, s => s.Contains("daily budget of"));
        Assert.NotEmpty(outcome.Alternatives);
        Assert.All(outcome.Alternatives, alt =>
        {
            Assert.True(alt.IsAlternative);
            if (!alt.Vehicle.IsAvailable)
            {
                Assert.DoesNotContain("Available for your selected dates", alt.Reasons);
                Assert.Contains(alt.TradeOffs, t => t.StartsWith("Not available for your selected dates"));
            }
        });
    }

    [Fact]
    public void Group_too_big_for_any_vehicle_is_explained()
    {
        var outcome = RecommendationEngine.Recommend(Prefs(passengers: 12), [Car("Five seats"), Car("Seven seats", "suv", seats: 7)]);

        Assert.Empty(outcome.Matches);
        Assert.Contains(outcome.UnmetRequirements, u => u.Contains("seats 12 people") && u.Contains("largest seats 7"));
    }
}
