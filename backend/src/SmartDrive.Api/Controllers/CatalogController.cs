using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;

namespace SmartDrive.Api.Controllers;

/// <summary>Public vehicle catalogue: categories, branches, search, details, availability and reviews.</summary>
[Route("api")]
[AllowAnonymous]
public sealed class CatalogController(CatalogService catalog, ReviewService reviews) : ApiControllerBase
{
    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => catalog.CategoriesAsync(ct);

    [HttpGet("locations")]
    public Task<IReadOnlyList<LocationDto>> Locations(CancellationToken ct) => catalog.LocationsAsync(ct);

    /// <summary>Searches the fleet. Supplying pickupAt and returnAt checks real availability for that period.</summary>
    [HttpGet("vehicles")]
    public Task<PagedResult<VehicleSummaryDto>> Search([FromQuery] VehicleSearchQuery query, CancellationToken ct) =>
        catalog.SearchAsync(query, ct);

    [HttpGet("vehicles/featured")]
    public Task<IReadOnlyList<VehicleSummaryDto>> Featured(
        [FromQuery] DateTimeOffset? pickupAt, [FromQuery] DateTimeOffset? returnAt, [FromQuery] int count = 6, CancellationToken ct = default) =>
        catalog.FeaturedAsync(pickupAt, returnAt, Math.Clamp(count, 1, 12), ct);

    [HttpGet("vehicles/{id:guid}")]
    public Task<VehicleDetailDto> Detail(Guid id, CancellationToken ct) => catalog.DetailAsync(id, ct);

    /// <summary>Checks bookings, maintenance and turnaround time for the requested period.</summary>
    [HttpGet("vehicles/{id:guid}/availability")]
    public Task<AvailabilityDto> Availability(Guid id, [FromQuery] DateTimeOffset pickupAt, [FromQuery] DateTimeOffset returnAt, CancellationToken ct) =>
        catalog.AvailabilityAsync(id, pickupAt, returnAt, ct);

    [HttpGet("vehicles/{id:guid}/ratings")]
    public Task<RatingSummaryDto> Ratings(Guid id, CancellationToken ct) => catalog.RatingSummaryAsync(id, ct);

    [HttpGet("vehicles/{id:guid}/reviews")]
    public Task<PagedResult<ReviewDto>> VehicleReviews(Guid id, [FromQuery] ReviewQuery query, CancellationToken ct) =>
        reviews.ListAsync(query with { VehicleId = id }, CurrentUser.UserId, ct);
}
