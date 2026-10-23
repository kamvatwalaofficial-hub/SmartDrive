using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDrive.Api.Infrastructure;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;
using SmartDrive.Domain.Common;

namespace SmartDrive.Api.Controllers.Admin;

/// <summary>
/// Dashboard metrics and Smart Insights. Every figure is calculated from stored records for the requested
/// period; definitions and limitations are returned with the data.
/// </summary>
[Route("api/admin/analytics")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminAnalyticsController(AnalyticsService analytics) : ApiControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> Dashboard(CancellationToken ct) => analytics.DashboardAsync(ct);

    [HttpGet("demand")]
    public Task<DemandDto> Demand([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.DemandAsync(query, ct);

    [HttpGet("utilisation")]
    public Task<UtilisationDto> Utilisation([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.UtilisationAsync(query, ct);

    [HttpGet("revenue")]
    public Task<RevenueDto> Revenue([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.RevenueAsync(query, ct);

    [HttpGet("sentiment")]
    public Task<SentimentDto> Sentiment([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.SentimentAsync(query, ct);

    /// <summary>Weekly demand forecast, compared against a moving-average baseline on held-out weeks.</summary>
    [HttpGet("forecast")]
    public Task<ForecastDto> Forecast([FromQuery] string? category, [FromQuery] int horizonWeeks = 4, CancellationToken ct = default) =>
        analytics.ForecastAsync(category, Math.Clamp(horizonWeeks, 1, 12), ct);

    [HttpGet("insights")]
    public Task<InsightsDto> Insights([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.InsightsAsync(query, ct);

    /// <summary>Downloads a report as CSV: utilisation, revenue or demand.</summary>
    [HttpGet("export/{report}")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export(string report, [FromQuery] AnalyticsQuery query, CancellationToken ct)
    {
        var csv = new StringBuilder();
        switch (report.ToLowerInvariant())
        {
            case "utilisation":
            {
                var data = await analytics.UtilisationAsync(query, ct);
                Csv.Row(csv, "Vehicle", "Category", "Fleet days", "Maintenance days", "Available days", "Booked days", "Utilisation %");
                foreach (var v in data.Vehicles)
                {
                    Csv.Row(csv, v.Name, v.Category, Num(v.FleetDays), Num(v.MaintenanceDays), Num(v.AvailableDays), Num(v.BookedDays), Num(v.Utilisation * 100));
                }

                break;
            }

            case "revenue":
            {
                var data = await analytics.RevenueAsync(query, ct);
                Csv.Row(csv, "Vehicle", "Category", "Rentals", "Revenue (ZAR)", "Revenue per rental (ZAR)");
                foreach (var v in data.TopVehicles)
                {
                    Csv.Row(csv, v.Vehicle, v.Category, v.Rentals.ToString(CultureInfo.InvariantCulture), Num(v.Revenue), Num(v.RevenuePerRental));
                }

                break;
            }

            case "demand":
            {
                var data = await analytics.DemandAsync(query, ct);
                Csv.Row(csv, "Month", "Bookings");
                foreach (var p in data.Monthly)
                {
                    Csv.Row(csv, p.Label, Num(p.Value));
                }

                break;
            }

            default:
                throw new DomainException("report.unknown", "Report must be 'utilisation', 'revenue' or 'demand'.", ErrorKind.NotFound);
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"smartdrive-{report.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string Num(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    private static string Num(decimal value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);
}

internal static class Csv
{
    /// <summary>Writes one RFC 4180 row. Cells starting with = + - @ are prefixed to stop spreadsheet formula injection.</summary>
    public static void Row(StringBuilder sb, params string?[] cells)
    {
        sb.AppendJoin(',', cells.Select(Escape));
        sb.Append("\r\n");
    }

    private static string Escape(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0]) && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            v = "'" + v;
        }

        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
