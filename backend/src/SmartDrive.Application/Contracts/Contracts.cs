namespace SmartDrive.Application.Contracts;

// ======================================================================= common

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public static (int Page, int PageSize) Normalise(int page, int pageSize, int max = 100) =>
        (Math.Max(1, page), Math.Clamp(pageSize <= 0 ? 12 : pageSize, 1, max));
}

// ======================================================================= catalogue

public sealed record CategoryDto(int Id, string Name, string Slug, string Description, int VehicleCount);

public sealed record LocationDto(int Id, string Name, string Code, string AddressLine, string City, string Province, bool IsAirport);

public sealed record MediaDto(Guid Id, string Kind, string Url, string AltText, bool IsPrimary, int SortOrder, string? Attribution, bool IsSampleData);

public sealed record RatingSummaryDto(
    double? Average,
    int Count,
    IReadOnlyDictionary<int, int> Distribution,
    double? Condition,
    double? Cleanliness,
    double? Comfort,
    double? Value);

public sealed record VehicleSummaryDto(
    Guid Id,
    string Make,
    string Model,
    int Year,
    string DisplayName,
    string Category,
    string CategorySlug,
    int Seats,
    int Doors,
    int LuggageCapacity,
    string Transmission,
    string FuelType,
    decimal DailyRate,
    string? ImageUrl,
    string? ImageAlt,
    double? AverageRating,
    int ReviewCount,
    string CurrentState,
    bool? AvailableForPeriod,
    bool Has3DModel,
    bool IsSampleData);

public sealed record VehicleDetailDto(
    VehicleSummaryDto Summary,
    string Description,
    IReadOnlyList<string> Features,
    decimal? FuelEconomyLitresPer100Km,
    int ComfortRating,
    int? DailyMileageLimitKm,
    decimal? ExcessMileageRatePerKm,
    decimal DepositAmount,
    IReadOnlyList<MediaDto> Media,
    string? VideoUrl,
    string? ModelAssetUrl,
    bool ModelHasInterior,
    string? ModelAttribution,
    RatingSummaryDto Ratings);

public sealed record VehicleSearchQuery
{
    public string? Q { get; init; }
    public string? Category { get; init; }
    public string? Transmission { get; init; }
    public string? Fuel { get; init; }
    public int? MinSeats { get; init; }
    public int? MinLuggage { get; init; }
    public decimal? MaxDailyRate { get; init; }
    public DateTimeOffset? PickupAt { get; init; }
    public DateTimeOffset? ReturnAt { get; init; }
    public bool AvailableOnly { get; init; }

    /// <summary>recommended | price-asc | price-desc | rating | newest | seats</summary>
    public string Sort { get; init; } = "recommended";

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
}

// ======================================================================= availability and pricing

public sealed record AvailabilityDto(bool IsAvailable, IReadOnlyList<string> Reasons, DateTimeOffset? NextAvailableFrom);

public sealed record QuoteRequest(Guid VehicleId, DateTimeOffset PickupAt, DateTimeOffset ReturnAt, int PickupLocationId, int ReturnLocationId);

public sealed record PriceLineDto(string Type, string Description, decimal Amount);

public sealed record QuoteDto(
    Guid VehicleId,
    string VehicleName,
    bool IsAvailable,
    IReadOnlyList<string> UnavailableReasons,
    int RentalDays,
    decimal DailyRate,
    IReadOnlyList<PriceLineDto> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal VatIncluded,
    decimal Deposit,
    DateTimeOffset PickupAt,
    DateTimeOffset ReturnAt,
    string PickupLocation,
    string ReturnLocation,
    string Currency);

// ======================================================================= bookings

public sealed record CreateBookingRequest(Guid VehicleId, DateTimeOffset PickupAt, DateTimeOffset ReturnAt, int PickupLocationId, int ReturnLocationId);

public sealed record BookingSummaryDto(
    Guid Id,
    string Reference,
    Guid VehicleId,
    string VehicleName,
    string? VehicleImageUrl,
    DateTimeOffset PickupAt,
    DateTimeOffset ReturnAt,
    string PickupLocation,
    string ReturnLocation,
    int RentalDays,
    decimal TotalAmount,
    string Status,
    string PaymentStatus,
    DateTimeOffset? HoldExpiresAt,
    bool CanCancel,
    bool CanPay,
    bool CanReview,
    bool HasReview,
    DateTimeOffset CreatedAt,
    string? CustomerName,
    string? CustomerEmail,
    bool IsSampleData);

public sealed record StatusChangeDto(string? From, string To, DateTimeOffset At, string? Reason);

