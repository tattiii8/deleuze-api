namespace DeleuzeMng.Models
{
    public class CreateTenantRequest
    {
        public string TenantName { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
    }
}