using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using DeleuzeMng.Data;
using Deleuze.Shared.Constants; // 共通定数を参照

namespace DeleuzeMng.Controllers
{
    [ApiController]
    [Route(ApiRoutes.Management.System + "/initialize")] // -> "api/system/initialize" (または "api/mng/system/initialize")
    public class SystemController : ControllerBase
    {
        private readonly string _authConnString;

        public SystemController(IConfiguration configuration)
        {
            _authConnString = configuration.GetConnectionString("AuthConnection")
                ?? throw new InvalidOperationException("接続文字列 'AuthConnection' が設定されていません。");
        }

        [HttpPost]
        public async Task<IActionResult> InitializeSystem()
        {
            try
            {
                // 共通テーブル（Tenants, Users）を作成（作成済みなら IF NOT EXISTS で自動スキップ）
                await DbInitializer.EnsureSeedDataAsync(_authConnString);
                return Ok(new { message = "共通基盤の初期化処理が完了しました。" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"初期化中にエラーが発生しました: {ex.Message}" });
            }
        }
    }
}