using SmartDrive.Domain.Analytics;
using SmartDrive.Domain.Entities;

namespace SmartDrive.UnitTests.Domain;

public sealed class UtilisationTests : TestBase
{
    [Fact]
    public void Utilisation_is_booked_days_over_available_days_excluding_maintenance()
    {
        // 30-day window; 3 days in the workshop; two rentals of 6 and 3 days.
        var input = new UtilisationInput(Guid.NewGuid(), "Car", "suv", At(-100), null,
            [new Period(At(1), At(7)), new Period(At(10), At(13))],
            [new Period(At(20), At(23))]);

        var result = UtilisationCalculator.Calculate(input, At(0), At(30));

        Assert.Equal(30, result.FleetDays);
        Assert.Equal(3, result.MaintenanceDays);
        Assert.Equal(27, result.AvailableDays);
        Assert.Equal(9, result.BookedDays);
        Assert.Equal(Math.Round(9.0 / 27, 4), result.Utilisation);
    }

    [Fact]
    public void Overlapping_intervals_are_counted_once_and_clipped_to_the_window()
    {
        var days = UtilisationCalculator.MergedDays([new Period(At(-5), At(3)), new Period(At(2), At(4)), new Period(At(8), At(40))], new Period(At(0), At(10)));
        Assert.Equal(4 + 2, days);
    }

    [Fact]
    public void A_vehicle_that_joined_mid_period_is_measured_from_when_it_joined()
    {
        var input = new UtilisationInput(Guid.NewGuid(), "New car", "suv", At(20), null, [new Period(At(21), At(25))], []);
        var result = UtilisationCalculator.Calculate(input, At(0), At(30));
        Assert.Equal(10, result.FleetDays);
        Assert.Equal(0.4, result.Utilisation);
    }

    [Fact]
    public void Fleet_utilisation_weights_vehicles_by_their_available_days()
    {
        var fleet = new[]
        {
            new VehicleUtilisation(Guid.NewGuid(), "A", "suv", 30, 0, 30, 30, 1.0),
            new VehicleUtilisation(Guid.NewGuid(), "B", "suv", 10, 0, 10, 0, 0.0),
        };
        Assert.Equal(0.75, UtilisationCalculator.Aggregate(fleet));
    }
}

public sealed class MaintenanceRulesTests : TestBase
{
    [Fact]
    public void Service_states_follow_kilometres_and_dates()
    {
        Assert.Equal(ServiceDueState.Overdue, MaintenanceRules.ServiceState(30_100, 30_000, null, At(0)));
        Assert.Equal(ServiceDueState.DueSoon, MaintenanceRules.ServiceState(29_400, 30_000, null, At(0)));
        Assert.Equal(ServiceDueState.Ok, MaintenanceRules.ServiceState(20_000, 30_000, At(200), At(0)));
        Assert.Equal(ServiceDueState.Overdue, MaintenanceRules.ServiceState(20_000, 30_000, At(-1), At(0)));
        Assert.Equal(ServiceDueState.DueSoon, MaintenanceRules.ServiceState(20_000, null, At(10), At(0)));
        Assert.Equal(ServiceDueState.NotScheduled, MaintenanceRules.ServiceState(20_000, null, null, At(0)));
    }

    [Fact]
    public void Two_reported_issues_in_ninety_days_are_flagged()
    {
        MaintenanceEvent[] events = [new(At(-80), MaintenanceType.Repair, true), new(At(-10), MaintenanceType.Repair, true), new(At(-400), MaintenanceType.Repair, true)];
        Assert.True(MaintenanceRules.HasRepeatedIssues(events, At(0)));
        Assert.False(MaintenanceRules.HasRepeatedIssues(events[1..], At(0)));
    }
}

public sealed class DemandForecasterTests
{
    [Fact]
    public void Refuses_to_forecast_without_enough_history()
    {
        var result = DemandForecaster.Forecast(Enumerable.Repeat(5.0, 10).ToList());
        Assert.False(result.HasEnoughData);
        Assert.Empty(result.Forecast);
        Assert.Contains("at least", result.Note);
    }

