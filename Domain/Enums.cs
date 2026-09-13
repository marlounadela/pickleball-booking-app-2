namespace Picklebook.Domain;

public enum YardStatus
{
    Active = 0,
    Inactive = 1,
    Suspended = 2
}

public enum CourtType
{
    Indoor = 0,
    Outdoor = 1
}

public enum CourtStatus
{
    Available = 0,
    Maintenance = 1,
    Inactive = 2
}

public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow = 4,
    Refunded = 5
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    PartiallyPaid = 2,
    Failed = 3,
    Refunded = 4
}

public enum PaymentMethod
{
    Cash = 0,
    GCash = 1,
    Card = 2,
    BankTransfer = 3,
    Online = 4,
    Other = 5
}

public enum TransactionType
{
    BookingPayment = 0,
    PartialPayment = 1,
    Refund = 2,
    Adjustment = 3
}

public enum BlockScheduleType
{
    Blocked = 0,
    Maintenance = 1,
    Holiday = 2,
    Closure = 3
}

public enum NotificationChannel
{
    InApp = 0,
    Email = 1,
    Sms = 2
}

public enum NotificationType
{
    BookingConfirmation = 0,
    BookingCancellation = 1,
    PaymentConfirmation = 2,
    BookingReminder = 3,
    NewBooking = 4,
    PaymentReceived = 5,
    BookingRequested = 6,
    Generic = 99
}

/// <summary>Statuses that lock a time slot (used by the double-booking guard).</summary>
public static class BookingActiveStatuses
{
    public static readonly BookingStatus[] Values = { BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Completed };

    public static string SqlList => "(0,1,2)";
}