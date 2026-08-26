namespace DeleuzeAuth.Models
{
    public class RegisterAuthUserRequest
    {
        public string SubjectId { get; set; } = string.Empty; // UUID
        public string LoginId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;   // ハッシュ化前パスワード
    }
}