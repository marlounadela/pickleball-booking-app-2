using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public partial class BookingService
{
    public async Task<decimal> GetTotalPaidAsync(Guid bookingId) =>
        await db.Payments.Where(p => p.BookingId == bookingId && p.Status == PaymentStatus.Paid).SumAsync(p => p.Amount);

    public async Task<List<Payment>> GetPaymentsAsync(Guid bookingId) =>
        await db.Payments.AsNoTracking().Where(p => p.BookingId == bookingId).OrderByDescending(p => p.CreatedAtUtc).ToListAsync();

    public async Task<List<Refund>> GetRefundsAsync(Guid bookingId) =>
        await db.Refunds.AsNoTracking().Where(r => r.BookingId == bookingId).OrderByDescending(r => r.RefundedAtUtc).ToListAsync();

    /// <summary>Owner-facing payment registration. Verifies yard ownership.</summary>
    public async Task<BookingResult> RecordPaymentAsync(Guid bookingId, PaymentMethod method, decimal amount, string? reference, string? note)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.Yard!.OwnerId == userContext.UserId && !b.Yard.IsDeleted);
        if (booking is null) return BookingResult.Fail("Booking not found or not owned by you.");
        if (amount <= 0) return BookingResult.Fail("Payment amount must be positive.");

        var result = await RecordPaymentCoreAsync(booking, method, amount, reference, note, true);
        if (result.Success)
        {
            await audit.LogAsync("Booking.PaymentRecorded", nameof(Booking), booking.Id.ToString(), booking.YardId, userContext.UserId,
                $"Recorded {YardTime.FormatPrice(amount, booking.Currency)} via {method} for {booking.BookingRef}");
        }
        return result;
    }

    /// <summary>Shared payment + transaction + running-totals logic.</summary>
    public async Task<BookingResult> RecordPaymentCoreAsync(Booking booking, PaymentMethod method, decimal amount, string? reference, string? note, bool reprocessStatus = true)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            Method = method,
            Amount = Math.Round(amount, 2),
            Reference = reference,
            Note = note,
            Status = PaymentStatus.Paid,
            PaidAtUtc = DateTime.UtcNow
        };
        booking.Payments.Add(payment);

        var tz = booking.Yard?.TimeZoneId ?? AppConstants.DefaultTimeZone;
        var txnDate = YardTime.Today(tz);
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            YardId = booking.YardId,
            BookingId = booking.Id,
            PaymentId = payment.Id,
            Type = TransactionType.BookingPayment,
            Amount = payment.Amount,
            Description = $"Payment for booking {booking.BookingRef}",
            Method = method,
            Reference = reference,
            Status = PaymentStatus.Paid,
            TransactionDate = txnDate,
            CreatedAtUtc = DateTime.UtcNow
        });

        if (reprocessStatus) await RefreshPaymentAndBookingStatusAsync(booking);
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RecalculateDailyTotalsAsync(booking.YardId, [txnDate]);
        await notifications.PaymentReceivedAsync(booking, payment);
        return BookingResult.Ok(booking.Id, booking.BookingRef);
    }

    /// <summary>Recompute booking payment status (Paid / PartiallyPaid / Pending) and confirm when fully paid.</summary>
    private async Task RefreshPaymentAndBookingStatusAsync(Booking booking)
    {
        var totalPaid = await db.Payments.Where(p => p.BookingId == booking.Id && p.Status == PaymentStatus.Paid).SumAsync(p => p.Amount);
        booking.PaymentStatus = totalPaid <= 0 ? PaymentStatus.Pending
            : totalPaid + 0.01m >= booking.TotalAmount ? PaymentStatus.Paid
            : PaymentStatus.PartiallyPaid;
        if (booking.PaymentStatus == PaymentStatus.Paid && booking.Status == BookingStatus.Pending)
            booking.Status = BookingStatus.Confirmed;
    }

    /// <summary>
    /// Cancel a booking (owner or the customer who booked it). Paid bookings are refunded
    /// automatically according to the yard's cancellation policy.
    /// </summary>
    public async Task<BookingResult> CancelAsync(Guid bookingId, string reason, string? actorUserId = null, bool isOwner = true)
    {
        var booking = await db.Bookings
            .Include(b => b.Yard).ThenInclude(y => y!.Settings)
            .FirstOrDefaultAsync(b => b.Id == bookingId && !b.Yard!.IsDeleted);
        if (booking is null) return BookingResult.Fail("Booking not found.");

        var uid = actorUserId ?? userContext.UserId;
        var isYardOwner = booking.Yard!.OwnerId == uid || await userContext.IsInRoleAsync(AppRoles.SuperAdmin);
        if (!isOwner && !isYardOwner) return BookingResult.Fail("You do not have permission to cancel this booking.");
        if (!isOwner && !await IsBookingCustomerAsync(booking, uid)) return BookingResult.Fail("This booking does not belong to you.");

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Refunded or BookingStatus.Completed)
            return BookingResult.Fail($"This booking is already {booking.Status.ToString().ToLower()}.");

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAtUtc = DateTime.UtcNow;
        booking.CancellationReason = reason;
        booking.UpdatedAtUtc = DateTime.UtcNow;

        var totalPaid = await db.Payments.Where(p => p.BookingId == booking.Id && p.Status == PaymentStatus.Paid).SumAsync(p => p.Amount);
        var txnDates = new List<DateOnly> { YardTime.Today(booking.Yard!.TimeZoneId) };
        if (totalPaid > 0)
        {
            var policy = booking.Yard!.Settings?.CancellationRefundPercent ?? 100m;
            var refundAmount = Math.Round(totalPaid * policy / 100m, 2);
            if (refundAmount > 0)
            {
                var refund = new Refund
                {
                    Id = Guid.NewGuid(),
                    BookingId = booking.Id,
                    Amount = refundAmount,
                    Reason = string.IsNullOrWhiteSpace(reason) ? "Cancelled per cancellation policy" : reason,
                    IsFullRefund = refundAmount + 0.01m >= totalPaid,
                    RefundedAtUtc = DateTime.UtcNow
                };
                var txnDate = YardTime.Today(booking.Yard!.TimeZoneId);
                txnDates.Add(txnDate);
                var tx = new Transaction
                {
                    Id = Guid.NewGuid(),
                    YardId = booking.YardId,
                    BookingId = booking.Id,
                    RefundId = refund.Id,
                    Type = TransactionType.Refund,
                    Amount = -refundAmount,
                    Description = $"Refund for cancelled booking {booking.BookingRef}",
                    Method = PaymentMethod.Other,
                    Status = PaymentStatus.Paid,
                    TransactionDate = txnDate,
                    CreatedAtUtc = DateTime.UtcNow
                };
                refund.Transaction = tx;
                db.Refunds.Add(refund);

                var remaining = totalPaid - refundAmount;
                booking.PaymentStatus = remaining <= 0.01m ? PaymentStatus.Refunded : PaymentStatus.PartiallyPaid;
                if (refund.IsFullRefund) booking.Status = BookingStatus.Refunded;
            }
        }

        await db.SaveChangesAsync();
        await RecalculateDailyTotalsAsync(booking.YardId, txnDates);
        await notifications.BookingCancelledAsync(booking, reason);
        await audit.LogAsync("Booking.Cancelled", nameof(Booking), booking.Id.ToString(), booking.YardId, uid, $"Cancelled {booking.BookingRef} — {reason}");
        return BookingResult.Ok(booking.Id, booking.BookingRef);
    }

    private async Task<bool> IsBookingCustomerAsync(Booking booking, string? actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId)) return false;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorUserId);
        return user is not null && !string.IsNullOrWhiteSpace(booking.CustomerEmail) &&
               string.Equals(user.Email, booking.CustomerEmail, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<BookingResult> ChangeStatusAsync(Guid bookingId, BookingStatus newStatus)
    {
        if (newStatus is BookingStatus.Cancelled or BookingStatus.Refunded)
            return BookingResult.Fail("Use the cancellation flow for cancellations and refunds.");

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.Yard!.OwnerId == userContext.UserId && !b.Yard.IsDeleted);
        if (booking is null) return BookingResult.Fail("Booking not found or not owned by you.");

        booking.Status = newStatus;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        if (newStatus == BookingStatus.Completed) booking.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync("Booking.StatusChanged", nameof(Booking), booking.Id.ToString(), booking.YardId, userContext.UserId,
            $"{booking.BookingRef} → {newStatus}");
        return BookingResult.Ok(booking.Id, booking.BookingRef);
    }

    // ---------- queries ----------

    public async Task<Booking?> GetOwnedBookingAsync(Guid bookingId) =>
        await db.Bookings
            .Include(b => b.Court).Include(b => b.Customer)
            .Include(b => b.Payments).Include(b => b.Refunds)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.Yard!.OwnerId == userContext.UserId && !b.Yard.IsDeleted);

    /// <summary>Public booking detail: accessible by booking id when the customer's email matches.</summary>
    public async Task<Booking?> GetPublicBookingAsync(string yardSlug, Guid bookingId, string? email = null)
    {
        var q = db.Bookings.AsNoTracking()
            .Include(b => b.Court).Include(b => b.Yard).Include(b => b.Payments)
            .Where(b => b.Yard!.Slug == yardSlug && b.Id == bookingId);
        var booking = await q.FirstOrDefaultAsync();
        if (booking is null) return null;
        if (!string.IsNullOrWhiteSpace(email) && string.Equals(booking.CustomerEmail, email, StringComparison.OrdinalIgnoreCase))
            return booking;
        return null;
    }

    public async Task<List<Booking>> GetBookingsAsync(Guid yardId, DateOnly? from = null, DateOnly? to = null,
        BookingStatus? status = null, Guid? courtId = null, string? q = null, int take = 200)
    {
        if (!userContext.IsAuthenticated) return [];
        var query = db.Bookings.AsNoTracking()
            .Include(b => b.Court).Include(b => b.Customer)
            .Where(b => b.YardId == yardId && b.Yard!.OwnerId == userContext.UserId);
        if (from.HasValue) query = query.Where(b => b.BookingDate >= from.Value);
        if (to.HasValue) query = query.Where(b => b.BookingDate <= to.Value);
        if (status.HasValue) query = query.Where(b => b.Status == status.Value);
        if (courtId.HasValue) query = query.Where(b => b.CourtId == courtId.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(b => b.CustomerName.Contains(q) || b.BookingRef.Contains(q) || (b.CustomerEmail ?? "").Contains(q));
        return await query.OrderByDescending(b => b.BookingDate).ThenBy(b => b.StartTime).Take(take).ToListAsync();
    }

    /// <summary>Recompute the running daily totals (net, paid, refunds) for the affected dates.</summary>
    public async Task RecalculateDailyTotalsAsync(Guid yardId, IEnumerable<DateOnly> dates)
    {
        var affected = dates.Distinct().ToList();
        if (affected.Count == 0) return;

        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.YardId == yardId && t.Status == PaymentStatus.Paid && affected.Contains(t.TransactionDate))
            .ToListAsync();

        foreach (var d in affected)
        {
            var dayTxs = transactions.Where(t => t.TransactionDate == d).ToList();
            var net = dayTxs.Sum(t => t.Amount);
            var paid = dayTxs.Where(t => t.Amount > 0).Sum(t => t.Amount);
            var refunds = dayTxs.Where(t => t.Amount < 0).Sum(t => -t.Amount);

            var row = await db.DailyRunningTotals.FirstOrDefaultAsync(r => r.YardId == yardId && r.Date == d);
            if (row is null)
                db.DailyRunningTotals.Add(new DailyRunningTotal { Id = Guid.NewGuid(), YardId = yardId, Date = d, NetTotal = net, PaidTotal = paid, RefundTotal = refunds, UpdatedAtUtc = DateTime.UtcNow });
            else
            {
                row.NetTotal = net;
                row.PaidTotal = paid;
                row.RefundTotal = refunds;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> AddDiscountAsync(Guid bookingId, decimal discount)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.Yard!.OwnerId == userContext.UserId);
        if (booking is null || discount <= 0 || discount > booking.Subtotal) return false;
        booking.DiscountAmount = Math.Round(discount, 2);
        booking.TotalAmount = Math.Round(booking.Subtotal + booking.FeesAmount - booking.DiscountAmount, 2);
        await db.SaveChangesAsync();
        await audit.LogAsync("Booking.Discount", nameof(Booking), booking.Id.ToString(), booking.YardId, userContext.UserId, $"Applied discount {discount}");
        return true;
    }
}