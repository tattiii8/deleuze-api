namespace DeleuzeAuth.Models
{
    public class CreateApiKeyRequest
    {
        public string Name { get; set; } = string.Empty;

        public DateTimeOffset? ExpiresAt { get; set; }
    }
}