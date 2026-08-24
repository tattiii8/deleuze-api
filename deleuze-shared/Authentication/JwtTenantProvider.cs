using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Deleuze.Shared.MultiTenancy;

namespace Deleuze.Shared.Authentication
{
    public class JwtTenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public JwtTenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetTenantId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
            {
                return string.Empty;
            }

            var tenantId = user.FindFirst("tenant_id")?.Value
                ?? user.FindFirst("tenant")?.Value
                ?? user.FindFirst("tenantId")?.Value
                ?? user.FindFirst("TenantId")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return tenantId ?? string.Empty;
        }
    }
}