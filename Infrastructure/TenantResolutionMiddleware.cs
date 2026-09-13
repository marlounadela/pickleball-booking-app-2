using Picklebook.Services;

namespace Picklebook.Infrastructure;

/// <summary>
/// Multi-tenant URL architecture:
///   app.picklebook.com        → owner admin app   (root path rewritten to /app/dashboard)
///   {yard-slug}.picklebook.com → that yard's public booking website
///   picklebook.com              → platform (landing/auth)
/// Internal nav uses dual-route pages, so only the root path needs rewriting.
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next, IConfiguration config)
{
    private readonly string? _root = config["AppSettings:RootDomain"];

    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host?.ToLowerInvariant() ?? string.Empty;
        var root = string.IsNullOrWhiteSpace(_root) ? AppConstants.RootDomain : _root;

        // normalize localhost subdomains (smash-yard.localhost works in dev)
        var rootDomain = host.EndsWith(".localhost") ? "localhost" : root;

        string? slug = null;
        var isAdmin = false;

        if (host == $"app.{rootDomain}")
            isAdmin = true;
        else if (host.EndsWith($".{rootDomain}"))
        {
            var candidate = host[..^(rootDomain.Length + 1)];
            if (candidate is not ("www" or "app" or "api" or "mail" or "admin"))
                slug = candidate;
        }

        var path = context.Request.Path.Value ?? "/";
        if (isAdmin && path == "/")
            context.Request.Path = "/app/dashboard";
        else if (slug is not null && path == "/")
            context.Request.Path = $"/yard/{slug}";

        context.Items["pb.subdomainSlug"] = slug;
        context.Items["pb.isAdminSubdomain"] = isAdmin;
        context.Items["pb.rootDomain"] = rootDomain;

        await next(context);
    }
}