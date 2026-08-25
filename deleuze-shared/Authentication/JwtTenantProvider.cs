// deleuze-shared/Authentication/JwtTenantProvider.cs

using System;
using Deleuze.Shared.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace Deleuze.Shared.Authentication
{
    public class JwtTenantProvider : ITenantProvider
    {
        public const string TenantClaimType = "tenantId";

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

            var tenantId = user.FindFirst(TenantClaimType)?.Value;

            if (string.IsNullOrEmpty(tenantId))
            {
                throw new InvalidOperationException("Tenant ID claim not found in user principal.");
            }

            return tenantId;
        }
    }
}