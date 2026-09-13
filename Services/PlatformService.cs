using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class PlatformStats
{
    public int TotalYards { get; set; }
    public int ActiveYards { get; set; }
    public int TotalUsers { get; set; }
    public int TotalBookings { get; set; }
    public int ActiveBookingsToday { get; set; }
    public decimal TotalTransactionValue { get; set; }
    public decimal PlatformRevenue { get; set; }
}

/// <summary>Super-admin platform-wide services.</summary>
public class PlatformService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
{
    public async Task<bool> IsSuperAdminAsync(IUserContext ctx) => await ctx.IsInRoleAsync(AppRoles.SuperAdmin);

    public async Task<PlatformStats> GetStatsAsync()
    {
        var bookedSlotsToday = DateOnly.FromDateTime(DateTime.UtcNow);
        return new PlatformStats
        {
            TotalYards = await db.Yards.IgnoreQueryFilters().CountAsync(),
            ActiveYards = await db.Yards.CountAsync(y => y.Status == YardStatus.Active),
            TotalUsers = await db.Users.CountAsync(),
            TotalBookings = await db.Bookings.IgnoreQueryFilters().CountAsync(),
            ActiveBookingsToday = await db.Bookings.CountAsync(b =>
                b.BookingDate == bookedSlotsToday && BookingActiveStatuses.Values.Contains(b.Status)),
            TotalTransactionValue = await db.Transactions.IgnoreQueryFilters().SumAsync(t => (decimal?)t.Amount) ?? 0,
            PlatformRevenue = await db.Transactions.IgnoreQueryFilters()
                .Where(t => t.Type == TransactionType.BookingPayment)
                .SumAsync(t => (decimal?)t.Amount) ?? 0
        };
    }

    public async Task<List<Yard>> GetAllYardsAsync(bool includeDeleted = false)
    {
        IQueryable<Yard> q = db.Yards.AsNoTracking().Include(y => y.Owner).Include(y => y.Courts);
        if (includeDeleted) q = q.IgnoreQueryFilters();
        return await q.OrderByDescending(y => y.CreatedAtUtc).Take(500).ToListAsync();
    }

    public async Task<List<ApplicationUser>> GetUsersAsync(int take = 500) =>
        await db.Users.AsNoTracking().OrderByDescending(u => u.CreatedAtUtc).Take(take).ToListAsync();

    public async Task<Yard?> GetYardWithDetailsAsync(Guid yardId) =>
        await db.Yards.AsNoTracking().IgnoreQueryFilters()
            .Include(y => y.Owner).Include(y => y.Courts).Include(y => y.Settings)
            .FirstOrDefaultAsync(y => y.Id == yardId);

    public async Task<int> YardBookingCountAsync(Guid yardId) =>
        await db.Bookings.IgnoreQueryFilters().CountAsync(b => b.YardId == yardId);

    public async Task<decimal> YardRevenueAsync(Guid yardId) =>
        await db.Transactions.IgnoreQueryFilters().Where(t => t.YardId == yardId).SumAsync(t => (decimal?)t.Amount) ?? 0;

    public async Task<List<string>> GetUserRolesAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<bool> ToggleUserActiveAsync(string userId, bool active)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;
        user.IsActive = active;
        await userManager.UpdateAsync(user);
        return true;
    }

    public async Task<bool> SetUserRoleAsync(string userId, string role)
    {
        if (!AppRoles.All.Contains(role)) return false;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return false;
        var current = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, current);
        await userManager.AddToRoleAsync(user, role);
        return true;
    }

    // ---------- platform settings ----------

    public async Task<Dictionary<string, string>> GetSettingsAsync(string[] keys)
    {
        var rows = await db.PlatformSettings.AsNoTracking().ToListAsync();
        return rows.Where(r => keys.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value ?? string.Empty);
    }

    public async Task SetSettingAsync(string key, string value, string? description = null)
    {
        var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row is null) db.PlatformSettings.Add(new PlatformSetting { Key = key, Value = value, Description = description, UpdatedAtUtc = DateTime.UtcNow });
        else { row.Value = value; row.UpdatedAtUtc = DateTime.UtcNow; if (description is not null) row.Description = description; }
        await db.SaveChangesAsync();
    }

    public async Task<List<DateOnly>> GetAllBookingDatesAsync(Guid? yardId = null)
    {
        var q = db.Bookings.AsNoTracking().AsQueryable();
        if (yardId.HasValue) q = q.Where(b => b.YardId == yardId.Value);
        return await q.Select(b => b.BookingDate).Distinct().OrderBy(d => d).ToListAsync();
    }
}