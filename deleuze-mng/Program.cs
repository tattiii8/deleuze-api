using System.Security.Cryptography;
using System.Text;
using DeleuzeMng.Services;
using DeleuzeMng.Data;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 1. データベース接続文字列の取得
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string not found.");

// 2. 💡 Nomadの環境変数等から「シークレットキー」を取得
// (トークンそのものではなく、署名検証用の共通鍵になります)
var apiSecret = builder.Configuration["MANAGEMENT_API_SECRET"];

if (string.IsNullOrEmpty(apiSecret))
{
    throw new InvalidOperationException("環境変数 'MANAGEMENT_API_SECRET' が設定されていません。");
}

// 管理用サービスの登録
builder.Services.AddScoped<TenantManagementService>(_ => new TenantManagementService(connectionString));

var app = builder.Build();

// データベースの初期化
await DbInitializer.EnsureSeedDataAsync(connectionString);

// =========================================================================
// 🔒 動的トークン（シークレット+ソルト+タイムスタンプ）のチェックミドルウェア
// =========================================================================
app.Use(async (context, next) =>
{
    // マネジメントAPIのルート（/api/mng/）のみ認証を必須にする
    if (context.Request.Path.StartsWithSegments("/api/mng"))
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var extractedToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authorization ヘッダーがありません。" });
            return;
        }

        // 💡 トークンの検証 (有効期限は5分間に設定)
        bool isValid = ValidateDynamicToken(extractedToken.ToString(), apiSecret, TimeSpan.FromMinutes(5));

        if (!isValid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "認証トークンが無効、または有効期限切れです。" });
            return;
        }
    }
    await next();
});

// 🛠️ 管理エンドポイント1: テナントの新規作成
app.MapPost("/api/mng/tenants", async (TenantCreationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.TenantId)) return Results.BadRequest("TenantId は必須です。");

    await mngService.CreateTenantAsync(req.TenantId.ToLower());
    return Results.Ok(new { message = $"テナント '{req.TenantId}' のスキーマ隔離環境を動的に構築しました。" });
});

// 🛠️ 管理エンドポイント2: ユーザーの新規登録
app.MapPost("/api/mng/users", async (UserRegistrationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.LoginId) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.TenantId))
    {
        return Results.BadRequest("すべての項目を入力してください。");
    }

    await mngService.RegisterUserAsync(req.LoginId, req.Password, req.TenantId.ToLower());
    return Results.Ok(new { message = $"ユーザー '{req.LoginId}' をテナント '{req.TenantId}' に登録しました（BCrypt暗号化済）。" });
});

app.Run();

// =========================================================================
// 🔑 トークン検証ロジック (ヘルパー関数)
// =========================================================================
static bool ValidateDynamicToken(string rawToken, string secretKey, TimeSpan validDuration)
{
    if (string.IsNullOrWhiteSpace(rawToken)) return false;

    // "Bearer " プレフィックスの除去
    if (rawToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        rawToken = rawToken[7..].Trim();
    }

    var parts = rawToken.Split(':');
    if (parts.Length != 2) return false;

    string payloadBase64 = parts[0];
    string signatureBase64 = parts[1];

    try
    {
        // 1. HMAC-SHA256 による署名の検証（改ざんチェック）
        var secretBytes = Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new HMACSHA256(secretBytes);
        var expectedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64));
        string expectedSignatureBase64 = Convert.ToBase64String(expectedSignatureBytes);

        if (signatureBase64 != expectedSignatureBase64) return false;

        // 2. ペイロード（タイムスタンプ | ソルト）のデコード
        var payloadBytes = Convert.FromBase64String(payloadBase64);
        string payload = Encoding.UTF8.GetString(payloadBytes);

        var payloadParts = payload.Split('|');
        if (payloadParts.Length != 2) return false;

        string timestampStr = payloadParts[0]; // ソルト(payloadParts[1])は一意性担保のため今回はデコードのみ

        // 3. タイムスタンプ（有効期限）の検証
        if (!long.TryParse(timestampStr, out long unixTimestamp)) return false;

        var tokenTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        var now = DateTimeOffset.UtcNow;

        // クロックズレを考慮し、未来5分〜過去の有効期限内であるかをチェック
        if (tokenTime > now.AddMinutes(5) || now - tokenTime > validDuration)
        {
            return false;
        }

        return true;
    }
    catch
    {
        return false;
    }
}

// DTO 定義
public record TenantCreationRequest(string TenantId);
public record UserRegistrationRequest(string LoginId, string Password, string TenantId);