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

                // 主キー
                entity.HasKey(e => e.SubjectId);

                // テナント内で login_id を一意にする
                //
                // flaubert / admin → OK
                // germinal / admin  → OK
                // flaubert / admin → NG
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

                // 主キー
                entity.HasKey(e => e.TenantId);
            });
        }
    }
}