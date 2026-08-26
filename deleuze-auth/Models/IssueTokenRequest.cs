namespace DeleuzeAuth.Models
{
    public class IssueTokenRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string LoginId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}