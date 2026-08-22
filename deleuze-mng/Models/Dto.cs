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

    public class UpdateAuthModeRequest
    {
        public AuthMode AuthMode { get; set; }
    }
}