using FluentValidation;
using SmartDrive.Application.Contracts;

namespace SmartDrive.Api.Validation;

// Shape and range checks for incoming requests. Business rules (availability, eligibility, pricing)
// are enforced by the application services, which never trust the client.

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.");
        RuleFor(x => x.PhoneNumber).MaximumLength(30).Matches(@"^[+0-9 ()-]*$").WithMessage("Enter a valid phone number.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.PhoneNumber).MaximumLength(30).Matches(@"^[+0-9 ()-]*$").WithMessage("Enter a valid phone number.");
        RuleFor(x => x.AddressLine).MaximumLength(200);
        RuleFor(x => x.Suburb).MaximumLength(100);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Province).MaximumLength(100);
        RuleFor(x => x.PostalCode).MaximumLength(10).Matches(@"^[0-9]*$").WithMessage("Postal codes contain digits only.");
        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || d <= DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-18)))
            .WithMessage("Drivers must be at least 18 years old.");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.")
            .NotEqual(x => x.CurrentPassword).WithMessage("Choose a password you have not used here before.");
    }
}

public sealed class ContactRequestValidator : AbstractValidator<ContactRequest>
{
    public ContactRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Message).NotEmpty().MinimumLength(10).MaximumLength(4000);
    }
}

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.PickupLocationId).GreaterThan(0);
        RuleFor(x => x.ReturnLocationId).GreaterThan(0);
        RuleFor(x => x.ReturnAt).GreaterThan(x => x.PickupAt).WithMessage("The return date and time must be after pickup.");
    }
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.PickupLocationId).GreaterThan(0);
        RuleFor(x => x.ReturnLocationId).GreaterThan(0);
        RuleFor(x => x.ReturnAt).GreaterThan(x => x.PickupAt).WithMessage("The return date and time must be after pickup.");
    }
}

public sealed class CancelBookingRequestValidator : AbstractValidator<CancelBookingRequest>
{
    public CancelBookingRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public sealed class ChangeBookingStatusRequestValidator : AbstractValidator<ChangeBookingStatusRequest>
{
    public ChangeBookingStatusRequestValidator()
    {
        RuleFor(x => x.Status).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.OdometerKm).InclusiveBetween(0, 2_000_000);
    }
}

public sealed class ReviewInputValidator : AbstractValidator<ReviewInput>
{
    public ReviewInputValidator()
    {
        RuleFor(x => x.Overall).InclusiveBetween(1, 5);
        RuleFor(x => x.Condition).InclusiveBetween(1, 5);
        RuleFor(x => x.Cleanliness).InclusiveBetween(1, 5);
        RuleFor(x => x.Comfort).InclusiveBetween(1, 5);
        RuleFor(x => x.Value).InclusiveBetween(1, 5);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Comment).NotEmpty().MinimumLength(10).MaximumLength(2000);
    }
}

public sealed class ReportReviewRequestValidator : AbstractValidator<ReportReviewRequest>
{
    public ReportReviewRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MinimumLength(5).MaximumLength(500);
}

public sealed class ModerateReviewRequestValidator : AbstractValidator<ModerateReviewRequest>
{
    public ModerateReviewRequestValidator()
    {
        RuleFor(x => x.Action).NotEmpty().Must(a => a is "publish" or "hide" or "restore" or "approve")
            .WithMessage("Action must be 'publish' or 'hide'.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class RecommendationRequestValidator : AbstractValidator<RecommendationRequest>
{
    public RecommendationRequestValidator()
    {
        RuleFor(x => x.ReturnAt).GreaterThan(x => x.PickupAt).WithMessage("The return date and time must be after pickup.");
        RuleFor(x => x.Passengers).InclusiveBetween(1, 15);
        RuleFor(x => x.LuggageBags).InclusiveBetween(0, 15);
        RuleFor(x => x.MaxDailyBudget).InclusiveBetween(50m, 100_000m);
        RuleFor(x => x.MaxTotalBudget).InclusiveBetween(50m, 1_000_000m);
        RuleFor(x => x.PreferredCategory).MaximumLength(30);
    }
}

public sealed class InterpretRequestValidator : AbstractValidator<InterpretRequest>
{
    public InterpretRequestValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(500);
}

public sealed class UpsertVehicleRequestValidator : AbstractValidator<UpsertVehicleRequest>
{
    public UpsertVehicleRequestValidator()
    {
        RuleFor(x => x.RegistrationNumber).NotEmpty().MaximumLength(20).Matches(@"^[A-Za-z0-9 -]+$")
            .WithMessage("Registration numbers contain letters, digits, spaces and hyphens only.");
        RuleFor(x => x.Make).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Year).InclusiveBetween(1990, DateTime.UtcNow.Year + 1);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Seats).InclusiveBetween(1, 15);
        RuleFor(x => x.Doors).InclusiveBetween(2, 6);
        RuleFor(x => x.LuggageCapacity).InclusiveBetween(0, 15);
        RuleFor(x => x.ComfortRating).InclusiveBetween(1, 5);
        RuleFor(x => x.FuelEconomyLitresPer100Km).InclusiveBetween(1m, 30m);
        RuleFor(x => x.DailyRate).InclusiveBetween(50m, 100_000m);
        RuleFor(x => x.DepositAmount).InclusiveBetween(0m, 1_000_000m);
        RuleFor(x => x.DailyMileageLimitKm).InclusiveBetween(10, 5000);
        RuleFor(x => x.ExcessMileageRatePerKm).InclusiveBetween(0m, 1000m);
        RuleFor(x => x.OdometerKm).InclusiveBetween(0, 2_000_000);
        RuleFor(x => x.ServiceIntervalKm).InclusiveBetween(1000, 100_000);
        RuleFor(x => x.Features).Must(f => f is null || (f.Count <= 30 && f.All(s => s.Length <= 80)))
            .WithMessage("Up to 30 features of at most 80 characters each.");
        RuleFor(x => x.VideoUrl).MaximumLength(500)
            .Must(u => string.IsNullOrWhiteSpace(u) || Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("The video link must be an https URL.");
    }
}

public sealed class UpsertMaintenanceRequestValidator : AbstractValidator<UpsertMaintenanceRequest>
{
    public UpsertMaintenanceRequestValidator()
    {
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.Type).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.EndAt).Must((x, end) => end is null || end > x.StartAt).WithMessage("The end must be after the start.");
        RuleFor(x => x.OdometerKm).InclusiveBetween(0, 2_000_000);
        RuleFor(x => x.Cost).InclusiveBetween(0m, 10_000_000m);
    }
}

public sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator() => RuleFor(x => x.Role).NotEmpty().Must(r => r is "Customer" or "Administrator")
        .WithMessage("Role must be 'Customer' or 'Administrator'.");
}

public sealed class UpsertCategoryRequestValidator : AbstractValidator<SmartDrive.Application.Services.UpsertCategoryRequest>
{
    public UpsertCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(40).Matches("^[a-z0-9-]+$").WithMessage("Use lowercase letters, digits and hyphens.");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
    }
}

public sealed class UpsertLocationRequestValidator : AbstractValidator<SmartDrive.Application.Services.UpsertLocationRequest>
{
    public UpsertLocationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10).Matches("^[A-Za-z0-9]+$");
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Province).NotEmpty().MaximumLength(80);
    }
}
