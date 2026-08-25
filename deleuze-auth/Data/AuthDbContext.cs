using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Models;

namespace DeleuzeAuth.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(
        DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants { get; set; } = null!;

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(t => t.Id);

            // ApiKey で検索するので Unique Index を張っておくと良い
            entity.HasIndex(t => t.ApiKey).IsUnique();
        });
    }
}