using SmartDrive.Domain.Analytics;

namespace SmartDrive.Application.Contracts;

public sealed record AnalyticsQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Category slug, or null for the whole fleet.</summary>
    public string? Category { get; init; }
}

public sealed record AnalyticsPeriodDto(DateOnly From, DateOnly To, string? Category, string Label);

public sealed record CountPointDto(string Label, double Value);

public sealed record SeriesPointDto(string Label, IReadOnlyDictionary<string, double> Values);

public sealed record DashboardDto(
    int TotalVehicles,
    int AvailableNow,
    int RentedNow,
    int ReservedNow,
    int InMaintenance,
    int ActiveRentals,
    int PendingBookings,
    int UpcomingPickups,
    int UpcomingReturns,
    int MaintenanceAlerts,
    decimal RevenueThisMonth,
    decimal RevenueLastMonth,
    double FleetUtilisationLast30Days,
    double? AverageRating,
    int ReviewCount,
    int ReportedReviews,
    IReadOnlyList<CountPointDto> WeeklyBookings,
    IReadOnlyList<BookingSummaryDto> NextPickups,
    IReadOnlyList<BookingSummaryDto> NextReturns,
    bool ContainsSampleData);

public sealed record DemandDto(
    AnalyticsPeriodDto Period,
    int TotalBookings,
    double AverageRentalDays,
    IReadOnlyList<SeriesPointDto> WeeklyByCategory,
    IReadOnlyList<CountPointDto> ByCategory,
    IReadOnlyList<CountPointDto> PickupsByWeekday,
    IReadOnlyList<CountPointDto> Monthly,
    int WeekdayPickups,
    int WeekendPickups,
    string Definition);

public sealed record UtilisationDto(
    AnalyticsPeriodDto Period,
    double FleetUtilisation,
    IReadOnlyList<CategoryUtilisationDto> Categories,
    IReadOnlyList<VehicleUtilisation> Vehicles,
    string Definition);

public sealed record CategoryUtilisationDto(string Category, int Vehicles, double BookedDays, double AvailableDays, double Utilisation);

public sealed record RevenueDto(
    AnalyticsPeriodDto Period,
    decimal RealisedRevenue,
    decimal BookedRevenue,
    decimal CancellationFees,
    decimal DiscountsGiven,
    decimal AverageRentalValue,
    double CancellationRate,
    int Bookings,
    int Cancellations,
    IReadOnlyList<SeriesPointDto> Monthly,
    IReadOnlyList<CountPointDto> ByCategory,
    IReadOnlyList<VehicleRevenueDto> TopVehicles,
    string Definition);

public sealed record VehicleRevenueDto(Guid VehicleId, string Vehicle, string Category, decimal Revenue, int Rentals, decimal RevenuePerRental);

public sealed record SentimentDto(
    AnalyticsPeriodDto Period,
    int ReviewsAnalysed,
    IReadOnlyList<CountPointDto> Labels,
    IReadOnlyList<ThemeCount> Themes,
    IReadOnlyList<SentimentExampleDto> NegativeExamples,
    IReadOnlyList<SentimentExampleDto> PositiveExamples,
    string Method,
    string Disclaimer);

public sealed record SentimentExampleDto(Guid ReviewId, string Vehicle, int Overall, string Title, string Excerpt, string Label, IReadOnlyList<string> Themes, DateTimeOffset CreatedAt);

public sealed record ForecastDto(
    string? Category,
    IReadOnlyList<CountPointDto> History,
    IReadOnlyList<CountPointDto> Forecast,
    DemandForecast Result,
    string Disclaimer);

public sealed record InsightsDto(AnalyticsPeriodDto Period, IReadOnlyList<Insight> Insights, int TotalBookings, string Notice);
