using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Picklebook.Components;
using Picklebook.Components.Account;
using Picklebook.Data;
using Picklebook.Infrastructure;
using Picklebook.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// SQLite connection string. DATABASE_URL (an env var) wins so production deployments can point at a
// persistent, writable path (e.g. a volume mount); otherwise the default in appsettings.json is used.
var connectionString = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false; // mock email sender in development
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// Trust X-Forwarded-* headers (proto/host/IP) so HTTPS redirection, HSTS and Secure cookies
// behave correctly when the app runs behind a TLS-terminating reverse proxy (the standard PaaS
// topology). Configuration is inert unless the ForwardedHeaderFilter middleware is installed.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.All;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---------- Application services ----------
builder.Services.AddScoped<IUserContext, ServerUserContext>();
builder.Services.AddScoped<TenantResolver>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<YardService>();
builder.Services.AddScoped<CourtService>();
builder.Services.AddScoped<ScheduleService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<RevenueService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<PlatformService>();
builder.Services.AddScoped<YardAccessService>();

// Email abstraction — real SMTP when `EmailSettings` are configured, mock/log fallback otherwise.
builder.Services.AddSingleton<INotificationSender, SmtpNotificationSender>();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddScoped<ToastService>();

var app = builder.Build();

// ---------- apply migrations + seed ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=10000;");
    await SeedData.SeedAsync(app.Services, app.Environment.IsProduction(), builder.Configuration);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Install the forwarded-header trust filter on every TLS-terminating production edge.
if (!app.Environment.IsDevelopment()
    || string.Equals(builder.Configuration["TRUST_PROXY_HEADERS"], "true", StringComparison.OrdinalIgnoreCase))
{
    app.UseForwardedHeaders();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
