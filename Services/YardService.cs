using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class YardResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Guid? YardId { get; init; }
    public static YardResult Ok(Guid id) => new() { Success = true, YardId = id };
    public static YardResult Fail(string error) => new() { Success = false, Error = error };
}

public class YardService(ApplicationDbContext db, IUserContext userContext, AuditService audit)
{
    public static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsAsciiLetterOrDigit(ch) && (ch is >= 'a' and <= 'z' || ch is >= '0' and <= '9')) sb.Append(ch);
            else if (ch is '-' or ' ' or '_') sb.Append('-');
        }
        var cleaned = string.Join("-", sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 60) cleaned = cleaned[..60];
        return cleaned;
    }

    public async Task<YardResult> CreateAsync(Yard yard, string ownerId, string slugHint)
    {
        if (yard is null || string.IsNullOrWhiteSpace(ownerId)) return YardResult.Fail("Missing yard or owner.");

        if (string.IsNullOrWhiteSpace(yard.Slug)) yard.Slug = Slugify(slugHint);
        else yard.Slug = Slugify(yard.Slug);
        if (yard.Slug.Length < 3) return YardResult.Fail("Yard slug must be at least 3 characters (letters, numbers, dashes).");
        if (yard.Slug.StartsWith("app") || yard.Slug.StartsWith("www") || yard.Slug.StartsWith("admin"))
            return YardResult.Fail("That subdomain name is reserved.");

        if (await db.Yards.IgnoreQueryFilters().AnyAsync(x => x.Slug == yard.Slug))
            return YardResult.Fail($"The subdomain '{yard.Slug}' is already taken. Choose another one.");

        yard.Id = Guid.NewGuid();
        yard.OwnerId = ownerId;
        yard.CreatedAtUtc = DateTime.UtcNow;
        yard.UpdatedAtUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(yard.Currency)) yard.Currency = AppConstants.DefaultCurrency;
        if (string.IsNullOrWhiteSpace(yard.TimeZoneId)) yard.TimeZoneId = AppConstants.DefaultTimeZone;

        yard.Settings = new YardSettings { YardId = yard.Id };
        db.Yards.Add(yard);
        await db.SaveChangesAsync();

        await audit.LogAsync("Yard.Created", nameof(Yard), yard.Id.ToString(), yard.Id, ownerId, $"Created yard '{yard.Name}' ({yard.Slug})");
        return YardResult.Ok(yard.Id);
    }

    public async Task<YardResult> UpdateAsync(Guid yardId, Yard input, bool isSuperAdmin = false)
    {
        var yard = await db.Yards.FirstOrDefaultAsync(x => x.Id == yardId && !x.IsDeleted);
        if (yard is null) return YardResult.Fail("Yard not found.");
        if (yard.OwnerId != userContext.UserId && !isSuperAdmin && !await userContext.IsInRoleAsync(AppRoles.SuperAdmin))
            return YardResult.Fail("You do not have permission to edit this yard.");

        var newSlug = Slugify(input.Slug ?? yard.Slug);
        if (newSlug != yard.Slug && await db.Yards.IgnoreQueryFilters().AnyAsync(x => x.Slug == newSlug && x.Id != yardId))
            return YardResult.Fail($"The subdomain '{newSlug}' is already taken.");

        yard.Name = input.Name;
        yard.Slug = newSlug;
        yard.Tagline = input.Tagline;
        yard.Description = input.Description;
        yard.LogoUrl = input.LogoUrl;
        yard.CoverImageUrl = input.CoverImageUrl;
        yard.Address = input.Address;
        yard.City = input.City;
        yard.Province = input.Province;
        yard.Country = input.Country;
        yard.Latitude = input.Latitude;
        yard.Longitude = input.Longitude;
        yard.Phone = input.Phone;
        yard.Email = input.Email;
        yard.Website = input.Website;
        yard.Facebook = input.Facebook;
        yard.Instagram = input.Instagram;
        yard.Twitter = input.Twitter;
        yard.Currency = string.IsNullOrWhiteSpace(input.Currency) ? yard.Currency : input.Currency;
        yard.TimeZoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? yard.TimeZoneId : input.TimeZoneId;
        yard.OperatingHoursNote = input.OperatingHoursNote;
        yard.BookingPolicy = input.BookingPolicy;
        yard.CancellationPolicy = input.CancellationPolicy;
        yard.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync("Yard.Updated", nameof(Yard), yard.Id.ToString(), yard.Id, userContext.UserId, $"Updated yard '{yard.Name}'");
        return YardResult.Ok(yard.Id);
    }

    public async Task<YardResult> UpdateStatusAsync(Guid yardId, YardStatus status)
    {
        var isSuper = await userContext.IsInRoleAsync(AppRoles.SuperAdmin);
        if (!isSuper)
        {
            var yard = await GetOwnedOrNullAsync(yardId);
            if (yard is null) return YardResult.Fail("Yard not found or not owned by you.");
            if (status == YardStatus.Suspended) return YardResult.Fail("Only a platform admin can suspend a yard.");
        }
        var y = await db.Yards.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == yardId);
        if (y is null) return YardResult.Fail("Yard not found.");
        y.Status = status;
        y.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync("Yard.StatusChanged", nameof(Yard), yardId.ToString(), yardId, userContext.UserId, $"Status → {status}");
        return YardResult.Ok(yardId);
    }

    public async Task<YardResult> DeleteAsync(Guid yardId)
    {
        var yard = await GetOwnedOrNullAsync(yardId);
        if (yard is null) return YardResult.Fail("Yard not found or not owned by you.");
        yard.IsDeleted = true;
        await db.SaveChangesAsync();
        await audit.LogAsync("Yard.Deleted", nameof(Yard), yardId.ToString(), yardId, userContext.UserId, "Soft-deleted yard");
        return YardResult.Ok(yardId);
    }

    public async Task<Yard?> GetOwnedOrNullAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return null;
        return await db.Yards.FirstOrDefaultAsync(x => x.Id == yardId && x.OwnerId == userContext.UserId && !x.IsDeleted);
    }

    public async Task<Yard?> GetPublicYardAsync(string slug) =>
        await db.Yards.AsNoTracking()
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(x => x.Slug == slug && x.Status != YardStatus.Suspended && !x.IsDeleted);

    public async Task<string> SaveUploadedImageAsync(IBrowserFile file, string folder)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", folder);
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg"))
            throw new InvalidOperationException("Only image uploads are allowed (.png, .jpg, .jpeg, .webp, .gif, .svg).");
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using var fs = File.Create(fullPath);
        await file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024).CopyToAsync(fs);
        return $"/uploads/{folder}/{fileName}";
    }
}