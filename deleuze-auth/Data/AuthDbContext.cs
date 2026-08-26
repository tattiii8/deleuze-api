// DeleuzeAuth/Data/AuthDbContext.cs

using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Data
{
    public class AuthDbContext : DbContext
    {
        public AuthDbContext(
            DbContextOptions<AuthDbContext> options)
            : base(options)
        {
        }

        public DbSet<AuthUser> Users { get; set; } = null!;

        public DbSet<AuthTenant> Tenants { get; set; } = null!;

        public DbSet<ApiKey> ApiKeys { get; set; } = null!;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================
            // auth.users
            // =========================
            modelBuilder.Entity<AuthUser>(entity =>
            {
                entity.ToTable("users", "auth");

                entity.HasKey(e => e.SubjectId);

                // テナント内で login_id を一意にする
                //
                // flaubert / admin → OK
                // germinal / admin  → OK
                // flaubert / admin  → NG
                entity.HasIndex(e => new
                {
                    e.TenantId,
                    e.LoginId
                })
                .IsUnique();
            });

            // =========================
            // auth.tenants
            // =========================
            modelBuilder.Entity<AuthTenant>(entity =>
            {
                entity.ToTable("tenants", "auth");

                entity.HasKey(e => e.TenantId);
            });

            // =========================
            // auth.apikeys
            // =========================
            modelBuilder.Entity<ApiKey>(entity =>
            {
                entity.ToTable("apikeys", "auth");

                // 主キー
                entity.HasKey(e => e.Id);

                // カラム
                entity.Property(e => e.Id)
                    .HasColumnName("id");

                entity.Property(e => e.SubjectId)
                    .HasColumnName("subject_id")
                    .IsRequired();

                entity.Property(e => e.TenantId)
                    .HasColumnName("tenant_id")
                    .IsRequired();

                entity.Property(e => e.KeyHash)
                    .HasColumnName("key_hash")
                    .IsRequired();

                entity.Property(e => e.Name)
                    .HasColumnName("name")
                    .IsRequired();

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .IsRequired();

                entity.Property(e => e.ExpiresAt)
                    .HasColumnName("expires_at");

                entity.Property(e => e.RevokedAt)
                    .HasColumnName("revoked_at");

                // API Key自体は一意
                entity.HasIndex(e => e.KeyHash)
                    .IsUnique();

                // ユーザーのAPI Key検索用
                entity.HasIndex(e => e.SubjectId);

                // テナントのAPI Key検索用
                entity.HasIndex(e => e.TenantId);
            });
        }
    }
}