using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

/// <summary>
/// Resolves the current yard for the signed-in owner and ALWAYS verifies ownership.
/// Tenant identity is derived from the authenticated user, never from query data.
/// </summary>
public class YardAccessService(ApplicationDbContext db, IUserContext userContext, ProtectedLocalStorage localStorage)
{
    private Yard? _current;

    public string StorageKey => "pb_selected_yard";

    public async Task<Yard?> GetVerifiedYardAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return null;
        var yard = await db.Yards.AsNoTracking()
            .Include(x => x.Settings)
            .FirstOrDefaultAsync(x => x.Id == yardId && !x.IsDeleted);
        if (yard is null) return null;
        var isSuperAdmin = await userContext.IsInRoleAsync(AppRoles.SuperAdmin);
        return yard.OwnerId == userContext.UserId || isSuperAdmin ? yard : null;
    }

    public async Task<List<Yard>> GetAccessibleYardsAsync()
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.Yards.AsNoTracking()
            .Include(x => x.Settings)
            .Where(x => !x.IsDeleted && x.OwnerId == userContext.UserId)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    /// <summary>Reads the selected yard (from protected local storage) verifying ownership each call.</summary>
    public async Task<Yard?> GetCurrentYardAsync(string? ownerId = null)
    {
        if (_current is not null) return _current;
        if (!userContext.IsAuthenticated) return null;
        var uid = ownerId ?? userContext.UserId;
        if (uid is null) return null;

        string? selectedId = null;
        try
        {
            var stored = await localStorage.GetAsync<string>(StorageKey);
            if (stored.Success) selectedId = stored.Value;
        }
        catch { /* interactive-only api */ }

        Yard? yard = null;
        if (Guid.TryParse(selectedId, out var sel))
            yard = await db.Yards.AsNoTracking().Include(x => x.Settings)
                .FirstOrDefaultAsync(x => x.Id == sel && x.OwnerId == uid && !x.IsDeleted);

        yard ??= await db.Yards.AsNoTracking().Include(x => x.Settings)
            .Where(x => x.OwnerId == uid && !x.IsDeleted)
            .OrderBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync();

        _current = yard;
        return yard;
    }

    public async Task<bool> SetSelectedYardAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return false;
        var verified = await GetVerifiedYardAsync(yardId);
        if (verified is null) return false;
        try { await localStorage.SetAsync(StorageKey, yardId.ToString()); } catch { /* ignore */ }
        return true;
    }

    public void Reset() => _current = null;
}