public sealed record PaymentDto(Guid Id, string Kind, decimal Amount, string Currency, string Provider, string Reference, string Status, bool IsSimulated, DateTimeOffset CreatedAt);

public sealed record CancellationPreviewDto(bool CanCancel, decimal Fee, decimal Refund, string Explanation);

public sealed record BookingDetailDto(
    BookingSummaryDto Summary,
    decimal DailyRate,
    IReadOnlyList<PriceLineDto> Lines,
    decimal DiscountAmount,
    decimal VatAmount,
    decimal DepositAmount,
    decimal CancellationFee,
    string? CancellationReason,
    int? PickupOdometerKm,
    int? ReturnOdometerKm,
    IReadOnlyList<StatusChangeDto> StatusHistory,
    IReadOnlyList<PaymentDto> Payments,
    CancellationPreviewDto Cancellation,
    IReadOnlyList<string> AllowedTransitions);

public sealed record CancelBookingRequest(string? Reason);

public sealed record SimulatedPaymentRequest(bool SimulateFailure = false);

public sealed record ChangeBookingStatusRequest(string Status, string? Reason, int? OdometerKm);

public sealed record AdminBookingQuery
{
    public string? Q { get; init; }
    public string? Status { get; init; }
    public Guid? VehicleId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

// ======================================================================= reviews

public sealed record ReviewDto(
    Guid Id,
    Guid VehicleId,
    string VehicleName,
    string AuthorName,
    int Overall,
    int Condition,
    int Cleanliness,
    int Comfort,
    int Value,
    string Title,
    string Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool IsMine,
    bool CanEdit,
    bool IsSampleData);

public sealed record ReviewInput(int Overall, int Condition, int Cleanliness, int Comfort, int Value, string Title, string Comment);

public sealed record ReportReviewRequest(string Reason);

public sealed record ModerateReviewRequest(string Action, string? Note);

public sealed record AdminReviewDto(
    ReviewDto Review,
    string Status,
    string CustomerName,
    string BookingReference,
    int ReportCount,
    IReadOnlyList<string> ReportReasons,
    string? SentimentLabel,
    decimal? SentimentScore,
    IReadOnlyList<string> PositiveThemes,
    IReadOnlyList<string> NegativeThemes,
    string? ModerationNote);

public sealed record ReviewQuery
{
    public Guid? VehicleId { get; init; }
    public int? Rating { get; init; }

