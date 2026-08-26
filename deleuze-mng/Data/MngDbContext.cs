using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Dapper;
using DeleuzeMng.Models;

namespace DeleuzeMng.Data
{
    public class MngDbContext : DbContext
    {
        public MngDbContext(DbContextOptions<MngDbContext> options) : base(options)
        {
        }

        // mng.users テーブルに対応する DbSet
        public DbSet<MngUser> Users { get; set; } = null!;

        // 必要に応じて他の管理用エンティティ（Tenants等）を追加
        // public DbSet<Tenant> Tenants { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // スキーマ指定やインデックス等の詳細設定
            modelBuilder.Entity<MngUser>(entity =>
            {
                // テーブル名とスキーマの設定 (Attribute指定がない場合の明示設定)
                entity.ToTable("users", schema: "mng");

                // 主キーの設定
                entity.HasKey(e => e.SubjectId);

                // login_id のユニーク制約（同名ログインIDの重複防止）
                entity.HasIndex(e => e.LoginId)
                      .IsUnique();

                // email のユニーク制約
                entity.HasIndex(e => e.Email)
                      .IsUnique();
            });
        }
    }
}