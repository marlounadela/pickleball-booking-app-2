using System.ComponentModel.DataAnnotations;

namespace Picklebook.Domain;

public class Court
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Number { get; set; }

    public CourtType Type { get; set; } = CourtType.Indoor;

    /// <summary>Hourly rate in the yard's currency.</summary>
    public decimal HourlyRate { get; set; }

    public CourtStatus Status { get; set; } = CourtStatus.Available;

    public string? Description { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public Yard? Yard { get; set; }
    public ICollection<OperatingHour> OperatingHours { get; set; } = new List<OperatingHour>();
    public ICollection<AvailabilityRule> AvailabilityRules { get; set; } = new List<AvailabilityRule>();
    public ICollection<BlockedSchedule> BlockedSchedules { get; set; } = new List<BlockedSchedule>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class OperatingHour
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    [Required]
    public int DayOfWeek { get; set; }

    public TimeSpan OpenTime { get; set; }

    public TimeSpan CloseTime { get; set; }

    public bool IsClosed { get; set; }

    /// <summary>Null = applies to all courts of the yard.</summary>
    public Guid? CourtId { get; set; }

    public Yard? Yard { get; set; }
    public Court? Court { get; set; }
}
public class AvailabilityRule
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    /// <summary>Null = applies to all courts.</summary>
    public Guid? CourtId { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public DateOnly? SingleDate { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public int? DayOfWeek { get; set; }

    /// <summary>Null = all day.</summary>
    public TimeSpan? StartTime { get; set; }

    /// <summary>Null = all day.</summary>
    public TimeSpan? EndTime { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Yard? Yard { get; set; }
    public Court? Court { get; set; }

    public bool AppliesOn(DateOnly date)
    {
        if (!IsActive) return false;
        if (SingleDate.HasValue && date == SingleDate) return true;
        var inRange = (!StartDate.HasValue || date >= StartDate.Value) && (!EndDate.HasValue || date <= EndDate.Value);
        if (!inRange) return false;
        if (DayOfWeek.HasValue) return (int)date.DayOfWeek == DayOfWeek.Value;
        return true;
    }
}

public class BlockedSchedule
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    /// <summary>Null = whole yard.</summary>
    public Guid? CourtId { get; set; }

    [Required, MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    public BlockScheduleType Type { get; set; } = BlockScheduleType.Blocked;

    /// <summary>Specific single date (e.g. a holiday).</summary>
    public DateOnly? SingleDate { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    /// <summary>Null = all day. When both times null the whole day is blocked.</summary>
    public TimeSpan? StartTime { get; set; }

    public TimeSpan? EndTime { get; set; }

    public bool RepeatsWeekly { get; set; }

    public int? WeeklyDayOfWeek { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Yard? Yard { get; set; }
    public Court? Court { get; set; }

    public bool AppliesToInterval(DateOnly date, TimeSpan start, TimeSpan end)
    {
        if (!AppliesOnDate(date)) return false;
        if (StartTime.HasValue || EndTime.HasValue)
        {
            var bs = StartTime ?? TimeSpan.Zero;
            var be = EndTime ?? TimeSpan.FromDays(1);
            return Math.Max(start.Ticks, bs.Ticks) < Math.Min(end.Ticks, be.Ticks);
        }
        return true;
    }

    public bool AppliesOnDate(DateOnly date)
    {
        if (date < (SingleDate ?? StartDate ?? DateOnly.MinValue)) return false;
        if (EndDate.HasValue && date > EndDate) return false;
        if (SingleDate.HasValue && date != SingleDate) return false;
        if (RepeatsWeekly && WeeklyDayOfWeek.HasValue && (int)date.DayOfWeek != WeeklyDayOfWeek.Value)
            return false;
        return true;
    }
}

public class Customer
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Yard? Yard { get; set; }
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}