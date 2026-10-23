using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Data;

public class SmartDriveDbContext(DbContextOptions<SmartDriveDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<VehicleCategory> VehicleCategories => Set<VehicleCategory>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleMedia> VehicleMedia => Set<VehicleMedia>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingCharge> BookingCharges => Set<BookingCharge>();
    public DbSet<BookingStatusChange> BookingStatusChanges => Set<BookingStatusChange>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<MileageRecord> MileageRecords => Set<MileageRecord>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<RecommendationLog> RecommendationLogs => Set<RecommendationLog>();

    public bool IsSqlite => Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(SmartDriveDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        // Money: decimal(18,2) on SQL Server. SQLite (used only by the automated tests) cannot order or
        // sum decimals, so there they are stored as REAL.
        if (IsSqlite)
        {
            configuration.Properties<decimal>().HaveConversion<double>();
        }
        else
        {
            configuration.Properties<decimal>().HavePrecision(18, 2);
        }

        // All timestamps are stored in UTC and read back as UTC.
        configuration.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // Enums are stored as readable text.
        configuration.Properties<TransmissionType>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<FuelType>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<VehicleStatus>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<MediaKind>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<BookingStatus>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<PaymentKind>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<PaymentTransactionStatus>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<ChargeType>().HaveConversion<string>().HaveMaxLength(30);
        configuration.Properties<MaintenanceType>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<MaintenanceStatus>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<ReviewStatus>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<MileageSource>().HaveConversion<string>().HaveMaxLength(20);
    }
}

/// <summary>Stores DateTime values as UTC and marks values read from the database as UTC.</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
