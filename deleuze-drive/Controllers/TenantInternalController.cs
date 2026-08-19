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

        /// <summary>
        /// DeleuzeMng から呼び出されるテナント用スキーマ・初期テーブル作成 API
        /// </summary>
        [HttpPost("{tenantId}/initialize")]
        public async Task<IActionResult> InitializeTenant(string tenantId)
        {
            string schemaName = $"app_{tenantId}";

            await _dbContext.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";");

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

        /// <summary>
        /// DeleuzeMng から呼び出されるテナント削除（ロールバック）用 API
        /// </summary>
        [HttpDelete("{tenantId}")]
        public async Task<IActionResult> DeleteTenant(string tenantId)
        {
            string schemaName = $"app_{tenantId}";
            await _dbContext.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;");
            return NoContent();
        }
    }
}