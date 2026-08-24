using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Models;
using Deleuze.Shared.Infrastructure;
using Deleuze.Shared.MultiTenancy;

namespace DeleuzeDrive.Data;

public class DriveDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public DriveDbContext(
        DbContextOptions<DriveDbContext> options,
        ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<FileMetadata> Files { get; set; }
    public DbSet<Folder> Folders { get; set; }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var tenantId =
            _tenantProvider.GetTenantId()
            ?? throw new InvalidOperationException(
                "Tenant ID is required.");

        var schemaName =
            TenantSchemaNaming.GetSchemaName(
                "drive",
                tenantId);

        modelBuilder.HasDefaultSchema(schemaName);

        modelBuilder.Entity<FileMetadata>()
            .ToTable("Files");

        modelBuilder.Entity<Folder>()
            .ToTable("Folders");
    }
}