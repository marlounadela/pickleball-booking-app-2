using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class BookingResult
{
    public bool Success { get; init; }
    public bool Conflict { get; init; }
    public string? Error { get; init; }
    public Guid? BookingId { get; init; }
    public string? BookingRef { get; init; }
    public static BookingResult Ok(Guid id, string bookingRef) => new() { Success = true, BookingId = id, BookingRef = bookingRef };
    public static BookingResult Fail(string error) => new() { Success = false, Error = error };
    public static BookingResult SlotTaken() => new() { Success = false, Conflict = true, Error = "That time slot was just booked by someone else. Please pick another." };
}

public class BookingCreateRequest
{
    public required string YardSlug { get; init; }
    public required Guid CourtId { get; init; }
    public required DateOnly Date { get; init; }
    public required TimeSpan StartTime { get; init; }
    public required int DurationMinutes { get; init; }
    public required string CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerPhone { get; init; }
    public string? Notes { get; init; }
    public decimal DiscountAmount { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public string? PaymentReference { get; init; }
    public decimal? PaymentAmount { get; init; }
}

public record Quote(Yard Yard, Court Court, DateOnly Date, TimeSpan Start, TimeSpan End, int DurationMinutes,
    decimal RatePerHour, decimal Subtotal, decimal DiscountAmount, decimal FeesAmount, decimal TotalAmount, decimal FeePercent, string Currency);

public partial class BookingService(ApplicationDbContext db, IUserContext userContext, AuditService audit, NotificationService notifications)
{
    /// <summary>Server-side price quote. NEVER trusts client figures.</summary>
    public async Task<Quote> QuoteAsync(string yardSlug, Guid courtId, DateOnly date, TimeSpan start, int durationMinutes)
    {
        var yard = await db.Yards.AsNoTracking().Include(y => y.Settings).FirstOrDefaultAsync(y => y.Slug == yardSlug && !y.IsDeleted)
            ?? throw new SlotUnavailableException("Yard not found.");
        var court = await db.Courts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courtId && c.YardId == yard.Id)
            ?? throw new SlotUnavailableException("Court not found.");
        if (court.Status != CourtStatus.Available) throw new SlotUnavailableException("This court is currently unavailable.");

        var durations = yard.Settings?.AllowedDurations() ?? [60];
        if (!durations.Contains(durationMinutes))
            durationMinutes = durations[0];

        var durationH = (decimal)durationMinutes / 60m;
        var subtotal = Math.Round(court.HourlyRate * durationH, 2);
        var s = yard.Settings;
        var fees = Math.Round(subtotal * ((s?.ServiceFeePercent ?? 0) + (s?.PlatformFeePercent ?? 0)) / 100m + (s?.ServiceFeeFixed ?? 0), 2);
        var total = Math.Round(subtotal + fees, 2);

