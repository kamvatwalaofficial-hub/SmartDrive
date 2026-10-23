namespace SmartDrive.Domain.Entities;

public enum TransmissionType { Manual = 1, Automatic = 2 }

public enum FuelType { Petrol = 1, Diesel = 2, Hybrid = 3, Electric = 4 }

/// <summary>
/// Operational status set by staff. Whether a vehicle is reserved or rented at a given moment is
/// <em>derived</em> from its bookings (see <see cref="VehicleAvailabilityState"/>) so it can never
/// go stale.
/// </summary>
public enum VehicleStatus { Available = 1, UnderMaintenance = 2, Inactive = 3 }

/// <summary>A vehicle's current situation, computed from status, bookings and maintenance.</summary>
public enum VehicleAvailabilityState { Available, Reserved, Rented, UnderMaintenance, Inactive }

public enum MediaKind { Exterior = 1, Interior = 2, Dashboard = 3, Boot = 4, Video = 5 }

public enum BookingStatus { Pending = 1, Confirmed = 2, Active = 3, Completed = 4, Cancelled = 5, Rejected = 6 }

public enum PaymentStatus { Unpaid = 1, Paid = 2, Refunded = 3, PartiallyRefunded = 4 }

public enum PaymentKind { Charge = 1, Refund = 2 }

public enum PaymentTransactionStatus { Succeeded = 1, Failed = 2 }

public enum ChargeType { Rental = 1, LongRentalDiscount = 2, OneWayFee = 3, CancellationFee = 4, ExcessMileage = 5 }

public enum MaintenanceType { Service = 1, Repair = 2, Inspection = 3, Tyres = 4, Other = 5 }

public enum MaintenanceStatus { Scheduled = 1, InProgress = 2, Completed = 3, Cancelled = 4 }

public enum ReviewStatus { Published = 1, Reported = 2, Hidden = 3, Removed = 4 }

public enum JourneyType { City = 1, Business = 2, Family = 3, RoadTrip = 4 }

public enum MileageSource { Pickup = 1, Return = 2, Service = 3, Manual = 4 }

public static class Roles
{
    public const string Customer = "Customer";
    public const string Administrator = "Administrator";
}
