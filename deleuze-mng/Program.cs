using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeleuzeMng.Services;
using DeleuzeMng.Data;

var builder = WebApplication.CreateBuilder(args);

// 💡 ログ設定を強制追加（コンテナ内でも Information ログを確実に出力させる）
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning); // 既定のシステムログは静かにする

// 1. 接続文字列の存在チェック（実際の取得は TenantManagementService / DbInitializer 側で行う）
var authConnectionString = builder.Configuration.GetConnectionString("AuthConnection")
    ?? throw new InvalidOperationException("接続文字列 'AuthConnection' が設定されていません。");

if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("AppConnection")))
{
    throw new InvalidOperationException("接続文字列 'AppConnection' が設定されていません。");
}

// 2. Nomad の環境変数等から「シークレットキー」を取得
var apiSecret = builder.Configuration["MANAGEMENT_API_SECRET"];

if (string.IsNullOrEmpty(apiSecret))
{
    throw new InvalidOperationException("環境変数 'MANAGEMENT_API_SECRET' が設定されていません。");
}

// 管理用サービスの登録（IConfiguration は DI コンテナが自動解決する）
builder.Services.AddScoped<TenantManagementService>();

var app = builder.Build();

// データベースの初期化（Users テーブルは認証DB側にあるため AuthConnection を使用）
await DbInitializer.EnsureSeedDataAsync(authConnectionString);

// テナントIDのAPI層での事前バリデーション用（サービス層でも二重にチェックされる）
var tenantIdPattern = new Regex(@"^[a-z][a-z0-9_]{2,62}$", RegexOptions.Compiled);

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

        var (isValid, reason) = ValidateDynamicTokenWithReason(rawToken, apiSecret, TimeSpan.FromMinutes(5));

        if (!isValid)
        {
            // 失敗理由はログにのみ出し、レスポンスには詳細を含めすぎない
            app.Logger.LogWarning("[MNG-AUTH-FAIL] トークン検証に失敗しました。理由: {Reason}", reason);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "認証トークンが無効、または有効期限切れです。" });
            return;
        }

        app.Logger.LogInformation("[MNG-AUTH-SUCCESS] トークン検証に成功しました。処理を継続します。");
    }
    await next();
});

// 🛠️ 管理エンドポイント1: テナントの新規作成（スキーマのみ）
app.MapPost("/api/mng/tenants", async (TenantCreationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.TenantId))
        return Results.BadRequest(new { error = "TenantId は必須です。" });

    string normalizedTenantId = req.TenantId.ToLower();

    if (!tenantIdPattern.IsMatch(normalizedTenantId))
        return Results.BadRequest(new { error = "TenantId は小文字英数字とアンダースコアのみ、3〜63文字で指定してください。" });

    try
    {
        await mngService.CreateTenantAsync(normalizedTenantId);
        return Results.Ok(new { message = $"テナント '{normalizedTenantId}' のスキーマ隔離環境を構築しました。" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "テナント作成処理でエラーが発生しました。TenantId={TenantId}", normalizedTenantId);
        return Results.Problem("テナント作成処理中にエラーが発生しました。", statusCode: StatusCodes.Status500InternalServerError);
    }
});

// 🛠️ 管理エンドポイント2: ユーザーの新規登録（テナント自動プロビジョニング機能付き）
app.MapPost("/api/mng/users", async (UserRegistrationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.LoginId) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.TenantId))
    {
        return Results.BadRequest(new { error = "すべての項目を入力してください。" });
    }

    string normalizedTenantId = req.TenantId.ToLower();

    if (!tenantIdPattern.IsMatch(normalizedTenantId))
        return Results.BadRequest(new { error = "TenantId は小文字英数字とアンダースコアのみ、3〜63文字で指定してください。" });

    try
    {
        // ① 先にテナント（DBスキーマ）を作成
        // ※ CreateTenantAsync 内で存在チェック済みのため、既に存在していても安全に実行される
        await mngService.CreateTenantAsync(normalizedTenantId);

        // ② ユーザーを登録
        await mngService.RegisterUserAsync(req.LoginId, req.Password, normalizedTenantId);

        return Results.Ok(new { message = $"テナント '{normalizedTenantId}' の構築およびユーザー '{req.LoginId}' の登録が完了しました。" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        // 例: LoginId 重複など、利用者に見せてよい業務エラー
        return Results.Conflict(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "ユーザー登録処理でエラーが発生しました。TenantId={TenantId}, LoginId={LoginId}", normalizedTenantId, req.LoginId);
        return Results.Problem("処理中にエラーが発生しました。時間をおいて再度お試しください。", statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();

// =========================================================================
// 🔑 トークン検証ロジック (ヘルパー関数)
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
        // 1. HMAC-SHA256 による署名の検証（定数時間比較でタイミング攻撃を防止）
        var secretBytes = Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new HMACSHA256(secretBytes);
        var expectedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64));

        byte[] providedSignatureBytes;
        try
        {
            providedSignatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return (false, "署名のBase64形式が不正です。");
        }

        bool signaturesMatch = providedSignatureBytes.Length == expectedSignatureBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedSignatureBytes, expectedSignatureBytes);

        if (!signaturesMatch)
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

        // クロックのズレを考慮（未来5分〜有効期限切れをチェック）
        if (tokenTime > now.AddMinutes(5))
        {
            return (false, "トークンの時刻が未来すぎます。");
        }

        if (now - tokenTime > validDuration)
        {
            return (false, "トークンの有効期限が切れています。");
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