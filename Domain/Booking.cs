using System.ComponentModel.DataAnnotations;

namespace Picklebook.Domain;

public class Booking
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    public Guid CourtId { get; set; }

    public Guid CustomerId { get; set; }

    [Required, MaxLength(24)]
    public string BookingRef { get; set; } = string.Empty;

    public DateOnly BookingDate { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>Per-hour rate at the time of booking.</summary>
    public decimal Rate { get; set; }

    public string Currency { get; set; } = "PHP";

    public decimal Subtotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal FeesAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    /// <summary>Denormalized customer snapshot for the booking.</summary>
    [Required, MaxLength(120)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? CustomerEmail { get; set; }

    [MaxLength(30)]
    public string? CustomerPhone { get; set; }

    public string? Notes { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Yard? Yard { get; set; }
    public Court? Court { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}

public class Payment
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    public decimal Amount { get; set; }

    [MaxLength(120)]
    public string? Reference { get; set; }

    public string? Note { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;

    public DateTime? PaidAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Booking? Booking { get; set; }
}

public class Transaction
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    public Guid? BookingId { get; set; }

    public Guid? PaymentId { get; set; }

    public Guid? RefundId { get; set; }

    public TransactionType Type { get; set; }

    /// <summary>Signed amount: positive = income, negative = refund/adjustment.</summary>
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    [MaxLength(120)]
    public string? Reference { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;

    /// <summary>Yard-local calendar date this transaction counts towards revenue.</summary>
    public DateOnly TransactionDate { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Yard? Yard { get; set; }
    public Booking? Booking { get; set; }
    public Payment? Payment { get; set; }
    public Refund? Refund { get; set; }
}

public class Refund
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(300)]
    public string Reason { get; set; } = string.Empty;

    public bool IsFullRefund { get; set; }

    public DateTime RefundedAtUtc { get; set; } = DateTime.UtcNow;

    public Booking? Booking { get; set; }
    public Transaction? Transaction { get; set; }
}

public class Notification
{
    public Guid Id { get; set; }

    public Guid? YardId { get; set; }

    /// <summary>Target application user (null for email-to-customer-only).</summary>
    public string? UserId { get; set; }

    public NotificationType Type { get; set; } = NotificationType.Generic;

    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    [MaxLength(150)]
    public string Subject { get; set; } = string.Empty;

    public string? Body { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? SentAtUtc { get; set; }

    public Yard? Yard { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; }

    public Guid? YardId { get; set; }

    public string? UserId { get; set; }

    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(80)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(80)]
    public string EntityId { get; set; } = string.Empty;

    public string? Details { get; set; }

    [MaxLength(60)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Running daily revenue snapshot, recomputed whenever transactions change.</summary>
public class DailyRunningTotal
{
    public Guid Id { get; set; }

    public Guid YardId { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Sum of all signed transaction amounts for the date.</summary>
    public decimal NetTotal { get; set; }

    /// <summary>Sum of positive transactions (income) for the date.</summary>
    public decimal PaidTotal { get; set; }

    /// <summary>Sum of negative transaction amounts (refunds) absolute value for the date.</summary>
    public decimal RefundTotal { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Yard? Yard { get; set; }
}

public class PlatformSetting
{
    [MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? Description { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}