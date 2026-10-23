namespace SmartDrive.Domain.Entities;

public class VehicleCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }

    public List<Vehicle> Vehicles { get; set; } = [];
}

public class Vehicle
{
    public Guid Id { get; set; }
    public string RegistrationNumber { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public int Year { get; set; }

    public int CategoryId { get; set; }
    public VehicleCategory? Category { get; set; }

    public string Description { get; set; } = "";
    public int Seats { get; set; }
    public int Doors { get; set; }

    /// <summary>Number of large (check-in size) suitcases the boot holds.</summary>
    public int LuggageCapacity { get; set; }

    public TransmissionType Transmission { get; set; }
    public FuelType FuelType { get; set; }

    /// <summary>Combined fuel consumption in litres per 100 km, only when a reliable figure is recorded.</summary>
    public decimal? FuelEconomyLitresPer100Km { get; set; }

    /// <summary>Staff-assigned comfort rating from 1 (basic) to 5 (premium).</summary>
    public int ComfortRating { get; set; } = 3;

    public List<string> Features { get; set; } = [];

    // Pricing (ZAR, VAT inclusive)
    public decimal DailyRate { get; set; }
    public int? DailyMileageLimitKm { get; set; }
    public decimal? ExcessMileageRatePerKm { get; set; }
    public decimal DepositAmount { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Available;

    // Fleet health
    public int OdometerKm { get; set; }
    public int ServiceIntervalKm { get; set; } = 15000;
    public int? NextServiceDueKm { get; set; }
    public DateTime? NextServiceDueDate { get; set; }

    // Rich media references (files live in storage, not in SQL Server)
    public string? VideoUrl { get; set; }
    public string? ModelAssetUrl { get; set; }
    public bool ModelHasInterior { get; set; }
    public string? ModelAttribution { get; set; }

    public bool IsSampleData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token. Every booking change for this vehicle increments it, so two
    /// requests racing for the same dates cannot both succeed.
    /// </summary>
    public int BookingVersion { get; set; }

    public List<VehicleMedia> Media { get; set; } = [];
    public List<Booking> Bookings { get; set; } = [];
    public List<MaintenanceRecord> MaintenanceRecords { get; set; } = [];

    public string DisplayName => $"{Year} {Make} {Model}";
}

public class VehicleMedia
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public MediaKind Kind { get; set; }
    public string Url { get; set; } = "";
    public string AltText { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }

    /// <summary>Credit or licence note, e.g. "Original illustration (sample media)".</summary>
    public string? Attribution { get; set; }

    public bool IsSampleData { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string AddressLine { get; set; } = "";
    public string City { get; set; } = "";
    public string Province { get; set; } = "";
    public bool IsAirport { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MaintenanceRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public MaintenanceType Type { get; set; }
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Scheduled;
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Downtime window. A null end means the vehicle is out of service until the record is completed.</summary>
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    public int? OdometerKm { get; set; }
    public decimal? Cost { get; set; }

    /// <summary>True when the record was raised because of a reported fault rather than routine work.</summary>
    public bool IsReportedIssue { get; set; }

    public bool IsSampleData { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MileageRecord
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public Guid? BookingId { get; set; }
    public int OdometerKm { get; set; }
    public MileageSource Source { get; set; }
    public DateTime RecordedAt { get; set; }
}
