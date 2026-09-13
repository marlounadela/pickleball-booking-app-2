using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public interface INotificationSender
{
    Task SendAsync(string to, string subject, string bodyHtml);
}

/// <summary>Development/mock implementation — logs instead of sending real email.</summary>
public class LogNotificationSender(ILogger logger) : INotificationSender
{
    public Task SendAsync(string to, string subject, string bodyHtml)
    {
        logger.LogInformation("[MOCK EMAIL] to: {To} | subject: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}

/// <summary>Real SMTP sender, activated when EmailSettings are configured.</summary>
public class SmtpNotificationSender(IConfiguration config, ILogger<SmtpNotificationSender> logger) : INotificationSender
{
    public async Task SendAsync(string to, string subject, string bodyHtml)
    {
        var host = config["EmailSettings:Host"];
        var port = int.TryParse(config["EmailSettings:Port"], out var p) ? p : 587;
        var username = config["EmailSettings:Username"];
        var password = config["EmailSettings:Password"];
        var from = config["EmailSettings:From"] ?? "no-reply@picklebook.com";
        if (string.IsNullOrWhiteSpace(host)) { await new LogNotificationSender(logger).SendAsync(to, subject, bodyHtml); return; }

        using var client = new System.Net.Mail.SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new System.Net.NetworkCredential(username, password)
        };
        var msg = new System.Net.Mail.MailMessage(from, to, subject, bodyHtml) { IsBodyHtml = true };
        await client.SendMailAsync(msg);
    }
}

/// <summary>Push in-app notifications for owners and route transactional emails through the sender abstraction.</summary>
public class NotificationService(ApplicationDbContext db, INotificationSender sender, ILogger<NotificationService> logger)
{
    public async Task BookingRequestedAsync(Booking booking, Yard yard) =>
        await NotifyOwnerAsync(yard, NotificationType.BookingRequested,
            "New booking request",
            $"{booking.CustomerName} requested {booking.BookingRef} on {booking.BookingDate:MMM d} at {YardTime.FormatTime(booking.StartTime)}.");

    public async Task BookingCreatedAsync(Booking booking, Yard yard, Customer customer, decimal prepaid)
    {
        await db.Entry(booking).Reference(b => b.Court).LoadAsync();

        await NotifyOwnerAsync(yard, prepaid > 0 ? NotificationType.NewBooking : NotificationType.BookingRequested,
            prepaid > 0 ? "New paid booking" : "New booking request",
            $"{booking.CustomerName} booked {booking.BookingRef} · {booking.BookingDate:MMM d} · {YardTime.FormatTimeRange(booking.StartTime, booking.EndTime)} · {YardTime.FormatPrice(booking.TotalAmount, booking.Currency)}");

        var emailBody = BuildBookingEmail(booking, yard, prepaid);
        await SendEmailAsync(booking.CustomerEmail,
            $"Booking {booking.Status} — {booking.BookingRef}", emailBody);
    }

    public async Task BookingCancelledAsync(Booking booking, string reason)
    {
        if (booking.Yard is null) await db.Entry(booking).Reference(b => b.Yard).LoadAsync();
        await NotifyOwnerAsync(booking.Yard!, NotificationType.BookingCancellation,
            "Booking cancelled",
            $"{booking.BookingRef} ({booking.CustomerName}) was cancelled. Reason: {reason}");

        var refundNote = booking.PaymentStatus == PaymentStatus.Refunded
            ? "<p>Your payment has been refunded.</p>" : "";
        await SendEmailAsync(booking.CustomerEmail,
            $"Booking cancelled — {booking.BookingRef}",
            $"<p>Hi {booking.CustomerName},</p><p>Your booking <b>{booking.BookingRef}</b> for {booking.BookingDate:MMMM d, yyyy} has been cancelled.</p>{refundNote}<p>Reason: {reason}</p>");
    }

    public async Task PaymentReceivedAsync(Booking booking, Payment payment)
    {
        if (booking.Yard is null) await db.Entry(booking).Reference(b => b.Yard).LoadAsync();
        await NotifyOwnerAsync(booking.Yard!, NotificationType.PaymentReceived,
            "Payment received",
            $"{YardTime.FormatPrice(payment.Amount, booking.Currency)} received for {booking.BookingRef} ({payment.Method}).");

        await SendEmailAsync(booking.CustomerEmail,
            $"Payment confirmed — {YardTime.FormatPrice(payment.Amount, booking.Currency)} received",
            $"<p>Hi {booking.CustomerName},</p><p>We received your payment of <b>{YardTime.FormatPrice(payment.Amount, booking.Currency)}</b> for booking <b>{booking.BookingRef}</b>.</p><p>See <b>{booking.BookingDate:MMMM d, yyyy}</b> at <b>{YardTime.FormatTime(booking.StartTime)}</b> at {booking.Yard?.Name}.</p>");
    }

    private async Task NotifyOwnerAsync(Yard yard, NotificationType type, string subject, string body)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            YardId = yard.Id,
            UserId = yard.OwnerId,
            Type = type,
            Channel = NotificationChannel.InApp,
            Subject = subject,
            Body = body,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
    }

    private static string BuildBookingEmail(Booking booking, Yard yard, decimal prepaid)
    {
        var confirmNote = booking.Status == BookingStatus.Confirmed
            ? "Your booking is confirmed."
            : "Your booking is pending the yard owner's confirmation.";
        var paymentNote = prepaid > 0
            ? $"<p>We received <b>{YardTime.FormatPrice(prepaid, booking.Currency)}</b>. Thank you!</p>"
            : $"<p>Please pay <b>{YardTime.FormatPrice(booking.TotalAmount, booking.Currency)}</b> at the venue.</p>";
        var instructions = string.IsNullOrWhiteSpace(yard.Settings?.PaymentInstructions) ? "" : $"<p><b>Payment info:</b> {yard.Settings!.PaymentInstructions}</p>";

        return $"""
            <div style="font-family:Segoe UI, Arial, sans-serif; max-width:600px; margin:auto; border:1px solid #e5e7eb; border-radius:12px; overflow:hidden;">
              <div style="background:#111827; color:#fff; padding:18px 24px; font-weight:700; font-size:20px;">{yard.Name}</div>
              <div style="padding:24px;">
                <p>Hi <b>{booking.CustomerName}</b>,</p>
                <p>{confirmNote}</p>
                <p><b>Booking reference:</b> {booking.BookingRef}</p>
                <p><b>Court:</b> {booking.Court?.Name ?? "—"}<br/>
                   <b>When:</b> {booking.BookingDate:MMMM d, yyyy}, {YardTime.FormatTimeRange(booking.StartTime, booking.EndTime)}<br/>
                   <b>Duration:</b> {booking.DurationMinutes} minutes<br/>
                   <b>Total:</b> {YardTime.FormatPrice(booking.TotalAmount, booking.Currency)}</p>
                {paymentNote}
                {instructions}
                <p style="color:#6b7280; font-size:13px;">{yard.Name} · {yard.City} · {yard.Phone}</p>
              </div>
            </div>
            """;
    }

    private async Task SendEmailAsync(string? to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(to)) return;
        try { await sender.SendAsync(to, subject, body); }
        catch (Exception ex) { logger.LogError(ex, "Failed to send notification email."); }
    }

    /// <summary>In-app inbox for a user (owner notifications).</summary>
    public async Task<List<Notification>> GetInboxAsync(string userId, bool unreadOnly = false, int take = 50)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        return await q.OrderByDescending(n => n.CreatedAtUtc).Take(take).ToListAsync();
    }

    public async Task MarkReadAsync(Guid notificationId)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId);
        if (n is null) return;
        n.IsRead = true;
        await db.SaveChangesAsync();
    }

    public async Task<int> UnreadCountAsync(string userId) =>
        await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
}