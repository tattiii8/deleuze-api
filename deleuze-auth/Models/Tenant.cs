namespace DeleuzeAuth.Models;

public class Tenant
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; // 👈 追加
    public string? ApiKey { get; set; }
    public AuthMode AuthMode { get; set; } = AuthMode.JwtOnly;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}