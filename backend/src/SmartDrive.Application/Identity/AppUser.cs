using Microsoft.AspNetCore.Identity;
using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Identity;

/// <summary>
/// An account. Passwords are hashed by ASP.NET Core Identity (PBKDF2); they are never stored or
/// logged in plain text.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsSampleData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public CustomerProfile? Profile { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
}
