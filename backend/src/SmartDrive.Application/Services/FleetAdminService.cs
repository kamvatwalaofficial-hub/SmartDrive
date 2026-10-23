using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Domain.Analytics;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Services;

/// <summary>Administrator fleet management: vehicles, media and maintenance.</summary>
public sealed class FleetAdminService(
    SmartDriveDbContext db,
    AvailabilityService availability,
    CatalogService catalog,
    IMediaStorage storage,
    MediaStorageOptions storageOptions,
    RentalPolicy policy,
    TimeProvider clock,
    AuditWriter audit)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ================================================================== vehicles

    public async Task<PagedResult<AdminVehicleDto>> ListAsync(AdminVehicleQuery q, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalise(q.Page, q.PageSize);
        var query = db.Vehicles.AsNoTracking().Include(v => v.Category).AsQueryable();
        if (!q.IncludeArchived)
        {
            query = query.Where(v => v.ArchivedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim().ToUpperInvariant();
            query = query.Where(v => v.RegistrationNumber.ToUpper().Contains(term) || v.Make.ToUpper().Contains(term) || v.Model.ToUpper().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            query = query.Where(v => v.Category!.Slug == q.Category);
        }

        if (Parse.OptionalEnum<VehicleStatus>(q.Status, "vehicle status") is { } status)
        {
            query = query.Where(v => v.Status == status);
        }

        var total = await query.CountAsync(ct);
        var vehicles = await query.OrderBy(v => v.Make).ThenBy(v => v.Model).ThenBy(v => v.RegistrationNumber)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AdminVehicleDto>(await ToAdminDtosAsync(vehicles, ct), page, pageSize, total);
    }

    public async Task<AdminVehicleDto> GetAsync(Guid id, CancellationToken ct)
    {
        var v = await db.Vehicles.AsNoTracking().Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Vehicle");
        return (await ToAdminDtosAsync([v], ct))[0];
    }

    public async Task<AdminVehicleDto> CreateAsync(UpsertVehicleRequest req, Guid adminId, CancellationToken ct)
    {
        await EnsureUniqueRegistrationAsync(req.RegistrationNumber, null, ct);
        await EnsureCategoryAsync(req.CategoryId, ct);
        var now = Now;
        var v = new Vehicle { Id = Guid.NewGuid(), CreatedAt = now, Status = VehicleStatus.Available };
        Apply(v, req, now);
        db.Vehicles.Add(v);
        audit.Write(adminId, "vehicle.created", nameof(Vehicle), v.Id.ToString(), new { v.RegistrationNumber, v.DailyRate });
        await db.SaveChangesAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    public async Task<AdminVehicleDto> UpdateAsync(Guid id, UpsertVehicleRequest req, Guid adminId, CancellationToken ct)
    {
        var v = await db.Vehicles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Vehicle");
        await EnsureUniqueRegistrationAsync(req.RegistrationNumber, id, ct);
        await EnsureCategoryAsync(req.CategoryId, ct);
        if (req.OdometerKm < v.OdometerKm)
        {
            throw new DomainException("vehicle.odometer", $"The odometer cannot go backwards (last reading {v.OdometerKm:N0} km).");
        }

        var oldRate = v.DailyRate;
        Apply(v, req, Now);
        audit.Write(adminId, "vehicle.updated", nameof(Vehicle), id.ToString(),
            oldRate != v.DailyRate ? new { oldRate, newRate = v.DailyRate } : null);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdminVehicleDto> SetStatusAsync(Guid id, string status, Guid adminId, CancellationToken ct)
    {
        var v = await db.Vehicles.FirstOrDefaultAsync(x => x.Id == id && x.ArchivedAt == null, ct) ?? throw DomainException.NotFound("Vehicle");
        var target = Parse.Enum<VehicleStatus>(status, "vehicle status");
        if (target == VehicleStatus.Inactive)
        {
            await EnsureNoUpcomingBookingsAsync(id, "deactivate", ct);
        }

        v.Status = target;
        v.UpdatedAt = Now;
        v.BookingVersion++;
        audit.Write(adminId, "vehicle.status_changed", nameof(Vehicle), id.ToString(), new { status = target.ToString() });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Archived vehicles disappear from the catalogue but keep their booking and maintenance history.</summary>
    public async Task ArchiveAsync(Guid id, Guid adminId, CancellationToken ct)
    {
        var v = await db.Vehicles.FirstOrDefaultAsync(x => x.Id == id && x.ArchivedAt == null, ct) ?? throw DomainException.NotFound("Vehicle");
        await EnsureNoUpcomingBookingsAsync(id, "archive", ct);
        v.ArchivedAt = Now;
        v.Status = VehicleStatus.Inactive;
        v.UpdatedAt = Now;
        v.BookingVersion++;
        audit.Write(adminId, "vehicle.archived", nameof(Vehicle), id.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task<VehicleHistoryDto> HistoryAsync(Guid id, CancellationToken ct)
    {
        _ = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Vehicle");
        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.PickupLocation).Include(b => b.ReturnLocation).Include(b => b.Vehicle)
            .Where(b => b.VehicleId == id).OrderByDescending(b => b.PickupAt).Take(100).ToListAsync(ct);
        var customers = await db.Users.AsNoTracking().Where(u => bookings.Select(b => b.CustomerId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var maintenance = await db.MaintenanceRecords.AsNoTracking().Include(m => m.Vehicle)
            .Where(m => m.VehicleId == id).OrderByDescending(m => m.StartAt).ToListAsync(ct);
        var mileage = await db.MileageRecords.AsNoTracking().Where(m => m.VehicleId == id).OrderBy(m => m.RecordedAt).ToListAsync(ct);

        return new VehicleHistoryDto(
            bookings.Select(b => new BookingSummaryDto(
                b.Id, b.Reference, b.VehicleId, b.Vehicle?.DisplayName ?? "", null,
                Mapping.Local(b.PickupAt), Mapping.Local(b.ReturnAt), b.PickupLocation?.Name ?? "", b.ReturnLocation?.Name ?? "",
                b.RentalDays, b.TotalAmount, b.Status.ToString(), b.PaymentStatus.ToString(), Mapping.Local(b.HoldExpiresAt),
                false, false, false, false, Mapping.Local(b.CreatedAt),
                customers.TryGetValue(b.CustomerId, out var c) ? c.FullName : null, c?.Email, b.IsSampleData)).ToList(),
            maintenance.Select(ToDto).ToList(),
            mileage.Select(m => new MileagePointDto(Mapping.Local(m.RecordedAt), m.OdometerKm, m.Source.ToString())).ToList());
    }

    // ================================================================== media

    public async Task<MediaDto> UploadImageAsync(Guid vehicleId, string fileName, long length, Stream content, string kind, string? altText, Guid adminId, CancellationToken ct)
    {
        var v = await db.Vehicles.Include(x => x.Media).FirstOrDefaultAsync(x => x.Id == vehicleId, ct) ?? throw DomainException.NotFound("Vehicle");
        var mediaKind = Parse.Enum<MediaKind>(kind, "media kind");
        if (mediaKind == MediaKind.Video)
        {
            throw new DomainException("media.video", "Videos are linked by URL, not uploaded.");
        }

        // Read the header to verify the file really is the image type it claims to be.
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length != length && length > 0)
        {
            length = buffer.Length;
        }

        var ext = ValidateImage(fileName, buffer, storageOptions.MaxBytes);
        buffer.Position = 0;
        var url = await storage.SaveAsync(buffer, ext, ct);

        var media = new VehicleMedia
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            Kind = mediaKind,
            Url = url,
            AltText = string.IsNullOrWhiteSpace(altText) ? $"{v.DisplayName} — {mediaKind.ToString().ToLowerInvariant()}" : altText.Trim(),
            SortOrder = v.Media.Count == 0 ? 0 : v.Media.Max(m => m.SortOrder) + 1,
            IsPrimary = !v.Media.Any(m => m.IsPrimary),
            Attribution = "Uploaded by SmartDrive staff",
            CreatedAt = Now,
        };
        db.VehicleMedia.Add(media);
        audit.Write(adminId, "vehicle.media_uploaded", nameof(Vehicle), vehicleId.ToString(), new { media.Kind, media.Url });
        await db.SaveChangesAsync(ct);
        return media.ToDto();
    }

    public async Task SetPrimaryImageAsync(Guid vehicleId, Guid mediaId, Guid adminId, CancellationToken ct)
    {
        var media = await db.VehicleMedia.Where(m => m.VehicleId == vehicleId).ToListAsync(ct);
        if (media.All(m => m.Id != mediaId))
        {
            throw DomainException.NotFound("Image");
        }

        foreach (var m in media)
        {
            m.IsPrimary = m.Id == mediaId;
        }

        audit.Write(adminId, "vehicle.media_primary", nameof(Vehicle), vehicleId.ToString(), new { mediaId });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteImageAsync(Guid vehicleId, Guid mediaId, Guid adminId, CancellationToken ct)
    {
        var media = await db.VehicleMedia.FirstOrDefaultAsync(m => m.Id == mediaId && m.VehicleId == vehicleId, ct) ?? throw DomainException.NotFound("Image");
        db.VehicleMedia.Remove(media);
        if (media.IsPrimary)
        {
            var next = await db.VehicleMedia.Where(m => m.VehicleId == vehicleId && m.Id != mediaId && m.Kind != MediaKind.Video)
                .OrderBy(m => m.SortOrder).FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsPrimary = true;
            }
        }

        audit.Write(adminId, "vehicle.media_deleted", nameof(Vehicle), vehicleId.ToString(), new { mediaId });
        await db.SaveChangesAsync(ct);
        if (!media.IsSampleData)
        {
            await storage.DeleteAsync(media.Url, ct);
        }
    }

    // ================================================================== maintenance

    public async Task<PagedResult<MaintenanceDto>> MaintenanceListAsync(Guid? vehicleId, string? status, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalise(page, pageSize);
        var query = db.MaintenanceRecords.AsNoTracking().Include(m => m.Vehicle).AsQueryable();
        if (vehicleId is { } id)
        {
            query = query.Where(m => m.VehicleId == id);
        }

        if (Parse.OptionalEnum<MaintenanceStatus>(status, "maintenance status") is { } s)
        {
            query = query.Where(m => m.Status == s);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(m => m.StartAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<MaintenanceDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<MaintenanceDto> CreateMaintenanceAsync(UpsertMaintenanceRequest req, Guid adminId, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == req.VehicleId, ct) ?? throw DomainException.NotFound("Vehicle");
        var record = new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = vehicle.Id, CreatedAt = Now, CreatedByUserId = adminId };
        await ApplyMaintenanceAsync(record, vehicle, req, ct);
        db.MaintenanceRecords.Add(record);
        vehicle.BookingVersion++;
        audit.Write(adminId, "maintenance.created", nameof(MaintenanceRecord), record.Id.ToString(), new { vehicle.RegistrationNumber, record.Title });
        await db.SaveChangesAsync(ct);
        record.Vehicle = vehicle;
        return ToDto(record);
    }

    public async Task<MaintenanceDto> UpdateMaintenanceAsync(Guid id, UpsertMaintenanceRequest req, Guid adminId, CancellationToken ct)
    {
        var record = await db.MaintenanceRecords.Include(m => m.Vehicle).FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw DomainException.NotFound("Maintenance record");
        if (record.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            throw new DomainException("maintenance.closed", "Completed or cancelled records cannot be edited.", ErrorKind.Conflict);
        }

        await ApplyMaintenanceAsync(record, record.Vehicle!, req, ct);
        record.Vehicle!.BookingVersion++;
        audit.Write(adminId, "maintenance.updated", nameof(MaintenanceRecord), id.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    public async Task<MaintenanceDto> StartMaintenanceAsync(Guid id, Guid adminId, CancellationToken ct)
    {
        var record = await db.MaintenanceRecords.Include(m => m.Vehicle).FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw DomainException.NotFound("Maintenance record");
        if (record.Status != MaintenanceStatus.Scheduled)
        {
            throw new DomainException("maintenance.state", "Only scheduled work can be started.", ErrorKind.Conflict);
        }

        record.Status = MaintenanceStatus.InProgress;
        record.UpdatedAt = Now;
        audit.Write(adminId, "maintenance.started", nameof(MaintenanceRecord), id.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    public async Task<MaintenanceDto> CompleteMaintenanceAsync(Guid id, CompleteMaintenanceRequest req, Guid adminId, CancellationToken ct)
    {
        var record = await db.MaintenanceRecords.Include(m => m.Vehicle).FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw DomainException.NotFound("Maintenance record");
        if (record.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            throw new DomainException("maintenance.closed", "This record is already closed.", ErrorKind.Conflict);
        }

        var vehicle = record.Vehicle!;
        var now = Now;
        var completedAt = req.CompletedAt?.UtcDateTime ?? now;
        if (completedAt < record.StartAt)
        {
            throw new DomainException("maintenance.dates", "Completion cannot be before the start of the work.");
        }

        record.Status = MaintenanceStatus.Completed;
        record.EndAt = completedAt;
        record.Cost = req.Cost ?? record.Cost;
        if (req.OdometerKm is { } km)
        {
            if (km < vehicle.OdometerKm)
            {
                throw new DomainException("vehicle.odometer", $"The odometer cannot go backwards (last reading {vehicle.OdometerKm:N0} km).");
            }

            record.OdometerKm = km;
            vehicle.OdometerKm = km;
            db.MileageRecords.Add(new MileageRecord { Id = Guid.NewGuid(), VehicleId = vehicle.Id, OdometerKm = km, Source = MileageSource.Service, RecordedAt = completedAt });
        }

        if (record.Type == MaintenanceType.Service)
        {
            vehicle.NextServiceDueKm = req.NextServiceDueKm ?? vehicle.OdometerKm + vehicle.ServiceIntervalKm;
            vehicle.NextServiceDueDate = req.NextServiceDueDate?.UtcDateTime ?? completedAt.AddMonths(12);
        }

        if (vehicle.Status == VehicleStatus.UnderMaintenance)
        {
            vehicle.Status = VehicleStatus.Available;
        }

        record.UpdatedAt = now;
        vehicle.UpdatedAt = now;
        vehicle.BookingVersion++;
        audit.Write(adminId, "maintenance.completed", nameof(MaintenanceRecord), id.ToString(), new { record.Cost, record.OdometerKm });
        await db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    public async Task CancelMaintenanceAsync(Guid id, Guid adminId, CancellationToken ct)
    {
        var record = await db.MaintenanceRecords.Include(m => m.Vehicle).FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw DomainException.NotFound("Maintenance record");
        if (record.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            throw new DomainException("maintenance.closed", "This record is already closed.", ErrorKind.Conflict);
        }

        record.Status = MaintenanceStatus.Cancelled;
        record.UpdatedAt = Now;
        record.Vehicle!.BookingVersion++;
        audit.Write(adminId, "maintenance.cancelled", nameof(MaintenanceRecord), id.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Service-due states and repeated-issue flags for the whole fleet.</summary>
    public async Task<IReadOnlyList<MaintenanceAlertDto>> AlertsAsync(CancellationToken ct)
    {
        var now = Now;
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.ArchivedAt == null).ToListAsync(ct);
        var since = now.AddDays(-MaintenanceRules.RepeatedIssueWindowDays);
        var events = await db.MaintenanceRecords.AsNoTracking()
            .Where(m => m.CreatedAt >= since || m.StartAt >= since)
            .Select(m => new { m.VehicleId, m.StartAt, m.Type, m.IsReportedIssue, m.Status })
            .ToListAsync(ct);

        var alerts = new List<MaintenanceAlertDto>();
        foreach (var v in vehicles)
        {
            var state = MaintenanceRules.ServiceState(v.OdometerKm, v.NextServiceDueKm, v.NextServiceDueDate, now);
            var vehicleEvents = events.Where(e => e.VehicleId == v.Id && e.Status != MaintenanceStatus.Cancelled)
                .Select(e => new MaintenanceEvent(e.StartAt, e.Type, e.IsReportedIssue)).ToList();
            var repeated = MaintenanceRules.HasRepeatedIssues(vehicleEvents, now);
            if (state is ServiceDueState.Ok or ServiceDueState.NotScheduled && !repeated)
            {
                continue;
            }

            alerts.Add(new MaintenanceAlertDto(
                v.Id, v.DisplayName, v.RegistrationNumber, state.ToString(), v.OdometerKm, v.NextServiceDueKm, Mapping.Local(v.NextServiceDueDate),
                v.NextServiceDueKm - v.OdometerKm,
                v.NextServiceDueDate is { } due ? (int)Math.Floor((due - now).TotalDays) : null,
                repeated,
                vehicleEvents.Count(e => e.IsReportedIssue || e.Type == MaintenanceType.Repair)));
        }

        return alerts
            .OrderBy(a => a.ServiceState == nameof(ServiceDueState.Overdue) ? 0 : a.ServiceState == nameof(ServiceDueState.DueSoon) ? 1 : 2)
            .ThenBy(a => a.KmRemaining ?? int.MaxValue)
            .ToList();
    }

    // ================================================================== helpers

    private async Task ApplyMaintenanceAsync(MaintenanceRecord record, Vehicle vehicle, UpsertMaintenanceRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 120)
        {
            throw new DomainException("maintenance.title", "Please give the work a title of up to 120 characters.");
        }

        var start = req.StartAt.UtcDateTime;
        var end = req.EndAt?.UtcDateTime;
        if (end is { } e && e <= start)
        {
            throw new DomainException("maintenance.dates", "The end of the maintenance window must be after its start.");
        }

        // Maintenance may not silently override a customer's confirmed rental.
        var windowEnd = end ?? start.AddYears(10);
        var clash = await db.Bookings.AsNoTracking()
            .Where(b => b.VehicleId == vehicle.Id && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active)
                        && b.PickupAt < windowEnd && b.ReturnAt > start)
            .OrderBy(b => b.PickupAt)
            .Select(b => b.Reference)
            .FirstOrDefaultAsync(ct);
        if (clash is not null)
        {
            throw new DomainException("maintenance.booking_clash",
                $"This maintenance window overlaps confirmed booking {clash}. Reschedule or cancel the booking first, or choose another window.",
                ErrorKind.Conflict);
        }

        record.Type = Parse.Enum<MaintenanceType>(req.Type, "maintenance type");
        record.Title = req.Title.Trim();
        record.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        record.StartAt = start;
        record.EndAt = end;
        record.OdometerKm = req.OdometerKm;
        record.Cost = req.Cost is { } cost ? Money.Round(cost) : null;
        record.IsReportedIssue = req.IsReportedIssue;
        record.UpdatedAt = Now;
        if (start <= Now && record.Status == MaintenanceStatus.Scheduled)
        {
            record.Status = MaintenanceStatus.InProgress;
        }
    }

    private static void Apply(Vehicle v, UpsertVehicleRequest r, DateTime now)
    {
        v.RegistrationNumber = r.RegistrationNumber.Trim().ToUpperInvariant();
        v.Make = r.Make.Trim();
        v.Model = r.Model.Trim();
        v.Year = r.Year;
        v.CategoryId = r.CategoryId;
        v.Description = r.Description.Trim();
        v.Seats = r.Seats;
        v.Doors = r.Doors;
        v.LuggageCapacity = r.LuggageCapacity;
        v.Transmission = Parse.Enum<TransmissionType>(r.Transmission, "transmission");
        v.FuelType = Parse.Enum<FuelType>(r.FuelType, "fuel type");
        v.FuelEconomyLitresPer100Km = r.FuelEconomyLitresPer100Km;
        v.ComfortRating = r.ComfortRating;
        v.Features = (r.Features ?? []).Select(f => f.Trim()).Where(f => f.Length is > 0 and <= 60).Distinct().Take(20).ToList();
        v.DailyRate = Money.Round(r.DailyRate);
        v.DailyMileageLimitKm = r.DailyMileageLimitKm;
        v.ExcessMileageRatePerKm = r.ExcessMileageRatePerKm;
        v.DepositAmount = Money.Round(r.DepositAmount);
        v.OdometerKm = r.OdometerKm;
        v.ServiceIntervalKm = r.ServiceIntervalKm;
        v.NextServiceDueKm = r.NextServiceDueKm;
        v.NextServiceDueDate = r.NextServiceDueDate?.UtcDateTime;
        v.VideoUrl = string.IsNullOrWhiteSpace(r.VideoUrl) ? null : r.VideoUrl.Trim();
        v.UpdatedAt = now;
    }

    private async Task EnsureUniqueRegistrationAsync(string registration, Guid? exceptId, CancellationToken ct)
    {
        var normalised = registration.Trim().ToUpperInvariant();
        if (await db.Vehicles.AnyAsync(v => v.RegistrationNumber == normalised && v.Id != exceptId, ct))
        {
            throw new DomainException("vehicle.registration_taken", $"A vehicle with registration {normalised} already exists.", ErrorKind.Conflict);
        }
    }

    private async Task EnsureCategoryAsync(int categoryId, CancellationToken ct)
    {
        if (!await db.VehicleCategories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new DomainException("vehicle.category", "Please choose a valid category.");
        }
    }

    private async Task EnsureNoUpcomingBookingsAsync(Guid vehicleId, string action, CancellationToken ct)
    {
        var now = Now;
        var reference = await db.Bookings.AsNoTracking()
            .Where(b => b.VehicleId == vehicleId && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active
                                                     || (b.Status == BookingStatus.Pending && b.HoldExpiresAt > now))
                        && b.ReturnAt > now)
            .OrderBy(b => b.PickupAt).Select(b => b.Reference).FirstOrDefaultAsync(ct);
        if (reference is not null)
        {
            throw new DomainException("vehicle.has_bookings", $"Cannot {action} this vehicle while it has upcoming booking {reference}.", ErrorKind.Conflict);
        }
    }

    private async Task<IReadOnlyList<AdminVehicleDto>> ToAdminDtosAsync(List<Vehicle> vehicles, CancellationToken ct)
    {
        var ids = vehicles.Select(v => v.Id).ToList();
        var ratings = await catalog.RatingsAsync(ids, ct);
        var images = await catalog.PrimaryImagesAsync(ids, ct);
        var states = await availability.CurrentStatesAsync(vehicles, ct);
        var now = Now;
        var upcoming = await db.Bookings.AsNoTracking()
            .Where(b => ids.Contains(b.VehicleId) && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Pending) && b.PickupAt > now)
            .GroupBy(b => b.VehicleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return vehicles.Select(v => new AdminVehicleDto(
            v.ToSummary(images.GetValueOrDefault(v.Id), ratings.TryGetValue(v.Id, out var r) ? r : null, states[v.Id], null),
            v.RegistrationNumber,
            v.Status.ToString(),
            v.OdometerKm,
            v.ServiceIntervalKm,
            v.NextServiceDueKm,
            Mapping.Local(v.NextServiceDueDate),
            MaintenanceRules.ServiceState(v.OdometerKm, v.NextServiceDueKm, v.NextServiceDueDate, now).ToString(),
            upcoming.GetValueOrDefault(v.Id),
            Mapping.Local(v.UpdatedAt),
            Mapping.Local(v.ArchivedAt))).ToList();
    }

    private static string ValidateImage(string fileName, MemoryStream buffer, long maxBytes) =>
        ImageValidator.Validate(fileName, buffer.Length, buffer.GetBuffer().AsSpan(0, (int)Math.Min(16, buffer.Length)), maxBytes);

    private static MaintenanceDto ToDto(MaintenanceRecord m) => new(
        m.Id, m.VehicleId, m.Vehicle?.DisplayName ?? "", m.Vehicle?.RegistrationNumber ?? "", m.Type.ToString(), m.Status.ToString(),
        m.Title, m.Description, Mapping.Local(m.StartAt), Mapping.Local(m.EndAt), m.OdometerKm, m.Cost, m.IsReportedIssue, m.IsSampleData);
}
