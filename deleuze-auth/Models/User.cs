using System;

namespace DeleuzeAuth.Models;

public class User
{
    public int Id { get; set; }

    public string LoginId { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ナビゲーションプロパティ（任意）
    public Tenant? Tenant { get; set; }
}