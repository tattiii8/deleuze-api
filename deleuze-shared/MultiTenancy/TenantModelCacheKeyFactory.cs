using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Deleuze.Shared.MultiTenancy
{
    public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            var tenantProvider = context.GetService<ITenantProvider>();
            var tenantId = tenantProvider?.GetTenantId() ?? "default";

            // DbContextの型 + テナントID を組み合わせたキャッシュキーを返却
            return (context.GetType(), tenantId, designTime);
        }
    }
}