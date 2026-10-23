using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Services;

public sealed class AccountService(
    SmartDriveDbContext db,
    UserManager<AppUser> users,
    ITokenService tokens,
    TimeProvider clock,
    AuditWriter audit)
{
    /// <summary>Self-registration always creates a Customer; administrator rights can only be granted by an administrator.</summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
        {
            throw new DomainException("account.email_taken", "An account with this email address already exists.", ErrorKind.Conflict);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber.Trim(),
            CreatedAt = now,
            Profile = new CustomerProfile { CreatedAt = now, UpdatedAt = now },
        };

        var created = await users.CreateAsync(user, req.Password);
        if (!created.Succeeded)
        {
            throw new DomainException("account.invalid", string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, Roles.Customer);
        audit.Write(user.Id, "account.registered", "User", user.Id.ToString());
        await db.SaveChangesAsync(ct);
        return await IssueAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        const string failure = "The email address or password is incorrect.";
        var user = await users.FindByEmailAsync(req.Email.Trim());
        if (user is null)
        {
            throw new DomainException("auth.invalid", failure, ErrorKind.Validation);
        }

        if (await users.IsLockedOutAsync(user))
        {
            throw new DomainException("auth.locked", "Too many failed attempts. Please try again in a few minutes.", ErrorKind.Forbidden);
        }

        if (!await users.CheckPasswordAsync(user, req.Password))
        {
            await users.AccessFailedAsync(user);
            throw new DomainException("auth.invalid", failure, ErrorKind.Validation);
        }

        if (!user.IsActive)
        {
            throw new DomainException("auth.inactive", "This account has been deactivated. Please contact SmartDrive.", ErrorKind.Forbidden);
        }

        await users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = clock.GetUtcNow().UtcDateTime;
        await users.UpdateAsync(user);
        return await IssueAsync(user);
    }

    public async Task<UserDto> MeAsync(Guid userId, CancellationToken ct)
    {
        var user = await ActiveUserAsync(userId);
        return ToDto(user, await users.GetRolesAsync(user));
    }

    public async Task<ProfileDto> ProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await ActiveUserAsync(userId);
        var profile = await db.CustomerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return ToProfile(user, await users.GetRolesAsync(user), profile);
    }

    public async Task<ProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest req, CancellationToken ct)
    {
        var user = await ActiveUserAsync(userId);
        var now = clock.GetUtcNow().UtcDateTime;
        user.FirstName = req.FirstName.Trim();
        user.LastName = req.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber.Trim();

        var profile = await db.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new CustomerProfile { UserId = userId, CreatedAt = now };
            db.CustomerProfiles.Add(profile);
        }

        profile.AddressLine = Clean(req.AddressLine);
        profile.Suburb = Clean(req.Suburb);
        profile.City = Clean(req.City);
        profile.Province = Clean(req.Province);
        profile.PostalCode = Clean(req.PostalCode);
        profile.DateOfBirth = req.DateOfBirth;
        profile.DriverLicenceExpiry = req.DriverLicenceExpiry;
        profile.UpdatedAt = now;

        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new DomainException("account.invalid", string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        audit.Write(userId, "account.profile_updated", "User", userId.ToString());
        await db.SaveChangesAsync(ct);
        return ToProfile(user, await users.GetRolesAsync(user), profile);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await ActiveUserAsync(userId);
        var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
        {
            throw new DomainException("account.password", string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        audit.Write(userId, "account.password_changed", "User", userId.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationDto>> NotificationsAsync(Guid userId, CancellationToken ct) =>
        (await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).Take(30).ToListAsync(ct))
        .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.Link, n.IsRead, Mapping.Local(n.CreatedAt)))
        .ToList();

    public async Task MarkNotificationsReadAsync(Guid userId, CancellationToken ct)
    {
        var unread = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        foreach (var n in unread)
        {
            n.IsRead = true;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitContactAsync(ContactRequest req, CancellationToken ct)
    {
        db.ContactMessages.Add(new ContactMessage
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Email = req.Email.Trim(),
            Subject = req.Subject.Trim(),
            Message = req.Message.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<AppUser> ActiveUserAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is { IsActive: true }
            ? user
            : throw new DomainException("auth.inactive", "This account is not active.", ErrorKind.Forbidden);
    }

    private async Task<AuthResponse> IssueAsync(AppUser user)
    {
        var roles = (await users.GetRolesAsync(user)).ToList();
        var (token, expires) = tokens.CreateToken(user, roles);
        return new AuthResponse(token, expires, ToDto(user, roles));
    }

    private static UserDto ToDto(AppUser u, IList<string> roles) =>
        new(u.Id, u.Email ?? "", u.FirstName, u.LastName, u.PhoneNumber, roles.ToList());

    private static ProfileDto ToProfile(AppUser u, IList<string> roles, CustomerProfile? p) =>
        new(ToDto(u, roles), p?.AddressLine, p?.Suburb, p?.City, p?.Province, p?.PostalCode, p?.DateOfBirth, p?.DriverLicenceExpiry);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
