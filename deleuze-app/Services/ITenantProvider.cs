using Microsoft.AspNetCore.Http;
using System;

namespace deleuze_app.Services;

public interface ITenantProvider 
{ 
    string GetTenantId(); 
}

public class HttpHeaderTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public HttpHeaderTenantProvider(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string GetTenantId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return "public";

        // ① リクエストヘッダーから取得
        string? headerTenant = context.Request.Headers.TryGetValue("X-Tenant-ID", out var tId) ? tId.ToString() : null;

        // ② JWTトークンから取得
        var tokenTenant = context.User.FindFirst("tenant_id")?.Value;

        // ③ 不正アクセスのブロック
        if (headerTenant != null && tokenTenant != null && headerTenant != tokenTenant)
        {
            throw new UnauthorizedAccessException("ヘッダーのテナントIDとJWT内のテナントIDが一致しません。");
        }

        return tokenTenant ?? headerTenant ?? "public";
    }
}