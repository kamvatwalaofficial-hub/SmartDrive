using SmartDrive.Domain.Entities;

namespace SmartDrive.Domain.Analytics;

public readonly record struct Period(DateTime Start, DateTime End)
{
    public double Days => Math.Max(0, (End - Start).TotalDays);
}

public sealed record UtilisationInput(
    Guid VehicleId,
    string Name,
    string Category,
    DateTime InFleetFrom,
    DateTime? InFleetUntil,
    IReadOnlyList<Period> Rentals,
    IReadOnlyList<Period> Maintenance);

public sealed record VehicleUtilisation(
    Guid VehicleId,
    string Name,
    string Category,
    double FleetDays,
    double MaintenanceDays,
    double AvailableDays,
    double BookedDays,
    double Utilisation);

/// <summary>
/// Utilisation = booked rental days ÷ available fleet days, where available fleet days are the days
/// the vehicle was in the fleet during the period minus days it was out of service for maintenance.
/// Bookings that are confirmed, active or completed count as booked; overlapping intervals are merged
/// so no day is counted twice.
/// </summary>
public static class UtilisationCalculator
{
    public static VehicleUtilisation Calculate(UtilisationInput v, DateTime fromUtc, DateTime toUtc)
    {
        var start = v.InFleetFrom > fromUtc ? v.InFleetFrom : fromUtc;
        var end = v.InFleetUntil is { } until && until < toUtc ? until : toUtc;
        if (end <= start)
        {
            return new VehicleUtilisation(v.VehicleId, v.Name, v.Category, 0, 0, 0, 0, 0);
        }

        var window = new Period(start, end);
        var fleetDays = window.Days;
        var maintenanceDays = MergedDays(v.Maintenance, window);
        var available = Math.Max(0, fleetDays - maintenanceDays);
        var booked = Math.Min(available, MergedDays(v.Rentals, window));
        var utilisation = available > 0 ? booked / available : 0;

        return new VehicleUtilisation(
            v.VehicleId, v.Name, v.Category,
            Math.Round(fleetDays, 2), Math.Round(maintenanceDays, 2), Math.Round(available, 2), Math.Round(booked, 2),
            Math.Round(utilisation, 4));
    }

    /// <summary>Fleet-level utilisation: total booked days ÷ total available days.</summary>
    public static double Aggregate(IEnumerable<VehicleUtilisation> vehicles)
    {
        var list = vehicles.ToList();
        var available = list.Sum(v => v.AvailableDays);
        return available > 0 ? Math.Round(list.Sum(v => v.BookedDays) / available, 4) : 0;
    }

    /// <summary>Total days covered by the intervals inside the window, counting overlaps once.</summary>
    public static double MergedDays(IEnumerable<Period> intervals, Period window)
    {
        var clipped = intervals
            .Select(p => new Period(p.Start < window.Start ? window.Start : p.Start, p.End > window.End ? window.End : p.End))
            .Where(p => p.End > p.Start)
            .OrderBy(p => p.Start)
            .ToList();

        double total = 0;
        DateTime? currentStart = null, currentEnd = null;
        foreach (var p in clipped)
        {
            if (currentEnd is null || p.Start > currentEnd)
            {
                if (currentStart is not null)
                {
                    total += (currentEnd!.Value - currentStart.Value).TotalDays;
                }

                currentStart = p.Start;
                currentEnd = p.End;
            }
            else if (p.End > currentEnd)
            {
                currentEnd = p.End;
            }
        }

        if (currentStart is not null)
        {
            total += (currentEnd!.Value - currentStart.Value).TotalDays;
        }

        return total;
    }
}

public enum ServiceDueState { Ok, DueSoon, Overdue, NotScheduled }

public sealed record MaintenanceEvent(DateTime At, MaintenanceType Type, bool IsReportedIssue);

/// <summary>
/// Deterministic fleet-health rules. These flag vehicles by service schedule and recorded issues;
/// they do not predict mechanical failure.
/// </summary>
public static class MaintenanceRules
{
    public const int DueSoonKm = 1000;
    public const int DueSoonDays = 14;
    public const int RepeatedIssueCount = 2;
    public const int RepeatedIssueWindowDays = 90;

    public static ServiceDueState ServiceState(int odometerKm, int? dueKm, DateTime? dueDateUtc, DateTime nowUtc)
    {
        if (dueKm is null && dueDateUtc is null)
        {
            return ServiceDueState.NotScheduled;
        }

        if ((dueKm is { } km && odometerKm >= km) || (dueDateUtc is { } date && nowUtc >= date))
        {
            return ServiceDueState.Overdue;
        }

        if ((dueKm is { } km2 && km2 - odometerKm <= DueSoonKm) || (dueDateUtc is { } date2 && (date2 - nowUtc).TotalDays <= DueSoonDays))
        {
            return ServiceDueState.DueSoon;
        }

        return ServiceDueState.Ok;
    }

    /// <summary>True when the vehicle had at least two reported issues or repairs in the last 90 days.</summary>
    public static bool HasRepeatedIssues(IEnumerable<MaintenanceEvent> events, DateTime nowUtc) =>
        events.Count(e => (e.IsReportedIssue || e.Type == MaintenanceType.Repair)
                          && e.At >= nowUtc.AddDays(-RepeatedIssueWindowDays) && e.At <= nowUtc) >= RepeatedIssueCount;
}
