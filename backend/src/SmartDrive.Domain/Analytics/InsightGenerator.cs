using System.Globalization;
using SmartDrive.Domain.Common;

namespace SmartDrive.Domain.Analytics;

public sealed record Insight(
    string Area,
    string Severity,
    string Title,
    string Finding,
    string Period,
    string Evidence,
    string SuggestedAction,
    string Confidence,
    string Limitations);

public sealed record CategoryMetrics(string Category, int Bookings, int Cancellations, double Utilisation, decimal Revenue, int Vehicles);

public sealed record ThemeCount(string Theme, int Positive, int Negative);

public sealed record InsightInputs(
    DateOnly From,
    DateOnly To,
    int TotalBookings,
    int CancelledBookings,
    IReadOnlyList<CategoryMetrics> Categories,
    IReadOnlyList<VehicleUtilisation> Vehicles,
    int WeekdayPickups,
    int WeekendPickups,
    IReadOnlyList<ThemeCount> ReviewThemes,
    int ReviewsAnalysed,
    int OverdueServices,
    int ServicesDueSoon,
    IReadOnlyList<string> VehiclesWithRepeatedIssues);

/// <summary>
/// Turns computed metrics into plain-language findings. Every insight states the period, the
/// evidence, a suggested action, a confidence level based on sample size, and its limitations.
/// An insight is only produced when the data meets its threshold; otherwise nothing is said.
/// </summary>
public static class InsightGenerator
{
    public const int MinBookingsPerCategory = 5;
    public const double UtilisationGap = 0.15;
    public const double LowUtilisation = 0.25;
    public const double HighUtilisation = 0.85;
    public const double HighCancellationRate = 0.15;
    public const int MinBookingsForRates = 20;
    public const int MinNegativeMentions = 3;

    public static string ConfidenceFor(int sampleSize) => sampleSize switch
    {
        < 20 => "Low",
        < 100 => "Medium",
        _ => "High",
    };

