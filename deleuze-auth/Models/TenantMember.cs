using System;

namespace DeleuzeAuth.Models;

public class TenantMember
{
    public Guid LoginId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}