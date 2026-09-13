namespace Picklebook.Services;

/// <summary>
/// Resolves the current tenant from the request host. Values are read from the
/// HttpContext during (pre)rendering and cached for the interactive circuit.
/// </summary>
public class TenantResolver(IHttpContextAccessor accessor)
{
    private string? _slug;
    private bool _slugSet;
    private bool _isAdminSet;
    private bool _isAdmin;

    public string? SubdomainSlug
    {
        get
        {
            if (!_slugSet)
            {
                _slug = accessor.HttpContext?.Items["pb.subdomainSlug"] as string;
                _slugSet = true;
            }
            return _slug;
        }
    }

    public bool IsAdminSubdomain
    {
        get
        {
            if (!_isAdminSet)
            {
                _isAdmin = accessor.HttpContext?.Items["pb.isAdminSubdomain"] is true;
                _isAdminSet = true;
            }
            return _isAdmin;
        }
    }

    /// <summary>
    /// Link helper for public yard URLs. When the current host IS the yard's subdomain
    /// we emit bare relative paths ({slug}.picklebook.com/courts); otherwise we fall back
    /// to the canonical /yard/{slug}/... form (also used by the dev server).
    /// </summary>
    public string Public(string slug, string path)
    {
        if (!string.IsNullOrEmpty(SubdomainSlug) && string.Equals(SubdomainSlug, slug, StringComparison.OrdinalIgnoreCase))
            return path;
        return path == "/" ? $"/yard/{slug}" : $"/yard/{slug}{path}";
    }
}