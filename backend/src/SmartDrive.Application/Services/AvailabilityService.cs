using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Data;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Services;

/// <summary>Bookings and maintenance windows that may affect a set of vehicles in a time window.</summary>
public sealed class SlotSet(
    IReadOnlyDictionary<Guid, List<BookingSlot>> bookings,
    IReadOnlyDictionary<Guid, List<MaintenanceSlot>> maintenance)
{
    public IReadOnlyList<BookingSlot> Bookings(Guid vehicleId) =>
        bookings.TryGetValue(vehicleId, out var list) ? list : [];

    public IReadOnlyList<MaintenanceSlot> Maintenance(Guid vehicleId) =>
        maintenance.TryGetValue(vehicleId, out var list) ? list : [];
}

/// <summary>
/// The single source of truth for "can this vehicle be rented for these dates?". Shared by the
/// catalogue, the booking workflow (checked on creation, payment and confirmation) and the
/// recommendation engine.
/// </summary>
public sealed class AvailabilityService(SmartDriveDbContext db, RentalPolicy policy, TimeProvider clock)
{
    private static readonly BookingStatus[] PotentiallyBlocking = [BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Active];

    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    /// <summary>Loads everything that could block the given vehicles between the two instants.</summary>
    public async Task<SlotSet> LoadSlotsAsync(IReadOnlyCollection<Guid> vehicleIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var from = fromUtc - policy.Turnaround;
        var to = toUtc + policy.Turnaround;

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => vehicleIds.Contains(b.VehicleId) && PotentiallyBlocking.Contains(b.Status) && b.PickupAt < to && b.ReturnAt > from)
            .Select(b => new { b.VehicleId, Slot = new BookingSlot(b.Id, b.PickupAt, b.ReturnAt, b.Status, b.HoldExpiresAt) })
            .ToListAsync(ct);

        var maintenance = await db.MaintenanceRecords.AsNoTracking()
            .Where(m => vehicleIds.Contains(m.VehicleId) && m.Status != MaintenanceStatus.Cancelled
                        && m.StartAt < to && (m.EndAt == null || m.EndAt > from))
            .Select(m => new { m.VehicleId, Slot = new MaintenanceSlot(m.Id, m.StartAt, m.EndAt, m.Status) })
            .ToListAsync(ct);

        return new SlotSet(
            bookings.GroupBy(x => x.VehicleId).ToDictionary(g => g.Key, g => g.Select(x => x.Slot).ToList()),
            maintenance.GroupBy(x => x.VehicleId).ToDictionary(g => g.Key, g => g.Select(x => x.Slot).ToList()));
    }

    public AvailabilityResult Evaluate(Vehicle vehicle, DateTime pickupUtc, DateTime returnUtc, SlotSet slots, Guid? ignoreBookingId = null) =>
        vehicle.ArchivedAt is not null
            ? new AvailabilityResult(false, [UnavailableReason.Inactive])
            : AvailabilityRules.Evaluate(vehicle.Status, pickupUtc, returnUtc, slots.Bookings(vehicle.Id), slots.Maintenance(vehicle.Id), UtcNow, policy, ignoreBookingId);

    public async Task<AvailabilityResult> CheckAsync(Vehicle vehicle, DateTime pickupUtc, DateTime returnUtc, CancellationToken ct, Guid? ignoreBookingId = null)
    {
        var slots = await LoadSlotsAsync([vehicle.Id], pickupUtc, returnUtc, ct);
        return Evaluate(vehicle, pickupUtc, returnUtc, slots, ignoreBookingId);
    }

    /// <summary>Current state (available / reserved / rented / maintenance / inactive) for each vehicle.</summary>
    public async Task<Dictionary<Guid, VehicleAvailabilityState>> CurrentStatesAsync(IReadOnlyCollection<Vehicle> vehicles, CancellationToken ct)
    {
        var now = UtcNow;
        var slots = await LoadSlotsAsync(vehicles.Select(v => v.Id).ToList(), now.AddDays(-1), now.AddDays(1), ct);
        return vehicles.ToDictionary(
            v => v.Id,
            v => v.ArchivedAt is not null
                ? VehicleAvailabilityState.Inactive
                : AvailabilityRules.CurrentState(v.Status, slots.Bookings(v.Id), slots.Maintenance(v.Id), now));
    }

    /// <summary>Earliest time the vehicle is free for a rental of the same length (searching 30 days ahead).</summary>
    public async Task<DateTime?> NextAvailableAsync(Vehicle vehicle, DateTime pickupUtc, DateTime returnUtc, CancellationToken ct)
    {
        if (vehicle.ArchivedAt is not null)
        {
            return null;
        }

        var duration = returnUtc - pickupUtc;
        var slots = await LoadSlotsAsync([vehicle.Id], pickupUtc, pickupUtc.AddDays(31) + duration, ct);
        return AvailabilityRules.NextAvailableStart(
            vehicle.Status, pickupUtc, duration, slots.Bookings(vehicle.Id).ToList(), slots.Maintenance(vehicle.Id).ToList(), UtcNow, policy);
    }

    public DateTime? NextAvailable(Vehicle vehicle, DateTime pickupUtc, DateTime returnUtc, SlotSet wideSlots) =>
        vehicle.ArchivedAt is not null
            ? null
            : AvailabilityRules.NextAvailableStart(
                vehicle.Status, pickupUtc, returnUtc - pickupUtc,
                wideSlots.Bookings(vehicle.Id).ToList(), wideSlots.Maintenance(vehicle.Id).ToList(), UtcNow, policy);
}