    public static IReadOnlyList<Insight> Generate(InsightInputs i)
    {
        var insights = new List<Insight>();
        var period = $"{i.From.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} – {i.To.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        const string correlation = " This shows a pattern, not its cause.";

        // Utilisation gap between categories
        var categories = i.Categories.Where(c => c.Bookings >= MinBookingsPerCategory && c.Vehicles > 0).OrderByDescending(c => c.Utilisation).ToList();
        if (categories.Count >= 2)
        {
            var high = categories[0];
            var low = categories[^1];
            if (high.Utilisation - low.Utilisation >= UtilisationGap)
            {
                insights.Add(new Insight(
                    "Utilisation", "attention",
                    $"{high.Category} vehicles are working harder than {low.Category}",
                    $"{high.Category} utilisation was {Pct(high.Utilisation)}, compared with {Pct(low.Utilisation)} for {low.Category}.",
                    period,
                    $"{high.Category}: {high.Bookings} bookings across {high.Vehicles} vehicles. {low.Category}: {low.Bookings} bookings across {low.Vehicles} vehicles.",
                    $"Review fleet allocation between {high.Category} and {low.Category} before buying additional vehicles.",
                    ConfidenceFor(high.Bookings + low.Bookings),
                    "Utilisation counts booked days over available days; it cannot see demand that was turned away when no vehicle was free." + correlation));
            }
        }

        // Individual low and high utilisation vehicles
        var eligibleVehicles = i.Vehicles.Where(v => v.AvailableDays >= 14).ToList();
        var lowVehicles = eligibleVehicles.Where(v => v.Utilisation < LowUtilisation).OrderBy(v => v.Utilisation).Take(3).ToList();
        if (lowVehicles.Count > 0)
        {
            insights.Add(new Insight(
                "Utilisation", "info",
                "Some vehicles are rarely booked",
                $"{lowVehicles.Count} vehicle{(lowVehicles.Count == 1 ? " was" : "s were")} booked for less than {Pct(LowUtilisation)} of their available days.",
                period,
                string.Join("; ", lowVehicles.Select(v => $"{v.Name}: {Pct(v.Utilisation)} ({v.BookedDays:0} of {v.AvailableDays:0} days)")),
                "Consider featuring these vehicles, relocating them to busier branches or reviewing their rates.",
                ConfidenceFor((int)eligibleVehicles.Sum(v => v.AvailableDays) / 7),
                "Low utilisation may be seasonal; compare with the same period last year before acting."));
        }

        var highVehicles = eligibleVehicles.Where(v => v.Utilisation >= HighUtilisation).OrderByDescending(v => v.Utilisation).Take(3).ToList();
        if (highVehicles.Count > 0)
        {
            insights.Add(new Insight(
                "Utilisation", "attention",
                "Some vehicles are close to fully booked",
                $"{highVehicles.Count} vehicle{(highVehicles.Count == 1 ? " was" : "s were")} booked for at least {Pct(HighUtilisation)} of available days.",
                period,
                string.Join("; ", highVehicles.Select(v => $"{v.Name}: {Pct(v.Utilisation)}")),
                "Customers may be turned away for these models; consider whether similar vehicles could absorb the demand.",
                ConfidenceFor((int)eligibleVehicles.Sum(v => v.AvailableDays) / 7),
                "High utilisation also reduces time available for servicing; check maintenance schedules."));
        }

        // Cancellations
        if (i.TotalBookings >= MinBookingsForRates)
        {
            var rate = (double)i.CancelledBookings / i.TotalBookings;
            if (rate >= HighCancellationRate)
            {
                insights.Add(new Insight(
                    "Revenue", "warning",
                    "Cancellation rate is high",
                    $"{Pct(rate)} of bookings in this period were cancelled.",
                    period,
                    $"{i.CancelledBookings} of {i.TotalBookings} bookings.",
                    "Investigate the cancellations: check timing relative to pickup, categories affected and whether prices or availability changed.",
                    ConfidenceFor(i.TotalBookings),
                    "The data shows how many bookings were cancelled, not why." + correlation));
            }
        }

        // Weekend vs weekday demand
        var pickups = i.WeekdayPickups + i.WeekendPickups;
        if (pickups >= MinBookingsForRates && i.WeekdayPickups > 0)
        {
            var weekendPerDay = i.WeekendPickups / 2.0;
            var weekdayPerDay = i.WeekdayPickups / 5.0;
            if (weekendPerDay >= 1.5 * weekdayPerDay || weekdayPerDay >= 1.5 * weekendPerDay)
            {
                var weekendBusier = weekendPerDay > weekdayPerDay;
                insights.Add(new Insight(
                    "Demand", "info",
                    weekendBusier ? "Pickups peak on weekends" : "Pickups peak on weekdays",
                    $"There were on average {(weekendBusier ? weekendPerDay / weekdayPerDay : weekdayPerDay / weekendPerDay):0.0}× as many pickups per {(weekendBusier ? "weekend" : "week")} day.",
                    period,
                    $"{i.WeekendPickups} weekend pickups and {i.WeekdayPickups} weekday pickups.",
                    weekendBusier
                        ? "Plan branch staffing and vehicle preparation around Friday to Sunday."
                        : "Weekday demand suggests business travel; consider corporate offers and early-morning staffing.",
                    ConfidenceFor(pickups),
                    "Counts pickups by local (SAST) day; public holidays are not separated." + correlation));
            }
        }

        // Review themes
        foreach (var theme in i.ReviewThemes.Where(t => t.Negative >= MinNegativeMentions && t.Negative > t.Positive).OrderByDescending(t => t.Negative).Take(2))
        {
            insights.Add(new Insight(
                "Reviews", "attention",
                $"Customers often raise {theme.Theme.ToLowerInvariant()}",
                $"{theme.Theme} was mentioned negatively in {theme.Negative} review{(theme.Negative == 1 ? "" : "s")} and positively in {theme.Positive}.",
                period,
                $"Automated theme detection across {i.ReviewsAnalysed} reviews.",
                $"Read the flagged reviews and check the {theme.Theme.ToLowerInvariant()} process at the relevant branches.",
                ConfidenceFor(i.ReviewsAnalysed),
                "Themes are detected automatically and can be wrong; always read the original reviews before acting."));
        }

        // Fleet health
        if (i.OverdueServices > 0)
        {
            insights.Add(new Insight(
                "Maintenance", "warning",
                "Services are overdue",
                $"{i.OverdueServices} vehicle{(i.OverdueServices == 1 ? " is" : "s are")} past the service due date or mileage.",
                "Now",
                "Based on recorded odometer readings and scheduled service dates.",
                "Schedule these services before the vehicles' next rentals.",
                "High",
                "Accuracy depends on odometer readings being recorded at each pickup and return."));
        }

        if (i.ServicesDueSoon > 0)
        {
            insights.Add(new Insight(
                "Maintenance", "info",
                "Services coming up",
                $"{i.ServicesDueSoon} vehicle{(i.ServicesDueSoon == 1 ? " is" : "s are")} within {MaintenanceRules.DueSoonKm:N0} km or {MaintenanceRules.DueSoonDays} days of a service.",
                "Next 14 days",
                "Based on recorded odometer readings and scheduled service dates.",
                "Book workshop slots now to avoid clashes with upcoming rentals.",
                "High",
                "Mileage-based due dates assume the vehicle keeps its recent usage pattern."));
        }

        if (i.VehiclesWithRepeatedIssues.Count > 0)
        {
            insights.Add(new Insight(
                "Maintenance", "attention",
                "Repeated issues recorded",
                $"{i.VehiclesWithRepeatedIssues.Count} vehicle{(i.VehiclesWithRepeatedIssues.Count == 1 ? " has" : "s have")} had {MaintenanceRules.RepeatedIssueCount} or more repairs or reported issues in {MaintenanceRules.RepeatedIssueWindowDays} days.",
                $"Last {MaintenanceRules.RepeatedIssueWindowDays} days",
                string.Join("; ", i.VehiclesWithRepeatedIssues),
                "Inspect these vehicles for an underlying fault before further rentals.",
                "Medium",
                "This flags a pattern in recorded events; it is not a prediction of mechanical failure."));
        }

        return insights;
    }

    private static string Pct(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    public static string Rand(decimal amount) => Money.Format(amount);
}
