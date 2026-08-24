using System.Security.Claims;
using Deleuze.Shared.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace Deleuze.Shared.Authentication;

public class JwtTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetTenantId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        return user?.FindFirstValue("tenant_id") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}