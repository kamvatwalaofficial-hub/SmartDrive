using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartDrive.Application.Data;
using SmartDrive.Application.Identity;
using SmartDrive.Application.Services;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Api.Infrastructure;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "SmartDrive.Api";
    public string Audience { get; set; } = "SmartDrive.Web";

    /// <summary>HMAC-SHA256 signing key (at least 32 characters). Supply it with user-secrets or an environment variable.</summary>
    public string SigningKey { get; set; } = "";

    public int ExpiryMinutes { get; set; } = 120;

    public SymmetricSecurityKey SecurityKey => new(Encoding.UTF8.GetBytes(SigningKey));

    /// <summary>
    /// Ensures a usable signing key. Outside Development and Testing a missing or short key stops the
    /// application from starting; in Development a random key is generated for this run only.
    /// </summary>
    public static JwtOptions Resolve(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        var options = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (options.SigningKey.Length >= 32)
        {
            return options;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be set to a secret of at least 32 characters (use user-secrets or the Jwt__SigningKey environment variable).");
        }

        options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        logger.LogWarning("Jwt:SigningKey is not configured: using a random development key. Sign-ins will not survive a restart.");
        return options;
    }
}

/// <summary>Issues short-lived JWT access tokens. Tokens carry the user id, email, name and roles; never secrets.</summary>
public sealed class JwtTokenService(JwtOptions options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler handler = new();

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(AppUser user, IReadOnlyList<string> roles)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(options.ExpiryMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(r => new Claim(AuthConstants.RoleClaim, r)));

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(options.SecurityKey, SecurityAlgorithms.HmacSha256),
        });

        return (token, expires);
    }
}

public static class AuthConstants
{
    public const string AdminPolicy = "AdminOnly";
    public const string RoleClaim = "role";
    public const string SubjectClaim = JwtRegisteredClaimNames.Sub;
}

/// <summary>The signed-in user, read from the validated token.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(AuthConstants.SubjectClaim), out var id) ? id : null;

    public bool IsAdmin => Principal?.IsInRole(Roles.Administrator) ?? false;
}

public static class JwtEvents
{
    /// <summary>
    /// Runs after a token's signature and lifetime are validated. The account must still exist and be
    /// active, and its roles are reloaded from the database, so deactivating a user or revoking the
    /// administrator role takes effect immediately rather than when the token expires.
    /// </summary>
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(AuthConstants.SubjectClaim), out var userId))
        {
            context.Fail("Invalid subject.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<SmartDriveDbContext>();
        var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, context.HttpContext.RequestAborted);
        if (!active)
        {
            context.Fail("Account not found or inactive.");
            return;
        }

        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == userId
                           select r.Name!).ToListAsync(context.HttpContext.RequestAborted);

        var identity = new ClaimsIdentity(
            principal!.Claims.Where(c => c.Type != AuthConstants.RoleClaim),
            JwtBearerDefaults.AuthenticationScheme,
            AuthConstants.SubjectClaim,
            AuthConstants.RoleClaim);
        identity.AddClaims(roles.Select(r => new Claim(AuthConstants.RoleClaim, r)));
        context.Principal = new ClaimsPrincipal(identity);
    }
}
