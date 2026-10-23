using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Services;

/// <summary>Public vehicle catalogue: categories, branches, search, featured vehicles and details.</summary>
public sealed class CatalogService(SmartDriveDbContext db, AvailabilityService availability, RentalPolicy policy)
{
    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct) =>
        await db.VehicleCategories.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description,
                c.Vehicles.Count(v => v.ArchivedAt == null && v.Status != VehicleStatus.Inactive)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LocationDto>> LocationsAsync(CancellationToken ct) =>
        await db.Locations.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.City).ThenBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name, l.Code, l.AddressLine, l.City, l.Province, l.IsAirport))
            .ToListAsync(ct);

    public async Task<PagedResult<VehicleSummaryDto>> SearchAsync(VehicleSearchQuery q, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalise(q.Page, q.PageSize, 48);
        var period = ResolvePeriod(q.PickupAt, q.ReturnAt);

        // 1. Filter in the database.
        var query = Rentable().Include(v => v.Category).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(v => v.Make.Contains(term) || v.Model.Contains(term) || v.Category!.Name.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            query = query.Where(v => v.Category!.Slug == q.Category);
        }

        if (Parse.OptionalEnum<TransmissionType>(q.Transmission, "transmission") is { } transmission)
        {
            query = query.Where(v => v.Transmission == transmission);
        }

        if (Parse.OptionalEnum<FuelType>(q.Fuel, "fuel type") is { } fuel)
        {
            query = query.Where(v => v.FuelType == fuel);
        }

        if (q.MinSeats is { } seats)
        {
            query = query.Where(v => v.Seats >= seats);
        }

        if (q.MinLuggage is { } luggage)
        {
            query = query.Where(v => v.LuggageCapacity >= luggage);
        }

        if (q.MaxDailyRate is { } max)
        {
            query = query.Where(v => v.DailyRate <= max);
        }

        var vehicles = await query.ToListAsync(ct);

        // 2. Availability and ratings (shared rules), then sort and page.
        var ids = vehicles.Select(v => v.Id).ToList();
        var ratings = await RatingsAsync(ids, ct);
        var states = await availability.CurrentStatesAsync(vehicles, ct);
        Dictionary<Guid, bool>? availableForPeriod = null;
        if (period is { } p)
        {
            var slots = await availability.LoadSlotsAsync(ids, p.Pickup, p.Return, ct);
            availableForPeriod = vehicles.ToDictionary(v => v.Id, v => availability.Evaluate(v, p.Pickup, p.Return, slots).IsAvailable);
            if (q.AvailableOnly)
            {
                vehicles = vehicles.Where(v => availableForPeriod[v.Id]).ToList();
            }
        }

        IEnumerable<Vehicle> sorted = q.Sort switch
        {
            "price-asc" => vehicles.OrderBy(v => v.DailyRate),
            "price-desc" => vehicles.OrderByDescending(v => v.DailyRate),
            "rating" => vehicles.OrderByDescending(v => ratings.TryGetValue(v.Id, out var r) ? r.Average : 0).ThenByDescending(v => ratings.TryGetValue(v.Id, out var r) ? r.Count : 0),
            "newest" => vehicles.OrderByDescending(v => v.Year).ThenBy(v => v.DailyRate),
            "seats" => vehicles.OrderByDescending(v => v.Seats).ThenBy(v => v.DailyRate),
            _ => vehicles
                .OrderByDescending(v => availableForPeriod is null || availableForPeriod[v.Id])
                .ThenBy(v => v.Category!.SortOrder).ThenBy(v => v.DailyRate),
        };

        var total = vehicles.Count;
        var pageItems = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var media = await PrimaryImagesAsync(pageItems.Select(v => v.Id).ToList(), ct);

        var items = pageItems.Select(v => v.ToSummary(
            media.GetValueOrDefault(v.Id),
            ratings.TryGetValue(v.Id, out var r) ? r : null,
            states[v.Id],
            availableForPeriod?[v.Id])).ToList();

        return new PagedResult<VehicleSummaryDto>(items, page, pageSize, total);
    }

    /// <summary>A small, varied selection: the best-rated vehicle of each category.</summary>
    public async Task<IReadOnlyList<VehicleSummaryDto>> FeaturedAsync(DateTimeOffset? pickupAt, DateTimeOffset? returnAt, int count, CancellationToken ct)
    {
        var vehicles = await Rentable().Include(v => v.Category).AsNoTracking().ToListAsync(ct);
        var ids = vehicles.Select(v => v.Id).ToList();
        var ratings = await RatingsAsync(ids, ct);

        var picks = vehicles
            .GroupBy(v => v.CategoryId)
            .Select(g => g.OrderByDescending(v => ratings.TryGetValue(v.Id, out var r) ? r.Average : 0).ThenByDescending(v => v.Year).First())
            .OrderBy(v => v.Category!.SortOrder)
            .Take(Math.Clamp(count, 1, 12))
            .ToList();

        var states = await availability.CurrentStatesAsync(picks, ct);
        var media = await PrimaryImagesAsync(picks.Select(v => v.Id).ToList(), ct);
        Dictionary<Guid, bool>? available = null;
        if (ResolvePeriod(pickupAt, returnAt) is { } p)
        {
            var slots = await availability.LoadSlotsAsync(picks.Select(v => v.Id).ToList(), p.Pickup, p.Return, ct);
            available = picks.ToDictionary(v => v.Id, v => availability.Evaluate(v, p.Pickup, p.Return, slots).IsAvailable);
        }

        return picks.Select(v => v.ToSummary(
            media.GetValueOrDefault(v.Id),
            ratings.TryGetValue(v.Id, out var r) ? r : null,
            states[v.Id],
            available?[v.Id])).ToList();
    }

    public async Task<VehicleDetailDto> DetailAsync(Guid id, CancellationToken ct)
    {
        var v = await db.Vehicles.AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Media)
            .FirstOrDefaultAsync(x => x.Id == id && x.ArchivedAt == null, ct)
            ?? throw DomainException.NotFound("Vehicle");

        var states = await availability.CurrentStatesAsync([v], ct);
        var ratings = await RatingSummaryAsync(id, ct);
        var media = v.Media.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.SortOrder).ToList();

        var summary = v.ToSummary(
            media.PrimaryImage(),
            ratings.Average is { } avg ? (avg, ratings.Count) : null,
            states[v.Id],
            null);

        return new VehicleDetailDto(
            summary,
            v.Description,
            v.Features,
            v.FuelEconomyLitresPer100Km,
            v.ComfortRating,
            v.DailyMileageLimitKm,
            v.ExcessMileageRatePerKm,
            v.DepositAmount,
            media.Where(m => m.Kind != MediaKind.Video).Select(m => m.ToDto()).ToList(),
            v.VideoUrl ?? media.FirstOrDefault(m => m.Kind == MediaKind.Video)?.Url,
            v.ModelAssetUrl,
            v.ModelHasInterior,
            v.ModelAttribution,
            ratings);
    }

    public async Task<AvailabilityDto> AvailabilityAsync(Guid id, DateTimeOffset pickupAt, DateTimeOffset returnAt, CancellationToken ct)
    {
        var v = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Vehicle");
        var pickup = pickupAt.UtcDateTime;
        var ret = returnAt.UtcDateTime;
        var problems = RentalPeriod.Validate(pickup, ret, availability.UtcNow, policy);
        if (problems.Count > 0)
        {
            throw new DomainException("rental.invalid_period", string.Join(" ", problems));
        }

        var result = await availability.CheckAsync(v, pickup, ret, ct);
        DateTime? next = null;
        if (!result.IsAvailable)
        {
            next = await availability.NextAvailableAsync(v, pickup, ret, ct);
        }

        return new AvailabilityDto(result.IsAvailable, result.Reasons.Select(AvailabilityRules.Describe).ToList(), Mapping.Local(next));
    }

    public async Task<RatingSummaryDto> RatingSummaryAsync(Guid vehicleId, CancellationToken ct)
    {
        var rows = await db.Reviews.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId && (r.Status == ReviewStatus.Published || r.Status == ReviewStatus.Reported))
            .Select(r => new { r.Overall, r.Condition, r.Cleanliness, r.Comfort, r.Value })
            .ToListAsync(ct);

        var distribution = Enumerable.Range(1, 5).ToDictionary(i => i, i => rows.Count(r => r.Overall == i));
        if (rows.Count == 0)
        {
            return new RatingSummaryDto(null, 0, distribution, null, null, null, null);
        }

        return new RatingSummaryDto(
            Math.Round(rows.Average(r => r.Overall), 1),
            rows.Count,
            distribution,
            Math.Round(rows.Average(r => r.Condition), 1),
            Math.Round(rows.Average(r => r.Cleanliness), 1),
            Math.Round(rows.Average(r => r.Comfort), 1),
            Math.Round(rows.Average(r => r.Value), 1));
    }

    // ------------------------------------------------------------------ helpers shared with other services

    internal IQueryable<Vehicle> Rentable() =>
        db.Vehicles.Where(v => v.ArchivedAt == null && v.Status != VehicleStatus.Inactive);

    internal async Task<Dictionary<Guid, (double Average, int Count)>> RatingsAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        var rows = await db.Reviews.AsNoTracking()
            .Where(r => vehicleIds.Contains(r.VehicleId) && (r.Status == ReviewStatus.Published || r.Status == ReviewStatus.Reported))
            .GroupBy(r => r.VehicleId)
            .Select(g => new { VehicleId = g.Key, Average = g.Average(r => (double)r.Overall), Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.VehicleId, r => (r.Average, r.Count));
    }

    internal async Task<Dictionary<Guid, VehicleMedia>> PrimaryImagesAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        var media = await db.VehicleMedia.AsNoTracking()
            .Where(m => vehicleIds.Contains(m.VehicleId) && m.Kind != MediaKind.Video)
            .ToListAsync(ct);
        return media.GroupBy(m => m.VehicleId).ToDictionary(g => g.Key, g => g.PrimaryImage()!);
    }

    private (DateTime Pickup, DateTime Return)? ResolvePeriod(DateTimeOffset? pickupAt, DateTimeOffset? returnAt)
    {
        if (pickupAt is null || returnAt is null)
        {
            return null;
        }

        var pickup = pickupAt.Value.UtcDateTime;
        var ret = returnAt.Value.UtcDateTime;
        if (ret <= pickup)
        {
            throw new DomainException("rental.invalid_period", "The return date and time must be after the pickup date and time.");
        }

        return (pickup, ret);
    }
}
