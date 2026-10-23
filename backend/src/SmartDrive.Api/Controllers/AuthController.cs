using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDrive.Api.Infrastructure;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;

namespace SmartDrive.Api.Controllers;

/// <summary>Registration, sign-in and the signed-in user's account.</summary>
[Route("api")]
public sealed class AuthController(AccountService accounts) : ApiControllerBase
{
    /// <summary>Creates a customer account and signs it in.</summary>
    [HttpPost("auth/register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var response = await accounts.RegisterAsync(request, ct);
        return Created("/api/auth/me", response);
    }

    /// <summary>Signs in and returns a JWT access token.</summary>
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => accounts.LoginAsync(request, ct);

    /// <summary>
    /// Signing out is handled by the client discarding its token; this endpoint exists so clients have a
    /// single place to call and so the event is recorded.
    /// </summary>
    [HttpPost("auth/logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout() => NoContent();

    [HttpGet("auth/me")]
    [Authorize]
    public Task<UserDto> Me(CancellationToken ct) => accounts.MeAsync(UserId, ct);

    [HttpGet("account/profile")]
    [Authorize]
    public Task<ProfileDto> Profile(CancellationToken ct) => accounts.ProfileAsync(UserId, ct);

    [HttpPut("account/profile")]
    [Authorize]
    public Task<ProfileDto> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) => accounts.UpdateProfileAsync(UserId, request, ct);

    [HttpPost("account/change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await accounts.ChangePasswordAsync(UserId, request, ct);
        return NoContent();
    }

    [HttpGet("account/notifications")]
    [Authorize]
    public Task<IReadOnlyList<NotificationDto>> Notifications(CancellationToken ct) => accounts.NotificationsAsync(UserId, ct);

    [HttpPost("account/notifications/read")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkNotificationsRead(CancellationToken ct)
    {
        await accounts.MarkNotificationsReadAsync(UserId, ct);
        return NoContent();
    }

    /// <summary>Sends a message to the SmartDrive team from the Contact page.</summary>
    [HttpPost("contact")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Contact)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Contact(ContactRequest request, CancellationToken ct)
    {
        await accounts.SubmitContactAsync(request, ct);
        return Accepted();
    }
}
