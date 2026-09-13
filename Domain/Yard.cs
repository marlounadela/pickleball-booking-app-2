using System.ComponentModel.DataAnnotations;
using Picklebook.Data;

namespace Picklebook.Domain;

public class Yard
{
    public Guid Id { get; set; }

    [Required, MaxLength(60)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Tagline { get; set; }

    public string? Description { get; set; }

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(80)]
    public string? Country { get; set; } = "Philippines";

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(120)]
    public string? Email { get; set; }

    [MaxLength(250)]
    public string? Website { get; set; }

    [MaxLength(250)]
    public string? Facebook { get; set; }

    [MaxLength(250)]
    public string? Instagram { get; set; }

    [MaxLength(250)]
    public string? Twitter { get; set; }

    [MaxLength(10)]
    public string Currency { get; set; } = "PHP";

    [MaxLength(80)]
    public string TimeZoneId { get; set; } = "Asia/Manila";

    public YardStatus Status { get; set; } = YardStatus.Active;

    public string? OperatingHoursNote { get; set; }

    public string? BookingPolicy { get; set; }

    public string? CancellationPolicy { get; set; }

    /// <summary>Owner (ApplicationUser) that administers this yard.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public ApplicationUser? Owner { get; set; }
    public YardSettings? Settings { get; set; }
    public ICollection<Court> Courts { get; set; } = new List<Court>();
    public ICollection<OperatingHour> OperatingHours { get; set; } = new List<OperatingHour>();
    public ICollection<AvailabilityRule> AvailabilityRules { get; set; } = new List<AvailabilityRule>();
    public ICollection<BlockedSchedule> BlockedSchedules { get; set; } = new List<BlockedSchedule>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

public class YardSettings
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    /// <summary>Default slot length in minutes.</summary>
    public int DefaultDurationMinutes { get; set; } = 60;

    /// <summary>Allowed slot lengths (minutes, comma separated).</summary>
    public string AllowedDurationsCsv { get; set; } = "30,60,90,120";

    /// <summary>How far in the future (days) customers may book.</summary>
    public int AdvanceBookingDays { get; set; } = 30;

    /// <summary>Minimum hours in advance required before a slot is bookable.</summary
    public int MinAdvanceHours { get; set; } = 2;

    public decimal ServiceFeePercent { get; set; }

    public decimal ServiceFeeFixed { get; set; }

    public decimal PlatformFeePercent { get; set; }

    public bool AutoConfirm { get; set; }

    public bool RequirePaymentAtBooking { get; set; }

    public string? PaymentInstructions { get; set; }

    public int CancellationNoticeHours { get; set; } = 24;

    public decimal CancellationRefundPercent { get; set; } = 100;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Yard? Yard { get; set; }

    public int[] AllowedDurations() =>
        (AllowedDurationsCsv ?? "60").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.TryParse(s, out var v) ? v : 60).Distinct().OrderBy(x => x).ToArray();

    public void SetAllowedDurations(IEnumerable<int> values) =>
        AllowedDurationsCsv = string.Join(",", values.Distinct().OrderBy(x => x));
}