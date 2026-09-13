using Microsoft.EntityFrameworkCore;
using Picklebook.Data;
using Picklebook.Domain;

namespace Picklebook.Services;

public class CustomerRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int BookingCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? LastBooking { get; set; }
}

/// <summary>Customer data is strictly scoped to the yard tenant.</summary>
public class CustomerService(ApplicationDbContext db, IUserContext userContext)
{
    public async Task<List<CustomerRow>> GetCustomersAsync(Guid yardId, string? q = null, int take = 300)
    {
        if (!userContext.IsAuthenticated) return [];
        var customers = await db.Customers.AsNoTracking()
            .Include(c => c.Bookings)
            .Where(c => c.YardId == yardId && c.Yard!.OwnerId == userContext.UserId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(1000)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(q))
            customers = customers.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (c.Email ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (c.Phone ?? "").Contains(q)).ToList();

        return customers.Take(take).Select(c => new CustomerRow
        {
            Id = c.Id,
            Name = c.Name,
            Email = c.Email,
            Phone = c.Phone,
            BookingCount = c.Bookings.Count(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow),
            TotalSpent = c.Bookings.Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow).Sum(b => b.TotalAmount),
            LastBooking = c.Bookings.OrderByDescending(b => b.CreatedAtUtc).FirstOrDefault()?.CreatedAtUtc
        }).ToList();
    }

    public async Task<List<Booking>> GetCustomerBookingsAsync(Guid yardId, Guid customerId)
    {
        if (!userContext.IsAuthenticated) return [];
        return await db.Bookings.AsNoTracking().Include(b => b.Court)
            .Where(b => b.YardId == yardId && b.CustomerId == customerId && b.Yard!.OwnerId == userContext.UserId)
            .OrderByDescending(b => b.BookingDate)
            .ToListAsync();
    }
}