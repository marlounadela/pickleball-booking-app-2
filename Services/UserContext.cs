using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Picklebook.Services;

/// <summary>Abstraction over the current authenticated principal so services never trust client-supplied tenant ids.</summary>
public interface IUserContext
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
    Task<bool> IsInRoleAsync(string role);
}

/// <summary>Server-side Blazor implementation backed by the authentication state provider.</summary>
public class ServerUserContext(AuthenticationStateProvider authenticationStateProvider) : IUserContext
{
    private ClaimsPrincipal? _principal;

    public string? UserId => GetPrincipal().FindFirstValue(ClaimTypes.NameIdentifier);

    public bool IsAuthenticated => GetPrincipal().Identity?.IsAuthenticated == true
        && !string.IsNullOrEmpty(UserId);

    public async Task<bool> IsInRoleAsync(string role)
    {
        var p = await GetPrincipalAsync();
        return p.IsInRole(role);
    }

    private ClaimsPrincipal GetPrincipal()
    {
        // Never cache an anonymous principal: the auth state may resolve a moment later
        // (or change after sign-in/out). Only an authenticated principal is sticky.
        try
        {
            var task = authenticationStateProvider.GetAuthenticationStateAsync();
            if (task.IsCompletedSuccessfully && task.Result.User.Identity?.IsAuthenticated == true)
            {
                _principal = task.Result.User;
                return _principal;
            }
        }
        catch { /* fall through to cached/empty principal */ }
        if (_principal is not null) return _principal;
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    private async Task<ClaimsPrincipal> GetPrincipalAsync()
    {
        if (_principal is null)
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            _principal = state.User;
        }
        return _principal;
    }
}

/// <summary>For smoke tests / tooling.</summary>
public class StaticUserContext(string userId) : IUserContext
{
    public string? UserId { get; } = userId;
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
    public Task<bool> IsInRoleAsync(string role) => Task.FromResult(role == AppRoles.SuperAdmin);
}