using Microsoft.AspNetCore.Identity;
using Picklebook.Domain;

namespace Picklebook.Data;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => string.IsNullOrWhiteSpace(FirstName)
        ? (UserName ?? Email ?? "User")
        : $"{FirstName} {LastName}".Trim();

    public ICollection<Yard> Yards { get; set; } = new List<Yard>();
}

