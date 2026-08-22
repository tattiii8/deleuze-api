using System.Collections.Generic;

namespace DeleuzeMng.Controllers
{
    public record TenantCreationRequest(string TenantId, List<string>? EnabledServices = null);
    public record EnableServiceRequest(string ServiceKey);
    public record UserRegistrationRequest(string LoginId, string Password, string TenantId);
}