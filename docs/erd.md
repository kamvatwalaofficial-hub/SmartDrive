# Data model

The schema is normalised: reference data (categories, branches) is separate from vehicles; prices are copied onto each booking at the time it is made, with an itemised `BookingCharges` table, so later rate changes never rewrite history. Identity tables (`AspNetUsers`, `AspNetRoles`, …) hold accounts; the user's optional profile is a one-to-one `CustomerProfiles` row.

Every table that the seeder can populate has an `IsSampleData` flag so generated rows are always distinguishable from real ones.

```mermaid
erDiagram
  AspNetUsers ||--o| CustomerProfiles : "has profile"
  AspNetUsers ||--o{ Bookings : "makes"
  AspNetUsers ||--o{ Reviews : "writes"
  AspNetUsers ||--o{ ReviewReports : "files"
  AspNetUsers ||--o{ Notifications : "receives"
  AspNetUsers ||--o{ AuditLogs : "performs"

  VehicleCategories ||--o{ Vehicles : "groups"
  Vehicles ||--o{ VehicleMedia : "has"
  Vehicles ||--o{ Bookings : "is booked in"
  Vehicles ||--o{ MaintenanceRecords : "is serviced in"
  Vehicles ||--o{ MileageRecords : "logs"
  Locations ||--o{ Bookings : "pickup"
  Locations ||--o{ Bookings : "return"

  Bookings ||--|{ BookingCharges : "itemised as"
  Bookings ||--|{ BookingStatusChanges : "history"
  Bookings ||--o{ PaymentTransactions : "paid by"
  Bookings ||--o| Reviews : "reviewed in"
  Reviews ||--o{ ReviewReports : "reported in"

  AspNetUsers {
    guid Id PK
    string Email UK
    string FirstName
    string LastName
    string PhoneNumber
    bool IsActive
    bool IsSampleData
    datetime CreatedAt
    datetime LastLoginAt
  }
  CustomerProfiles {
    guid UserId PK, FK
    string AddressLine
    string Suburb
    string City
    string Province
    string PostalCode
    date DateOfBirth
    date DriverLicenceExpiry
  }
  VehicleCategories {
    int Id PK
    string Name
    string Slug UK
    string Description
    int SortOrder
  }
  Vehicles {
    guid Id PK
    string RegistrationNumber UK
    string Make
    string Model
    int Year
    int CategoryId FK
    int Seats
    int Doors
    int LuggageCapacity
    string Transmission
    string FuelType
    decimal FuelEconomyLitresPer100Km
    int ComfortRating
    json Features
    decimal DailyRate
    int DailyMileageLimitKm
    decimal ExcessMileageRatePerKm
    decimal DepositAmount
    string Status
    int OdometerKm
    int ServiceIntervalKm
    int NextServiceDueKm
    datetime NextServiceDueDate
    string ModelAssetUrl
    int BookingVersion "concurrency token"
    datetime ArchivedAt
    bool IsSampleData
  }
  VehicleMedia {
    guid Id PK
    guid VehicleId FK
    string Kind
    string Url
    string AltText
    bool IsPrimary
    string Attribution
  }
  Locations {
    int Id PK
    string Code UK
    string Name
    string AddressLine
    string City
    string Province
    bool IsAirport
    bool IsActive
  }
  Bookings {
    guid Id PK
    string Reference UK "SD-XXXXXX"
    guid CustomerId FK
    guid VehicleId FK
    int PickupLocationId FK
    int ReturnLocationId FK
    datetime PickupAt
    datetime ReturnAt
    int RentalDays
    decimal DailyRate
    decimal DiscountAmount
    decimal TotalAmount
    decimal VatAmount
    decimal DepositAmount
    decimal CancellationFee
    string Status
    string PaymentStatus
    datetime HoldExpiresAt
    int PickupOdometerKm
    int ReturnOdometerKm
    bool IsSampleData
  }
  BookingCharges {
    guid Id PK
    guid BookingId FK
    string Type
    string Description
    decimal Amount
  }
  BookingStatusChanges {
    guid Id PK
    guid BookingId FK
    string FromStatus
    string ToStatus
    guid ChangedByUserId
    string Reason
    datetime ChangedAt
  }
  PaymentTransactions {
    guid Id PK
    guid BookingId FK
    string Kind
    decimal Amount
    string Currency
    string Provider
    string ProviderReference
    string Status
    string IdempotencyKey UK
    bool IsSimulated
  }
  MaintenanceRecords {
    guid Id PK
    guid VehicleId FK
    string Type
    string Status
    string Title
    datetime StartAt
    datetime EndAt
    int OdometerKm
    decimal Cost
    bool IsReportedIssue
  }
  MileageRecords {
    guid Id PK
    guid VehicleId FK
    guid BookingId
    int OdometerKm
    string Source
    datetime RecordedAt
  }
  Reviews {
    guid Id PK
    guid BookingId FK, UK
    guid VehicleId FK
    guid CustomerId FK
    int Overall
    int Condition
    int Cleanliness
    int Comfort
    int Value
    string Title
    string Comment
    string Status
    string SentimentLabel
    decimal SentimentScore
    json PositiveThemes
    json NegativeThemes
  }
  ReviewReports {
    guid Id PK
    guid ReviewId FK
    guid ReporterId FK
    string Reason
  }
  Notifications {
    guid Id PK
    guid UserId FK
    string Title
    string Message
    string Link
    bool IsRead
  }
  AuditLogs {
    long Id PK
    guid UserId
    string Action
    string EntityType
    string EntityId
    string Details
    datetime CreatedAt
  }
  ContactMessages {
    guid Id PK
    string Name
    string Email
    string Subject
    string Message
    bool IsHandled
  }
  RecommendationLogs {
    guid Id PK
    guid UserId
    string PreferencesJson
    int MatchCount
    string Source
  }
```

## Notes

- **Enums are stored as strings** for readable data and safe reordering.
- **One review per booking** is enforced by a unique index on `Reviews.BookingId`; eligibility (completed rental, by the same customer, within 90 days) is enforced in the service.
- **Payment idempotency** is enforced by a unique index on `PaymentTransactions.IdempotencyKey`.
- **Double-booking protection** relies on `Vehicles.BookingVersion`, an optimistic concurrency token updated with every booking change for that vehicle.
- `RecommendationLogs` stores the preferences of a recommendation request (no personal data) for later tuning.
- Exact indexes and lengths are in `backend/src/SmartDrive.Application/Data/Configurations/EntityConfigurations.cs` and the generated migration.
