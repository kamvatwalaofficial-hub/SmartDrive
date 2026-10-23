using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Domain.Rules;

/// <summary>
/// The only legal booking status changes:
/// <code>
/// Pending   → Confirmed | Rejected | Cancelled
/// Confirmed → Active    | Cancelled
/// Active    → Completed
/// Completed, Cancelled, Rejected are final.
/// </code>
/// </summary>
public static class BookingStateMachine
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Pending] = [BookingStatus.Confirmed, BookingStatus.Rejected, BookingStatus.Cancelled],
        [BookingStatus.Confirmed] = [BookingStatus.Active, BookingStatus.Cancelled],
        [BookingStatus.Active] = [BookingStatus.Completed],
        [BookingStatus.Completed] = [],
        [BookingStatus.Cancelled] = [],
        [BookingStatus.Rejected] = [],
    };

    public static IReadOnlyList<BookingStatus> NextStatuses(BookingStatus from) => Allowed[from];

    public static bool CanTransition(BookingStatus from, BookingStatus to) => Allowed[from].Contains(to);

    public static void EnsureCanTransition(BookingStatus from, BookingStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainException(
                "booking.invalid_transition",
                $"A {from.ToString().ToLowerInvariant()} booking cannot be changed to {to.ToString().ToLowerInvariant()}.",
                ErrorKind.Conflict);
        }
    }

    public static bool IsFinal(BookingStatus status) => Allowed[status].Length == 0;
}

public sealed record CancellationAssessment(bool CanCancel, decimal Fee, decimal Refund, string Explanation);

/// <summary>
/// Sample cancellation policy: free until 48 hours before pickup, then one day's rental
/// (never more than was charged). Bookings cannot be cancelled once the pickup time has passed.
/// </summary>
public static class CancellationPolicy
{
    public static CancellationAssessment Assess(
        BookingStatus status,
        PaymentStatus paymentStatus,
        DateTime pickupUtc,
        decimal dailyRate,
        decimal totalAmount,
        DateTime nowUtc,
        RentalPolicy policy)
    {
        if (status is not (BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return new(false, 0, 0, "Only pending or confirmed bookings can be cancelled.");
        }

        if (nowUtc >= pickupUtc)
        {
            return new(false, 0, 0, "The pickup time has passed, so this booking can no longer be cancelled online.");
        }

        var paid = paymentStatus == PaymentStatus.Paid ? totalAmount : 0m;
        var hoursBefore = (pickupUtc - nowUtc).TotalHours;
        if (hoursBefore >= policy.FreeCancellationHours || paid == 0)
        {
            return new(true, 0, paid, paid == 0
                ? "Nothing has been paid, so cancelling is free."
                : $"Free cancellation: more than {policy.FreeCancellationHours} hours before pickup.");
        }

        var fee = Money.Round(Math.Min(paid, dailyRate * policy.LateCancellationFeeDays));
        return new(true, fee, Money.Round(paid - fee),
            $"Cancelling within {policy.FreeCancellationHours} hours of pickup costs {policy.LateCancellationFeeDays} day's rental ({Money.Format(fee)}).");
    }
}

public enum ReviewIneligibility
{
    NotOwner,
    NotCompleted,
    AlreadyReviewed,
    WindowClosed,
}

public static class ReviewEligibility
{
    /// <summary>
    /// A customer may review a vehicle once per completed rental, within the review window after return.
    /// Returns null when the review is allowed.
    /// </summary>
    public static ReviewIneligibility? Check(
        Guid bookingCustomerId,
        BookingStatus status,
        DateTime returnUtc,
        bool alreadyReviewed,
        Guid userId,
        DateTime nowUtc,
        RentalPolicy policy)
    {
        if (bookingCustomerId != userId)
        {
            return ReviewIneligibility.NotOwner;
        }

        if (status != BookingStatus.Completed)
        {
            return ReviewIneligibility.NotCompleted;
        }

        if (alreadyReviewed)
        {
            return ReviewIneligibility.AlreadyReviewed;
        }

        if (nowUtc > returnUtc.AddDays(policy.ReviewWindowDays))
        {
            return ReviewIneligibility.WindowClosed;
        }

        return null;
    }

    public static string Describe(ReviewIneligibility reason) => reason switch
    {
        ReviewIneligibility.NotOwner => "You can only review your own rentals.",
        ReviewIneligibility.NotCompleted => "You can review a vehicle once the rental has been completed.",
        ReviewIneligibility.AlreadyReviewed => "You have already reviewed this rental.",
        ReviewIneligibility.WindowClosed => "The review period for this rental has closed.",
        _ => "This rental cannot be reviewed.",
    };
}

public static class BookingReference
{
    // No 0/O or 1/I so references are easy to read out over the phone.
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Generate(Random random)
    {
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        return $"SD-{new string(chars)}";
    }
}
