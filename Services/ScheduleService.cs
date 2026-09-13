using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class ScheduleResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public static ScheduleResult Ok() => new() { Success = true };
    public static ScheduleResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Manages operating hours, availability rules (special open windows) and blocked schedules.
/// All mutating methods verify yard ownership.
/// </summary>
public class ScheduleService(ApplicationDbContext db, IUserContext userContext, AuditService audit)
{
    public async Task<List<OperatingHour>> GetYardOperatingHoursAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.OperatingHours.AsNoTracking()
            .Where(o => o.YardId == yardId && o.CourtId == null && o.Yard!.OwnerId == userContext.UserId)
            .OrderBy(o => o.DayOfWeek)
            .ToListAsync();
    }

    public async Task<ScheduleResult> SaveYardOperatingHoursAsync(Guid yardId, List<OperatingHour> hours)
    {
        var yard = await db.Yards.FirstOrDefaultAsync(y => y.Id == yardId && y.OwnerId == userContext.UserId && !y.IsDeleted);
        if (yard is null) return ScheduleResult.Fail("Yard not found or not owned by you.");

        var existing = db.OperatingHours.Where(o => o.YardId == yardId && o.CourtId == null);
        db.OperatingHours.RemoveRange(existing);
        foreach (var h in hours.Where(h => h.DayOfWeek is >= 0 and <= 6))
        {
            db.OperatingHours.Add(new OperatingHour
            {
                Id = Guid.NewGuid(),
                YardId = yardId,
                DayOfWeek = h.DayOfWeek,
                OpenTime = h.OpenTime,
                CloseTime = h.CloseTime,
                IsClosed = h.IsClosed
            });
        }
        await db.SaveChangesAsync();
        await audit.LogAsync("Schedule.HoursSaved", "OperatingHour", yardId.ToString(), yardId, userContext.UserId, "Updated operating hours");
        return ScheduleResult.Ok();
    }

    public async Task<List<OperatingHour>> GetCourtHoursAsync(Guid courtId)
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.OperatingHours.AsNoTracking()
            .Where(o => o.CourtId == courtId && o.Yard!.OwnerId == userContext.UserId)
            .OrderBy(o => o.DayOfWeek)
            .ToListAsync();
    }

public async Task<ScheduleResult> SaveCourtHoursAsync(Guid courtId, List<OperatingHour> hours)
    {
        var court = await db.Courts.FirstOrDefaultAsync(c => c.Id == courtId && c.Yard!.OwnerId == userContext.UserId && !c.Yard.IsDeleted);
        if (court is null) return ScheduleResult.Fail("Court not found or not owned by you.");

        var existing = db.OperatingHours.Where(o => o.CourtId == courtId);
        db.OperatingHours.RemoveRange(existing);
        foreach (var h in hours.Where(h => h.DayOfWeek is >= 0 and <= 6))
        {
            db.OperatingHours.Add(new OperatingHour
            {
                Id = Guid.NewGuid(),
                YardId = court.YardId,
                CourtId = courtId,
                DayOfWeek = h.DayOfWeek,
                OpenTime = h.OpenTime,
                CloseTime = h.CloseTime,
                IsClosed = h.IsClosed
            });
        }
        await db.SaveChangesAsync();
        await audit.LogAsync("Schedule.CourtHoursSaved", "OperatingHour", courtId.ToString(), court.YardId, userContext.UserId, "Updated court operating hours");
        return ScheduleResult.Ok();
    }
    public async Task<List<BlockedSchedule>> GetBlockedAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.BlockedSchedules.AsNoTracking()
            .Where(x => x.YardId == yardId && x.Yard!.OwnerId == userContext.UserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task<ScheduleResult> SaveBlockedAsync(Guid yardId, BlockedSchedule block)
    {
        var yard = await db.Yards.FirstOrDefaultAsync(y => y.Id == yardId && y.OwnerId == userContext.UserId && !y.IsDeleted);
        if (yard is null) return ScheduleResult.Fail("Yard not found or not owned by you.");
        if (block.CourtId.HasValue && !await db.Courts.AnyAsync(c => c.Id == block.CourtId && c.YardId == yardId))
            return ScheduleResult.Fail("Court does not belong to this yard.");
        if (block.SingleDate is null && block.StartDate is null)
            return ScheduleResult.Fail("A blocked date or date range is required.");

        block.Id = Guid.NewGuid();
        block.YardId = yardId;
        block.CreatedAtUtc = DateTime.UtcNow;
        db.BlockedSchedules.Add(block);
        await db.SaveChangesAsync();
        await audit.LogAsync("Schedule.Blocked", "BlockedSchedule", block.Id.ToString(), yardId, userContext.UserId, $"Blocked: {block.Title}");
        return ScheduleResult.Ok();
    }

    public async Task<ScheduleResult> DeleteBlockedAsync(Guid blockedId)
    {
        var block = await db.BlockedSchedules.FirstOrDefaultAsync(x => x.Id == blockedId && x.Yard!.OwnerId == userContext.UserId);
        if (block is null) return ScheduleResult.Fail("Blocked schedule not found or not owned by you.");
        var yardId = block.YardId;
        db.BlockedSchedules.Remove(block);
        await db.SaveChangesAsync();
        await audit.LogAsync("Schedule.BlockRemoved", "BlockedSchedule", blockedId.ToString(), yardId, userContext.UserId, "Removed blocked schedule");
        return ScheduleResult.Ok();
    }

    public async Task<List<AvailabilityRule>> GetRulesAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.AvailabilityRules.AsNoTracking()
            .Where(x => x.YardId == yardId && x.Yard!.OwnerId == userContext.UserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task<ScheduleResult> SaveRuleAsync(Guid yardId, AvailabilityRule rule)
    {
        var yard = await db.Yards.FirstOrDefaultAsync(y => y.Id == yardId && y.OwnerId == userContext.UserId && !y.IsDeleted);
        if (yard is null) return ScheduleResult.Fail("Yard not found or not owned by you.");
        if (rule.CourtId.HasValue && !await db.Courts.AnyAsync(c => c.Id == rule.CourtId && c.YardId == yardId))
            return ScheduleResult.Fail("Court does not belong to this yard.");

        rule.Id = Guid.NewGuid();
        rule.YardId = yardId;
        rule.CreatedAtUtc = DateTime.UtcNow;
        db.AvailabilityRules.Add(rule);
        await db.SaveChangesAsync();
        await audit.LogAsync("Schedule.RuleCreated", "AvailabilityRule", rule.Id.ToString(), yardId, userContext.UserId, $"Rule: {rule.Name}");
        return ScheduleResult.Ok();
    }

    public async Task<ScheduleResult> ToggleRuleAsync(Guid ruleId)
    {
        var rule = await db.AvailabilityRules.FirstOrDefaultAsync(x => x.Id == ruleId && x.Yard!.OwnerId == userContext.UserId);
        if (rule is null) return ScheduleResult.Fail("Rule not found or not owned by you.");
        rule.IsActive = !rule.IsActive;
        await db.SaveChangesAsync();
        return ScheduleResult.Ok();
    }

    public async Task<ScheduleResult> DeleteRuleAsync(Guid ruleId)
    {
        var rule = await db.AvailabilityRules.FirstOrDefaultAsync(x => x.Id == ruleId && x.Yard!.OwnerId == userContext.UserId);
        if (rule is null) return ScheduleResult.Fail("Rule not found or not owned by you.");
        db.AvailabilityRules.Remove(rule);
        await db.SaveChangesAsync();
        return ScheduleResult.Ok();
    }
}