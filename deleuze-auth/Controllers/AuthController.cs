// deleuze-auth/Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Deleuze.Shared.Constants;

namespace DeleuzeAuth.Controllers
{
    [ApiController]
    [Route(ApiRoutes.Auth.Base)] // -> "api/auth"
    public class AuthController : ControllerBase
    {
        // 独自認証API（例: セッション確認、ログアウト等）を追加する場合ここに定義
    }
}