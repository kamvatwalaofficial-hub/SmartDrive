using SmartDrive.Application.Contracts;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.UnitTests.Services;

public sealed class ReviewServiceTests : TestBase, IDisposable
{
    private readonly TestDatabase database = new();
    private readonly FixedClock clock = new();

    public void Dispose() => database.Dispose();

    private static ReviewInput Good => new(5, 5, 4, 5, 4, "Great trip", "The car was spotless and very comfortable on the highway.");

    [Fact]
    public async Task Only_the_customer_who_completed_the_rental_can_review_it_once()
    {
        var db = database.NewContext();
        var world = Seed.Basics(db);
        var vehicle = Seed.Vehicle(db, world.Category);
        var customer = Seed.Customer(db);
        var completed = Seed.Booking(db, vehicle, customer, world.Johannesburg, At(-10), At(-7), BookingStatus.Completed);
        var services = new ServiceSet(db, clock);

        var review = await services.Reviews.CreateAsync(completed.Reference, Good, customer.Id, Ct);
        Assert.Equal(5, review.Overall);
        Assert.True(review.IsMine);

        var duplicate = await Assert.ThrowsAsync<DomainException>(() => services.Reviews.CreateAsync(completed.Reference, Good, customer.Id, Ct));
        Assert.Equal("review.alreadyreviewed", duplicate.Code);

        var stranger = Seed.Customer(db, "Other");
        var notYours = await Assert.ThrowsAsync<DomainException>(() => services.Reviews.CreateAsync(completed.Reference, Good, stranger.Id, Ct));
        Assert.Equal(ErrorKind.NotFound, notYours.Kind);
    }

    [Fact]
    public async Task Upcoming_rentals_cannot_be_reviewed()
    {
        var db = database.NewContext();
        var world = Seed.Basics(db);
        var vehicle = Seed.Vehicle(db, world.Category);
        var customer = Seed.Customer(db);
        var upcoming = Seed.Booking(db, vehicle, customer, world.Johannesburg, At(3), At(5), BookingStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<DomainException>(() => new ServiceSet(db, clock).Reviews.CreateAsync(upcoming.Reference, Good, customer.Id, Ct));
        Assert.Equal("review.notcompleted", ex.Code);
    }

    [Fact]
    public async Task Ratings_summary_comes_from_published_reviews()
    {
        var db = database.NewContext();
        var world = Seed.Basics(db);
        var vehicle = Seed.Vehicle(db, world.Category);
        var services = new ServiceSet(db, clock);
        foreach (var (name, rating) in new[] { ("A", 5), ("B", 4), ("C", 3) })
        {
            var customer = Seed.Customer(db, name);
            var booking = Seed.Booking(db, vehicle, customer, world.Johannesburg, At(-20), At(-17), BookingStatus.Completed);
            await services.Reviews.CreateAsync(booking.Reference, Good with { Overall = rating }, customer.Id, Ct);
        }

        var summary = await services.Catalog.RatingSummaryAsync(vehicle.Id, Ct);

        Assert.Equal(3, summary.Count);
        Assert.Equal(4.0, summary.Average);
        Assert.Equal(1, summary.Distribution[5]);
    }
}

public sealed class AnalyticsServiceTests : TestBase, IDisposable
{
    private readonly TestDatabase database = new();
    private readonly FixedClock clock = new();

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task Dashboard_counts_come_from_stored_records()
    {
        var db = database.NewContext();
        var world = Seed.Basics(db);
        var rented = Seed.Vehicle(db, world.Category, model: "Rented");
        var reserved = Seed.Vehicle(db, world.Category, model: "Reserved");
        var idle = Seed.Vehicle(db, world.Category, model: "Idle");
        var customer = Seed.Customer(db);

        Seed.Booking(db, rented, customer, world.Johannesburg, At(-1), At(2), BookingStatus.Active);
        Seed.Booking(db, reserved, customer, world.Johannesburg, At(0, 5), At(3), BookingStatus.Confirmed);
        Seed.Booking(db, idle, customer, world.Johannesburg, At(-20), At(-15), BookingStatus.Completed, total: 4500m);
        var pending = Seed.Booking(db, idle, customer, world.Johannesburg, At(10), At(12), BookingStatus.Pending);
        pending.HoldExpiresAt = At(0, 1);
        db.SaveChanges();

        var dashboard = await new ServiceSet(db, clock).Analytics.DashboardAsync(Ct);

        Assert.Equal(3, dashboard.TotalVehicles);
        Assert.Equal(1, dashboard.RentedNow);
        Assert.Equal(1, dashboard.ReservedNow);
        Assert.Equal(1, dashboard.AvailableNow);
        Assert.Equal(1, dashboard.ActiveRentals);
        Assert.Equal(1, dashboard.PendingBookings);
        Assert.Equal(1, dashboard.UpcomingPickups);
        Assert.Equal(1, dashboard.UpcomingReturns);
        Assert.Equal(12, dashboard.WeeklyBookings.Count);
    }

    [Fact]
    public async Task Utilisation_report_uses_the_documented_definition()
    {
        var db = database.NewContext();
        var world = Seed.Basics(db);
        var vehicle = Seed.Vehicle(db, world.Category);
        var customer = Seed.Customer(db);
        Seed.Booking(db, vehicle, customer, world.Johannesburg, At(-25), At(-15), BookingStatus.Completed);

        var report = await new ServiceSet(db, clock).Analytics.UtilisationAsync(
            new AnalyticsQuery { From = new DateOnly(2026, 5, 1), To = new DateOnly(2026, 5, 31) }, Ct);

        var row = Assert.Single(report.Vehicles);
        Assert.Equal(10, row.BookedDays, precision: 1);
        Assert.True(row.AvailableDays is > 29 and <= 31);
        Assert.False(string.IsNullOrWhiteSpace(report.Definition));
    }
}

public sealed class RecommendationServiceTests : TestBase, IDisposable
{
    private readonly TestDatabase database = new();
    private readonly FixedClock clock = new();

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task A_vehicle_booked_for_the_dates_is_not_recommended_as_available()
    {
        var db = database.NewContext();
        var world = Seed.Basics(db);
        var booked = Seed.Vehicle(db, world.Category, dailyRate: 700m, model: "Booked");
        var free = Seed.Vehicle(db, world.Category, dailyRate: 800m, model: "Free");
        Seed.Booking(db, booked, Seed.Customer(db), world.Johannesburg, At(2), At(6), BookingStatus.Confirmed);

        var result = await new ServiceSet(db, clock).Recommendations.RecommendAsync(
            new RecommendationRequest { PickupAt = Sast(3), ReturnAt = Sast(5), Passengers = 2 }, null, Ct);

        Assert.All(result.Matches, m => Assert.True(m.IsAvailable));
        Assert.Contains(result.Matches, m => m.Vehicle.Id == free.Id);
        Assert.DoesNotContain(result.Matches, m => m.Vehicle.Id == booked.Id);
    }
}
