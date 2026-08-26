namespace DeleuzeAuth.Models
{
    public class RegisterAuthUserRequest
    {
        public string SubjectId { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;

        public string LoginId { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}