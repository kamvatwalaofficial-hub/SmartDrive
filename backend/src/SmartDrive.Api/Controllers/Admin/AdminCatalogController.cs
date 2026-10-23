using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDrive.Api.Infrastructure;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;

namespace SmartDrive.Api.Controllers.Admin;

/// <summary>Vehicle categories and rental branches.</summary>
[Route("api/admin")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminCatalogController(CatalogAdminService catalogAdmin, CatalogService catalog) : ApiControllerBase
{
    [HttpGet("categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => catalog.CategoriesAsync(ct);

    [HttpPost("categories")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategory(UpsertCategoryRequest request, CancellationToken ct)
    {
        var category = await catalogAdmin.CreateCategoryAsync(request, UserId, ct);
        return Created($"/api/admin/categories/{category.Id}", category);
    }

    [HttpPut("categories/{id:int}")]
    public Task<CategoryDto> UpdateCategory(int id, UpsertCategoryRequest request, CancellationToken ct) =>
        catalogAdmin.UpdateCategoryAsync(id, request, UserId, ct);

    /// <summary>Deletes an empty category. Categories that still have vehicles cannot be deleted.</summary>
    [HttpDelete("categories/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        await catalogAdmin.DeleteCategoryAsync(id, UserId, ct);
        return NoContent();
    }

    [HttpGet("locations")]
    public Task<IReadOnlyList<AdminLocationDto>> Locations(CancellationToken ct) => catalogAdmin.LocationsAsync(ct);

    [HttpPost("locations")]
    public Task<AdminLocationDto> CreateLocation(UpsertLocationRequest request, CancellationToken ct) =>
        catalogAdmin.UpsertLocationAsync(null, request, UserId, ct);

    [HttpPut("locations/{id:int}")]
    public Task<AdminLocationDto> UpdateLocation(int id, UpsertLocationRequest request, CancellationToken ct) =>
        catalogAdmin.UpsertLocationAsync(id, request, UserId, ct);
}
