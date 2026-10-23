using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.UnitTests.Domain;

public sealed class AvailabilityRulesTests : TestBase
{
    private readonly RentalPolicy policy = new();   // 2-hour turnaround between rentals
    private static readonly MaintenanceSlot[] NoMaintenance = [];

    private static BookingSlot Booking(DateTime pickup, DateTime ret, BookingStatus status = BookingStatus.Confirmed, DateTime? hold = null) =>
        new(Guid.NewGuid(), pickup, ret, status, hold);

    [Fact]
    public void Free_vehicle_is_available()
    {
        var result = AvailabilityRules.Evaluate(VehicleStatus.Available, At(1), At(3), [], NoMaintenance, At(0), policy);
        Assert.True(result.IsAvailable);
    }

    [Fact]
    public void Overlapping_confirmed_booking_blocks_the_vehicle()
    {
        var result = AvailabilityRules.Evaluate(VehicleStatus.Available, At(2), At(5), [Booking(At(1), At(3))], NoMaintenance, At(0), policy);
        Assert.False(result.IsAvailable);
        Assert.Contains(UnavailableReason.ConflictingBooking, result.Reasons);
    }

    [Fact]
    public void Turnaround_time_is_needed_between_rentals()
    {
        var existing = Booking(At(1), At(3));
        Assert.False(AvailabilityRules.Evaluate(VehicleStatus.Available, At(3, 1), At(5), [existing], NoMaintenance, At(0), policy).IsAvailable);
        Assert.True(AvailabilityRules.Evaluate(VehicleStatus.Available, At(3, 2), At(5), [existing], NoMaintenance, At(0), policy).IsAvailable);
    }

    [Fact]
    public void Cancelled_rejected_and_expired_holds_do_not_block()
    {
        BookingSlot[] slots =
        [
            Booking(At(1), At(3), BookingStatus.Cancelled),
            Booking(At(1), At(3), BookingStatus.Rejected),
            Booking(At(1), At(3), BookingStatus.Pending, hold: At(0, -1)),
        ];
        Assert.True(AvailabilityRules.Evaluate(VehicleStatus.Available, At(1), At(3), slots, NoMaintenance, At(0), policy).IsAvailable);
    }

    [Fact]
    public void Unexpired_pending_hold_blocks()
    {
        var hold = Booking(At(1), At(3), BookingStatus.Pending, hold: At(0, 1));
        Assert.False(AvailabilityRules.Evaluate(VehicleStatus.Available, At(1), At(3), [hold], NoMaintenance, At(0), policy).IsAvailable);
    }

    [Fact]
    public void Status_alone_never_makes_a_vehicle_available()
    {
        // The vehicle's status says "Available", but a maintenance window covers the dates.
        MaintenanceSlot[] maintenance = [new(Guid.NewGuid(), At(2), At(4), MaintenanceStatus.Scheduled)];
        var result = AvailabilityRules.Evaluate(VehicleStatus.Available, At(1), At(3), [], maintenance, At(0), policy);
        Assert.False(result.IsAvailable);
        Assert.Contains(UnavailableReason.MaintenanceScheduled, result.Reasons);

        Assert.False(AvailabilityRules.Evaluate(VehicleStatus.Inactive, At(1), At(3), [], NoMaintenance, At(0), policy).IsAvailable);
    }

    [Fact]
    public void Open_ended_maintenance_blocks_until_completed()
    {
        MaintenanceSlot[] open = [new(Guid.NewGuid(), At(-1), null, MaintenanceStatus.InProgress)];
        Assert.False(AvailabilityRules.Evaluate(VehicleStatus.Available, At(20), At(22), [], open, At(0), policy).IsAvailable);
    }

    [Fact]
    public void A_booking_can_be_rechecked_while_ignoring_itself()
    {
        var self = Booking(At(1), At(3));
        Assert.True(AvailabilityRules.Evaluate(VehicleStatus.Available, At(1), At(3), [self], NoMaintenance, At(0), policy, ignoreBookingId: self.Id).IsAvailable);
    }

    [Fact]
    public void Next_available_start_skips_past_the_conflict_and_turnaround()
    {
        var next = AvailabilityRules.NextAvailableStart(VehicleStatus.Available, At(1), TimeSpan.FromDays(2), [Booking(At(1), At(4))], NoMaintenance, At(0), policy);
        Assert.Equal(At(4, 2), next);
    }

    [Fact]
    public void Current_state_is_derived_from_bookings()
    {
        Assert.Equal(VehicleAvailabilityState.Rented,
            AvailabilityRules.CurrentState(VehicleStatus.Available, [Booking(At(-1), At(2), BookingStatus.Active)], NoMaintenance, At(0)));
        Assert.Equal(VehicleAvailabilityState.Reserved,
            AvailabilityRules.CurrentState(VehicleStatus.Available, [Booking(At(0, 6), At(2))], NoMaintenance, At(0)));
        Assert.Equal(VehicleAvailabilityState.Available,
            AvailabilityRules.CurrentState(VehicleStatus.Available, [Booking(At(5), At(7))], NoMaintenance, At(0)));
    }
}
