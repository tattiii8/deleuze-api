using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;

namespace Deleuze.Shared.Services;

public class JwtTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetTenantId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return null;

        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(token)) return null;

        var jwtToken = handler.ReadJwtToken(token);
        return jwtToken.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
    }
}