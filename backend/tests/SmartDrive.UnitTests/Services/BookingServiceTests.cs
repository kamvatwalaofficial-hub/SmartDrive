using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.UnitTests.Services;

public sealed class BookingServiceTests : TestBase, IDisposable
{
    private readonly TestDatabase database = new();
    private readonly FixedClock clock = new();

    public void Dispose() => database.Dispose();

    private (ServiceSet Services, Seed.World World, Vehicle Vehicle, Guid Customer) Setup()
    {
        var db = database.NewContext();
        var world = Seed.Basics(db);
        var vehicle = Seed.Vehicle(db, world.Category, dailyRate: 900m);
        var customer = Seed.Customer(db);
        return (new ServiceSet(db, clock), world, vehicle, customer.Id);
    }

    private static CreateBookingRequest Request(Vehicle v, Seed.World w, int fromDay, int toDay, bool oneWay = false) =>
        new(v.Id, Sast(fromDay), Sast(toDay), w.Johannesburg.Id, oneWay ? w.CapeTown.Id : w.Johannesburg.Id);

    [Fact]
    public async Task Creating_a_booking_prices_it_on_the_server_and_holds_the_vehicle()
    {
        var (services, world, vehicle, customer) = Setup();

        var booking = await services.Bookings.CreateAsync(Request(vehicle, world, 2, 5, oneWay: true), customer, Ct);

        Assert.Equal("Pending", booking.Summary.Status);
        Assert.Equal(3, booking.Summary.RentalDays);
        Assert.Equal(3 * 900m + services.Policy.OneWayFee, booking.Summary.TotalAmount);
        Assert.Contains(booking.Lines, l => l.Type == "OneWayFee");
        Assert.NotNull(booking.Summary.HoldExpiresAt);
        Assert.StartsWith("SD-", booking.Summary.Reference);
    }

    [Fact]
    public async Task Overlapping_booking_for_the_same_vehicle_is_refused()
    {
        var (services, world, vehicle, customer) = Setup();
        await services.Bookings.CreateAsync(Request(vehicle, world, 2, 5), customer, Ct);

        var ex = await Assert.ThrowsAsync<DomainException>(() => services.Bookings.CreateAsync(Request(vehicle, world, 4, 7), customer, Ct));

        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        Assert.Equal("booking.unavailable", ex.Code);
    }

    [Fact]
    public async Task An_expired_unpaid_hold_frees_the_vehicle()
    {
        var (services, world, vehicle, customer) = Setup();
        await services.Bookings.CreateAsync(Request(vehicle, world, 2, 5), customer, Ct);

        clock.Advance(TimeSpan.FromMinutes(services.Policy.PendingHoldMinutes + 1));
        var second = await services.Bookings.CreateAsync(Request(vehicle, world, 2, 5), Seed.Customer(database.NewContext(), "Sipho").Id, Ct);

        Assert.Equal("Pending", second.Summary.Status);
    }

    [Fact]
    public async Task Concurrent_requests_for_the_same_dates_produce_exactly_one_booking()
    {
        using var fileDb = new TestDatabase(useFile: true);
        Guid vehicleId, customerId;
        int locationId;
        using (var setup = fileDb.NewContext())
        {
            var world = Seed.Basics(setup);
            vehicleId = Seed.Vehicle(setup, world.Category).Id;
            customerId = Seed.Customer(setup).Id;
            locationId = world.Johannesburg.Id;
        }

        var request = new CreateBookingRequest(vehicleId, Sast(3), Sast(6), locationId, locationId);
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = fileDb.NewContext();
            try
            {
                await new ServiceSet(db, clock).Bookings.CreateAsync(request, customerId, Ct);
                return (Ok: true, Error: (DomainException?)null);
            }
            catch (DomainException ex)
            {
                return (Ok: false, Error: ex);
            }
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.All(results.Where(r => !r.Ok), r => Assert.Equal(ErrorKind.Conflict, r.Error!.Kind));
        await using var check = fileDb.NewContext();
        Assert.Equal(1, await check.Bookings.CountAsync(b => b.VehicleId == vehicleId, Ct));
    }

