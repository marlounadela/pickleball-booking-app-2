using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class CourtResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Guid? CourtId { get; init; }
    public static CourtResult Ok(Guid id) => new() { Success = true, CourtId = id };
    public static CourtResult Fail(string error) => new() { Success = false, Error = error };
}

public class CourtService(ApplicationDbContext db, IUserContext userContext, AuditService audit)
{
    /// <summary>Tenant-aware court fetch: verifies the yard belongs to the caller.</summary>
    public async Task<Court?> GetOwnedCourtAsync(Guid courtId)
    {
        if (!userContext.IsAuthenticated) return null;
        return await db.Courts
            .FirstOrDefaultAsync(c => c.Id == courtId && c.Yard!.OwnerId == userContext.UserId && !c.Yard.IsDeleted);
    }

    public async Task<List<Court>> GetCourtsForYardAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.Courts.AsNoTracking()
            .Where(c => c.YardId == yardId && c.Yard!.OwnerId == userContext.UserId)
            .OrderBy(c => c.Number).ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<CourtResult> CreateAsync(Guid yardId, Court court)
    {
        var yard = await db.Yards.FirstOrDefaultAsync(y => y.Id == yardId && y.OwnerId == userContext.UserId && !y.IsDeleted);
        if (yard is null) return CourtResult.Fail("Yard not found or not owned by you.");
        if (court.HourlyRate < 0) return CourtResult.Fail("Hourly rate cannot be negative.");
        if (string.IsNullOrWhiteSpace(court.Name)) return CourtResult.Fail("Court name is required.");

        court.Id = Guid.NewGuid();
        court.YardId = yardId;
        court.CreatedAtUtc = DateTime.UtcNow;
        court.UpdatedAtUtc = DateTime.UtcNow;
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        await audit.LogAsync("Court.Created", nameof(Court), court.Id.ToString(), yardId, userContext.UserId, $"Created court '{court.Name}'");
        return CourtResult.Ok(court.Id);
    }

    public async Task<CourtResult> UpdateAsync(Guid courtId, Court input)
    {
        var court = await GetOwnedCourtAsync(courtId);
        if (court is null) return CourtResult.Fail("Court not found or not owned by you.");
        if (input.HourlyRate < 0) return CourtResult.Fail("Hourly rate cannot be negative.");
        if (string.IsNullOrWhiteSpace(input.Name)) return CourtResult.Fail("Court name is required.");

        court.Name = input.Name;
        court.Number = input.Number;
        court.Type = input.Type;
        court.HourlyRate = input.HourlyRate;
        court.Status = input.Status;
        court.Description = input.Description;
        court.ImageUrl = input.ImageUrl;
        court.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync("Court.Updated", nameof(Court), court.Id.ToString(), court.YardId, userContext.UserId, $"Updated court '{court.Name}'");
        return CourtResult.Ok(court.Id);
    }

    public async Task<CourtResult> SetStatusAsync(Guid courtId, CourtStatus status)
    {
        var court = await GetOwnedCourtAsync(courtId);
        if (court is null) return CourtResult.Fail("Court not found or not owned by you.");
        court.Status = status;
        court.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync("Court.StatusChanged", nameof(Court), court.Id.ToString(), court.YardId, userContext.UserId, $"Status → {status}");
        return CourtResult.Ok(court.Id);
    }

    public async Task<CourtResult> DeleteAsync(Guid courtId)
    {
        var court = await GetOwnedCourtAsync(courtId);
        if (court is null) return CourtResult.Fail("Court not found or not owned by you.");
        if (await db.Bookings.AnyAsync(b => b.CourtId == courtId && BookingActiveStatuses.Values.Contains(b.Status)))
            return CourtResult.Fail("This court has active bookings and cannot be deleted. Set it to Inactive instead.");
        court.IsDeleted = true;
        await db.SaveChangesAsync();
        await audit.LogAsync("Court.Deleted", nameof(Court), court.Id.ToString(), court.YardId, userContext.UserId, $"Deleted court '{court.Name}'");
        return CourtResult.Ok(court.Id);
    }
}