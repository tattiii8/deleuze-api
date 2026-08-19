using Microsoft.AspNetCore.Http;

namespace DeleuzeDrive.Services
{
    public class HeaderTenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HeaderTenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string GetTenantId()
        {
            var tenantId = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-ID"].ToString();
            return string.IsNullOrEmpty(tenantId) ? "default" : tenantId;
        }
    }
}