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
        var query = db.Customers.AsNoTracking()
            .Where(c => c.YardId == yardId && c.Yard!.OwnerId == userContext.UserId);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => c.Name.Contains(q) || (c.Email ?? "").Contains(q) || (c.Phone ?? "").Contains(q));
        var customers = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new { c.Id, c.Name, c.Email, c.Phone })
            .Take(Math.Max(1, take))
            .ToListAsync();
        if (customers.Count == 0) return [];

        var ids = customers.Select(c => c.Id).ToList();
        var stats = await db.Bookings.AsNoTracking()
            .Where(b => b.YardId == yardId && ids.Contains(b.CustomerId))
            .GroupBy(b => b.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Count = g.Count(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow),
                Total = g.Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow)
                    .Sum(b => (decimal?)b.TotalAmount) ?? 0,
                Last = g.Max(b => (DateTime?)b.CreatedAtUtc)
            })
            .ToListAsync();
        var byId = stats.ToDictionary(s => s.CustomerId);

        return customers.Select(c =>
        {
            byId.TryGetValue(c.Id, out var s);
            return new CustomerRow
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                BookingCount = s?.Count ?? 0,
                TotalSpent = s?.Total ?? 0,
                LastBooking = s?.Last
            };
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