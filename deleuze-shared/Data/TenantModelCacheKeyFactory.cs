using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Deleuze.Shared.Services;

namespace Deleuze.Shared.Data;

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        // EF Core の ServiceProvider から ITenantProvider を取得
        var tenantProvider = context.GetService<ITenantProvider>();
        var tenantId = tenantProvider?.GetTenantId();

        // (DbContextの型, テナントID, デザインタイムフラグ) を複合キーとして返す
        return (context.GetType(), tenantId ?? "default", designTime);
    }
}