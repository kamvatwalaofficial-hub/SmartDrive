using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Domain.Rules;

public sealed record PriceLine(ChargeType Type, string Description, decimal Amount);

/// <summary>An itemised, VAT-inclusive price. Always calculated on the server.</summary>
public sealed record PriceQuote(
    int RentalDays,
    decimal DailyRate,
    IReadOnlyList<PriceLine> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal VatIncluded,
    decimal Deposit)
{
    public decimal EffectiveDailyRate => RentalDays == 0 ? 0 : Money.Round(Total / RentalDays);
}

public static class PricingCalculator
{
    /// <param name="dailyRate">VAT-inclusive daily rate in ZAR.</param>
    /// <param name="pickupCity">City of the pickup branch.</param>
    /// <param name="returnCity">City of the return branch; a different city adds the one-way fee.</param>
    public static PriceQuote Quote(
        decimal dailyRate,
        decimal deposit,
        DateTime pickupUtc,
        DateTime returnUtc,
        string pickupCity,
        string returnCity,
        RentalPolicy policy)
    {
        if (dailyRate <= 0)
        {
            throw new DomainException("pricing.invalid_rate", "The daily rate must be greater than zero.");
        }

        var days = RentalPeriod.CalculateDays(pickupUtc, returnUtc, policy);
        var lines = new List<PriceLine>();

        var rental = Money.Round(dailyRate * days);
        lines.Add(new PriceLine(ChargeType.Rental, $"{days} day{(days == 1 ? "" : "s")} × {Money.Format(dailyRate)}", rental));

        var discountRate = DiscountRateFor(days, policy);
        var discount = 0m;
        if (discountRate > 0)
        {
            discount = Money.Round(rental * discountRate);
            var percent = (discountRate * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            lines.Add(new PriceLine(
                ChargeType.LongRentalDiscount,
                $"Long-rental discount ({percent}% for {MinDaysFor(days, policy)}+ days)",
                -discount));
        }

        if (!string.Equals(pickupCity.Trim(), returnCity.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new PriceLine(ChargeType.OneWayFee, $"One-way fee ({pickupCity} → {returnCity})", Money.Round(policy.OneWayFee)));
        }

        var total = Money.Round(lines.Sum(l => l.Amount));
        var vat = Money.Round(total * policy.VatRate / (1 + policy.VatRate));

        return new PriceQuote(days, dailyRate, lines, rental, discount, total, vat, Money.Round(deposit));
    }

    public static decimal DiscountRateFor(int days, RentalPolicy policy) =>
        days >= policy.LongTermDiscountMinDays ? policy.LongTermDiscountRate
        : days >= policy.WeeklyDiscountMinDays ? policy.WeeklyDiscountRate
        : 0m;

    private static int MinDaysFor(int days, RentalPolicy policy) =>
        days >= policy.LongTermDiscountMinDays ? policy.LongTermDiscountMinDays : policy.WeeklyDiscountMinDays;
}
