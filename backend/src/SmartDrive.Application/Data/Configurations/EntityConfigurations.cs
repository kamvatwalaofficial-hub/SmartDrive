using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Data.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(u => u.FirstName).HasMaxLength(60).IsRequired();
        b.Property(u => u.LastName).HasMaxLength(60).IsRequired();
        b.HasOne(u => u.Profile).WithOne().HasForeignKey<CustomerProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> b)
    {
        b.HasKey(p => p.UserId);
        b.Property(p => p.AddressLine).HasMaxLength(120);
        b.Property(p => p.Suburb).HasMaxLength(60);
        b.Property(p => p.City).HasMaxLength(60);
        b.Property(p => p.Province).HasMaxLength(40);
        b.Property(p => p.PostalCode).HasMaxLength(10);
    }
}

internal sealed class VehicleCategoryConfiguration : IEntityTypeConfiguration<VehicleCategory>
{
    public void Configure(EntityTypeBuilder<VehicleCategory> b)
    {
        b.Property(c => c.Name).HasMaxLength(60).IsRequired();
        b.Property(c => c.Slug).HasMaxLength(40).IsRequired();
        b.Property(c => c.Description).HasMaxLength(500);
        b.HasIndex(c => c.Name).IsUnique();
        b.HasIndex(c => c.Slug).IsUnique();
    }
}

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.Property(v => v.RegistrationNumber).HasMaxLength(20).IsRequired();
        b.HasIndex(v => v.RegistrationNumber).IsUnique();
        b.Property(v => v.Make).HasMaxLength(60).IsRequired();
        b.Property(v => v.Model).HasMaxLength(60).IsRequired();
        b.Property(v => v.Description).HasMaxLength(2000);
        b.Property(v => v.FuelEconomyLitresPer100Km).HasPrecision(4, 1);
        b.Property(v => v.ExcessMileageRatePerKm).HasPrecision(8, 2);
        b.Property(v => v.VideoUrl).HasMaxLength(500);
        b.Property(v => v.ModelAssetUrl).HasMaxLength(500);
        b.Property(v => v.ModelAttribution).HasMaxLength(300);
        b.Property(v => v.BookingVersion).IsConcurrencyToken();
        b.Ignore(v => v.DisplayName);

        b.HasOne(v => v.Category).WithMany(c => c.Vehicles).HasForeignKey(v => v.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(v => v.Media).WithOne(m => m.Vehicle).HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(v => v.MaintenanceRecords).WithOne(m => m.Vehicle).HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(v => v.Status);
        b.HasIndex(v => v.CategoryId);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Vehicles_Seats", "[Seats] BETWEEN 1 AND 20");
            t.HasCheckConstraint("CK_Vehicles_Doors", "[Doors] BETWEEN 2 AND 6");
            t.HasCheckConstraint("CK_Vehicles_Luggage", "[LuggageCapacity] BETWEEN 0 AND 20");
            t.HasCheckConstraint("CK_Vehicles_DailyRate", "[DailyRate] > 0");
            t.HasCheckConstraint("CK_Vehicles_Deposit", "[DepositAmount] >= 0");
            t.HasCheckConstraint("CK_Vehicles_Comfort", "[ComfortRating] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Vehicles_Year", "[Year] BETWEEN 1990 AND 2100");
            t.HasCheckConstraint("CK_Vehicles_Odometer", "[OdometerKm] >= 0");
        });
    }
}

internal sealed class VehicleMediaConfiguration : IEntityTypeConfiguration<VehicleMedia>
{
    public void Configure(EntityTypeBuilder<VehicleMedia> b)
    {
        b.Property(m => m.Url).HasMaxLength(500).IsRequired();
        b.Property(m => m.AltText).HasMaxLength(200);
        b.Property(m => m.Attribution).HasMaxLength(300);
        b.HasIndex(m => new { m.VehicleId, m.SortOrder });
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.Property(l => l.Name).HasMaxLength(120).IsRequired();
        b.Property(l => l.Code).HasMaxLength(10).IsRequired();
        b.Property(l => l.AddressLine).HasMaxLength(200);
        b.Property(l => l.City).HasMaxLength(60).IsRequired();
        b.Property(l => l.Province).HasMaxLength(40).IsRequired();
        b.HasIndex(l => l.Code).IsUnique();
    }
}

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.Property(x => x.Reference).HasMaxLength(12).IsRequired();
        b.HasIndex(x => x.Reference).IsUnique();
        b.Property(x => x.CancellationReason).HasMaxLength(500);

        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehicle).WithMany(v => v.Bookings).HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PickupLocation).WithMany().HasForeignKey(x => x.PickupLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReturnLocation).WithMany().HasForeignKey(x => x.ReturnLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Charges).WithOne().HasForeignKey(c => c.BookingId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(c => c.BookingId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(c => c.BookingId).OnDelete(DeleteBehavior.Restrict);

        // Availability checks search bookings by vehicle and date range.
        b.HasIndex(x => new { x.VehicleId, x.PickupAt, x.ReturnAt });
        b.HasIndex(x => new { x.CustomerId, x.PickupAt });
        b.HasIndex(x => x.Status);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Bookings_Period", "[ReturnAt] > [PickupAt]");
            t.HasCheckConstraint("CK_Bookings_Days", "[RentalDays] >= 1");
            t.HasCheckConstraint("CK_Bookings_Total", "[TotalAmount] >= 0");
        });
    }
}

