using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Models;
using Deleuze.Shared.Data;
using Deleuze.Shared.Services;

namespace DeleuzeDrive.Data;

public class DriveDbContext : TenantDbContextBase
{
    public DriveDbContext(DbContextOptions<DriveDbContext> options, ITenantProvider tenantProvider) 
        // ★ 第3引数にサービスキー "drive" を渡す
        : base(options, tenantProvider, "drive") 
    {
    }

    public DbSet<FileMetadata> Files { get; set; } = null!;
    public DbSet<Folder> Folders { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ★ 基底クラスの OnModelCreating を呼ぶことで drive_{tenantId} スキーマが自動設定される
        base.OnModelCreating(modelBuilder);

        // テーブルマッピングの個別設定のみ記述
        modelBuilder.Entity<FileMetadata>().ToTable("Files");
        modelBuilder.Entity<Folder>().ToTable("Folders");
    }
}