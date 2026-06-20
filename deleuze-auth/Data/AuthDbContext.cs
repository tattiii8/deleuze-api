using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // 認証用テーブルはマルチテナントの隔離スキーマではなく、共通の public スキーマに配置
        modelBuilder.HasDefaultSchema("public");
    }
}