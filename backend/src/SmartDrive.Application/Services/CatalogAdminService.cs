using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Services;

public sealed record UpsertCategoryRequest(string Name, string Slug, string Description, int SortOrder);

public sealed record UpsertLocationRequest(string Name, string Code, string AddressLine, string City, string Province, bool IsAirport, bool IsActive);

public sealed record AdminLocationDto(int Id, string Name, string Code, string AddressLine, string City, string Province, bool IsAirport, bool IsActive);

/// <summary>Administrator management of vehicle categories and rental branches.</summary>
public sealed class CatalogAdminService(SmartDriveDbContext db, AuditWriter audit)
{
    public async Task<CategoryDto> CreateCategoryAsync(UpsertCategoryRequest req, Guid adminId, CancellationToken ct)
    {
        var slug = NormaliseSlug(req.Slug);
        if (await db.VehicleCategories.AnyAsync(c => c.Slug == slug, ct))
        {
            throw new DomainException("category.slug_taken", $"A category with the slug '{slug}' already exists.", ErrorKind.Conflict);
        }

        var category = new VehicleCategory { Name = req.Name.Trim(), Slug = slug, Description = req.Description.Trim(), SortOrder = req.SortOrder };
        db.VehicleCategories.Add(category);
        await db.SaveChangesAsync(ct);
        audit.Write(adminId, "category.created", nameof(VehicleCategory), category.Id.ToString(), new { category.Name, category.Slug });
        await db.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name, category.Slug, category.Description, 0);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(int id, UpsertCategoryRequest req, Guid adminId, CancellationToken ct)
    {
        var category = await db.VehicleCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Category");
        var slug = NormaliseSlug(req.Slug);
        if (slug != category.Slug && await db.VehicleCategories.AnyAsync(c => c.Slug == slug, ct))
        {
            throw new DomainException("category.slug_taken", $"A category with the slug '{slug}' already exists.", ErrorKind.Conflict);
        }

        category.Name = req.Name.Trim();
        category.Slug = slug;
        category.Description = req.Description.Trim();
        category.SortOrder = req.SortOrder;
        audit.Write(adminId, "category.updated", nameof(VehicleCategory), id.ToString(), new { category.Name, category.Slug });
        await db.SaveChangesAsync(ct);
        var count = await db.Vehicles.CountAsync(v => v.CategoryId == id && v.ArchivedAt == null, ct);
        return new CategoryDto(category.Id, category.Name, category.Slug, category.Description, count);
    }

    public async Task DeleteCategoryAsync(int id, Guid adminId, CancellationToken ct)
    {
        var category = await db.VehicleCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Category");
        if (await db.Vehicles.AnyAsync(v => v.CategoryId == id, ct))
        {
            throw new DomainException("category.in_use", "This category still has vehicles. Move or archive them first.", ErrorKind.Conflict);
        }

        db.VehicleCategories.Remove(category);
        audit.Write(adminId, "category.deleted", nameof(VehicleCategory), id.ToString(), new { category.Slug });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AdminLocationDto>> LocationsAsync(CancellationToken ct) =>
        await db.Locations.AsNoTracking().OrderBy(l => l.City).ThenBy(l => l.Name)
            .Select(l => new AdminLocationDto(l.Id, l.Name, l.Code, l.AddressLine, l.City, l.Province, l.IsAirport, l.IsActive))
            .ToListAsync(ct);

    public async Task<AdminLocationDto> UpsertLocationAsync(int? id, UpsertLocationRequest req, Guid adminId, CancellationToken ct)
    {
        var code = req.Code.Trim().ToUpperInvariant();
        if (await db.Locations.AnyAsync(l => l.Code == code && l.Id != (id ?? 0), ct))
        {
            throw new DomainException("location.code_taken", $"A branch with the code '{code}' already exists.", ErrorKind.Conflict);
        }

        var location = id is null ? new Location() : await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw DomainException.NotFound("Branch");
        location.Name = req.Name.Trim();
        location.Code = code;
        location.AddressLine = req.AddressLine.Trim();
        location.City = req.City.Trim();
        location.Province = req.Province.Trim();
        location.IsAirport = req.IsAirport;
        location.IsActive = req.IsActive;
        if (id is null)
        {
            db.Locations.Add(location);
        }

        await db.SaveChangesAsync(ct);
        audit.Write(adminId, id is null ? "location.created" : "location.updated", nameof(Location), location.Id.ToString(), new { location.Code });
        await db.SaveChangesAsync(ct);
        return new AdminLocationDto(location.Id, location.Name, location.Code, location.AddressLine, location.City, location.Province, location.IsAirport, location.IsActive);
    }

    private static string NormaliseSlug(string slug)
    {
        var s = new string(slug.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrEmpty(s) ? throw new DomainException("category.slug", "Enter a slug of letters, digits and hyphens.") : s;
    }
}
