public class CreateTenantRequest
{
    public string TenantId { get; set; } = string.Empty;

    public string TenantName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
}