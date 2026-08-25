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

    public DbSet<TenantMember> Members { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var schema = TenantSchemaNaming.GetSchemaName("auth", _tenantId);

        modelBuilder.Entity<TenantMember>(entity =>
        {
            entity.ToTable("Members", schema);

            // 主キー: グローバルで一意な loginId (Guid)
            entity.HasKey(m => m.LoginId);

            entity.Property(m => m.LoginId)
                .IsRequired();

            entity.Property(m => m.TenantId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(m => m.Role)
                .HasMaxLength(50)
                .HasDefaultValue("Member")
                .IsRequired();

            entity.Property(m => m.JoinedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(m => m.TenantId)
                .HasDatabaseName($"IX_{schema}_Members_TenantId");
        });
    }
}