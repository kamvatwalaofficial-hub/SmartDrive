using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDrive.Application.Ai;
using SmartDrive.Application.Data;
using SmartDrive.Application.Identity;
using SmartDrive.Application.Services;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.UnitTests;

/// <summary>A clock the tests control. Monday 1 June 2026, 08:00 UTC (10:00 in South Africa) by default.</summary>
public sealed class FixedClock(DateTimeOffset? now = null) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now ?? new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    public DateTime UtcNow => Now.UtcDateTime;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// A real relational database for service tests: SQLite in memory (one shared connection), or a temporary
/// file when a test needs several connections at once (the concurrency tests).
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection? shared;
    private readonly string? file;

    public TestDatabase(bool useFile = false)
    {
        if (useFile)
        {
            file = Path.Combine(Path.GetTempPath(), $"smartdrive-test-{Guid.NewGuid():N}.db");
        }
        else
        {
            shared = new SqliteConnection("Data Source=:memory:");
            shared.Open();
        }

        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public SmartDriveDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<SmartDriveDbContext>();
        if (shared is not null)
        {
            builder.UseSqlite(shared);
        }
        else
        {
            builder.UseSqlite($"Data Source={file};Pooling=False;Default Timeout=30");
        }

        return new SmartDriveDbContext(builder.Options);
    }

    public void Dispose()
    {
        shared?.Dispose();
        if (file is not null && File.Exists(file))
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(file); } catch (IOException) { /* best effort */ }
        }
    }
}

/// <summary>Builds the services exactly as the API does, around one DbContext.</summary>
public sealed class ServiceSet(SmartDriveDbContext db, FixedClock clock, RentalPolicy? policy = null)
{
    public RentalPolicy Policy { get; } = policy ?? new RentalPolicy();

    public AvailabilityService Availability => new(db, Policy, clock);

    public BookingService Bookings => new(db, Availability, Policy, clock, new AuditWriter(db, clock), new Notifier(db, clock), NullLogger<BookingService>.Instance);

    public ReviewService Reviews => new(db, Policy, clock, new AuditWriter(db, clock), new Notifier(db, clock));

    public CatalogService Catalog => new(db, Availability, Policy);

    public FleetAdminService Fleet => new(db, Availability, Catalog,
        new LocalMediaStorage(new MediaStorageOptions { RootPath = Path.Combine(Path.GetTempPath(), "smartdrive-test-media") }),
        new MediaStorageOptions(), clock, new AuditWriter(db, clock));

    public AnalyticsService Analytics => new(db, Availability, Fleet, clock);

    public RecommendationService Recommendations => new(db, Catalog, Availability, Policy, new RuleBasedPreferenceInterpreter(), clock);
}

/// <summary>Small fixtures: a category, two branches in different cities, vehicles and customers.</summary>
public static class Seed
{
    public sealed record World(VehicleCategory Category, Location Johannesburg, Location CapeTown);

    public static World Basics(SmartDriveDbContext db)
    {
        var category = new VehicleCategory { Name = "SUV", Slug = "suv", Description = "SUVs", SortOrder = 1 };
        var hatch = new VehicleCategory { Name = "Hatchback", Slug = "hatchback", Description = "Hatchbacks", SortOrder = 2 };
        var jhb = new Location { Name = "OR Tambo", Code = "JNB", AddressLine = "Airport", City = "Johannesburg", Province = "Gauteng", IsAirport = true };
        var cpt = new Location { Name = "Cape Town Airport", Code = "CPT", AddressLine = "Airport", City = "Cape Town", Province = "Western Cape", IsAirport = true };
        db.AddRange(category, hatch, jhb, cpt);
        db.SaveChanges();
        return new World(category, jhb, cpt);
    }

    public static Vehicle Vehicle(SmartDriveDbContext db, VehicleCategory category, decimal dailyRate = 900m, int seats = 5, int luggage = 3,
        string model = "Corolla Cross", TransmissionType transmission = TransmissionType.Automatic, int comfort = 4)
    {
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            RegistrationNumber = $"TEST {Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()} GP",
            Make = "Toyota",
            Model = model,
            Year = 2024,
            CategoryId = category.Id,
            Description = "Test vehicle",
            Seats = seats,
            Doors = 5,
            LuggageCapacity = luggage,
            Transmission = transmission,
            FuelType = FuelType.Petrol,
            ComfortRating = comfort,
            DailyRate = dailyRate,
            DepositAmount = 5000m,
            OdometerKm = 20000,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Vehicles.Add(vehicle);
        db.SaveChanges();
        return vehicle;
    }

    public static AppUser Customer(SmartDriveDbContext db, string first = "Thandi", bool isActive = true)
    {
        var email = $"{first.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com";
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FirstName = first,
            LastName = "Tester",
            IsActive = isActive,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    /// <summary>A booking inserted directly (bypassing the service) for setting up history.</summary>
    public static Booking Booking(SmartDriveDbContext db, Vehicle vehicle, AppUser customer, Location location, DateTime pickup, DateTime ret,
        BookingStatus status, decimal? total = null)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            Reference = BookingReference.Generate(Random.Shared),
            CustomerId = customer.Id,
            VehicleId = vehicle.Id,
            PickupLocationId = location.Id,
            ReturnLocationId = location.Id,
            PickupAt = pickup,
            ReturnAt = ret,
            RentalDays = Math.Max(1, (int)Math.Ceiling((ret - pickup).TotalDays)),
            DailyRate = vehicle.DailyRate,
            TotalAmount = total ?? vehicle.DailyRate * Math.Max(1, (int)Math.Ceiling((ret - pickup).TotalDays)),
            Status = status,
            PaymentStatus = status is BookingStatus.Pending ? PaymentStatus.Unpaid : PaymentStatus.Paid,
            CreatedAt = pickup.AddDays(-7),
            UpdatedAt = pickup.AddDays(-7),
        };
        db.Bookings.Add(booking);
        db.SaveChanges();
        return booking;
    }
}

public abstract class TestBase
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A UTC instant relative to the default clock.</summary>
    protected static DateTime At(int days, int hours = 0) => new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(days).AddHours(hours);

    protected static DateTimeOffset Sast(int days, int hours = 0) => new DateTimeOffset(At(days, hours)).ToOffset(TimeSpan.FromHours(2));
}
