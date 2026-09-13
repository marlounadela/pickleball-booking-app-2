using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class TodayStats
{
    public DateOnly Date { get; set; }
    public int TodayBookings { get; set; }
    public int TodayCompleted { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal PendingAmount { get; set; }
    public decimal Refunds { get; set; }
    public decimal UpcomingValue { get; set; }
}

public class DailyPoint
{
    public DateOnly Date { get; set; }
    public decimal Net { get; set; }
    public decimal Paid { get; set; }
    public decimal Refunds { get; set; }
    public int Bookings { get; set; }
}

public class RevenueService(ApplicationDbContext db, IUserContext userContext, BookingService bookings)
{
    public async Task<DailyRunningTotal?> GetDailyTotalAsync(Guid yardId, DateOnly date)
    {
        var row = await db.DailyRunningTotals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.YardId == yardId && x.Date == date);
        if (row is not null) return row;
        await bookings.RecalculateDailyTotalsAsync(yardId, [date]);
        return await db.DailyRunningTotals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.YardId == yardId && x.Date == date);
    }

    public async Task<TodayStats> GetTodayStatsAsync(Guid yardId)
    {
        var yard = await db.Yards.AsNoTracking().FirstOrDefaultAsync(y => y.Id == yardId);
        var today = YardTime.Today(yard?.TimeZoneId ?? AppConstants.DefaultTimeZone);

        var stats = new TodayStats { Date = today };
        var todayBookings = (await db.Bookings.AsNoTracking()
            .Where(b => b.YardId == yardId && b.BookingDate == today)
            .ToListAsync())?? [];

        stats.TodayBookings = todayBookings.Count(b => BookingActiveStatuses.Values.Contains(b.Status));
        stats.TodayCompleted = todayBookings.Count(b => b.Status == BookingStatus.Completed);

        var total = await GetDailyTotalAsync(yardId, today);
        stats.PaidAmount = total?.PaidTotal ?? 0;
        stats.Refunds = total?.RefundTotal ?? 0;
        stats.NetRevenue = total?.NetTotal ?? 0;

        var paidPerBooking = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid && p.Booking!.BookingDate == today && p.Booking.YardId == yardId)
            .GroupBy(p => p.BookingId)
            .Select(g => new { BookingId = g.Key, Sum = g.Sum(p => p.Amount) })
            .ToListAsync();
        stats.PendingAmount = todayBookings
            .Where(b => BookingActiveStatuses.Values.Contains(b.Status) && b.PaymentStatus != PaymentStatus.Paid)
            .Sum(b => Math.Max(0, b.TotalAmount - (paidPerBooking.FirstOrDefault(p => p.BookingId == b.Id)?.Sum ?? 0)));

        stats.UpcomingValue = await db.Bookings.AsNoTracking()
            .Where(b => b.YardId == yardId && b.BookingDate > today && BookingActiveStatuses.Values.Contains(b.Status))
            .SumAsync(b => (decimal?)b.TotalAmount) ?? 0;

        return stats;
    }

    public async Task<List<DailyPoint>> GetSeriesAsync(Guid yardId, int days)
    {
        var yard = await db.Yards.AsNoTracking().FirstOrDefaultAsync(y => y.Id == yardId);
        var today = YardTime.Today(yard?.TimeZoneId ?? AppConstants.DefaultTimeZone);
        var from = today.AddDays(-(days - 1));

        var totals = await db.DailyRunningTotals.AsNoTracking()
            .Where(x => x.YardId == yardId && x.Date >= from && x.Date <= today)
            .ToListAsync();
        var bookingCounts = await db.Bookings.AsNoTracking()
            .Where(b => b.YardId == yardId && b.BookingDate >= from && b.BookingDate <= today)
            .GroupBy(b => b.BookingDate)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        var result = new List<DailyPoint>();
        for (var d = from; d <= today; d = d.AddDays(1))
        {
            var t = totals.FirstOrDefault(x => x.Date == d);
            result.Add(new DailyPoint
            {
                Date = d,
                Net = t?.NetTotal ?? 0,
                Paid = t?.PaidTotal ?? 0,
                Refunds = t?.RefundTotal ?? 0,
                Bookings = bookingCounts.FirstOrDefault(x => x.Key == d)?.Count ?? 0
            });
        }
        return result;
    }

    public async Task<List<(string Label, decimal Amount)>> RevenueByCourtAsync(Guid yardId, DateOnly from, DateOnly to)
    {
        var tx = await db.Transactions.AsNoTracking()
            .Include(t => t.Booking)!.ThenInclude(b => b!.Court)
            .Where(t => t.YardId == yardId && t.TransactionDate >= from && t.TransactionDate <= to && t.Status == PaymentStatus.Paid)
            .ToListAsync();
        return tx.GroupBy(t => t.Booking?.Court?.Name ?? "Unassigned")
            .Select(g => (g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    public async Task<List<(string Label, decimal Amount)>> RevenueByBookingStatusAsync(Guid yardId, DateOnly from, DateOnly to)
    {
        var tx = await db.Transactions.AsNoTracking()
            .Include(t => t.Booking)
            .Where(t => t.YardId == yardId && t.TransactionDate >= from && t.TransactionDate <= to && t.Status == PaymentStatus.Paid)
            .ToListAsync();
        return tx.GroupBy(t => t.Booking?.Status.ToString() ?? "Unassigned")
            .Select(g => (g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    public async Task<List<(string Label, decimal Amount)>> RevenueByPaymentStatusAsync(Guid yardId, DateOnly from, DateOnly to)
    {
        var tx = await db.Transactions.AsNoTracking()
            .Include(t => t.Booking)
            .Where(t => t.YardId == yardId && t.TransactionDate >= from && t.TransactionDate <= to && t.Status == PaymentStatus.Paid)
            .ToListAsync();
        return tx.GroupBy(t => t.Booking?.PaymentStatus.ToString() ?? "—")
            .Select(g => (g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    public async Task<List<Transaction>> GetTransactionsAsync(Guid yardId, DateOnly from, DateOnly to, int take = 500)
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.Transactions.AsNoTracking()
            .Include(t => t.Booking)
            .Where(t => t.YardId == yardId && t.Yard!.OwnerId == userContext.UserId &&
                        t.TransactionDate >= from && t.TransactionDate <= to)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(take)
            .ToListAsync();
    }
}