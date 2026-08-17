namespace DeleuzeAuth.Models;

public class User
{
    public int Id { get; set; }
    public string LoginId { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}