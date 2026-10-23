namespace SmartDrive.Domain.Entities;

public class CustomerProfile
{
    public Guid UserId { get; set; }
    public string? AddressLine { get; set; }
    public string? Suburb { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? PostalCode { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DriverLicenceExpiry { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Review
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking? Booking { get; set; }
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public Guid CustomerId { get; set; }

    // Ratings, 1 to 5
    public int Overall { get; set; }
    public int Condition { get; set; }
    public int Cleanliness { get; set; }
    public int Comfort { get; set; }
    public int Value { get; set; }

    public string Title { get; set; } = "";
    public string Comment { get; set; } = "";

    public ReviewStatus Status { get; set; } = ReviewStatus.Published;
    public DateTime? ModeratedAt { get; set; }
    public Guid? ModeratedByUserId { get; set; }
    public string? ModerationNote { get; set; }

    // Automated sentiment (may be wrong; admins always see the original text)
    public string? SentimentLabel { get; set; }
    public decimal? SentimentScore { get; set; }
    public List<string> PositiveThemes { get; set; } = [];
    public List<string> NegativeThemes { get; set; } = [];
    public string? SentimentMethod { get; set; }
    public DateTime? SentimentAnalysedAt { get; set; }

    public bool IsSampleData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ReviewReport> Reports { get; set; } = [];
}

public class ReviewReport
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Guid ReporterId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Who did what to which record. Never stores secrets, tokens or card data.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ContactMessage
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsHandled { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>An anonymised record of a recommendation request (no personal data) for later tuning.</summary>
public class RecommendationLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string PreferencesJson { get; set; } = "";
    public int MatchCount { get; set; }
    public int AlternativeCount { get; set; }
    public string Source { get; set; } = "wizard";
    public DateTime CreatedAt { get; set; }
}
