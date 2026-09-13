using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public record TimeSlotInfo(
    Guid CourtId,
    string CourtName,
    CourtType CourtType,
    string? CourtImage,
    TimeSpan Start,
    TimeSpan End,
    int DurationMinutes,
    decimal RatePerHour,
    decimal Price,
    bool IsAvailable,
    string? Note);

/// <summary>A half-open window [Start, End).</summary>
public record SlotWindow(TimeSpan Start, TimeSpan End);

public class SlotUnavailableException(string message) : Exception(message);

/// <summary>
/// Server-side availability engine. Generates bookable slots as:
/// (OperatingHours ∪ AvailabilityRules) − (BlockedSchedules ∪ ExistingBookings).
/// All validation happens here on the server; the UI merely displays results.
/// </summary>
public class AvailabilityService(ApplicationDbContext db)
{
    public static readonly TimeSpan DayEnd = new(23, 59, 59);

    /// <summary>All available slots for a yard/date/duration. Optionally limited to one court.</summary>
    public async Task<List<TimeSlotInfo>> GetSlotsAsync(Guid yardId, DateOnly date, int durationMinutes, Guid? courtId = null)
    {
        var yard = await db.Yards.AsNoTracking().Include(y => y.Settings).FirstOrDefaultAsync(y => y.Id == yardId && !y.IsDeleted);
        if (yard is null) return [];

        var today = YardTime.Today(yard.TimeZoneId);
        if (date < today) return [];
        if (date > today.AddDays(Math.Max(0, yard.Settings?.AdvanceBookingDays ?? 30))) return [];

        var duration = TimeSpan.FromMinutes(durationMinutes);
        if (duration <= TimeSpan.Zero) return [];

        var courtsQuery = db.Courts.AsNoTracking()
            .Where(c => c.YardId == yardId && c.Status == CourtStatus.Available)
            .AsQueryable();
        if (courtId.HasValue) courtsQuery = courtsQuery.Where(c => c.Id == courtId.Value);
        var courts = await courtsQuery.OrderBy(c => c.Number).ThenBy(c => c.Name).ToListAsync();

        // Performance: only the current weekday matters for operating hours, and blocks/rules
        // only matter when they can overlap the requested date.
        var weekday = (int)date.DayOfWeek;
        var yardHours = await db.OperatingHours.AsNoTracking()
            .Where(o => o.YardId == yardId && o.CourtId == null && o.DayOfWeek == weekday).ToListAsync();
        var courtIds = courts.Select(c => c.Id).ToList();
        var courtHours = await db.OperatingHours.AsNoTracking()
            .Where(o => o.YardId == yardId && courtIds.Contains(o.CourtId ?? Guid.Empty) && o.DayOfWeek == weekday).ToListAsync();

        var blocks = await db.BlockedSchedules.AsNoTracking()
            .Where(b => b.YardId == yardId
                && (b.SingleDate == null || b.SingleDate == date)
                && (b.StartDate == null || b.StartDate <= date)
                && (b.EndDate == null || b.EndDate >= date)).ToListAsync();
        var rules = await db.AvailabilityRules.AsNoTracking()
            .Where(r => r.YardId == yardId && r.IsActive
                && (r.SingleDate == null || r.SingleDate == date)
                && (r.StartDate == null || r.StartDate <= date)
                && (r.EndDate == null || r.EndDate >= date)).ToListAsync();

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.YardId == yardId && b.BookingDate == date &&
                        BookingActiveStatuses.Values.Contains(b.Status))
            .Select(b => new { b.CourtId, b.StartTime, b.EndTime })
            .ToListAsync();

        var now = YardTime.NowInZone(yard.TimeZoneId).TimeOfDay;
        var minAdvance = TimeSpan.FromHours(yard.Settings?.MinAdvanceHours ?? 0);
        var results = new List<TimeSlotInfo>();

        foreach (var court in courts)
        {
            var windows = BuildWindows(yardHours, courtHours.Where(h => h.CourtId == court.Id).ToList(), rules, court.Id, date);
            windows = SubtractBlocks(windows, blocks.Where(b => b.CourtId == null || b.CourtId == court.Id), date);
            foreach (var b in bookings.Where(b => b.CourtId == court.Id))
                windows = SubtractWindow(windows, b.StartTime, b.EndTime);

            var slotLen = duration;
            foreach (var window in windows)
            {
                var t = window.Start;
                while (t + slotLen <= window.End)
                {
                    var start = t;
                    var end = t + slotLen;

                    if (date == today)
                    {
                        var cutoff = now + minAdvance;
                        if (end <= now) { t += slotLen; continue; }
                        if (start < cutoff && cutoff < end) { t += slotLen; continue; }
                        if (start < cutoff) { t = AlignNextBreakpoint(cutoff, slotLen); continue; }
                    }

                    var rate = court.HourlyRate;
                    var price = Math.Round(rate * (decimal)slotLen.TotalHours, 2);
                    results.Add(new TimeSlotInfo(court.Id, court.Name, court.Type, court.ImageUrl,
                        start, end, durationMinutes, rate, price, true, null));
                    t += slotLen;
                }
            }
        }

