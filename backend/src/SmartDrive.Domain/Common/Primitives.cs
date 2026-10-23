namespace SmartDrive.Domain.Common;

/// <summary>How a <see cref="DomainException"/> should be reported to API clients.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
}

/// <summary>
/// A business-rule violation. Thrown by the domain and application layers and translated into
/// an RFC 7807 problem response by the API (400, 404, 409 or 403 depending on <see cref="Kind"/>).
/// </summary>
public sealed class DomainException(string code, string message, ErrorKind kind = ErrorKind.Validation)
    : Exception(message)
{
    /// <summary>Stable, machine-readable code such as <c>booking.overlap</c>.</summary>
    public string Code { get; } = code;

    public ErrorKind Kind { get; } = kind;

    public static DomainException NotFound(string what) =>
        new($"{what.ToLowerInvariant()}.not_found", $"{what} was not found.", ErrorKind.NotFound);
}

/// <summary>Money helpers. All amounts are South African rand (ZAR), stored as decimal.</summary>
public static class Money
{
    public const string Currency = "ZAR";

    /// <summary>Rounds to cents using commercial (away-from-zero) rounding.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Formats an amount as "R1,234.50", independent of the server's culture.</summary>
    public static string Format(decimal amount) =>
        (amount < 0 ? "-R" : "R") + Math.Abs(amount).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// South African time handling. South Africa Standard Time is UTC+02:00 all year (no daylight
/// saving), so dates are stored in UTC and shown at a fixed +02:00 offset.
/// </summary>
public static class SouthAfricanTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    public static DateTimeOffset ToLocal(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(Offset);

    public static DateTime ToUtc(DateTimeOffset value) => value.UtcDateTime;

    /// <summary>The local calendar date (SAST) of a UTC instant.</summary>
    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc).DateTime);
}
