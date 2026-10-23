using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDrive.Api.Infrastructure;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;
using SmartDrive.Domain.Common;

namespace SmartDrive.Api.Controllers.Admin;

/// <summary>Fleet management for administrators: vehicles, pricing, status, media and history.</summary>
[Route("api/admin/vehicles")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminVehiclesController(FleetAdminService fleet) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<AdminVehicleDto>> List([FromQuery] AdminVehicleQuery query, CancellationToken ct) => fleet.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<AdminVehicleDto> Get(Guid id, CancellationToken ct) => fleet.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<AdminVehicleDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(UpsertVehicleRequest request, CancellationToken ct)
    {
        var vehicle = await fleet.CreateAsync(request, UserId, ct);
        return CreatedAtAction(nameof(Get), new { id = vehicle.Summary.Id }, vehicle);
    }

    [HttpPut("{id:guid}")]
    public Task<AdminVehicleDto> Update(Guid id, UpsertVehicleRequest request, CancellationToken ct) => fleet.UpdateAsync(id, request, UserId, ct);

    /// <summary>Sets the operational status (Available, UnderMaintenance or Inactive).</summary>
    [HttpPatch("{id:guid}/status")]
    public Task<AdminVehicleDto> SetStatus(Guid id, UpdateVehicleStatusRequest request, CancellationToken ct) =>
        fleet.SetStatusAsync(id, request.Status, UserId, ct);

    /// <summary>Archives a vehicle so it can no longer be rented. Its history is kept.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await fleet.ArchiveAsync(id, UserId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/history")]
    public Task<VehicleHistoryDto> History(Guid id, CancellationToken ct) => fleet.HistoryAsync(id, ct);

    /// <summary>Uploads a JPG, PNG or WebP image (max 5 MB). The file type is checked by its content, not its name.</summary>
    [HttpPost("{id:guid}/media")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<MediaDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadMedia(Guid id, IFormFile? file, [FromForm] string kind, [FromForm] string? altText, CancellationToken ct)
    {
        if (file is null)
        {
            throw new DomainException("media.missing", "Choose an image to upload.");
        }

        await using var stream = file.OpenReadStream();
        var media = await fleet.UploadImageAsync(id, file.FileName, file.Length, stream, kind, altText, UserId, ct);
        return Created(media.Url, media);
    }

    [HttpPut("{id:guid}/media/{mediaId:guid}/primary")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPrimary(Guid id, Guid mediaId, CancellationToken ct)
    {
        await fleet.SetPrimaryImageAsync(id, mediaId, UserId, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMedia(Guid id, Guid mediaId, CancellationToken ct)
    {
        await fleet.DeleteImageAsync(id, mediaId, UserId, ct);
        return NoContent();
    }
}

/// <summary>Service, repair and inspection records, and the downtime they create.</summary>
[Route("api/admin/maintenance")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminMaintenanceController(FleetAdminService fleet) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<MaintenanceDto>> List(
        [FromQuery] Guid? vehicleId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        fleet.MaintenanceListAsync(vehicleId, status, page, pageSize, ct);

    /// <summary>Vehicles that are due or overdue for a service, or have repeated reported issues.</summary>
    [HttpGet("alerts")]
    public Task<IReadOnlyList<MaintenanceAlertDto>> Alerts(CancellationToken ct) => fleet.AlertsAsync(ct);

    [HttpPost]
    [ProducesResponseType<MaintenanceDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(UpsertMaintenanceRequest request, CancellationToken ct)
    {
        var record = await fleet.CreateMaintenanceAsync(request, UserId, ct);
        return Created($"/api/admin/maintenance/{record.Id}", record);
    }

    [HttpPut("{id:guid}")]
    public Task<MaintenanceDto> Update(Guid id, UpsertMaintenanceRequest request, CancellationToken ct) =>
        fleet.UpdateMaintenanceAsync(id, request, UserId, ct);

    [HttpPost("{id:guid}/start")]
    public Task<MaintenanceDto> Start(Guid id, CancellationToken ct) => fleet.StartMaintenanceAsync(id, UserId, ct);

    [HttpPost("{id:guid}/complete")]
    public Task<MaintenanceDto> Complete(Guid id, CompleteMaintenanceRequest request, CancellationToken ct) =>
        fleet.CompleteMaintenanceAsync(id, request, UserId, ct);

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await fleet.CancelMaintenanceAsync(id, UserId, ct);
        return NoContent();
    }
}
