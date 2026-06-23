using System.Security.Cryptography;
using System.Text;
using DeleuzeMng.Services;
using DeleuzeMng.Data;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 1. データベース接続文字列の取得
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string not found.");

// 2. Nomadの環境変数等から「シークレットキー」を取得
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
// 🔒 動的トークンチェック ＆ ログ出力ミドルウェア
// =========================================================================
app.Use(async (context, next) =>
{
    // マネジメントAPIのルート（/api/mng/）のみ認証を必須にする
    if (context.Request.Path.StartsWithSegments("/api/mng"))
    {
        app.Logger.LogInformation("[MNG-AUTH] 管理APIへのアクセスを検知しました: {Path} ({Method})", context.Request.Path, context.Request.Method);

        if (!context.Request.Headers.TryGetValue("Authorization", out var extractedToken))
        {
            app.Logger.LogWarning("[MNG-AUTH-FAIL] Authorization ヘッダーがリクエストに含まれていません。");
            
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authorization ヘッダーがありません。" });
            return;
        }

        string rawToken = extractedToken.ToString();
        
        // セキュリティを考慮し、トークンの先頭部分のみをマスクしてログ出力
        string maskedToken = rawToken.Length > 25 ? $"{rawToken[..25]}..." : rawToken;
        app.Logger.LogInformation("[MNG-AUTH] トークンを受信しました: {Token}", maskedToken);

        // 💡 ログ出力を連動させるため、検証結果のステータスを受け取るように拡張
        var (isValid, reason) = ValidateDynamicTokenWithReason(rawToken, apiSecret, TimeSpan.FromMinutes(5));

        if (!isValid)
        {
            app.Logger.LogWarning("[MNG-AUTH-FAIL] トークン検証に失敗しました。理由: {Reason}", reason);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = $"認証トークンが無効、または有効期限切れです。({reason})" });
            return;
        }

        app.Logger.LogInformation("[MNG-AUTH-SUCCESS] トークン検証に成功しました。処理を継続します。");
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
// 🔑 トークン検証ロジック (エラー理由返却版)
// =========================================================================
static (bool IsValid, string Reason) ValidateDynamicTokenWithReason(string rawToken, string secretKey, TimeSpan validDuration)
{
    if (string.IsNullOrWhiteSpace(rawToken)) return (false, "トークンが空です。");

    if (rawToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        rawToken = rawToken[7..].Trim();
    }

    var parts = rawToken.Split(':');
    if (parts.Length != 2) return (false, "トークンのフォーマットが不正です（':' がありません）。");

    string payloadBase64 = parts[0];
    string signatureBase64 = parts[1];

    try
    {
        // 1. HMAC-SHA256 による署名の検証
        var secretBytes = Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new HMACSHA256(secretBytes);
        var expectedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64));
        string expectedSignatureBase64 = Convert.ToBase64String(expectedSignatureBytes);

        if (signatureBase64 != expectedSignatureBase64)
        {
            return (false, "署名が一致しません（改ざん、またはシークレットキーの不一致）。");
        }

        // 2. ペイロード（タイムスタンプ | ソルト）のデコード
        var payloadBytes = Convert.FromBase64String(payloadBase64);
        string payload = Encoding.UTF8.GetString(payloadBytes);

        var payloadParts = payload.Split('|');
        if (payloadParts.Length != 2) return (false, "ペイロードの構造が不正です。");

        string timestampStr = payloadParts[0];

        // 3. タイムスタンプ（有効期限）の検証
        if (!long.TryParse(timestampStr, out long unixTimestamp))
        {
            return (false, "タイムスタンプの解析に失敗しました。");
        }

        var tokenTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        var now = DateTimeOffset.UtcNow;

        // クロックのズレを許容（未来5分〜有効期限切れをチェック）
        if (tokenTime > now.AddMinutes(5))
        {
            return (false, $"トークンの時刻が未来すぎます (Token: {tokenTime}, Server: {now})。");
        }
        
        if (now - tokenTime > validDuration)
        {
            return (false, $"トークンの有効期限が切れています（5分以上経過）。");
        }

        return (true, "成功");
    }
    catch (Exception ex)
    {
        return (false, $"デコード中に例外が発生しました: {ex.Message}");
    }
}

// DTO 定義
public record TenantCreationRequest(string TenantId);
public record UserRegistrationRequest(string LoginId, string Password, string TenantId);