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
        var yard = await db.Yards.AsNoTracking().FirstOrDefaultAsync(y => y.Id == yardId && y.OwnerId == userContext.UserId && !y.IsDeleted);
        if (yard is null) return null;

        var data = new ReportDataSet { From = from, To = to };

        data.Bookings = await db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.YardId == yardId && b.BookingDate >= from && b.BookingDate <= to)
            .OrderBy(b => b.BookingDate).ThenBy(b => b.StartTime)
            .ToListAsync();

        data.BookingCount = data.Bookings.Count;
        data.CompletedCount = data.Bookings.Count(b => b.Status == BookingStatus.Completed);

        var txs = await db.Transactions.AsNoTracking().Include(t => t.Booking)
            .Where(t => t.YardId == yardId && t.TransactionDate >= from && t.TransactionDate <= to && t.Status == PaymentStatus.Paid)
            .ToListAsync();
        data.Transactions = await revenue.GetTransactionsAsync(yardId, from, to);
        data.GrossRevenue = txs.Where(t => t.Amount > 0).Sum(t => t.Amount);
        data.Refunds = txs.Where(t => t.Amount < 0).Sum(t => -t.Amount);
        data.NetRevenue = txs.Sum(t => t.Amount);

        // daily series (fill missing days with zero)
        var series = await revenue.GetSeriesAsync(yardId, Math.Max(1, to.DayNumber - from.DayNumber + 1));
        var span = series.Where(s => s.Date >= from && s.Date <= to).ToList();
        // GetSeries uses last N days ending today, so rebuild within range:
        data.DailySeries = BuildDailySeries(txs, data.Bookings, from, to);

        // utilization: hours booked / (operating hours over range) per court
        data.Utilization = await ComputeUtilizationAsync(yardId, from, to, data.Bookings);
        data.ByCourt = await revenue.RevenueByCourtAsync(yardId, from, to);
        data.ByBookingStatus = await revenue.RevenueByBookingStatusAsync(yardId, from, to);
        data.ByPaymentStatus = await revenue.RevenueByPaymentStatusAsync(yardId, from, to);
        data.Customers = await customers.GetCustomersAsync(yardId);

        return data;
    }

    private static List<DailyPoint> BuildDailySeries(List<Transaction> txs, List<Booking> bookings, DateOnly from, DateOnly to)
    {
        var result = new List<DailyPoint>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            result.Add(new DailyPoint
            {
                Date = d,
                Net = txs.Where(t => t.TransactionDate == d).Sum(t => t.Amount),
                Paid = txs.Where(t => t.TransactionDate == d && t.Amount > 0).Sum(t => t.Amount),
                Refunds = txs.Where(t => t.TransactionDate == d && t.Amount < 0).Sum(t => -t.Amount),
                Bookings = bookings.Count(b => b.BookingDate == d)
            });
        }
        return result;
    }

    private async Task<List<UtilizationRow>> ComputeUtilizationAsync(Guid yardId, DateOnly from, DateOnly to, List<Booking> bookings)
    {
        var courts = await db.Courts.AsNoTracking().Where(c => c.YardId == yardId).ToListAsync();
        var hours = await db.OperatingHours.AsNoTracking()
            .Where(o => o.YardId == yardId && o.CourtId == null).ToListAsync();

        var rows = new List<UtilizationRow>();
        var totalDays = (to.DayNumber - from.DayNumber) + 1;
        foreach (var court in courts)
        {
            var courtBookings = bookings.Where(b => b.CourtId == court.Id).ToList();
            var hoursBooked = courtBookings.Where(b => BookingActiveStatuses.Values.Contains(b.Status)).Sum(b => b.DurationMinutes) / 60.0;
            var openHoursPerDay = hours.FirstOrDefault(h => !h.IsClosed && h.CloseTime > h.OpenTime) is { } hh
                ? (hh.CloseTime - hh.OpenTime).TotalHours
                : 0;
            var availableHours = openHoursPerDay * totalDays;
            rows.Add(new UtilizationRow
            {
                Court = court.Name,
                HoursBooked = Math.Round(hoursBooked, 1),
                Bookings = courtBookings.Count,
                Revenue = courtBookings.Where(b => BookingActiveStatuses.Values.Contains(b.Status)).Sum(b => b.TotalAmount),
                UtilizationPercent = availableHours > 0 ? Math.Round(hoursBooked / availableHours * 100, 1) : 0
            });
        }
        return rows;
    }
}