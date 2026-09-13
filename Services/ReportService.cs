using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class UtilizationRow
{
    public string Court { get; set; } = string.Empty;
    public double HoursBooked { get; set; }
    public int Bookings { get; set; }
    public decimal Revenue { get; set; }
    public double UtilizationPercent { get; set; }
}

public class ReportDataSet
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int BookingCount { get; set; }
    public int CompletedCount { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetRevenue { get; set; }
    public List<DailyPoint> DailySeries { get; set; } = new();
    public List<UtilizationRow> Utilization { get; set; } = new();
    public List<Booking> Bookings { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public List<CustomerRow> Customers { get; set; } = new();
    public List<(string Label, decimal Amount)> ByCourt { get; set; } = new();
    public List<(string Label, decimal Amount)> ByBookingStatus { get; set; } = new();
    public List<(string Label, decimal Amount)> ByPaymentStatus { get; set; } = new();
}

/// <summary>Aggregates reporting data. All queries verify yard ownership.</summary>
public class ReportService(ApplicationDbContext db, IUserContext userContext, RevenueService revenue, CustomerService customers)
{
    public async Task<ReportDataSet?> BuildAsync(Guid yardId, DateOnly from, DateOnly to)
    {
        if (!userContext.IsAuthenticated) return null;
        if (to > from.AddDays(365)) to = from.AddDays(365);
        if (from > to) (from, to) = (to, from);
        var yard = await db.Yards.AsNoTracking().FirstOrDefaultAsync(y => y.Id == yardId && y.OwnerId == userContext.UserId && !y.IsDeleted);
        if (yard is null) return null;

        var data = new ReportDataSet { From = from, To = to };

        data.Bookings = await db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.YardId == yardId && b.BookingDate >= from && b.BookingDate <= to)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime)
            .Take(5000)
            .ToListAsync();

        data.BookingCount = data.Bookings.Count;
        data.CompletedCount = data.Bookings.Count(b => b.Status == BookingStatus.Completed);

        var txs = await db.Transactions.AsNoTracking()
            .Where(t => t.YardId == yardId && t.TransactionDate >= from && t.TransactionDate <= to && t.Status == PaymentStatus.Paid)
            .Select(t => new { t.Amount, t.TransactionDate })
            .ToListAsync();
        data.Transactions = await revenue.GetTransactionsAsync(yardId, from, to);
        data.GrossRevenue = txs.Where(t => t.Amount > 0).Sum(t => t.Amount);
        data.Refunds = txs.Where(t => t.Amount < 0).Sum(t => -t.Amount);
        data.NetRevenue = txs.Sum(t => t.Amount);

        // daily series (fill missing days with zero)
        // GetSeries uses last N days ending today, so rebuild within range:
        data.DailySeries = BuildDailySeries(txs.Select(t => (t.TransactionDate, t.Amount)).ToList(), data.Bookings, from, to);

        // utilization: hours booked / (operating hours over range) per court
        data.Utilization = await ComputeUtilizationAsync(yardId, from, to, data.Bookings);
        data.ByCourt = await revenue.RevenueByCourtAsync(yardId, from, to);
        data.ByBookingStatus = await revenue.RevenueByBookingStatusAsync(yardId, from, to);
        data.ByPaymentStatus = await revenue.RevenueByPaymentStatusAsync(yardId, from, to);
        data.Customers = await customers.GetCustomersAsync(yardId);

        return data;
    }

    private static List<DailyPoint> BuildDailySeries(List<(DateOnly TransactionDate, decimal Amount)> txs, List<Booking> bookings, DateOnly from, DateOnly to)
    {
        var byDate = txs.GroupBy(t => t.TransactionDate)
            .ToDictionary(g => g.Key, g => new
            {
                Net = g.Sum(t => t.Amount),
                Paid = g.Where(t => t.Amount > 0).Sum(t => t.Amount),
                Refunds = g.Where(t => t.Amount < 0).Sum(t => -t.Amount)
            });
        var bookingsByDate = bookings.GroupBy(b => b.BookingDate).ToDictionary(g => g.Key, g => g.Count());
        var result = new List<DailyPoint>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            byDate.TryGetValue(d, out var t);
            bookingsByDate.TryGetValue(d, out var bookingCount);
            result.Add(new DailyPoint
            {
                Date = d,
                Net = t?.Net ?? 0,
                Paid = t?.Paid ?? 0,
                Refunds = t?.Refunds ?? 0,
                Bookings = bookingCount
            });
        }
        return result;
    }

    private async Task<List<UtilizationRow>> ComputeUtilizationAsync(Guid yardId, DateOnly from, DateOnly to, List<Booking> bookings)
    {
        var courts = await db.Courts.AsNoTracking()
            .Where(c => c.YardId == yardId)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();
        var openRow = await db.OperatingHours.AsNoTracking()
            .Where(o => o.YardId == yardId && o.CourtId == null && !o.IsClosed && o.CloseTime > o.OpenTime)
            .Select(o => new { o.OpenTime, o.CloseTime })
            .FirstOrDefaultAsync();

        var rows = new List<UtilizationRow>();
        var totalDays = (to.DayNumber - from.DayNumber) + 1;
        var openHoursPerDay = openRow is null ? 0 : (openRow.CloseTime - openRow.OpenTime).TotalHours;
        var availableHours = openHoursPerDay * totalDays;
        var activeByCourt = bookings
            .Where(b => BookingActiveStatuses.Values.Contains(b.Status))
            .GroupBy(b => b.CourtId)
            .ToDictionary(g => g.Key, g => new
            {
                Minutes = g.Sum(b => b.DurationMinutes),
                Revenue = g.Sum(b => b.TotalAmount)
            });
        var allByCourt = bookings.GroupBy(b => b.CourtId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var court in courts)
        {
            activeByCourt.TryGetValue(court.Id, out var active);
            allByCourt.TryGetValue(court.Id, out var bookingCount);
            var hoursBooked = (active?.Minutes ?? 0) / 60.0;
            rows.Add(new UtilizationRow
            {
                Court = court.Name,
                HoursBooked = Math.Round(hoursBooked, 1),
                Bookings = bookingCount,
                Revenue = active?.Revenue ?? 0,
                UtilizationPercent = availableHours > 0 ? Math.Round(hoursBooked / availableHours * 100, 1) : 0
            });
        }
        return rows;
    }
}