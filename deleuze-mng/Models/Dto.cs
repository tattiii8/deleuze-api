using System.Collections.Generic;

namespace DeleuzeMng.Controllers
{
    public record TenantCreationRequest(string TenantId, List<string>? EnabledServices = null);
    public record EnableServiceRequest(string ServiceKey);
    public record UserRegistrationRequest(string LoginId, string Password, string TenantId);
}

namespace DeleuzeMng.Models
{
    public enum AuthMode
    {
        JwtOnly = 0,
        ApiKeyOnly = 1,
        Both = 2
    }

    public class UpdateAuthModeRequest
    {
        public AuthMode AuthMode { get; set; }
    }

    public class ApiKeyResponse
    {
        public string ApiKey { get; set; } = string.Empty;
    }
}