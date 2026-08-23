using Microsoft.EntityFrameworkCore;
using Deleuze.Shared.Services;

namespace Deleuze.Shared.Data;

public abstract class TenantDbContextBase : DbContext
{
    private readonly ITenantProvider _tenantProvider;
    private readonly string _servicePrefix;

    protected TenantDbContextBase(
        DbContextOptions options, 
        ITenantProvider tenantProvider, 
        string servicePrefix) 
        : base(options)
    {
        _tenantProvider = tenantProvider;
        _servicePrefix = servicePrefix;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // テナントIDを取得し、{servicePrefix}_{tenantId} の形式で動的スキーマを設定
        var tenantId = _tenantProvider.GetTenantId();
        if (!string.IsNullOrEmpty(tenantId))
        {
            var schemaName = $"{_servicePrefix}_{tenantId}";
            modelBuilder.HasDefaultSchema(schemaName);
        }
    }
}