internal sealed class BookingChargeConfiguration : IEntityTypeConfiguration<BookingCharge>
{
    public void Configure(EntityTypeBuilder<BookingCharge> b)
    {
        b.Property(c => c.Description).HasMaxLength(200).IsRequired();
    }
}

internal sealed class BookingStatusChangeConfiguration : IEntityTypeConfiguration<BookingStatusChange>
{
    public void Configure(EntityTypeBuilder<BookingStatusChange> b)
    {
        b.Property(c => c.Reason).HasMaxLength(500);
        b.HasIndex(c => new { c.BookingId, c.ChangedAt });
    }
}

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> b)
    {
        b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        b.Property(p => p.Provider).HasMaxLength(40).IsRequired();
        b.Property(p => p.ProviderReference).HasMaxLength(80).IsRequired();
        b.Property(p => p.IdempotencyKey).HasMaxLength(80).IsRequired();

        // The same idempotency key can never record two transactions.
        b.HasIndex(p => p.IdempotencyKey).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "[Amount] >= 0"));
    }
}

internal sealed class MaintenanceRecordConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> b)
    {
        b.Property(m => m.Title).HasMaxLength(120).IsRequired();
        b.Property(m => m.Description).HasMaxLength(2000);
        b.HasIndex(m => new { m.VehicleId, m.StartAt });
        b.HasIndex(m => m.Status);
    }
}

internal sealed class MileageRecordConfiguration : IEntityTypeConfiguration<MileageRecord>
{
    public void Configure(EntityTypeBuilder<MileageRecord> b)
    {
        b.HasOne<Vehicle>().WithMany().HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Booking>().WithMany().HasForeignKey(m => m.BookingId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(m => new { m.VehicleId, m.RecordedAt });
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.Property(r => r.Title).HasMaxLength(120).IsRequired();
        b.Property(r => r.Comment).HasMaxLength(2000).IsRequired();
        b.Property(r => r.ModerationNote).HasMaxLength(500);
        b.Property(r => r.SentimentLabel).HasMaxLength(20);
        b.Property(r => r.SentimentScore).HasPrecision(4, 2);
        b.Property(r => r.SentimentMethod).HasMaxLength(40);

        // One review per rental.
        b.HasIndex(r => r.BookingId).IsUnique();
        b.HasIndex(r => new { r.VehicleId, r.Status });

        b.HasOne(r => r.Booking).WithOne(x => x.Review).HasForeignKey<Review>(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Vehicle).WithMany().HasForeignKey(r => r.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(r => r.Reports).WithOne().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Reviews_Overall", "[Overall] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Reviews_Condition", "[Condition] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Reviews_Cleanliness", "[Cleanliness] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Reviews_Comfort", "[Comfort] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Reviews_Value", "[Value] BETWEEN 1 AND 5");
        });
    }
}

internal sealed class ReviewReportConfiguration : IEntityTypeConfiguration<ReviewReport>
{
    public void Configure(EntityTypeBuilder<ReviewReport> b)
    {
        b.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(r => new { r.ReviewId, r.ReporterId }).IsUnique();
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(n => n.Title).HasMaxLength(120).IsRequired();
        b.Property(n => n.Message).HasMaxLength(500).IsRequired();
        b.Property(n => n.Link).HasMaxLength(200);
        b.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(a => a.Action).HasMaxLength(60).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(60).IsRequired();
        b.Property(a => a.Details).HasMaxLength(2000);
        b.HasIndex(a => a.CreatedAt);
        b.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}

internal sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> b)
    {
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Email).HasMaxLength(200).IsRequired();
        b.Property(c => c.Subject).HasMaxLength(150).IsRequired();
        b.Property(c => c.Message).HasMaxLength(4000).IsRequired();
        b.HasIndex(c => c.CreatedAt);
    }
}

internal sealed class RecommendationLogConfiguration : IEntityTypeConfiguration<RecommendationLog>
{
    public void Configure(EntityTypeBuilder<RecommendationLog> b)
    {
        b.Property(r => r.PreferencesJson).HasMaxLength(4000).IsRequired();
        b.Property(r => r.Source).HasMaxLength(20).IsRequired();
        b.HasIndex(r => r.CreatedAt);
    }
}