    [Fact]
    public void Compares_against_the_baseline_and_picks_the_more_accurate_model()
    {
        // A steady upward trend: Holt's method should beat a flat moving average on held-out weeks.
        var trend = Enumerable.Range(0, 30).Select(i => 10.0 + i).ToList();
        var result = DemandForecaster.Forecast(trend, horizon: 4);

        Assert.True(result.HasEnoughData);
        Assert.Equal(2, result.Evaluations.Count);
        Assert.Equal(DemandForecaster.Holt, result.ChosenMethod);
        Assert.Equal(4, result.Forecast.Count);
        Assert.True(result.Forecast[0] > trend[^1] - 2);
        Assert.Contains("estimates", result.Note);
    }

    [Fact]
    public void Flat_noisy_demand_keeps_the_simple_baseline_when_it_is_as_good()
    {
        var flat = Enumerable.Range(0, 24).Select(i => i % 2 == 0 ? 8.0 : 12.0).ToList();
        var result = DemandForecaster.Forecast(flat);
        var baseline = result.Evaluations.Single(e => e.Method == DemandForecaster.Baseline);
        var holt = result.Evaluations.Single(e => e.Method == DemandForecaster.Holt);
        Assert.Equal(holt.MeanAbsoluteError < baseline.MeanAbsoluteError ? DemandForecaster.Holt : DemandForecaster.Baseline, result.ChosenMethod);
    }
}

public sealed class SentimentAnalyzerTests
{
    [Fact]
    public void Positive_review_with_themes()
    {
        var result = SentimentAnalyzer.Analyse("Great experience", "The car was spotless and the staff were friendly and helpful.");
        Assert.Equal("Positive", result.Label);
        Assert.Contains("Cleanliness", result.PositiveThemes);
        Assert.Contains("Customer service", result.PositiveThemes);
    }

    [Fact]
    public void Negative_review_with_themes()
    {
        var result = SentimentAnalyzer.Analyse("Disappointing", "The car was dirty inside and we waited a long time at the counter.");
        Assert.Equal("Negative", result.Label);
        Assert.Contains("Cleanliness", result.NegativeThemes);
    }

    [Fact]
    public void Negation_flips_the_meaning()
    {
        var result = SentimentAnalyzer.Analyse(null, "The car was not clean.");
        Assert.NotEqual("Positive", result.Label);
        Assert.Contains("Cleanliness", result.NegativeThemes);
    }
}

public sealed class InsightGeneratorTests
{
    private static InsightInputs Inputs(IReadOnlyList<CategoryMetrics> categories, int total = 10, int cancelled = 0) =>
        new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), total, cancelled, categories, [], 0, 0, [], 0, 0, 0, []);

    [Fact]
    public void Says_nothing_when_the_data_does_not_meet_the_thresholds()
    {
        var insights = InsightGenerator.Generate(Inputs([new("SUV", 2, 0, 0.9, 1000m, 1), new("Economy", 1, 0, 0.1, 100m, 1)]));
        Assert.Empty(insights);
    }

    [Fact]
    public void A_utilisation_gap_produces_an_evidenced_insight_with_limitations()
    {
        var insights = InsightGenerator.Generate(Inputs(
        [
            new("SUV", 40, 2, 0.82, 90_000m, 4),
            new("Economy", 25, 1, 0.41, 30_000m, 3),
        ], total: 65, cancelled: 3));

        var gap = Assert.Single(insights, i => i.Area == "Utilisation");
        Assert.Contains("SUV", gap.Title);
        Assert.Contains("82", gap.Finding);
        Assert.Contains("40 bookings", gap.Evidence);
        Assert.Contains("1 Jan 2026", gap.Period);
        Assert.Contains("not its cause", gap.Limitations);
        Assert.False(string.IsNullOrWhiteSpace(gap.SuggestedAction));
        Assert.False(string.IsNullOrWhiteSpace(gap.Confidence));
    }

    [Fact]
    public void A_high_cancellation_rate_is_flagged_only_with_enough_bookings()
    {
        Assert.DoesNotContain(InsightGenerator.Generate(Inputs([], total: 10, cancelled: 5)), i => i.Area.Contains("Cancel"));
        Assert.Contains(InsightGenerator.Generate(Inputs([], total: 100, cancelled: 30)), i => i.Title.Contains("ancel"));
    }
}
