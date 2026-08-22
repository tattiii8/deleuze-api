using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Tenant> Tenants => Set<Tenant>(); // 👈 追加

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // 認証用テーブルはマルチテナントの隔離スキーマではなく、共通の public スキーマに配置
        modelBuilder.HasDefaultSchema("public");

        // Tenants テーブルの設定
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ApiKey).HasMaxLength(255);
            
            // ApiKey による検索の高速化と重複防止
            entity.HasIndex(e => e.ApiKey).IsUnique();
        });
    }
}