    [Fact]
    public async Task The_concurrency_token_rejects_a_stale_write()
    {
        var (_, world, vehicle, customer) = Setup();
        await using var first = database.NewContext();
        await using var second = database.NewContext();
        var a = await first.Vehicles.SingleAsync(v => v.Id == vehicle.Id, Ct);
        var b = await second.Vehicles.SingleAsync(v => v.Id == vehicle.Id, Ct);

        a.BookingVersion++;
        await first.SaveChangesAsync(Ct);
        b.BookingVersion++;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Customers_cannot_see_each_others_bookings()
    {
        var (services, world, vehicle, customer) = Setup();
        var booking = await services.Bookings.CreateAsync(Request(vehicle, world, 2, 4), customer, Ct);
        var stranger = Seed.Customer(database.NewContext(), "Lerato").Id;

        var ex = await Assert.ThrowsAsync<DomainException>(() => services.Bookings.DetailAsync(booking.Summary.Reference, stranger, isAdmin: false, Ct));
        Assert.Equal(ErrorKind.NotFound, ex.Kind);

        var asAdmin = await services.Bookings.DetailAsync(booking.Summary.Reference, stranger, isAdmin: true, Ct);
        Assert.Equal(booking.Summary.Reference, asAdmin.Summary.Reference);
    }

    [Fact]
    public async Task Simulated_payment_confirms_once_per_idempotency_key()
    {
        var (services, world, vehicle, customer) = Setup();
        var booking = await services.Bookings.CreateAsync(Request(vehicle, world, 2, 4), customer, Ct);

        var paid = await services.Bookings.PaySimulatedAsync(booking.Summary.Reference, customer, "key-12345678", false, Ct);
        var retried = await services.Bookings.PaySimulatedAsync(booking.Summary.Reference, customer, "key-12345678", false, Ct);

        Assert.Equal("Confirmed", paid.Summary.Status);
        Assert.Equal("Paid", paid.Summary.PaymentStatus);
        Assert.Single(retried.Payments, p => p.Kind == "Charge" && p.Status == "Succeeded");
        Assert.All(retried.Payments, p => Assert.True(p.IsSimulated));
    }

    [Fact]
    public async Task Late_cancellation_charges_the_policy_fee()
    {
        var (services, world, vehicle, customer) = Setup();
        var booking = await services.Bookings.CreateAsync(Request(vehicle, world, 1, 4), customer, Ct);
        await services.Bookings.PaySimulatedAsync(booking.Summary.Reference, customer, "pay-abcdefgh", false, Ct);

        var preview = await services.Bookings.CancellationPreviewAsync(booking.Summary.Reference, customer, false, Ct);
        var cancelled = await services.Bookings.CancelAsync(booking.Summary.Reference, customer, "Change of plans", Ct);

        Assert.Equal(900m, preview.Fee);   // within 48 hours: one day's rental
        Assert.Equal("Cancelled", cancelled.Summary.Status);
        Assert.Equal(900m, cancelled.CancellationFee);
    }

    [Fact]
    public async Task Admin_cannot_jump_a_pending_booking_straight_to_completed()
    {
        var (services, world, vehicle, customer) = Setup();
        var booking = await services.Bookings.CreateAsync(Request(vehicle, world, 2, 4), customer, Ct);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            services.Bookings.ChangeStatusAsync(booking.Summary.Reference, new ChangeBookingStatusRequest("Completed", null, 25000), Guid.NewGuid(), Ct));

        Assert.Equal("booking.invalid_transition", ex.Code);
    }

    [Fact]
    public async Task Inactive_customers_cannot_book()
    {
        var (services, world, vehicle, _) = Setup();
        var inactive = Seed.Customer(database.NewContext(), "Gone", isActive: false);

        var ex = await Assert.ThrowsAsync<DomainException>(() => services.Bookings.CreateAsync(Request(vehicle, world, 2, 4), inactive.Id, Ct));
        Assert.Equal(ErrorKind.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task Quote_reports_unavailability_instead_of_pretending()
    {
        var (services, world, vehicle, customer) = Setup();
        await services.Bookings.CreateAsync(Request(vehicle, world, 2, 5), customer, Ct);

        var quote = await services.Bookings.QuoteAsync(new QuoteRequest(vehicle.Id, Sast(3), Sast(4), world.Johannesburg.Id, world.Johannesburg.Id), Ct);

        Assert.False(quote.IsAvailable);
        Assert.NotEmpty(quote.UnavailableReasons);
    }
}
