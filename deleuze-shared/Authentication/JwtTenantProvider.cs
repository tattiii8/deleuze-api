using System;
using Deleuze.Shared.MultiTenancy;
using Microsoft.AspNetCore.Http;

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
                throw new InvalidOperationException("HttpContext or User is not available.");
            }

            var tenantId = user.FindFirst("tenant_id")?.Value 
                        ?? user.FindFirst("tenant")?.Value 
                        ?? user.FindFirst("tenantId")?.Value 
                        ?? user.FindFirst("TenantId")?.Value;

            if (string.IsNullOrEmpty(tenantId))
            {
                throw new InvalidOperationException("Tenant ID claim not found in user principal.");
            }

            return tenantId;
        }
    }
}