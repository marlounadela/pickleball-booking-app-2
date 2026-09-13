using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Picklebook.Domain;
using Picklebook.Services;

namespace Picklebook.Data
{
    public static class SeedData
    {
        public const string SuperAdminEmail = "admin@picklebook.com";
        public const string SuperAdminPassword = "Admin@123!";
        public const string DemoOwnerEmail = "owner@smashyard.com";
        public const string DemoOwnerPassword = "Owner@123!";
        public const string CebuOwnerEmail = "owner@cebupickle.com";
        public const string CebuOwnerPassword = "Owner@123!";

        public static async Task SeedAsync(IServiceProvider services, bool isProduction, IConfiguration config)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();

            foreach (var role in AppRoles.All)
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));

            // Demo data includes accounts with well-known passwords (admin@picklebook.com / Admin@123!,
            // owner@smashyard.com / Owner@123!, player@picklebook.com / Player@123!). Those must NEVER be
            // created on a production instance unless the operator explicitly opts in.
            var seedDemo = !isProduction
                || string.Equals(config["SEED_DEMO_DATA"], "true", StringComparison.OrdinalIgnoreCase);

            if (seedDemo)
            {
                await SeedDemoDataAsync(db, userManager);
            }
            else
            {
                await SeedPlatformSettingsAsync(db);
                await SeedBootstrapAdminAsync(userManager, config);
                await db.SaveChangesAsync();
            }
        }

        /// <summary>Full demo dataset (roles, demo users, yards, bookings, platform settings).</summary>
        private static async Task SeedDemoDataAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            if (await db.Yards.IgnoreQueryFilters().AnyAsync()) return; // already seeded

            var superAdmin = await EnsureUserAsync(userManager, SuperAdminEmail, SuperAdminPassword, "Platform", "Admin", AppRoles.SuperAdmin);
            var smashOwner = await EnsureUserAsync(userManager, DemoOwnerEmail, DemoOwnerPassword, "Mar", "Lou", AppRoles.Owner);
            var cebuOwner = await EnsureUserAsync(userManager, CebuOwnerEmail, CebuOwnerPassword, "Bea", "Aguilar", AppRoles.Owner);
            await EnsureUserAsync(userManager, "player@picklebook.com", "Player@123!", "Player", "Demo", AppRoles.Player);

            var today = YardTime.Today(AppConstants.DefaultTimeZone);

            // ---------------- Smash Yard (full demo tenant) ----------------
            var smash = new Yard
            {
                Id = Guid.NewGuid(),
                Slug = "smash-yard",
                Name = "Smash Yard",
                Tagline = "Cebu's home court for serious pickleball",
                Description = "Smash Yard is a dedicated indoor pickleball facility with premium cushioned courts, night lighting, training programs and friendly open-play sessions for all levels.",
                Address = "Unit 4, Seaside Sports Complex",
                City = "Mandaue City",
                Province = "Cebu",
                Country = "Philippines",
                Latitude = 10.3235,
                Longitude = 123.9380,
                Phone = "+63 917 555 1234",
                Email = "play@smashyard.com",
                Facebook = "https://facebook.com/smashyard",
                Instagram = "https://instagram.com/smashyard",
                Currency = "PHP",
                TimeZoneId = "Asia/Manila",
                Status = YardStatus.Active,
                OwnerId = smashOwner.Id,
                OperatingHoursNote = "Open daily 8:00 AM - 9:00 PM",
                BookingPolicy = "Slots are 60 minutes by default (30/90/120 available).\nReserve online; walk-ins subject to availability.\nPlease arrive 10 minutes before your slot.",
                CancellationPolicy = "Free cancellation up to 24 hours before your slot. Cancellations after that are charged 50% of the booking fee.",
                CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
                Settings = new YardSettings
                {
                    Id = Guid.NewGuid(),
                    DefaultDurationMinutes = 60,
                    ServiceFeePercent = 3,
                    PlatformFeePercent = 2,
                    AdvanceBookingDays = 30,
                    MinAdvanceHours = 1,
                    AutoConfirm = true,
                    RequirePaymentAtBooking = false,
                    PaymentInstructions = "Cash at the venue, or GCash to 0917 555 1234 (reference: your booking code).",
                    CancellationNoticeHours = 24,
                    CancellationRefundPercent = 100
                }
            };
            db.Yards.Add(smash);

            var c1 = Court(smash.Id, "Court 1", "1", CourtType.Indoor, 500, "Premium cushioned indoor court with tournament-standard lines.");
            var c2 = Court(smash.Id, "Court 2", "2", CourtType.Indoor, 500, "Indoor court with competition lighting.");
            var c3 = Court(smash.Id, "Court 3", "3", CourtType.Outdoor, 400, "All-weather outdoor court with shaded benches.");
            db.Courts.AddRange(c1, c2, c3);

            db.BlockedSchedules.Add(new BlockedSchedule
            {
                Id = Guid.NewGuid(), YardId = smash.Id, CourtId = c1.Id, Title = "Weekly court maintenance",
                Type = BlockScheduleType.Maintenance, StartDate = today, EndDate = today.AddDays(60),
                StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(10, 0, 0),
                RepeatsWeekly = true, WeeklyDayOfWeek = 1, Reason = "Resurfacing and line repainting",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.BlockedSchedules.Add(new BlockedSchedule
            {
                Id = Guid.NewGuid(), YardId = smash.Id, Title = "Community holiday closure",
                Type = BlockScheduleType.Holiday, SingleDate = today.AddDays(10),
                Reason = "Venue closed for the community holiday", CreatedAtUtc = DateTime.UtcNow
            });

            var michelle = Customer(smash.Id, "Michelle Tan", "michelle.tan@mail.com", "0917 111 2233");
            var jose = Customer(smash.Id, "Jose Ramirez", "jose.ramirez@mail.com", "0918 222 3344");
            var ana = Customer(smash.Id, "Ana Lim", "ana.lim@mail.com", "0919 333 4455");
            db.Customers.AddRange(michelle, jose, ana);

            var bookings = SeedSmashBookings(smash, db, c1, c2, c3, michelle, jose, ana, today);
            db.Bookings.AddRange(bookings);

            await db.SaveChangesAsync();
            SeedCebuYardAsync(db, userManager, cebuOwner, today);
            await SeedPlatformSettingsAsync(db);
            await db.SaveChangesAsync();

            // Running daily totals from live transactions (uses the same math as the app).
            var txnDates = db.Transactions.IgnoreQueryFilters().Where(t => t.YardId == smash.Id)
                .Select(t => t.TransactionDate).Distinct().ToList();
            await RecalcTotalsAsync(db, smash.Id, txnDates);
        }

        /// <summary>
        /// Production bootstrap: creates the platform SuperAdmin from environment variables.
        /// Fails fast when the credentials are missing so a bare deployment can never be left
        /// without an administrator (and never falls back to a hard-coded default password).
        /// </summary>
        private static async Task SeedBootstrapAdminAsync(UserManager<ApplicationUser> userManager, IConfiguration config)
        {
            var email = config["SUPER_ADMIN_EMAIL"];
            var password = config["SUPER_ADMIN_PASSWORD"];

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException(
                    "Production requires SUPER_ADMIN_EMAIL and SUPER_ADMIN_PASSWORD environment variables. "
                    + "Set them on the host, or set SEED_DEMO_DATA=true to seed demo data instead.");

            await EnsureUserAsync(userManager, email, password, "Platform", "Admin", AppRoles.SuperAdmin);
        }

        private static async Task RecalcTotalsAsync(ApplicationDbContext db, Guid yardId, IEnumerable<DateOnly> dates)
        {
            foreach (var d in dates.Distinct())
            {
                var txs = await db.Transactions.IgnoreQueryFilters().Where(t => t.YardId == yardId && t.TransactionDate == d && t.Status == PaymentStatus.Paid).ToListAsync();
                var row = await db.DailyRunningTotals.FirstOrDefaultAsync(x => x.YardId == yardId && x.Date == d);
                if (row is null)
                    db.DailyRunningTotals.Add(new DailyRunningTotal
                    {
                        Id = Guid.NewGuid(), YardId = yardId, Date = d,
                        NetTotal = txs.Sum(t => t.Amount),
                        PaidTotal = txs.Where(t => t.Amount > 0).Sum(t => t.Amount),
                        RefundTotal = txs.Where(t => t.Amount < 0).Sum(t => -t.Amount),
                        UpdatedAtUtc = DateTime.UtcNow
                    });
            }
            await db.SaveChangesAsync();
        }

        private static async Task<ApplicationUser> EnsureUserAsync(UserManager<ApplicationUser> userManager, string email, string password, string firstName, string lastName, string role)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FirstName = firstName, LastName = lastName, CreatedAtUtc = DateTime.UtcNow };
                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded) throw new InvalidOperationException($"Failed to seed user {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
            if (!await userManager.IsInRoleAsync(user, role))
                await userManager.AddToRoleAsync(user, role);
            return user;
        }

        private static Court Court(Guid yardId, string name, string number, CourtType type, decimal rate, string description)
        {
            return new Court
            {
                Id = Guid.NewGuid(), YardId = yardId, Name = name, Number = number, Type = type,
                HourlyRate = rate, Status = CourtStatus.Available, Description = description,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private static Customer Customer(Guid yardId, string name, string email, string phone)
        {
            return new Customer { Id = Guid.NewGuid(), YardId = yardId, Name = name, Email = email, Phone = phone, CreatedAtUtc = DateTime.UtcNow };
        }

        private static List<Booking> SeedSmashBookings(Yard yard, ApplicationDbContext db, Court c1, Court c2, Court c3,
            Customer michelle, Customer jose, Customer ana, DateOnly today)
        {
            var list = new List<Booking>();
            var counter = 2000;

            void Add(Court court, Customer customer, DateOnly d, TimeSpan start, int duration, BookingStatus status, PaymentStatus payStatus)
            {
                var end = start + TimeSpan.FromMinutes(duration);
                var subtotal = Math.Round(court.HourlyRate * ((decimal)duration / 60m), 2);
                var fees = Math.Round(subtotal * 0.05m, 2);
                var total = Math.Round(subtotal + fees, 2);
                counter++;
                var booking = new Booking
                {
                    Id = Guid.NewGuid(), YardId = yard.Id, CourtId = court.Id, CustomerId = customer.Id,
                    BookingRef = $"SMA-{counter:D6}",
                    BookingDate = d, StartTime = start, EndTime = end, DurationMinutes = duration,
                    Rate = court.HourlyRate, Currency = "PHP",
                    Subtotal = subtotal, DiscountAmount = 0, FeesAmount = fees, TotalAmount = total,
                    PaymentStatus = payStatus, Status = status,
                    CustomerName = customer.Name, CustomerEmail = customer.Email, CustomerPhone = customer.Phone,
                    ConcurrencyToken = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                };
                list.Add(booking);

                if (payStatus == PaymentStatus.Paid || status == BookingStatus.Completed)
                {
                    db.Transactions.Add(new Transaction
                    {
                        Id = Guid.NewGuid(), YardId = yard.Id, BookingId = booking.Id,
                        Type = TransactionType.BookingPayment, Amount = total,
                        Description = $"Payment for booking {booking.BookingRef}",
                        Method = PaymentMethod.Cash, Status = PaymentStatus.Paid,
                        TransactionDate = d, CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            Add(c1, michelle, today.AddDays(-5), new TimeSpan(9,0,0), 60, BookingStatus.Completed, PaymentStatus.Paid);
            Add(c2, jose, today.AddDays(-4), new TimeSpan(14,0,0), 90, BookingStatus.Completed, PaymentStatus.Paid);
            Add(c3, ana, today.AddDays(-3), new TimeSpan(10,0,0), 60, BookingStatus.Completed, PaymentStatus.Paid);
            Add(c1, michelle, today.AddDays(-2), new TimeSpan(16,0,0), 60, BookingStatus.Completed, PaymentStatus.Paid);
            Add(c2, jose, today.AddDays(-1), new TimeSpan(18,0,0), 60, BookingStatus.Completed, PaymentStatus.Paid);

            Add(c1, michelle, today, new TimeSpan(10,0,0), 60, BookingStatus.Confirmed, PaymentStatus.Paid);
            Add(c3, ana, today, new TimeSpan(16,0,0), 60, BookingStatus.Pending, PaymentStatus.Pending);
            Add(c2, jose, today.AddDays(1), new TimeSpan(18,0,0), 60, BookingStatus.Pending, PaymentStatus.Pending);
            Add(c2, michelle, today.AddDays(2), new TimeSpan(8,0,0), 90, BookingStatus.Pending, PaymentStatus.Pending);

            return list;
        }

        private static async Task SeedPlatformSettingsAsync(ApplicationDbContext db)
        {
            if (!await db.PlatformSettings.AnyAsync(s => s.Key == "PlatformName"))
                db.PlatformSettings.Add(new PlatformSetting { Key = "PlatformName", Value = "Picklebook", Description = "Platform display name" });
            if (!await db.PlatformSettings.AnyAsync(s => s.Key == "DefaultCurrency"))
                db.PlatformSettings.Add(new PlatformSetting { Key = "DefaultCurrency", Value = "PHP", Description = "Default currency" });
            if (!await db.PlatformSettings.AnyAsync(s => s.Key == "DefaultTimeZone"))
                db.PlatformSettings.Add(new PlatformSetting { Key = "DefaultTimeZone", Value = "Asia/Manila", Description = "Default time zone" });
            if (!await db.PlatformSettings.AnyAsync(s => s.Key == "PlatformFeePercent"))
                db.PlatformSettings.Add(new PlatformSetting { Key = "PlatformFeePercent", Value = "2", Description = "Platform fee percentage" });
        }

        private static void SeedCebuYardAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ApplicationUser owner, DateOnly today)
        {
            var yard = new Yard
            {
                Id = Guid.NewGuid(), Slug = "cebu-pickleball", Name = "Cebu Pickleball",
                Tagline = "Pickleball by the bay", City = "Lapu-Lapu", Province = "Cebu", Country = "Philippines",
                Phone = "+63 917 555 9887", Email = "hello@cebupickle.com",
                Currency = "PHP", TimeZoneId = "Asia/Manila", Status = YardStatus.Active,
                OwnerId = owner.Id, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                Settings = new YardSettings { Id = Guid.NewGuid(), DefaultDurationMinutes = 60, AdvanceBookingDays = 14 }
            };
            db.Yards.Add(yard);
            for (int d = 0; d <= 6; d++)
                db.OperatingHours.Add(new OperatingHour { Id = Guid.NewGuid(), YardId = yard.Id, DayOfWeek = d, OpenTime = new TimeSpan(7, 0, 0), CloseTime = new TimeSpan(20, 0, 0) });
            db.Courts.Add(Court(yard.Id, "Court A", "A", CourtType.Outdoor, 350, "Open-air court with sea breeze."));
        }
    }
}
