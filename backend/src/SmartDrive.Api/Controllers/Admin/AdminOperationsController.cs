using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDrive.Api.Infrastructure;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;

namespace SmartDrive.Api.Controllers.Admin;

/// <summary>Booking management for administrators, including status changes and CSV export.</summary>
[Route("api/admin/bookings")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminBookingsController(BookingService bookings) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<BookingSummaryDto>> List([FromQuery] AdminBookingQuery query, CancellationToken ct) => bookings.AdminListAsync(query, ct);

    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export([FromQuery] AdminBookingQuery query, CancellationToken ct)
    {
        var csv = await bookings.AdminExportCsvAsync(query, ct);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", $"smartdrive-bookings-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("{reference}")]
    public Task<BookingDetailDto> Detail(string reference, CancellationToken ct) => bookings.DetailAsync(reference, UserId, isAdmin: true, ct);

    /// <summary>
    /// Moves a booking through its lifecycle: Confirmed, Rejected, Active (pickup, with odometer),
    /// Completed (return, with odometer) or Cancelled. Illegal transitions return 409.
    /// </summary>
    [HttpPost("{reference}/status")]
    public Task<BookingDetailDto> ChangeStatus(string reference, ChangeBookingStatusRequest request, CancellationToken ct) =>
        bookings.ChangeStatusAsync(reference, request, UserId, ct);
}

/// <summary>Customer accounts, roles and the audit log.</summary>
[Route("api/admin")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminCustomersController(CustomerAdminService customers) : ApiControllerBase
{
    [HttpGet("customers")]
    public Task<PagedResult<AdminCustomerDto>> List(
        [FromQuery] string? q, [FromQuery] string? role, [FromQuery] bool? isActive,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        customers.ListAsync(q, role, isActive, page, pageSize, ct);

    [HttpGet("customers/{id:guid}")]
    public Task<AdminCustomerDto> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPatch("customers/{id:guid}/status")]
    public Task<AdminCustomerDto> SetStatus(Guid id, UpdateCustomerStatusRequest request, CancellationToken ct) =>
        customers.SetActiveAsync(id, request.IsActive, UserId, ct);

    [HttpPost("customers/{id:guid}/roles")]
    public Task<AdminCustomerDto> SetRole(Guid id, UpdateRoleRequest request, CancellationToken ct) =>
        customers.SetRoleAsync(id, request.Role, request.Grant, UserId, ct);

    /// <summary>Messages sent through the public contact form.</summary>
    [HttpGet("messages")]
    public Task<PagedResult<ContactMessageDto>> Messages([FromQuery] bool? handled, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        customers.MessagesAsync(handled, page, pageSize, ct);

    [HttpPatch("messages/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateMessage(Guid id, UpdateMessageRequest request, CancellationToken ct)
    {
        await customers.SetMessageHandledAsync(id, request.IsHandled, UserId, ct);
        return NoContent();
    }

    [HttpGet("audit-log")]
    public Task<PagedResult<AuditLogDto>> AuditLog(
        [FromQuery] string? action, [FromQuery] string? entityType, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        customers.AuditLogAsync(action, entityType, page, pageSize, ct);
}

/// <summary>Review moderation. Administrators always see the original text alongside any automated sentiment.</summary>
[Route("api/admin/reviews")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminReviewsController(ReviewService reviews) : ApiControllerBase
{
    /// <summary>status: reported (default), published, hidden, removed or all.</summary>
    [HttpGet]
    public Task<PagedResult<AdminReviewDto>> List([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        reviews.AdminListAsync(status, page, pageSize, ct);

    /// <summary>action: publish or hide.</summary>
    [HttpPost("{id:guid}/moderate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Moderate(Guid id, ModerateReviewRequest request, CancellationToken ct)
    {
        await reviews.ModerateAsync(id, request, UserId, ct);
        return NoContent();
    }

    /// <summary>Re-runs the sentiment classifier over every review (for example after improving the lexicon).</summary>
    [HttpPost("reanalyse")]
    public async Task<IActionResult> Reanalyse(CancellationToken ct) => Ok(new { analysed = await reviews.ReanalyseAllAsync(ct) });
}
