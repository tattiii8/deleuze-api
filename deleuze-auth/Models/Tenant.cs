using System;

namespace DeleuzeAuth.Models;

public class Tenant
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public AuthMode AuthMode { get; set; } = AuthMode.JwtOnly;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}