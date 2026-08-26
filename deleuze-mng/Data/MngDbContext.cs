using Microsoft.EntityFrameworkCore;
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

        public DbSet<MngUser> Users { get; set; } = null!;

        public DbSet<Tenant> Tenants { get; set; } = null!;

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================
            // mng.users
            // =========================
            modelBuilder.Entity<MngUser>(entity =>
            {
                entity.ToTable("users", "mng");

                entity.HasKey(e => e.SubjectId);

                entity.HasIndex(e => new
                {
                    e.TenantId,
                    e.LoginId
                })
                .IsUnique();

                entity.HasIndex(e => e.Email)
                    .IsUnique();
            });

            // =========================
            // mng.tenants
            // =========================
            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.ToTable("tenants", "mng");

                entity.HasKey(e => e.TenantId);

                entity.HasIndex(e => e.TenantName)
                    .IsUnique();
            });
        }
    }
}