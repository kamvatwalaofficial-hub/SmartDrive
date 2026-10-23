using SmartDrive.Domain.Entities;

namespace SmartDrive.Domain.Rules;

/// <summary>The parts of a booking that matter for availability.</summary>
public sealed record BookingSlot(Guid Id, DateTime PickupAt, DateTime ReturnAt, BookingStatus Status, DateTime? HoldExpiresAt);

/// <summary>The parts of a maintenance record that matter for availability.</summary>
public sealed record MaintenanceSlot(Guid Id, DateTime StartAt, DateTime? EndAt, MaintenanceStatus Status);

public enum UnavailableReason
{
    Inactive,
    UnderMaintenance,
    MaintenanceScheduled,
    ConflictingBooking,
}

public sealed record AvailabilityResult(bool IsAvailable, IReadOnlyList<UnavailableReason> Reasons)
{
    public static readonly AvailabilityResult Available = new(true, []);
}

/// <summary>
/// Decides whether a vehicle can be rented for a period. A vehicle is never treated as available
/// just because its status says so: its bookings, maintenance windows and turnaround time are all
/// checked. Used by both the booking flow and the recommendation engine.
/// </summary>
public static class AvailabilityRules
{
    /// <summary>Half-open interval overlap: [aStart, aEnd) intersects [bStart, bEnd).</summary>
    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) =>
        aStart < bEnd && bStart < aEnd;

    /// <summary>
    /// Confirmed and active bookings always block the vehicle. A pending booking blocks it only while
    /// its payment hold is still running, so abandoned checkouts free the car automatically.
    /// </summary>
    public static bool IsBlocking(BookingStatus status, DateTime? holdExpiresAt, DateTime nowUtc) =>
        status is BookingStatus.Confirmed or BookingStatus.Active
        || (status == BookingStatus.Pending && holdExpiresAt is { } expires && expires > nowUtc);

    public static AvailabilityResult Evaluate(
        VehicleStatus status,
        DateTime pickupUtc,
        DateTime returnUtc,
        IEnumerable<BookingSlot> bookings,
        IEnumerable<MaintenanceSlot> maintenance,
        DateTime nowUtc,
        RentalPolicy policy,
        Guid? ignoreBookingId = null)
    {
        var reasons = new List<UnavailableReason>();

        if (status == VehicleStatus.Inactive)
        {
            reasons.Add(UnavailableReason.Inactive);
        }

        foreach (var m in maintenance)
        {
            if (m.Status == MaintenanceStatus.Cancelled)
            {
                continue;
            }

            // A completed record without an end date is treated as finished "now".
            var end = m.EndAt ?? (m.Status == MaintenanceStatus.Completed ? m.StartAt : DateTime.MaxValue);
            if (Overlaps(pickupUtc, returnUtc + policy.Turnaround, m.StartAt, end))
            {
                var ongoing = m.StartAt <= nowUtc && end > nowUtc;
                var reason = ongoing ? UnavailableReason.UnderMaintenance : UnavailableReason.MaintenanceScheduled;
                if (!reasons.Contains(reason))
                {
                    reasons.Add(reason);
                }
            }
        }

        if (status == VehicleStatus.UnderMaintenance && !reasons.Contains(UnavailableReason.UnderMaintenance))
        {
            reasons.Add(UnavailableReason.UnderMaintenance);
        }

        foreach (var b in bookings)
        {
            if (b.Id == ignoreBookingId || !IsBlocking(b.Status, b.HoldExpiresAt, nowUtc))
            {
                continue;
            }

            // Each rental needs the turnaround buffer after it before the next one starts.
            if (Overlaps(pickupUtc, returnUtc + policy.Turnaround, b.PickupAt, b.ReturnAt + policy.Turnaround))
            {
                reasons.Add(UnavailableReason.ConflictingBooking);
                break;
            }
        }

        return reasons.Count == 0 ? AvailabilityResult.Available : new AvailabilityResult(false, reasons);
    }

    /// <summary>
    /// What the vehicle is doing right now: inactive, in the workshop, out on a rental, reserved
    /// for a rental starting within 24 hours, or available.
    /// </summary>
    public static VehicleAvailabilityState CurrentState(
        VehicleStatus status,
        IEnumerable<BookingSlot> bookings,
        IEnumerable<MaintenanceSlot> maintenance,
        DateTime nowUtc)
    {
        if (status == VehicleStatus.Inactive)
        {
            return VehicleAvailabilityState.Inactive;
        }

        var inWorkshop = status == VehicleStatus.UnderMaintenance || maintenance.Any(m =>
            m.Status is MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress
            && m.StartAt <= nowUtc && (m.EndAt ?? DateTime.MaxValue) > nowUtc);
        if (inWorkshop)
        {
            return VehicleAvailabilityState.UnderMaintenance;
        }

        var list = bookings.ToList();
        if (list.Any(b => b.Status == BookingStatus.Active))
        {
            return VehicleAvailabilityState.Rented;
        }

        var reserved = list.Any(b => IsBlocking(b.Status, b.HoldExpiresAt, nowUtc)
                                     && b.PickupAt <= nowUtc.AddHours(24) && b.ReturnAt > nowUtc);
        return reserved ? VehicleAvailabilityState.Reserved : VehicleAvailabilityState.Available;
    }

    /// <summary>
    /// The earliest pickup time at or after <paramref name="fromUtc"/> when the vehicle is free for a
    /// rental of the same length, searching up to <paramref name="searchDays"/> ahead. Candidate start
    /// times are the end of each blocking booking (plus turnaround), the end of each maintenance window,
    /// and the requested time shifted by whole days. Returns null if nothing fits.
    /// </summary>
    public static DateTime? NextAvailableStart(
        VehicleStatus status,
        DateTime fromUtc,
        TimeSpan duration,
        IReadOnlyCollection<BookingSlot> bookings,
        IReadOnlyCollection<MaintenanceSlot> maintenance,
        DateTime nowUtc,
        RentalPolicy policy,
        int searchDays = 30)
    {
        if (status == VehicleStatus.Inactive)
        {
            return null;
        }

        var limit = fromUtc.AddDays(searchDays);
        var candidates = new SortedSet<DateTime> { fromUtc };
        for (var d = 1; d <= searchDays; d++)
        {
            candidates.Add(fromUtc.AddDays(d));
        }

        foreach (var b in bookings.Where(b => IsBlocking(b.Status, b.HoldExpiresAt, nowUtc)))
        {
            candidates.Add(b.ReturnAt + policy.Turnaround);
        }

        foreach (var m in maintenance.Where(m => m.EndAt is not null && m.Status != MaintenanceStatus.Cancelled))
        {
            candidates.Add(m.EndAt!.Value);
        }

        foreach (var start in candidates.Where(c => c >= fromUtc && c <= limit))
        {
            // Status is evaluated per candidate: a vehicle flagged "under maintenance" with an open
            // record becomes available only once that record has an end date.
            var result = Evaluate(status == VehicleStatus.UnderMaintenance ? VehicleStatus.Available : status,
                start, start + duration, bookings, maintenance, nowUtc, policy);
            if (result.IsAvailable)
            {
                return start;
            }
        }

        return null;
    }

    public static string Describe(UnavailableReason reason) => reason switch
    {
        UnavailableReason.Inactive => "This vehicle is no longer offered for rental.",
        UnavailableReason.UnderMaintenance => "This vehicle is currently in for maintenance.",
        UnavailableReason.MaintenanceScheduled => "Maintenance is scheduled during these dates.",
        UnavailableReason.ConflictingBooking => "This vehicle is already booked for part of these dates.",
        _ => "This vehicle is unavailable for these dates.",
    };
}
