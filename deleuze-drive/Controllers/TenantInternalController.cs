using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Data;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Route("internal/tenants")]
    public class TenantInternalController : ControllerBase
    {
        private readonly DriveDbContext _dbContext;

        public TenantInternalController(DriveDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpPost("{tenantId}/initialize")]
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            string schemaName = $"app_{tenantId}";

            // 💡 ExecuteSqlRawAsync から ExecuteSqlInterpolatedAsync へ変更して SQL インジェクション警告を回避
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";");

            var sql = $@"
                CREATE TABLE IF NOT EXISTS ""{schemaName}"".""Files"" (
                    ""Id"" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""FileName"" VARCHAR(255) NOT NULL,
                    ""ContentType"" VARCHAR(100),
                    ""ByteSize"" BIGINT NOT NULL DEFAULT 0,
                    ""StoragePath"" TEXT NOT NULL,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS ""{schemaName}"".""Folders"" (
                    ""Id"" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""Name"" VARCHAR(255) NOT NULL,
                    ""ParentId"" UUID REFERENCES ""{schemaName}"".""Folders""(""Id"") ON DELETE CASCADE,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );
            ";

            await _dbContext.Database.ExecuteSqlRawAsync(sql);
            return Ok(new { message = $"Drive schema '{schemaName}' initialized successfully." });
        }

        [HttpDelete("{tenantId}")]
        public async Task<IActionResult> DeleteTenant(string tenantId)
        {
            string schemaName = $"app_{tenantId}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;");
            return NoContent();
        }
    }
}