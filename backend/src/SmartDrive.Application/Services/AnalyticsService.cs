using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Domain.Analytics;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Services;

/// <summary>
/// Dashboard metrics and Smart Insights, always computed from persisted records.
///
/// Definitions:
/// <list type="bullet">
/// <item><b>Demand</b> counts bookings by pickup date (local SAST), excluding rejected bookings and
///   expired unpaid holds.</item>
/// <item><b>Realised revenue</b> is the total of completed rentals plus retained cancellation fees, by
///   pickup date. <b>Booked revenue</b> is confirmed or active rentals not yet completed.</item>
/// <item><b>Utilisation</b>: see <see cref="UtilisationCalculator"/>.</item>
/// </list>
/// </summary>
public sealed class AnalyticsService(
    SmartDriveDbContext db,
    AvailabilityService availability,
    FleetAdminService fleet,
    TimeProvider clock)
{
    private static readonly BookingStatus[] Countable = [BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Active, BookingStatus.Completed, BookingStatus.Cancelled];
    private static readonly BookingStatus[] Occupying = [BookingStatus.Confirmed, BookingStatus.Active, BookingStatus.Completed];

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ================================================================== dashboard

    public async Task<DashboardDto> DashboardAsync(CancellationToken ct)
    {
        var now = Now;
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.ArchivedAt == null && v.Status != VehicleStatus.Inactive).ToListAsync(ct);
        var states = await availability.CurrentStatesAsync(vehicles, ct);

        var weekAhead = now.AddDays(7);
        var activeRentals = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Active, ct);
        var pending = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending && b.HoldExpiresAt > now, ct);
        var upcomingPickups = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Confirmed && b.PickupAt >= now && b.PickupAt < weekAhead, ct);
        var upcomingReturns = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Active && b.ReturnAt < weekAhead, ct);

        var localToday = SouthAfricanTime.LocalDate(now);
        var monthStart = new DateOnly(localToday.Year, localToday.Month, 1);
        var revenueThisMonth = await RealisedRevenueAsync(monthStart, monthStart.AddMonths(1), ct);
        var revenueLastMonth = await RealisedRevenueAsync(monthStart.AddMonths(-1), monthStart, ct);

        var utilisation = await UtilisationAsync(new AnalyticsQuery { From = localToday.AddDays(-30), To = localToday }, ct);

        var ratings = await db.Reviews.AsNoTracking()
            .Where(r => r.Status == ReviewStatus.Published || r.Status == ReviewStatus.Reported)
            .Select(r => r.Overall).ToListAsync(ct);
        var reported = await db.Reviews.CountAsync(r => r.Status == ReviewStatus.Reported, ct);
        var alerts = await fleet.AlertsAsync(ct);

        // Weekly bookings for the last 12 weeks (by booking creation date).
        var since = now.AddDays(-7 * 12);
        var created = await db.Bookings.AsNoTracking().Where(b => b.CreatedAt >= since && b.Status != BookingStatus.Rejected)
            .Select(b => b.CreatedAt).ToListAsync(ct);
        var weekly = Enumerable.Range(0, 12).Select(i =>
        {
            var start = WeekStart(SouthAfricanTime.LocalDate(now)).AddDays(-7 * (11 - i));
            var count = created.Count(c => WeekStart(SouthAfricanTime.LocalDate(c)) == start);
            return new CountPointDto(start.ToString("d MMM", CultureInfo.InvariantCulture), count);
        }).ToList();

        var nextPickups = await NextBookingsAsync(b => b.Status == BookingStatus.Confirmed && b.PickupAt >= now, b => b.PickupAt, ct);
        var nextReturns = await NextBookingsAsync(b => b.Status == BookingStatus.Active, b => b.ReturnAt, ct);

        return new DashboardDto(
            vehicles.Count,
            states.Values.Count(s => s == VehicleAvailabilityState.Available),
            states.Values.Count(s => s == VehicleAvailabilityState.Rented),
            states.Values.Count(s => s == VehicleAvailabilityState.Reserved),
            states.Values.Count(s => s == VehicleAvailabilityState.UnderMaintenance),
            activeRentals,
            pending,
            upcomingPickups,
            upcomingReturns,
            alerts.Count,
            revenueThisMonth,
            revenueLastMonth,
            utilisation.FleetUtilisation,
            ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2),
            ratings.Count,
            reported,
            weekly,
            nextPickups,
            nextReturns,
            await db.Bookings.AnyAsync(b => b.IsSampleData, ct));
    }

    // ================================================================== demand

    public async Task<DemandDto> DemandAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var period = ResolvePeriod(q);
        var rows = await BookingsInPeriodAsync(period, q.Category, ct);
        var demand = rows.Where(r => r.Status != BookingStatus.Cancelled).ToList();

        var categories = rows.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();
        var weeks = new List<DateOnly>();
        for (var w = WeekStart(period.From); w <= period.To; w = w.AddDays(7))
        {
            weeks.Add(w);
        }

        var weekly = weeks.Select(w => new SeriesPointDto(
            w.ToString("d MMM", CultureInfo.InvariantCulture),
            categories.ToDictionary(c => c, c => (double)demand.Count(r => r.Category == c && WeekStart(r.PickupLocalDate) == w)))).ToList();

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var byWeekday = weekdays.Select(d => new CountPointDto(d.ToString()[..3], demand.Count(r => r.PickupLocalDate.DayOfWeek == d))).ToList();
        var weekend = demand.Count(r => r.PickupLocalDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

        var monthly = demand.GroupBy(r => new DateOnly(r.PickupLocalDate.Year, r.PickupLocalDate.Month, 1)).OrderBy(g => g.Key)
            .Select(g => new CountPointDto(g.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture), g.Count())).ToList();

        return new DemandDto(
            ToDto(period, q.Category),
            demand.Count,
            demand.Count == 0 ? 0 : Math.Round(demand.Average(r => r.RentalDays), 1),
            weekly,
            demand.GroupBy(r => r.Category).OrderByDescending(g => g.Count()).Select(g => new CountPointDto(g.Key, g.Count())).ToList(),
            byWeekday,
            monthly,
            demand.Count - weekend,
            weekend,
            "Bookings counted by pickup date in South African time. Cancelled and rejected bookings and expired unpaid holds are excluded.");
    }

    // ================================================================== utilisation

    public async Task<UtilisationDto> UtilisationAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var period = ResolvePeriod(q);
        var (fromUtc, toUtc) = Utc(period);

        var vehicles = await db.Vehicles.AsNoTracking().Include(v => v.Category)
            .Where(v => v.CreatedAt < toUtc && (v.ArchivedAt == null || v.ArchivedAt > fromUtc))
            .Where(v => q.Category == null || v.Category!.Slug == q.Category)
            .ToListAsync(ct);
        var ids = vehicles.Select(v => v.Id).ToList();

        var rentals = await db.Bookings.AsNoTracking()
            .Where(b => ids.Contains(b.VehicleId) && Occupying.Contains(b.Status) && b.PickupAt < toUtc && b.ReturnAt > fromUtc)
            .Select(b => new { b.VehicleId, b.PickupAt, b.ReturnAt }).ToListAsync(ct);
        var maintenance = await db.MaintenanceRecords.AsNoTracking()
            .Where(m => ids.Contains(m.VehicleId) && m.Status != MaintenanceStatus.Cancelled && m.StartAt < toUtc && (m.EndAt == null || m.EndAt > fromUtc))
            .Select(m => new { m.VehicleId, m.StartAt, m.EndAt }).ToListAsync(ct);

        var results = vehicles.Select(v => UtilisationCalculator.Calculate(
            new UtilisationInput(
                v.Id, $"{v.DisplayName} ({v.RegistrationNumber})", v.Category!.Name, v.CreatedAt, v.ArchivedAt,
                rentals.Where(r => r.VehicleId == v.Id).Select(r => new Period(r.PickupAt, r.ReturnAt)).ToList(),
                maintenance.Where(m => m.VehicleId == v.Id).Select(m => new Period(m.StartAt, m.EndAt ?? toUtc)).ToList()),
            fromUtc, toUtc)).ToList();

        var categories = results.GroupBy(r => r.Category).Select(g => new CategoryUtilisationDto(
            g.Key, g.Count(), Math.Round(g.Sum(x => x.BookedDays), 1), Math.Round(g.Sum(x => x.AvailableDays), 1), UtilisationCalculator.Aggregate(g)))
            .OrderByDescending(c => c.Utilisation).ToList();

        return new UtilisationDto(
            ToDto(period, q.Category),
            UtilisationCalculator.Aggregate(results),
            categories,
            results.OrderByDescending(r => r.Utilisation).ToList(),
            "Utilisation = booked rental days ÷ available fleet days. Available days are days in the fleet during the period minus maintenance downtime. Confirmed, active and completed rentals count as booked; overlapping periods are counted once.");
    }

    // ================================================================== revenue

    public async Task<RevenueDto> RevenueAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var period = ResolvePeriod(q);
        var rows = await BookingsInPeriodAsync(period, q.Category, ct);

        decimal Realised(BookingRow r) => r.Status == BookingStatus.Completed ? r.Total : r.Status == BookingStatus.Cancelled ? r.CancellationFee : 0m;
        decimal Booked(BookingRow r) => r.Status is BookingStatus.Confirmed or BookingStatus.Active ? r.Total : 0m;

        var completed = rows.Where(r => r.Status == BookingStatus.Completed).ToList();
        var cancellations = rows.Count(r => r.Status == BookingStatus.Cancelled);
        var decided = rows.Count(r => r.Status != BookingStatus.Pending);

        var monthly = rows.GroupBy(r => new DateOnly(r.PickupLocalDate.Year, r.PickupLocalDate.Month, 1)).OrderBy(g => g.Key)
            .Select(g => new SeriesPointDto(g.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture), new Dictionary<string, double>
            {
                ["Realised"] = (double)g.Sum(Realised),
                ["Booked"] = (double)g.Sum(Booked),
            })).ToList();

        var topVehicles = completed.GroupBy(r => new { r.VehicleId, r.Vehicle, r.Category })
            .Select(g => new VehicleRevenueDto(g.Key.VehicleId, g.Key.Vehicle, g.Key.Category, g.Sum(x => x.Total), g.Count(), Money.Round(g.Sum(x => x.Total) / g.Count())))
            .OrderByDescending(v => v.Revenue).Take(10).ToList();

        return new RevenueDto(
            ToDto(period, q.Category),
            rows.Sum(Realised),
            rows.Sum(Booked),
            rows.Where(r => r.Status == BookingStatus.Cancelled).Sum(r => r.CancellationFee),
            completed.Sum(r => r.Discount),
            completed.Count == 0 ? 0 : Money.Round(completed.Average(r => r.Total)),
            decided == 0 ? 0 : Math.Round((double)cancellations / decided, 4),
            rows.Count,
            cancellations,
            monthly,
            rows.GroupBy(r => r.Category).Select(g => new CountPointDto(g.Key, (double)g.Sum(Realised))).OrderByDescending(c => c.Value).ToList(),
            topVehicles,
            "Realised revenue = completed rentals plus retained cancellation fees, by pickup date (VAT inclusive). Booked revenue = confirmed and active rentals not yet completed. Payments are simulated in this demonstration.");
    }

    // ================================================================== review sentiment

    public async Task<SentimentDto> SentimentAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var period = ResolvePeriod(q);
        var (fromUtc, toUtc) = Utc(period);
        var reviews = await db.Reviews.AsNoTracking().Include(r => r.Vehicle).ThenInclude(v => v!.Category)
            .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt < toUtc && r.Status != ReviewStatus.Removed)
            .Where(r => q.Category == null || r.Vehicle!.Category!.Slug == q.Category)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var themes = SentimentAnalyzer.Themes.Keys.Select(t => new ThemeCount(
            t, reviews.Count(r => r.PositiveThemes.Contains(t)), reviews.Count(r => r.NegativeThemes.Contains(t))))
            .OrderByDescending(t => t.Negative + t.Positive).ToList();

        SentimentExampleDto Example(Review r) => new(
            r.Id, r.Vehicle?.DisplayName ?? "", r.Overall, r.Title,
            r.Comment.Length > 220 ? r.Comment[..220] + "…" : r.Comment,
            r.SentimentLabel ?? "Unknown", [.. r.NegativeThemes, .. r.PositiveThemes], Mapping.Local(r.CreatedAt));

        return new SentimentDto(
            ToDto(period, q.Category),
            reviews.Count,
            reviews.GroupBy(r => r.SentimentLabel ?? "Unknown").Select(g => new CountPointDto(g.Key, g.Count())).OrderByDescending(c => c.Value).ToList(),
            themes,
            reviews.Where(r => r.SentimentLabel == "Negative" || r.NegativeThemes.Count > 0).Take(8).Select(Example).ToList(),
            reviews.Where(r => r.SentimentLabel == "Positive").Take(5).Select(Example).ToList(),
            SentimentAnalyzer.Method,
            "Sentiment and themes are detected automatically by a word-list method and can be wrong (for example with sarcasm or mixed opinions). Always read the original review before acting.");
    }

    // ================================================================== forecast

    public async Task<ForecastDto> ForecastAsync(string? category, int horizonWeeks, CancellationToken ct)
    {
        var now = Now;
        var thisWeek = WeekStart(SouthAfricanTime.LocalDate(now));
        var firstWeek = thisWeek.AddDays(-7 * 52);
        var (fromUtc, _) = Utc((firstWeek, thisWeek));

        var pickups = await db.Bookings.AsNoTracking()
            .Where(b => b.PickupAt >= fromUtc && b.PickupAt < now && Countable.Contains(b.Status) && b.Status != BookingStatus.Cancelled)
            .Where(b => category == null || b.Vehicle!.Category!.Slug == category)
            .Select(b => new { b.PickupAt, b.Status, b.HoldExpiresAt })
            .ToListAsync(ct);
        var counted = pickups.Where(p => p.Status != BookingStatus.Pending).Select(p => WeekStart(SouthAfricanTime.LocalDate(p.PickupAt))).ToList();

        // Only complete weeks, starting from the first week with any bookings.
        var weeks = new List<DateOnly>();
        for (var w = firstWeek; w < thisWeek; w = w.AddDays(7))
        {
            weeks.Add(w);
        }

        var firstData = counted.Count == 0 ? thisWeek : counted.Min();
        weeks = weeks.Where(w => w >= firstData).ToList();
        var series = weeks.Select(w => (double)counted.Count(c => c == w)).ToList();
        var result = DemandForecaster.Forecast(series, Math.Clamp(horizonWeeks, 1, 12));

        return new ForecastDto(
            category,
            weeks.Select((w, i) => new CountPointDto(w.ToString("d MMM yyyy", CultureInfo.InvariantCulture), series[i])).ToList(),
            result.Forecast.Select((v, i) => new CountPointDto(thisWeek.AddDays(7 * i).ToString("d MMM yyyy", CultureInfo.InvariantCulture), v)).ToList(),
            result,
            "Forecasts are statistical estimates from past bookings, not guaranteed bookings or revenue.");
    }

    // ================================================================== insights

    public async Task<InsightsDto> InsightsAsync(AnalyticsQuery q, CancellationToken ct)
    {
        var period = ResolvePeriod(q);
        var rows = await BookingsInPeriodAsync(period, q.Category, ct);
        var utilisation = await UtilisationAsync(q, ct);
        var sentiment = await SentimentAsync(q, ct);
        var alerts = await fleet.AlertsAsync(ct);

        var categories = rows.GroupBy(r => r.Category).Select(g => new CategoryMetrics(
            g.Key,
            g.Count(r => r.Status != BookingStatus.Cancelled),
            g.Count(r => r.Status == BookingStatus.Cancelled),
            utilisation.Categories.FirstOrDefault(c => c.Category == g.Key)?.Utilisation ?? 0,
            g.Where(r => r.Status == BookingStatus.Completed).Sum(r => r.Total),
            utilisation.Categories.FirstOrDefault(c => c.Category == g.Key)?.Vehicles ?? 0)).ToList();

        var active = rows.Where(r => r.Status != BookingStatus.Cancelled).ToList();
        var weekend = active.Count(r => r.PickupLocalDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

        var inputs = new InsightInputs(
            period.From, period.To,
            rows.Count(r => r.Status != BookingStatus.Pending),
            rows.Count(r => r.Status == BookingStatus.Cancelled),
            categories,
            utilisation.Vehicles,
            active.Count - weekend,
            weekend,
            sentiment.Themes,
            sentiment.ReviewsAnalysed,
            alerts.Count(a => a.ServiceState == nameof(ServiceDueState.Overdue)),
            alerts.Count(a => a.ServiceState == nameof(ServiceDueState.DueSoon)),
            alerts.Where(a => a.HasRepeatedIssues).Select(a => $"{a.VehicleName} ({a.RegistrationNumber}): {a.RecentIssueCount} issues").ToList());

        return new InsightsDto(
            ToDto(period, q.Category),
            InsightGenerator.Generate(inputs),
            rows.Count,
            "Insights are produced only when the data meets each rule's threshold. They describe patterns in recorded data; they do not prove causes.");
    }

    // ================================================================== helpers

    private sealed record BookingRow(
        Guid VehicleId, string Vehicle, string Category, BookingStatus Status, DateOnly PickupLocalDate, int RentalDays,
        decimal Total, decimal Discount, decimal CancellationFee);

    private async Task<List<BookingRow>> BookingsInPeriodAsync((DateOnly From, DateOnly To) period, string? category, CancellationToken ct)
    {
        var (fromUtc, toUtc) = Utc(period);
        var now = Now;
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.PickupAt >= fromUtc && b.PickupAt < toUtc && Countable.Contains(b.Status))
            .Where(b => category == null || b.Vehicle!.Category!.Slug == category)
            .Select(b => new
            {
                b.VehicleId, b.Vehicle!.Make, b.Vehicle.Model, b.Vehicle.Year, Category = b.Vehicle.Category!.Name, b.Status, b.HoldExpiresAt,
                b.PickupAt, b.RentalDays, b.TotalAmount, b.DiscountAmount, b.CancellationFee,
            })
            .ToListAsync(ct);

        // Unpaid holds that lapsed never became real demand.
        return rows
            .Where(r => r.Status != BookingStatus.Pending || (r.HoldExpiresAt ?? DateTime.MinValue) > now)
            .Select(r => new BookingRow(r.VehicleId, $"{r.Year} {r.Make} {r.Model}", r.Category, r.Status, SouthAfricanTime.LocalDate(r.PickupAt),
                r.RentalDays, r.TotalAmount, r.DiscountAmount, r.CancellationFee))
            .ToList();
    }

    private async Task<decimal> RealisedRevenueAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await BookingsInPeriodAsync((from, to.AddDays(-1)), null, ct);
        return rows.Sum(r => r.Status == BookingStatus.Completed ? r.Total : r.Status == BookingStatus.Cancelled ? r.CancellationFee : 0m);
    }

    private async Task<IReadOnlyList<BookingSummaryDto>> NextBookingsAsync(
        System.Linq.Expressions.Expression<Func<Booking, bool>> filter,
        System.Linq.Expressions.Expression<Func<Booking, DateTime>> order,
        CancellationToken ct)
    {
        var list = await db.Bookings.AsNoTracking().Include(b => b.Vehicle).Include(b => b.PickupLocation).Include(b => b.ReturnLocation)
            .Where(filter).OrderBy(order).Take(6).ToListAsync(ct);
        var ids = list.Select(b => b.CustomerId).Distinct().ToList();
        var customers = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return list.Select(b => new BookingSummaryDto(
            b.Id, b.Reference, b.VehicleId, b.Vehicle?.DisplayName ?? "", null, Mapping.Local(b.PickupAt), Mapping.Local(b.ReturnAt),
            b.PickupLocation?.Name ?? "", b.ReturnLocation?.Name ?? "", b.RentalDays, b.TotalAmount, b.Status.ToString(), b.PaymentStatus.ToString(),
            Mapping.Local(b.HoldExpiresAt), false, false, false, false, Mapping.Local(b.CreatedAt),
            customers.TryGetValue(b.CustomerId, out var c) ? c.FullName : null, c?.Email, b.IsSampleData)).ToList();
    }

    /// <summary>Defaults to the last 90 days; dates are inclusive local (SAST) calendar dates.</summary>
    private (DateOnly From, DateOnly To) ResolvePeriod(AnalyticsQuery q)
    {
        var today = SouthAfricanTime.LocalDate(Now);
        var to = q.To ?? today;
        var from = q.From ?? to.AddDays(-89);
        if (from > to)
        {
            throw new DomainException("analytics.period", "The start date must be on or before the end date.");
        }

        if (to.DayNumber - from.DayNumber > 3 * 366)
        {
            throw new DomainException("analytics.period", "Please choose a period of up to three years.");
        }

        return (from, to);
    }

    /// <summary>Local inclusive dates → UTC half-open interval [from 00:00 SAST, day after "to" 00:00 SAST).</summary>
    private static (DateTime From, DateTime To) Utc((DateOnly From, DateOnly To) period) =>
        (new DateTimeOffset(period.From.ToDateTime(TimeOnly.MinValue), SouthAfricanTime.Offset).UtcDateTime,
         new DateTimeOffset(period.To.AddDays(1).ToDateTime(TimeOnly.MinValue), SouthAfricanTime.Offset).UtcDateTime);

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static AnalyticsPeriodDto ToDto((DateOnly From, DateOnly To) p, string? category) =>
        new(p.From, p.To, category,
            $"{p.From.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} – {p.To.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");
}
