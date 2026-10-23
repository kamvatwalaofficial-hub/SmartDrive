using System.Text.Json;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.Application.Services;

/// <summary>The signed-in user, resolved from the validated JWT by the API layer.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAdmin { get; }

    Guid RequireUserId() => UserId ?? throw new DomainException("auth.required", "You need to sign in.", ErrorKind.Forbidden);
}

/// <summary>Issues access tokens. Implemented in the API layer, which owns the signing key.</summary>
public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateToken(AppUser user, IReadOnlyList<string> roles);
}

/// <summary>Where uploaded vehicle media is stored. SQL Server only keeps the URL and metadata.</summary>
public interface IMediaStorage
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct);

    Task DeleteAsync(string url, CancellationToken ct);
}

public sealed class MediaStorageOptions
{
    /// <summary>Folder on disk, normally the API's wwwroot/media/uploads.</summary>
    public string RootPath { get; set; } = "wwwroot/media/uploads";

    /// <summary>URL prefix the files are served from.</summary>
    public string PublicBasePath { get; set; } = "/media/uploads";

    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
}

public sealed class LocalMediaStorage(MediaStorageOptions options) : IMediaStorage
{
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        Directory.CreateDirectory(options.RootPath);
        var name = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(options.RootPath, name);
        await using (var file = File.Create(path))
        {
            await content.CopyToAsync(file, ct);
        }

        return $"{options.PublicBasePath.TrimEnd('/')}/{name}";
    }

    public Task DeleteAsync(string url, CancellationToken ct)
    {
        if (url.StartsWith(options.PublicBasePath, StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(url);
            var path = Path.Combine(options.RootPath, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>Validates uploaded images by size, extension <em>and</em> file signature (not just the declared type).</summary>
public static class ImageValidator
{
    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]],
    };

    public static string Validate(string fileName, long length, ReadOnlySpan<byte> header, long maxBytes)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!Signatures.TryGetValue(ext, out var signatures))
        {
            throw new DomainException("media.type", "Only JPG, PNG and WebP images can be uploaded.");
        }

        if (length <= 0 || length > maxBytes)
        {
            throw new DomainException("media.size", $"Images must be smaller than {maxBytes / (1024 * 1024)} MB.");
        }

        var ok = false;
        foreach (var signature in signatures)
        {
            if (header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature))
            {
                ok = true;
            }
        }

        if (ext == ".webp" && ok)
        {
            ok = header.Length >= 12 && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P';
        }

        if (!ok)
        {
            throw new DomainException("media.signature", "The file content does not match its image type.");
        }

        return ext == ".jpeg" ? ".jpg" : ext;
    }
}

/// <summary>Records important actions. Details must never contain secrets, tokens or card data.</summary>
public sealed class AuditWriter(SmartDriveDbContext db, TimeProvider clock)
{
    public void Write(Guid? userId, string action, string entityType, string entityId, object? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : Truncate(JsonSerializer.Serialize(details), 2000),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed class Notifier(SmartDriveDbContext db, TimeProvider clock)
{
    public void Notify(Guid userId, string title, string message, string? link = null)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Link = link,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
    }
}

internal static class Parse
{
    public static TEnum Enum<TEnum>(string? value, string field)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(value?.Replace(" ", "", StringComparison.Ordinal), ignoreCase: true, out var result) && System.Enum.IsDefined(result)
            ? result
            : throw new DomainException($"{field}.invalid", $"'{value}' is not a valid {field}.");

    public static TEnum? OptionalEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value) || value.Equals("any", StringComparison.OrdinalIgnoreCase) ? null : Enum<TEnum>(value, field);

    public static JourneyType? Journey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "city" => JourneyType.City,
            "business" => JourneyType.Business,
            "family" => JourneyType.Family,
            "roadtrip" => JourneyType.RoadTrip,
            _ => throw new DomainException("journey.invalid", $"'{value}' is not a valid journey type."),
        };
}

internal static class Mapping
{
    public static DateTimeOffset Local(DateTime utc) => SouthAfricanTime.ToLocal(utc);

    public static DateTimeOffset? Local(DateTime? utc) => utc is null ? null : SouthAfricanTime.ToLocal(utc.Value);

    public static string JourneyName(JourneyType journey) => journey switch
    {
        JourneyType.RoadTrip => "road-trip",
        _ => journey.ToString().ToLowerInvariant(),
    };

    public static MediaDto ToDto(this VehicleMedia m) =>
        new(m.Id, m.Kind.ToString(), m.Url, m.AltText, m.IsPrimary, m.SortOrder, m.Attribution, m.IsSampleData);

    public static VehicleMedia? PrimaryImage(this IEnumerable<VehicleMedia> media) =>
        media.Where(m => m.Kind != MediaKind.Video).OrderByDescending(m => m.IsPrimary).ThenBy(m => m.SortOrder).FirstOrDefault();

    public static VehicleSummaryDto ToSummary(
        this Vehicle v,
        VehicleMedia? image,
        (double Average, int Count)? rating,
        VehicleAvailabilityState state,
        bool? availableForPeriod) =>
        new(
            v.Id,
            v.Make,
            v.Model,
            v.Year,
            v.DisplayName,
            v.Category?.Name ?? "",
            v.Category?.Slug ?? "",
            v.Seats,
            v.Doors,
            v.LuggageCapacity,
            v.Transmission.ToString(),
            v.FuelType.ToString(),
            v.DailyRate,
            image?.Url,
            image?.AltText,
            rating is { Count: > 0 } r ? Math.Round(r.Average, 1) : null,
            rating?.Count ?? 0,
            state.ToString(),
            availableForPeriod,
            !string.IsNullOrWhiteSpace(v.ModelAssetUrl),
            v.IsSampleData);

    public static PriceLineDto ToDto(this PriceLine l) => new(l.Type.ToString(), l.Description, l.Amount);

    public static PriceLineDto ToDto(this BookingCharge c) => new(c.Type.ToString(), c.Description, c.Amount);

    public static string AuthorName(string firstName, string lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName : $"{firstName} {char.ToUpperInvariant(lastName[0])}.";
}
