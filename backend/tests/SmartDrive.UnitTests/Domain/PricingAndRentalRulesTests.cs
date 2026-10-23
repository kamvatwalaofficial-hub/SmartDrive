using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.UnitTests.Domain;

public sealed class RentalDurationTests : TestBase
{
    private readonly RentalPolicy policy = new();

    [Theory]
    [InlineData(4, 1)]          // a few hours is still one day
    [InlineData(24, 1)]         // exactly one day
    [InlineData(24.98, 1)]      // within the 59-minute grace period
    [InlineData(25, 2)]         // grace exceeded: a second day starts
    [InlineData(72, 3)]
    [InlineData(24 * 7, 7)]
    public void Charges_per_started_day_after_the_grace_period(double hours, int expectedDays)
    {
        var pickup = At(1);
        var days = RentalPeriod.CalculateDays(pickup, pickup.AddHours(hours), policy);
        Assert.Equal(expectedDays, days);
    }

    [Fact]
    public void Return_before_pickup_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => RentalPeriod.CalculateDays(At(2), At(1), policy));
        Assert.Equal("rental.invalid_period", ex.Code);
    }

    [Fact]
    public void Validation_reports_lead_time_and_maximum_length()
    {
        var now = At(0);
        Assert.Contains(RentalPeriod.Validate(now.AddMinutes(10), now.AddDays(1), now, policy), e => e.Contains("at least 60 minutes"));
        Assert.Contains(RentalPeriod.Validate(now.AddDays(1), now.AddDays(80), now, policy), e => e.Contains("maximum rental period"));
        Assert.Empty(RentalPeriod.Validate(now.AddDays(1), now.AddDays(4), now, policy));
    }
}

public sealed class PricingTests : TestBase
{
    private readonly RentalPolicy policy = new();

    [Fact]
    public void Three_day_rental_in_the_same_city_is_rate_times_days_with_vat_shown()
    {
        var quote = PricingCalculator.Quote(900m, 5000m, At(1), At(4), "Johannesburg", "Johannesburg", policy);

        Assert.Equal(3, quote.RentalDays);
        Assert.Equal(2700m, quote.Subtotal);
        Assert.Equal(0m, quote.Discount);
        Assert.Equal(2700m, quote.Total);
        Assert.Equal(Money.Round(2700m * 0.15m / 1.15m), quote.VatIncluded);   // VAT is included, not added
        Assert.Equal(5000m, quote.Deposit);
        Assert.Single(quote.Lines);
    }

    [Fact]
    public void Weekly_and_long_term_discounts_apply_at_their_thresholds()
    {
        var week = PricingCalculator.Quote(1000m, 0m, At(1), At(8), "Durban", "Durban", policy);
        Assert.Equal(7000m, week.Subtotal);
        Assert.Equal(700m, week.Discount);                // 10% from 7 days
        Assert.Equal(6300m, week.Total);
        Assert.Contains(week.Lines, l => l.Type == ChargeType.LongRentalDiscount && l.Amount == -700m);

        var month = PricingCalculator.Quote(1000m, 0m, At(1), At(22), "Durban", "Durban", policy);
        Assert.Equal(21000m * 0.15m, month.Discount);     // 15% from 21 days
    }

    [Fact]
    public void Returning_in_another_city_adds_the_one_way_fee()
    {
        var quote = PricingCalculator.Quote(600m, 0m, At(1), At(3), "Johannesburg", "Cape Town", policy);

        Assert.Contains(quote.Lines, l => l.Type == ChargeType.OneWayFee && l.Amount == policy.OneWayFee);
        Assert.Equal(1200m + policy.OneWayFee, quote.Total);
    }

    [Fact]
    public void A_zero_rate_is_never_priced()
    {
        Assert.Throws<DomainException>(() => PricingCalculator.Quote(0m, 0m, At(1), At(2), "A", "A", policy));
    }
}

public sealed class BookingStateMachineTests
{
    private static readonly (BookingStatus From, BookingStatus To)[] LegalPairs =
    [
        (BookingStatus.Pending, BookingStatus.Confirmed),
        (BookingStatus.Pending, BookingStatus.Rejected),
        (BookingStatus.Pending, BookingStatus.Cancelled),
        (BookingStatus.Confirmed, BookingStatus.Active),
        (BookingStatus.Confirmed, BookingStatus.Cancelled),
        (BookingStatus.Active, BookingStatus.Completed),
    ];

