using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DeleuzeDrive.Services;

namespace DeleuzeDrive.Data
{
    public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            if (context is DriveDbContext driveContext)
            {
                var tenantProvider = driveContext.GetService<ITenantProvider>();
                return (context.GetType(), tenantProvider.GetTenantId(), designTime);
            }

            return context.GetType();
        }
    }
}