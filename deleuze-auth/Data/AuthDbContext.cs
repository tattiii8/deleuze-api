using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Models;
using Deleuze.Shared.Infrastructure;

namespace DeleuzeAuth.Data;

public class TenantAuthDbContext : DbContext
{
    private readonly string _tenantId;

    public TenantAuthDbContext(
        DbContextOptions<TenantAuthDbContext> options,
        string tenantId)
        : base(options)
    {
        _tenantId = tenantId;
    }

    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var schema =
            TenantSchemaNaming.GetSchemaName(
                "auth",
                _tenantId);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable(
                "Users",
                schema);

            entity.HasKey(u => u.Id);

            entity.Property(u => u.LoginId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(u => u.PasswordHash)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(u => u.TenantId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(u => u.CreatedAt)
                .HasDefaultValueSql(
                    "CURRENT_TIMESTAMP");

            entity.HasIndex(u => u.LoginId)
                .IsUnique()
                .HasDatabaseName(
                    "IX_Users_LoginId");

            entity.HasIndex(u => u.TenantId)
                .HasDatabaseName(
                    "IX_Users_TenantId");
        });
    }
}