        return new Quote(yard, court, date, start, TimeSpan.FromMinutes(durationMinutes), durationMinutes,
            court.HourlyRate, subtotal, 0m, fees, total, s?.ServiceFeePercent ?? 0, yard.Currency);
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        if (ex is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 or 2067 or 1555 }) return true;
        return ex.InnerException is not null && IsUniqueConstraintViolation(ex.InnerException);
    }

    /// <summary>
    /// The booking engine. Wrapped in a serializable transaction:
    /// 1. acquires the SQLite write lock so concurrent writers serialize,
    /// 2. re-checks availability on the same connection/snapshot,
    /// 3. inserts — the partial unique index (court/date/start, active statuses) is the final guard.
    /// </summary>
    public async Task<BookingResult> CreateAsync(BookingCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerName)) return BookingResult.Fail("Customer name is required.");
        if (string.IsNullOrWhiteSpace(req.CustomerEmail) && string.IsNullOrWhiteSpace(req.CustomerPhone))
            return BookingResult.Fail("Contact email or phone is required.");

        var yard = await db.Yards.AsNoTracking().Include(y => y.Settings)
            .FirstOrDefaultAsync(y => y.Slug == req.YardSlug && !y.IsDeleted);
        if (yard is null) return BookingResult.Fail("Yard not found.");
        if (yard.Status != YardStatus.Active) return BookingResult.Fail("This yard is not accepting bookings right now.");

        var court = await db.Courts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CourtId && c.YardId == yard.Id);
        if (court is null) return BookingResult.Fail("Court not found.");
        if (court.Status != CourtStatus.Available) return BookingResult.Fail("This court is currently unavailable.");

        var settings = yard.Settings;
        var durations = settings?.AllowedDurations() ?? [60];
        if (!durations.Contains(req.DurationMinutes))
            return BookingResult.Fail($"Only these durations are allowed: {string.Join(" / ", durations)} minutes.");

        var today = YardTime.Today(yard.TimeZoneId);
        if (req.Date < today) return BookingResult.Fail("You cannot book a date in the past.");
        if (req.Date > today.AddDays(Math.Max(0, settings?.AdvanceBookingDays ?? 30)))
            return BookingResult.Fail($"Bookings open up to {today.AddDays(settings?.AdvanceBookingDays ?? 30):MMM d, yyyy}.");

        var start = req.StartTime;
        var end = start + TimeSpan.FromMinutes(req.DurationMinutes);
        if (end <= start) return BookingResult.Fail("Invalid slot time.");

        if (req.Date == today)
        {
            var nowTime = YardTime.NowInZone(yard.TimeZoneId).TimeOfDay;
            var minAdvance = TimeSpan.FromHours(settings?.MinAdvanceHours ?? 0);
            if (end <= nowTime) return BookingResult.Fail("This slot is already in the past.");
            if (start < nowTime + minAdvance)
                return BookingResult.Fail($"Bookings must be made at least {settings?.MinAdvanceHours ?? 0} hour(s) in advance.");
        }

        // ---- server-side pricing (never trust the client) ----
        var durationH = (decimal)req.DurationMinutes / 60m;
        var subtotal = Math.Round(court.HourlyRate * durationH, 2);
        var fees = Math.Round(subtotal * ((settings?.ServiceFeePercent ?? 0) + (settings?.PlatformFeePercent ?? 0)) / 100m + (settings?.ServiceFeeFixed ?? 0), 2);
        var discount = Math.Clamp(Math.Round(req.DiscountAmount, 2), 0m, subtotal);
        var total = Math.Round(subtotal + fees - discount, 2);

        Booking booking;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        // Take the write lock so concurrent booking attempts serialize here.
        await db.Database.ExecuteSqlRawAsync("BEGIN IMMEDIATE");

        if (await ConflictExistsAsync(yard.Id, court.Id, req.Date, start, end))
        {
            await tx.RollbackAsync();
            return BookingResult.SlotTaken();
        }
        var customer = await FindOrCreateCustomerAsync(yard.Id, req);
        string bookingRef;
        try
        {
            bookingRef = BookingRef.Generate(yard.Slug);
            while (await db.Bookings.AnyAsync(b => b.BookingRef == bookingRef))
                bookingRef = BookingRef.Generate(yard.Slug);

            booking = new Booking
            {
                Id = Guid.NewGuid(),
                YardId = yard.Id,
                CourtId = court.Id,
                CustomerId = customer.Id,
                BookingRef = bookingRef,
                BookingDate = req.Date,
                StartTime = start,
                EndTime = end,
                DurationMinutes = req.DurationMinutes,
                Rate = court.HourlyRate,
                Currency = yard.Currency,
                Subtotal = subtotal,
                DiscountAmount = discount,
                FeesAmount = fees,
                TotalAmount = total,
                PaymentStatus = PaymentStatus.Pending,
                Status = settings?.AutoConfirm == true ? BookingStatus.Confirmed : BookingStatus.Pending,
                CustomerName = req.CustomerName.Trim(),
                CustomerEmail = req.CustomerEmail?.Trim(),
                CustomerPhone = req.CustomerPhone?.Trim(),
                Notes = req.Notes,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await tx.RollbackAsync();
            return BookingResult.SlotTaken();
        }

        await tx.CommitAsync();

        // ---- side effects: payment + notifications + audit ----
        var paidNow = req.PaymentMethod.HasValue ? Math.Round(req.PaymentAmount ?? total, 2) : 0m;
        if (paidNow > 0m)
            await RecordPaymentCoreAsync(booking, req.PaymentMethod!.Value, paidNow, req.PaymentReference, "Payment recorded at booking time", false);

        await notifications.BookingCreatedAsync(booking, yard, customer, paidNow);
        await audit.LogAsync("Booking.Created", nameof(Booking), booking.Id.ToString(), yard.Id, userContext.UserId,
            $"Created {bookingRef} for {req.CustomerName} · {court.Name} · {req.Date:MMM d} {YardTime.FormatTime(start)}");

        return BookingResult.Ok(booking.Id, bookingRef);
    }

    private async Task<Customer> FindOrCreateCustomerAsync(Guid yardId, BookingCreateRequest req)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.YardId == yardId && c.Email == req.CustomerEmail && !string.IsNullOrWhiteSpace(req.CustomerEmail));
        if (customer is null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                YardId = yardId,
                Name = req.CustomerName.Trim(),
                Email = req.CustomerEmail?.Trim(),
                Phone = req.CustomerPhone?.Trim()
            };
            db.Customers.Add(customer);
        }
        else
        {
            customer.Name = req.CustomerName.Trim();
            if (!string.IsNullOrWhiteSpace(req.CustomerPhone)) customer.Phone = req.CustomerPhone.Trim();
        }
        return customer;
    }

    private async Task<bool> ConflictExistsAsync(Guid yardId, Guid courtId, DateOnly date, TimeSpan start, TimeSpan end)
    {
        var overlap = await db.Bookings.AnyAsync(b =>
            b.YardId == yardId && b.CourtId == courtId && b.BookingDate == date &&
            BookingActiveStatuses.Values.Contains(b.Status) &&
            b.StartTime < end && b.EndTime > start);
        if (overlap) return true;

        var blocks = await db.BlockedSchedules
            .Where(b => b.YardId == yardId && (b.CourtId == null || b.CourtId == courtId))
            .ToListAsync();
        return blocks.Any(b => b.AppliesToInterval(date, start, end));
    }
}