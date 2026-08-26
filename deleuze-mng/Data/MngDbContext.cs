using Microsoft.EntityFrameworkCore;
using Npgsql;
using Dapper;
using DeleuzeMng.Models;

namespace DeleuzeMng.Data
{
    public class MngDbContext : DbContext
    {
        public MngDbContext(
            DbContextOptions<MngDbContext> options)
            : base(options)
        {
        }

        // mng.users テーブルに対応する DbSet
        public DbSet<MngUser> Users { get; set; } = null!;

        // 必要に応じて他の管理用エンティティ（Tenants等）を追加
        // public DbSet<Tenant> Tenants { get; set; } = null!;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MngUser>(entity =>
            {
                // テーブル
                entity.ToTable("users", schema: "mng");

                // 主キー
                entity.HasKey(e => e.SubjectId);

                // tenant_id + login_id の複合ユニーク制約
                //
                // 同じテナント内では login_id は一意
                // 別テナントなら同じ login_id を使用可能
                entity.HasIndex(e => new
                {
                    e.TenantId,
                    e.LoginId
                })
                .IsUnique();

                // email のユニーク制約
                entity.HasIndex(e => e.Email)
                      .IsUnique();
            });
        }
    }
}