    public static TheoryData<BookingStatus, BookingStatus> Legal
    {
        get
        {
            var data = new TheoryData<BookingStatus, BookingStatus>();
            foreach (var (from, to) in LegalPairs)
            {
                data.Add(from, to);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Legal))]
    public void Allows_the_documented_transitions(BookingStatus from, BookingStatus to) =>
        Assert.True(BookingStateMachine.CanTransition(from, to));

    [Fact]
    public void Rejects_every_other_transition()
    {
        var legal = LegalPairs.ToHashSet();
        foreach (var from in Enum.GetValues<BookingStatus>())
        {
            foreach (var to in Enum.GetValues<BookingStatus>().Where(t => !legal.Contains((from, t))))
            {
                Assert.False(BookingStateMachine.CanTransition(from, to), $"{from} → {to} should be illegal");
            }
        }
    }

    [Fact]
    public void Illegal_transition_is_a_conflict()
    {
        var ex = Assert.Throws<DomainException>(() => BookingStateMachine.EnsureCanTransition(BookingStatus.Completed, BookingStatus.Active));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        Assert.True(BookingStateMachine.IsFinal(BookingStatus.Cancelled));
    }
}

public sealed class CancellationPolicyTests : TestBase
{
    private readonly RentalPolicy policy = new();

    [Fact]
    public void Free_more_than_48_hours_before_pickup()
    {
        var result = CancellationPolicy.Assess(BookingStatus.Confirmed, PaymentStatus.Paid, At(5), 800m, 2400m, At(0), policy);
        Assert.True(result.CanCancel);
        Assert.Equal(0m, result.Fee);
        Assert.Equal(2400m, result.Refund);
    }

    [Fact]
    public void One_days_rental_inside_48_hours()
    {
        var result = CancellationPolicy.Assess(BookingStatus.Confirmed, PaymentStatus.Paid, At(1), 800m, 2400m, At(0), policy);
        Assert.Equal(800m, result.Fee);
        Assert.Equal(1600m, result.Refund);
    }

    [Fact]
    public void Fee_never_exceeds_what_was_paid_and_unpaid_bookings_cancel_free()
    {
        Assert.Equal(500m, CancellationPolicy.Assess(BookingStatus.Confirmed, PaymentStatus.Paid, At(1), 800m, 500m, At(0), policy).Fee);
        Assert.Equal(0m, CancellationPolicy.Assess(BookingStatus.Pending, PaymentStatus.Unpaid, At(1), 800m, 2400m, At(0), policy).Fee);
    }

    [Fact]
    public void Cannot_cancel_after_pickup_or_once_active()
    {
        Assert.False(CancellationPolicy.Assess(BookingStatus.Confirmed, PaymentStatus.Paid, At(0, -1), 800m, 2400m, At(0), policy).CanCancel);
        Assert.False(CancellationPolicy.Assess(BookingStatus.Active, PaymentStatus.Paid, At(3), 800m, 2400m, At(0), policy).CanCancel);
    }
}

public sealed class ReviewEligibilityTests : TestBase
{
    private readonly RentalPolicy policy = new();
    private readonly Guid customer = Guid.NewGuid();

    [Fact]
    public void Completed_own_rental_within_the_window_can_be_reviewed() =>
        Assert.Null(ReviewEligibility.Check(customer, BookingStatus.Completed, At(-3), false, customer, At(0), policy));

    [Fact]
    public void Each_rule_has_its_own_reason()
    {
        Assert.Equal(ReviewIneligibility.NotOwner, ReviewEligibility.Check(customer, BookingStatus.Completed, At(-3), false, Guid.NewGuid(), At(0), policy));
        Assert.Equal(ReviewIneligibility.NotCompleted, ReviewEligibility.Check(customer, BookingStatus.Active, At(-3), false, customer, At(0), policy));
        Assert.Equal(ReviewIneligibility.AlreadyReviewed, ReviewEligibility.Check(customer, BookingStatus.Completed, At(-3), true, customer, At(0), policy));
        Assert.Equal(ReviewIneligibility.WindowClosed, ReviewEligibility.Check(customer, BookingStatus.Completed, At(-120), false, customer, At(0), policy));
    }
}
