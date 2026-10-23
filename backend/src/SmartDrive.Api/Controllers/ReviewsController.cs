using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;

namespace SmartDrive.Api.Controllers;

/// <summary>Customer reviews. Only the customer who completed a rental can review it, once.</summary>
[Route("api")]
public sealed class ReviewsController(ReviewService reviews) : ApiControllerBase
{
    /// <summary>Published reviews across the fleet (or one vehicle with vehicleId), newest first by default.</summary>
    [HttpGet("reviews")]
    [AllowAnonymous]
    public Task<PagedResult<ReviewDto>> List([FromQuery] ReviewQuery query, CancellationToken ct) =>
        reviews.ListAsync(query, CurrentUser.UserId, ct);

    [HttpPost("bookings/{reference}/review")]
    [Authorize]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(string reference, ReviewInput input, CancellationToken ct)
    {
        var review = await reviews.CreateAsync(reference, input, UserId, ct);
        return Created($"/api/reviews/{review.Id}", review);
    }

    [HttpPut("reviews/{id:guid}")]
    [Authorize]
    public Task<ReviewDto> Update(Guid id, ReviewInput input, CancellationToken ct) => reviews.UpdateAsync(id, input, UserId, ct);

    [HttpDelete("reviews/{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await reviews.DeleteAsync(id, UserId, ct);
        return NoContent();
    }

    /// <summary>Flags a review for an administrator to look at.</summary>
    [HttpPost("reviews/{id:guid}/report")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Report(Guid id, ReportReviewRequest request, CancellationToken ct)
    {
        await reviews.ReportAsync(id, request.Reason, UserId, ct);
        return Accepted();
    }
}