        return results.OrderBy(r => r.Start).ThenBy(r => r.CourtName).ToList();
    }

    private static TimeSpan AlignNextBreakpoint(TimeSpan cutoff, TimeSpan step)
    {
        var ticks = step.Ticks;
        return new TimeSpan(((cutoff.Ticks + ticks - 1) / ticks) * ticks);
    }

    /// <summary>Open windows: yard default hours, court-specific overrides, plus availability-rule windows.</summary>
    private static List<SlotWindow> BuildWindows(
        List<OperatingHour> yardHours,
        List<OperatingHour> courtHours,
        List<AvailabilityRule> rules,
        Guid courtId,
        DateOnly date)
    {
        var windows = new List<SlotWindow>();

        var effective = courtHours.Count > 0 ? courtHours : yardHours;
        var defaultHour = effective.FirstOrDefault();
        if (defaultHour is not null && !defaultHour.IsClosed && defaultHour.CloseTime > defaultHour.OpenTime)
            windows.Add(new SlotWindow(defaultHour.OpenTime, defaultHour.CloseTime));

        foreach (var rule in rules)
        {
            if (!rule.AppliesOn(date)) continue;
            if (rule.CourtId.HasValue && rule.CourtId != courtId) continue;
            var rs = rule.StartTime ?? TimeSpan.Zero;
            var re = rule.EndTime ?? DayEnd;
            if (re > rs) windows.Add(new SlotWindow(rs, re));
        }

        windows = windows.OrderBy(w => w.Start).ThenBy(w => w.End).ToList();
        var merged = new List<SlotWindow>();
        foreach (var w in windows)
        {
            if (merged.Count == 0 || w.Start > merged[^1].End)
                merged.Add(w);
            else if (w.End > merged[^1].End)
                merged[^1] = merged[^1] with { End = w.End };
        }
        return merged;
    }

    private static List<SlotWindow> SubtractBlocks(List<SlotWindow> windows, IEnumerable<BlockedSchedule> blocks, DateOnly date)
    {
        foreach (var block in blocks)
        {
            if (!block.AppliesToInterval(date, TimeSpan.Zero, DayEnd)) continue;
            if (block.StartTime.HasValue || block.EndTime.HasValue)
            {
                var bs = block.StartTime ?? TimeSpan.Zero;
                var be = block.EndTime ?? DayEnd;
                windows = SubtractWindow(windows, bs, be);
            }
            else
            {
                windows = [];
            }
        }
        return windows;
    }

    /// <summary>Subtract [removeStart, removeEnd) from a list of windows.</summary>
    public static List<SlotWindow> SubtractWindow(List<SlotWindow> windows, TimeSpan removeStart, TimeSpan removeEnd)
    {
        var result = new List<SlotWindow>();
        foreach (var w in windows)
        {
            if (removeEnd <= w.Start || removeStart >= w.End)
            {
                result.Add(w);
                continue;
            }
            if (removeStart > w.Start)
                result.Add(new SlotWindow(w.Start, removeStart < w.End ? removeStart : w.End));
            if (removeEnd < w.End)
                result.Add(new SlotWindow(removeEnd > w.Start ? removeEnd : w.Start, w.End));
        }
        return result;
    }

    /// <summary>Day overview for the calendar page (windows + bookings per court).</summary>
    public async Task<DayAvailability> GetDayAsync(Guid yardId, DateOnly date)
    {
        var courts = await db.Courts.AsNoTracking()
            .Where(c => c.YardId == yardId && c.Status != CourtStatus.Inactive)
            .OrderBy(c => c.Number).ThenBy(c => c.Name).ToListAsync();
        var weekday = (int)date.DayOfWeek;
        var yardHours = await db.OperatingHours.AsNoTracking()
            .Where(o => o.YardId == yardId && o.CourtId == null && o.DayOfWeek == weekday).ToListAsync();
        var courtHours = await db.OperatingHours.AsNoTracking()
            .Where(o => o.YardId == yardId && o.DayOfWeek == weekday).ToListAsync();
        var blocks = await db.BlockedSchedules.AsNoTracking()
            .Where(b => b.YardId == yardId
                && (b.SingleDate == null || b.SingleDate == date)
                && (b.StartDate == null || b.StartDate <= date)
                && (b.EndDate == null || b.EndDate >= date)).ToListAsync();
        var rules = await db.AvailabilityRules.AsNoTracking()
            .Where(r => r.YardId == yardId && r.IsActive
                && (r.SingleDate == null || r.SingleDate == date)
                && (r.StartDate == null || r.StartDate <= date)
                && (r.EndDate == null || r.EndDate >= date)).ToListAsync();
        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Court)
            .Where(b => b.YardId == yardId && b.BookingDate == date &&
                        BookingActiveStatuses.Values.Contains(b.Status))
            .OrderBy(b => b.StartTime)
            .ToListAsync();

        var courtStates = new List<CourtDayState>();
        foreach (var court in courts)
        {
            var windows = BuildWindows(yardHours, courtHours.Where(h => h.CourtId == court.Id).ToList(), rules, court.Id, date);
            var availabilityWindows = SubtractBlocks(windows, blocks.Where(b => b.CourtId == null || b.CourtId == court.Id), date);
            var courtBookings = bookings.Where(b => b.CourtId == court.Id)
                .Select(b => new BookingOnDay(b.Id, b.CourtId, b.StartTime, b.EndTime, b.CustomerName, b.Status, b.BookingRef, b.TotalAmount, b.Court?.Name ?? ""))
                .ToList();
            courtStates.Add(new CourtDayState(court.Id, court.Name, court.Type, court.Status, availabilityWindows, courtBookings));
        }
        return new DayAvailability(date, courtStates);
    }
}

public record BookingOnDay(Guid BookingId, Guid CourtId, TimeSpan Start, TimeSpan End, string CustomerName, BookingStatus Status, string BookingRef, decimal Total, string CourtName);

public record CourtDayState(Guid CourtId, string CourtName, CourtType CourtType, CourtStatus Status, IReadOnlyList<SlotWindow> OpenWindows, IReadOnlyList<BookingOnDay> Bookings);

public record DayAvailability(DateOnly Date, IReadOnlyList<CourtDayState> Courts);