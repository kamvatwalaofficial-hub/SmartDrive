using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using SmartDrive.Api.Infrastructure;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;
using SmartDrive.Domain.Common;

namespace SmartDrive.Api.Controllers;

/// <summary>
/// Quotes, bookings and simulated payments. Prices are always calculated on the server; customers can only
/// see and change their own bookings (another customer's booking reference returns 404).
/// </summary>
[Route("api/bookings")]
[Authorize]
public sealed class BookingsController(BookingService bookings) : ApiControllerBase
{
    /// <summary>Prices a rental and checks availability without creating anything.</summary>
    [HttpPost("quote")]
    [AllowAnonymous]
    public Task<QuoteDto> Quote(QuoteRequest request, CancellationToken ct) => bookings.QuoteAsync(request, ct);

    /// <summary>Creates a pending booking that holds the vehicle while the customer pays.</summary>
    [HttpPost]
    [ProducesResponseType<BookingDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var booking = await bookings.CreateAsync(request, UserId, ct);
        return CreatedAtAction(nameof(Detail), new { reference = booking.Summary.Reference }, booking);
    }

    /// <summary>The signed-in customer's bookings. scope: upcoming (default), past, cancelled or all.</summary>
    [HttpGet("mine")]
    public Task<IReadOnlyList<BookingSummaryDto>> Mine([FromQuery] string scope = "upcoming", CancellationToken ct = default) =>
        bookings.MineAsync(UserId, scope.ToLowerInvariant(), ct);

    [HttpGet("{reference}")]
    public Task<BookingDetailDto> Detail(string reference, CancellationToken ct) =>
        bookings.DetailAsync(reference, UserId, CurrentUser.IsAdmin, ct);

    /// <summary>
    /// Records a SIMULATED payment for demonstration (no card details are accepted or stored).
    /// Send an Idempotency-Key header: retrying with the same key never charges twice.
    /// </summary>
    [HttpPost("{reference}/pay")]
    [EnableRateLimiting(RateLimitPolicies.Payments)]
    public Task<BookingDetailDto> Pay(
        string reference,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SimulatedPaymentRequest? request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 100)
        {
            throw new DomainException("payment.idempotency_key", "An Idempotency-Key header (8 to 100 characters) is required.");
        }

        return bookings.PaySimulatedAsync(reference, UserId, idempotencyKey.Trim(), request?.SimulateFailure ?? false, ct);
    }

    /// <summary>Shows the fee and refund that cancelling now would produce, according to the policy.</summary>
    [HttpGet("{reference}/cancellation")]
    public Task<CancellationPreviewDto> CancellationPreview(string reference, CancellationToken ct) =>
        bookings.CancellationPreviewAsync(reference, UserId, CurrentUser.IsAdmin, ct);

    [HttpPost("{reference}/cancel")]
    public Task<BookingDetailDto> Cancel(string reference, CancelBookingRequest request, CancellationToken ct) =>
        bookings.CancelAsync(reference, UserId, request.Reason, ct);
}
