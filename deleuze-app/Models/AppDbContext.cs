using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MultiTenantApp.Services;

namespace MultiTenantApp.Models;

public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;
    
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider) 
        : base(options) => _tenantProvider = tenantProvider;
        
    public DbSet<Product> Products => Set<Product>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // テナントIDをスキーマ名として動的に指定
        modelBuilder.HasDefaultSchema(_tenantProvider.GetTenantId()); 
    }
}

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => 
        context is AppDbContext db 
            ? (context.GetType(), db.GetService<ITenantProvider>().GetTenantId(), designTime) 
            : context.GetType();
}