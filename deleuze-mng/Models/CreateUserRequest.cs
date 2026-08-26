namespace DeleuzeMng.Models
{
    public class CreateUserRequest
    {
        public string LoginId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}