    /// <summary>newest | oldest | highest | lowest</summary>
    public string Sort { get; init; } = "newest";

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

// ======================================================================= recommendations

public sealed record RecommendationRequest
{
    public DateTimeOffset PickupAt { get; init; }
    public DateTimeOffset ReturnAt { get; init; }
    public int? PickupLocationId { get; init; }
    public int? ReturnLocationId { get; init; }
    public int? Passengers { get; init; }
    public int? LuggageBags { get; init; }
    public decimal? MaxDailyBudget { get; init; }
    public decimal? MaxTotalBudget { get; init; }
    public string? PreferredCategory { get; init; }
    public string? Transmission { get; init; }
    public bool TransmissionRequired { get; init; }
    public string? Fuel { get; init; }
    public string? Journey { get; init; }
    public bool PrioritiseComfort { get; init; }
    public bool PrioritiseFuelEfficiency { get; init; }
    public bool PrioritiseSpace { get; init; }
}

public sealed record ScoreComponentDto(string Name, double Weight, double Score);

public sealed record RecommendedVehicleDto(
    VehicleSummaryDto Vehicle,
    int Score,
    IReadOnlyList<ScoreComponentDto> Components,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> TradeOffs,
    string Summary,
    bool IsAvailable,
    DateTimeOffset? NextAvailableFrom,
    int RentalDays,
    decimal EstimatedTotal,
    decimal EffectiveDailyRate,
    bool IsAlternative);

public sealed record RecommendationResponseDto(
    IReadOnlyList<RecommendedVehicleDto> Matches,
    IReadOnlyList<RecommendedVehicleDto> Alternatives,
    IReadOnlyList<string> UnmetRequirements,
    IReadOnlyList<string> Suggestions,
    int FleetSize,
    int EligibleCount,
    string Algorithm);

public sealed record InterpretRequest(string Text);

public sealed record InterpretedPreferencesDto(
    int? Passengers,
    int? LuggageBags,
    int? DurationDays,
    decimal? MaxDailyBudget,
    decimal? MaxTotalBudget,
    string? PreferredCategory,
    string? Transmission,
    bool TransmissionRequired,
    string? Fuel,
    string? Journey,
    bool PrioritiseComfort,
    bool PrioritiseFuelEfficiency,
    bool PrioritiseSpace,
    IReadOnlyList<string> Understood,
    IReadOnlyList<string> Notes,
    string Source);

// ======================================================================= accounts

public sealed record RegisterRequest(string FirstName, string LastName, string Email, string Password, string? PhoneNumber);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(Guid Id, string Email, string FirstName, string LastName, string? PhoneNumber, IReadOnlyList<string> Roles);

public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

public sealed record ProfileDto(
    UserDto User,
    string? AddressLine,
    string? Suburb,
    string? City,
    string? Province,
    string? PostalCode,
    DateOnly? DateOfBirth,
    DateOnly? DriverLicenceExpiry);

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? AddressLine,
    string? Suburb,
    string? City,
    string? Province,
    string? PostalCode,
    DateOnly? DateOfBirth,
    DateOnly? DriverLicenceExpiry);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record NotificationDto(Guid Id, string Title, string Message, string? Link, bool IsRead, DateTimeOffset CreatedAt);

public sealed record ContactRequest(string Name, string Email, string Subject, string Message);

// ======================================================================= administration

public sealed record UpsertVehicleRequest(
    string RegistrationNumber,
    string Make,
    string Model,
    int Year,
    int CategoryId,
    string Description,
    int Seats,
    int Doors,
    int LuggageCapacity,
    string Transmission,
    string FuelType,
    decimal? FuelEconomyLitresPer100Km,
    int ComfortRating,
    IReadOnlyList<string>? Features,
    decimal DailyRate,
    int? DailyMileageLimitKm,
    decimal? ExcessMileageRatePerKm,
    decimal DepositAmount,
    int OdometerKm,
    int ServiceIntervalKm,
    int? NextServiceDueKm,
    DateTimeOffset? NextServiceDueDate,
    string? VideoUrl);

public sealed record UpdateVehicleStatusRequest(string Status);

public sealed record AdminVehicleDto(
    VehicleSummaryDto Summary,
    string RegistrationNumber,
    string Status,
    int OdometerKm,
    int ServiceIntervalKm,
    int? NextServiceDueKm,
    DateTimeOffset? NextServiceDueDate,
    string ServiceState,
    int UpcomingBookings,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

public sealed record AdminVehicleQuery
{
    public string? Q { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
    public bool IncludeArchived { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record MaintenanceDto(
    Guid Id,
    Guid VehicleId,
    string VehicleName,
    string RegistrationNumber,
    string Type,
    string Status,
    string Title,
    string? Description,
    DateTimeOffset StartAt,
    DateTimeOffset? EndAt,
    int? OdometerKm,
    decimal? Cost,
    bool IsReportedIssue,
    bool IsSampleData);

public sealed record UpsertMaintenanceRequest(
    Guid VehicleId,
    string Type,
    string Title,
    string? Description,
    DateTimeOffset StartAt,
    DateTimeOffset? EndAt,
    int? OdometerKm,
    decimal? Cost,
    bool IsReportedIssue);

public sealed record CompleteMaintenanceRequest(
    DateTimeOffset? CompletedAt,
    int? OdometerKm,
    decimal? Cost,
    int? NextServiceDueKm,
    DateTimeOffset? NextServiceDueDate);

public sealed record MaintenanceAlertDto(
    Guid VehicleId,
    string VehicleName,
    string RegistrationNumber,
    string ServiceState,
    int OdometerKm,
    int? NextServiceDueKm,
    DateTimeOffset? NextServiceDueDate,
    int? KmRemaining,
    int? DaysRemaining,
    bool HasRepeatedIssues,
    int RecentIssueCount);

public sealed record VehicleHistoryDto(
    IReadOnlyList<BookingSummaryDto> Bookings,
    IReadOnlyList<MaintenanceDto> Maintenance,
    IReadOnlyList<MileagePointDto> Mileage);

public sealed record MileagePointDto(DateTimeOffset RecordedAt, int OdometerKm, string Source);

public sealed record AdminCustomerDto(
    Guid Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    bool IsActive,
    IReadOnlyList<string> Roles,
    int BookingCount,
    decimal TotalSpend,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsSampleData);

public sealed record UpdateCustomerStatusRequest(bool IsActive);

public sealed record UpdateRoleRequest(string Role, bool Grant);

public sealed record AuditLogDto(long Id, string? UserEmail, string Action, string EntityType, string EntityId, string? Details, DateTimeOffset CreatedAt);

public sealed record ContactMessageDto(Guid Id, string Name, string Email, string Subject, string Message, bool IsHandled, DateTimeOffset CreatedAt);

public sealed record UpdateMessageRequest(bool IsHandled);
