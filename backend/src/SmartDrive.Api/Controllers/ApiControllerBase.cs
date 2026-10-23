using Microsoft.AspNetCore.Mvc;
using SmartDrive.Application.Services;

namespace SmartDrive.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ICurrentUser? currentUser;

    protected ICurrentUser CurrentUser => currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    /// <summary>The signed-in user's id. Only call from actions that require authentication.</summary>
    protected Guid UserId => CurrentUser.RequireUserId();
}
