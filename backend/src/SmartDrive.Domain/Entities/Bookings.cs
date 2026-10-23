namespace SmartDrive.Domain.Entities;

public class Booking
{
    public Guid Id { get; set; }

    /// <summary>Human-friendly unique reference, e.g. SD-7K4M2Q.</summary>
    public string Reference { get; set; } = "";

    public Guid CustomerId { get; set; }
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int PickupLocationId { get; set; }
    public Location? PickupLocation { get; set; }
    public int ReturnLocationId { get; set; }
    public Location? ReturnLocation { get; set; }

    /// <summary>Stored in UTC; shown in South African time (UTC+02:00).</summary>
    public DateTime PickupAt { get; set; }
    public DateTime ReturnAt { get; set; }

    public int RentalDays { get; set; }

    /// <summary>The daily rate at the time of booking (later price changes do not affect it).</summary>
    public decimal DailyRate { get; set; }

    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal CancellationFee { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    /// <summary>An unpaid pending booking holds the vehicle only until this moment.</summary>
    public DateTime? HoldExpiresAt { get; set; }

    public int? PickupOdometerKm { get; set; }
    public int? ReturnOdometerKm { get; set; }
    public string? CancellationReason { get; set; }

    public bool IsSampleData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<BookingCharge> Charges { get; set; } = [];
    public List<BookingStatusChange> StatusHistory { get; set; } = [];
    public List<PaymentTransaction> Payments { get; set; } = [];
    public Review? Review { get; set; }
}

public class BookingCharge
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public ChargeType Type { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
}

public class BookingStatusChange
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public BookingStatus? FromStatus { get; set; }
    public BookingStatus ToStatus { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// A payment or refund. SmartDrive currently records <em>simulated</em> transactions only
/// (<see cref="IsSimulated"/> = true); no card details are ever collected or stored.
/// </summary>
public class PaymentTransaction
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public PaymentKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Provider { get; set; } = "Simulated";
    public string ProviderReference { get; set; } = "";
    public PaymentTransactionStatus Status { get; set; }

    /// <summary>Client-supplied key that makes retries safe: the same key never charges twice.</summary>
    public string IdempotencyKey { get; set; } = "";

    public bool IsSimulated { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
