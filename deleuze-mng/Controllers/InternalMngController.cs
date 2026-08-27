using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Deleuze.Shared.Constants;
using DeleuzeMng.Services;

namespace DeleuzeMng.Controllers
{
    /// <summary>
    /// 内部管理 API
    /// </summary>
    [ApiController]
    [Route(ApiRoutes.Management.InternalBase)]
    public class InternalMngController : ControllerBase
    {
        private readonly IDbInitializerService _dbInitializer;

        public InternalMngController(
            IDbInitializerService dbInitializer)
        {
            _dbInitializer = dbInitializer;
        }

        /// <summary>
        /// DBの初期スキーマおよびテーブルを
        /// .sqlファイルから作成します。
        ///
        /// POST /api/mng/internal/init
        /// </summary>
        [HttpPost("init")]
        public async Task<IActionResult> InitializeDatabase()
        {
            try
            {
                await _dbInitializer.ExecuteInitSqlAsync();

                return Ok(new
                {
                    message = "mng スキーマおよび初期テーブルの作成が完了しました。",
                    timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "データベース初期化処理に失敗しました。",
                    detail = ex.Message
                });
            }
        }
    }
}
