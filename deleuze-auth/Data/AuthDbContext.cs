using Microsoft.EntityFrameworkCore;
// ★ deleuze-shared の共通 Tenant モデルを使用
using Deleuze.Shared.Models; 
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // 認証用テーブルは共通の public スキーマに固定
        modelBuilder.HasDefaultSchema("public");

        // Tenants テーブルの設定（deleuze-shared の Tenant クラスに対するマッピング）
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ApiKey).HasMaxLength(255);
            
            // ApiKey インデックス
            entity.HasIndex(e => e.ApiKey).IsUnique();
        });
    }
}