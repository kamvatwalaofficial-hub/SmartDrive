using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Application.Identity;
using SmartDrive.Domain.Common;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Services;

/// <summary>
/// Administrator tools for customer accounts: search, activate or deactivate, grant or revoke roles,
/// and browse the audit log. Admins cannot lock themselves out, and the last administrator cannot be removed.
/// </summary>
public sealed class CustomerAdminService(
    SmartDriveDbContext db,
    UserManager<AppUser> users,
    AuditWriter audit)
{
    private static readonly BookingStatus[] SpendStatuses = [BookingStatus.Confirmed, BookingStatus.Active, BookingStatus.Completed];

    public async Task<PagedResult<AdminCustomerDto>> ListAsync(string? q, string? role, bool? isActive, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalise(page, pageSize);
        var query = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u => u.Email!.Contains(term) || u.FirstName.Contains(term) || u.LastName.Contains(term) || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }

        if (isActive is not null)
        {
            query = query.Where(u => u.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleId = await db.Roles.Where(r => r.Name == role).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
            query = roleId is null ? query.Where(_ => false) : query.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }

        var total = await query.CountAsync(ct);
        var pageUsers = await query
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var items = await ToDtosAsync(pageUsers, ct);
        return new PagedResult<AdminCustomerDto>(items, page, pageSize, total);
    }

    public async Task<AdminCustomerDto> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw DomainException.NotFound("Customer");
        return (await ToDtosAsync([user], ct))[0];
    }

    public async Task<AdminCustomerDto> SetActiveAsync(Guid id, bool isActive, Guid adminId, CancellationToken ct)
    {
        if (id == adminId && !isActive)
        {
            throw new DomainException("account.self_deactivate", "You cannot deactivate your own account.", ErrorKind.Conflict);
        }

        var user = await users.FindByIdAsync(id.ToString()) ?? throw DomainException.NotFound("Customer");
        if (!isActive && await users.IsInRoleAsync(user, Roles.Administrator) && await ActiveAdminCountAsync(ct) <= 1)
        {
            throw new DomainException("account.last_admin", "The last active administrator cannot be deactivated.", ErrorKind.Conflict);
        }

        user.IsActive = isActive;

        // A deactivated account also gets a new security stamp, which invalidates any refresh of its session.
        var result = await users.UpdateAsync(user);
        if (result.Succeeded && !isActive)
        {
            await users.UpdateSecurityStampAsync(user);
        }

        EnsureSucceeded(result);
        audit.Write(adminId, isActive ? "customer.activated" : "customer.deactivated", "User", id.ToString());
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AdminCustomerDto> SetRoleAsync(Guid id, string role, bool grant, Guid adminId, CancellationToken ct)
    {
        var normalised = role.Trim();
        if (normalised != Roles.Customer && normalised != Roles.Administrator)
        {
            throw new DomainException("role.invalid", $"'{role}' is not a valid role.");
        }

        var user = await users.FindByIdAsync(id.ToString()) ?? throw DomainException.NotFound("Customer");
        var hasRole = await users.IsInRoleAsync(user, normalised);

        if (grant && !hasRole)
        {
            EnsureSucceeded(await users.AddToRoleAsync(user, normalised));
        }
        else if (!grant && hasRole)
        {
            if (normalised == Roles.Administrator)
            {
                if (id == adminId)
                {
                    throw new DomainException("role.self_revoke", "You cannot remove your own administrator role.", ErrorKind.Conflict);
                }

                if (await ActiveAdminCountAsync(ct) <= 1)
                {
                    throw new DomainException("role.last_admin", "At least one administrator must remain.", ErrorKind.Conflict);
                }
            }

            EnsureSucceeded(await users.RemoveFromRoleAsync(user, normalised));
        }

        audit.Write(adminId, grant ? "role.granted" : "role.revoked", "User", id.ToString(), new { Role = normalised });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PagedResult<AuditLogDto>> AuditLogAsync(string? action, string? entityType, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalise(page, pageSize, max: 200);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action.StartsWith(action));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.Action, a.EntityType, a.EntityId, a.Details, a.CreatedAt,
                Email = db.Users.Where(u => u.Id == a.UserId).Select(u => u.Email).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AuditLogDto(r.Id, r.Email, r.Action, r.EntityType, r.EntityId, r.Details, Mapping.Local(r.CreatedAt))).ToList();
        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<ContactMessageDto>> MessagesAsync(bool? handled, int page, int pageSize, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalise(page, pageSize);
        var query = db.ContactMessages.AsNoTracking().AsQueryable();
        if (handled is not null)
        {
            query = query.Where(m => m.IsHandled == handled);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(m => new ContactMessageDto(m.Id, m.Name, m.Email, m.Subject, m.Message, m.IsHandled, Mapping.Local(m.CreatedAt))).ToList();
        return new PagedResult<ContactMessageDto>(items, page, pageSize, total);
    }

    public async Task SetMessageHandledAsync(Guid id, bool handled, Guid adminId, CancellationToken ct)
    {
        var message = await db.ContactMessages.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw DomainException.NotFound("Message");
        message.IsHandled = handled;
        audit.Write(adminId, handled ? "message.handled" : "message.reopened", "ContactMessage", id.ToString());
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<int> ActiveAdminCountAsync(CancellationToken ct)
    {
        var adminRoleId = await db.Roles.Where(r => r.Name == Roles.Administrator).Select(r => r.Id).FirstAsync(ct);
        return await db.Users.CountAsync(u => u.IsActive && db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId), ct);
    }

    private async Task<List<AdminCustomerDto>> ToDtosAsync(IReadOnlyList<AppUser> pageUsers, CancellationToken ct)
    {
        var ids = pageUsers.Select(u => u.Id).ToList();
        var roleRows = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              where ids.Contains(ur.UserId)
                              select new { ur.UserId, r.Name }).ToListAsync(ct);
        var bookingRows = await db.Bookings.AsNoTracking()
            .Where(b => ids.Contains(b.CustomerId))
            .Select(b => new { b.CustomerId, b.Status, b.TotalAmount })
            .ToListAsync(ct);

        return pageUsers.Select(u =>
        {
            var mine = bookingRows.Where(b => b.CustomerId == u.Id).ToList();
            var spend = mine.Where(b => SpendStatuses.Contains(b.Status)).Sum(b => b.TotalAmount);
            return new AdminCustomerDto(
                u.Id,
                u.FullName,
                u.Email ?? "",
                u.PhoneNumber,
                u.IsActive,
                roleRows.Where(r => r.UserId == u.Id).Select(r => r.Name!).OrderBy(n => n).ToList(),
                mine.Count,
                Money.Round(spend),
                Mapping.Local(u.CreatedAt),
                Mapping.Local(u.LastLoginAt),
                u.IsSampleData);
        }).ToList();
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new DomainException("account.update_failed", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
