using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class DashboardModel
{
    public TodayStats Today { get; set; } = new();
    public List<DailyPoint> Series { get; set; } = new();
    public List<BookingOnDay> TodaySchedule { get; set; } = new();
    public List<Transaction> RecentTransactions { get; set; } = new();
    public List<Booking> UpcomingBookings { get; set; } = new();
    public int AvailableCourts { get; set; }
    public int OccupiedCourts { get; set; }
    public int PendingPayments { get; set; }
}

public class DashboardService(ApplicationDbContext db, RevenueService revenue, IUserContext userContext)
{
    public async Task<DashboardModel?> GetAsync(Guid yardId)
    {
        if (!userContext.IsAuthenticated) return null;
        var yard = await db.Yards.AsNoTracking().FirstOrDefaultAsync(y => y.Id == yardId && y.OwnerId == userContext.UserId && !y.IsDeleted);
        if (yard is null) return null;

        var today = YardTime.Today(yard.TimeZoneId);
        var now = YardTime.NowInZone(yard.TimeZoneId).TimeOfDay;
        var model = new DashboardModel
        {
            Today = await revenue.GetTodayStatsAsync(yardId),
            Series = await revenue.GetSeriesAsync(yardId, 14)
        };

        // today's schedule
        var day = await db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.YardId == yardId && b.BookingDate == today)
            .OrderBy(b => b.StartTime)
            .ToListAsync();
        model.TodaySchedule = day.Where(b => BookingActiveStatuses.Values.Contains(b.Status))
            .Select(b => new BookingOnDay(b.Id, b.CourtId, b.StartTime, b.EndTime,
                $"{b.CustomerName} · {b.Court?.Name}", b.Status, b.BookingRef, b.TotalAmount, b.Court?.Name ?? ""))
            .ToList();

        // recent transactions
        model.RecentTransactions = await db.Transactions.AsNoTracking()
            .Include(t => t.Booking)
            .Where(t => t.YardId == yardId)
            .OrderByDescending(t => t.CreatedAtUtc).Take(8)
            .ToListAsync();

        // upcoming bookings
        model.UpcomingBookings = await db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.YardId == yardId && b.BookingDate >= today && BookingActiveStatuses.Values.Contains(b.Status))
            .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime).Take(10)
            .ToListAsync();

        model.PendingPayments = await db.Bookings.CountAsync(b => b.YardId == yardId &&
            BookingActiveStatuses.Values.Contains(b.Status) && b.PaymentStatus != PaymentStatus.Paid);

        // court availability overview (right now)
        var occupiedNow = await db.Bookings.AsNoTracking()
            .Where(b => b.YardId == yardId && b.BookingDate == today && BookingActiveStatuses.Values.Contains(b.Status))
            .Where(b => b.StartTime <= now && b.EndTime > now)
            .Select(b => b.CourtId).Distinct().ToListAsync();
        var totalCourts = await db.Courts.AsNoTracking().CountAsync(c => c.YardId == yardId && c.Status == CourtStatus.Available);
        model.AvailableCourts = Math.Max(0, totalCourts - occupiedNow.Count);
        model.OccupiedCourts = occupiedNow.Count;

        return model;
    }
}