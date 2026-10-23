using SmartDrive.Domain.Common;

namespace SmartDrive.Domain.Rules;

/// <summary>
/// Business settings for rentals. Bound from the "Rental" configuration section so a business can
/// tune them without code changes. Defaults are sample policies for demonstration.
/// </summary>
public sealed class RentalPolicy
{
    /// <summary>South African VAT. Daily rates are VAT inclusive; the VAT portion is shown for transparency.</summary>
    public decimal VatRate { get; set; } = 0.15m;

    /// <summary>A rental may run this long past a whole day before another day is charged.</summary>
    public int GraceMinutes { get; set; } = 59;

    public int MinimumLeadMinutes { get; set; } = 60;
    public int MaximumRentalDays { get; set; } = 60;
    public int MaximumAdvanceBookingDays { get; set; } = 365;

    public int WeeklyDiscountMinDays { get; set; } = 7;
    public decimal WeeklyDiscountRate { get; set; } = 0.10m;
    public int LongTermDiscountMinDays { get; set; } = 21;
    public decimal LongTermDiscountRate { get; set; } = 0.15m;

    /// <summary>Charged when the vehicle is returned in a different city.</summary>
    public decimal OneWayFee { get; set; } = 850m;

    /// <summary>Cleaning and inspection time needed between consecutive rentals.</summary>
    public int TurnaroundMinutes { get; set; } = 120;

    /// <summary>How long an unpaid pending booking reserves the vehicle.</summary>
    public int PendingHoldMinutes { get; set; } = 30;

    /// <summary>Cancellations at least this many hours before pickup are free.</summary>
    public int FreeCancellationHours { get; set; } = 48;

    /// <summary>Later cancellations cost this many days of rental (capped at the booking total).</summary>
    public int LateCancellationFeeDays { get; set; } = 1;

    public int ReviewWindowDays { get; set; } = 90;
    public int ReviewEditWindowDays { get; set; } = 30;

    public TimeSpan Grace => TimeSpan.FromMinutes(GraceMinutes);
    public TimeSpan Turnaround => TimeSpan.FromMinutes(TurnaroundMinutes);
}

public static class RentalPeriod
{
    /// <summary>
    /// Number of chargeable days. Rentals are charged per started 24-hour block, after a grace
    /// period: 24h59m is one day, 25h is two days. The minimum charge is one day.
    /// </summary>
    public static int CalculateDays(DateTime pickupUtc, DateTime returnUtc, RentalPolicy policy)
    {
        if (returnUtc <= pickupUtc)
        {
            throw new DomainException("rental.invalid_period", "The return time must be after the pickup time.");
        }

        var chargeable = (returnUtc - pickupUtc) - policy.Grace;
        var days = (int)Math.Ceiling(chargeable.TotalDays);
        return Math.Max(1, days);
    }

    /// <summary>Validates a requested rental period and returns every problem found (empty when valid).</summary>
    public static IReadOnlyList<string> Validate(DateTime pickupUtc, DateTime returnUtc, DateTime nowUtc, RentalPolicy policy)
    {
        var errors = new List<string>();
        if (returnUtc <= pickupUtc)
        {
            errors.Add("The return date and time must be after the pickup date and time.");
            return errors;
        }

        if (pickupUtc < nowUtc.AddMinutes(policy.MinimumLeadMinutes))
        {
            errors.Add($"Pickup must be at least {policy.MinimumLeadMinutes} minutes from now.");
        }

        if (pickupUtc > nowUtc.AddDays(policy.MaximumAdvanceBookingDays))
        {
            errors.Add($"Bookings can be made up to {policy.MaximumAdvanceBookingDays} days in advance.");
        }

        if ((returnUtc - pickupUtc).TotalDays > policy.MaximumRentalDays)
        {
            errors.Add($"The maximum rental period is {policy.MaximumRentalDays} days.");
        }

        return errors;
    }
}
