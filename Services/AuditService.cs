using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class AuditService(ApplicationDbContext db)
{
    public async Task LogAsync(string action, string entityType, string entityId, Guid? yardId, string? userId, string? details = null, string? ip = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            YardId = yardId,
            UserId = userId,
            Details = details,
            IpAddress = ip,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditLog>> RecentAsync(Guid? yardId, int take = 50)
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (yardId.HasValue && yardId != Guid.Empty) q = q.Where(x => x.YardId == yardId.Value);
        return await q.OrderByDescending(x => x.CreatedAtUtc).Take(take).ToListAsync();
    }
}