using System.Collections.Generic;

namespace DeleuzeMng.Models
{
    public enum AuthMode
    {
        JwtOnly = 0,
        ApiKeyOnly = 1,
        Hybrid = 2
    }

    public class CreateTenantRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public List<string>? Services { get; set; }
    }

    public class EnableServiceRequest
    {
        public string ServiceKey { get; set; } = string.Empty;
    }

    // サービス無効化用のリクエストボディ型
    public class DisableServiceRequest
    {
        public string ServiceKey { get; set; } = string.Empty;
    }

    public class UpdateAuthModeRequest
    {
        public AuthMode AuthMode { get; set; }
    }

    // テナントのステータス変更用
    // active / suspended
    public class UpdateTenantStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public class TenantAuthDto
    {
        public string Id { get; set; } = string.Empty;

        public int AuthMode { get; set; }

        public string ApiKey { get; set; } = string.Empty;

        // テナントの運用ステータス
        // active / suspended
        public string Status { get; set; } = "active";
    }
}