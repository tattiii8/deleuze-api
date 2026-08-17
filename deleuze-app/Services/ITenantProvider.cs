namespace DeleuzeApp.Services;

public interface ITenantProvider
{
    string GetTenantId();
}

public class TenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetTenantId()
    {
        // JWTのクレームから tenant_id を取得。無ければ public スキーマへのフォールバックを想定。
        var tenantId = _httpContextAccessor.HttpContext?.User?.FindFirst("tenant_id")?.Value;
        return string.IsNullOrEmpty(tenantId) ? "public" : tenantId;
    }
}