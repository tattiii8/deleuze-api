namespace DeleuzeAuth.Models
{
    public class AdminCreateApiKeyRequest
    {
        public string TenantId { get; set; } = string.Empty;

        public string LoginId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
