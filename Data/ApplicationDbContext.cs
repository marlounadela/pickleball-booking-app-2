using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Picklebook.Domain;

namespace Picklebook.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Yard> Yards => Set<Yard>();
    public DbSet<YardSettings> YardSettings => Set<YardSettings>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<OperatingHour> OperatingHours => Set<OperatingHour>();
    public DbSet<AvailabilityRule> AvailabilityRules => Set<AvailabilityRule>();
    public DbSet<BlockedSchedule> BlockedSchedules => Set<BlockedSchedule>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DailyRunningTotal> DailyRunningTotals => Set<DailyRunningTotal>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // ---------- Yard ----------
        b.Entity<Yard>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne(x => x.Owner).WithMany(x => x.Yards).HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Settings).WithOne(x => x.Yard).HasForeignKey<YardSettings>(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<YardSettings>(e =>
        {
            e.HasIndex(x => x.YardId).IsUnique();
        });

        // ---------- Court ----------
        b.Entity<Court>(e =>
        {
            e.HasIndex(x => x.YardId);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne(x => x.Yard).WithMany(x => x.Courts).HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- OperatingHour ----------
        b.Entity<OperatingHour>(e =>
        {
            e.HasIndex(x => new { x.YardId, x.DayOfWeek });
            e.HasIndex(x => new { x.YardId, x.CourtId });
            e.HasOne(x => x.Yard).WithMany(x => x.OperatingHours).HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Court).WithMany(x => x.OperatingHours).HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- AvailabilityRule / BlockedSchedule ----------
        b.Entity<AvailabilityRule>(e =>
        {
            e.HasIndex(x => x.YardId);
            e.HasOne(x => x.Yard).WithMany(x => x.AvailabilityRules).HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Court).WithMany(x => x.AvailabilityRules).HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BlockedSchedule>(e =>
        {
            e.HasIndex(x => x.YardId);
            e.HasOne(x => x.Yard).WithMany(x => x.BlockedSchedules).HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Court).WithMany(x => x.BlockedSchedules).HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Customer ----------
        b.Entity<Customer>(e =>
        {
            e.HasIndex(x => new { x.YardId, x.Email });
            e.HasOne(x => x.Yard).WithMany(x => x.Customers).HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Booking ----------
        b.Entity<Booking>(e =>
        {
            e.HasIndex(x => x.BookingRef).IsUnique();
            e.HasIndex(x => new { x.CourtId, x.BookingDate, x.StartTime });
            e.HasIndex(x => new { x.YardId, x.BookingDate });
            e.HasIndex(x => new { x.YardId, x.Status });
            e.HasIndex(x => x.CustomerId);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.BookingRef).HasMaxLength(24);

            // Concurrency token — optimistic concurrency for the duplicate-slot guard.
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();

            e.HasOne(x => x.Yard).WithMany(x => x.Bookings).HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Court).WithMany(x => x.Bookings).HasForeignKey(x => x.CourtId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany(x => x.Bookings).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Refunds).WithOne(x => x.Booking).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        // Partial unique index: the same court + date + start-time can only ever have ONE
        // active booking (Pending/Confirmed/Completed). Cancelled/NoShow/Refunded slots
        // are freed so the slot can be re-booked.
        b.Entity<Booking>().HasIndex(x => new { x.CourtId, x.BookingDate, x.StartTime })
            .IsUnique()
            .HasFilter($"[Status] IN {BookingActiveStatuses.SqlList}");

        // ---------- Payment / Transaction / Refund ----------
        b.Entity<Payment>(e =>
        {
            e.HasIndex(x => x.BookingId);
            e.HasOne(x => x.Booking).WithMany(x => x.Payments).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Transaction>(e =>
        {
            e.HasIndex(x => new { x.YardId, x.TransactionDate });
            e.HasIndex(x => x.BookingId);
            e.HasOne(x => x.Yard).WithMany().HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Refund>(e =>
        {
            e.HasIndex(x => x.BookingId);
            e.HasOne(x => x.Transaction).WithOne(x => x.Refund).HasForeignKey<Transaction>(x => x.RefundId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---------- Daily running totals ----------
        b.Entity<DailyRunningTotal>(e =>
        {
            e.HasIndex(x => new { x.YardId, x.Date }).IsUnique();
            e.HasOne(x => x.Yard).WithMany().HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
        });

        // ---------- Notification / Audit log / Settings ----------
        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.HasOne(x => x.Yard).WithMany().HasForeignKey(x => x.YardId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.YardId, x.CreatedAtUtc });
        });

        b.Entity<PlatformSetting>(e =>
        {
            e.HasKey(x => x.Key);
        });
    }
}
