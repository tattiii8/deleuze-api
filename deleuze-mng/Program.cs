using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeleuzeMng.Services;
using DeleuzeMng.Data;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 💡 ログ設定を強制追加
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

// 1. 接続文字列の存在チェック
var authConnectionString = builder.Configuration.GetConnectionString("AuthConnection")
    ?? throw new InvalidOperationException("接続文字列 'AuthConnection' が設定されていません。");

if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("AppConnection")))
{
    throw new InvalidOperationException("接続文字列 'AppConnection' が設定されていません。");
}

// 2. Nomad の環境変数等から「シークレットキー」を取得
var apiSecret = builder.Configuration["MANAGEMENT_API_SECRET"]
    ?? throw new InvalidOperationException("環境変数 'MANAGEMENT_API_SECRET' が設定されていません。");

// 管理用サービスの登録
builder.Services.AddScoped<TenantManagementService>();

// Swagger / OpenAPI サービスの登録
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DeleuzeMng API", Version = "v1" });

    // 1. Bearer認証スキームの定義を追加
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWTまたはカスタム動的トークンを入力してください。\n例: `Bearer base64Payload:signature` またはそのまま入力",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey, // または SecuritySchemeType.Http
        Scheme = "Bearer"
    });

    // 2. すべてのエンドポイントでこのセキュリティ定義を有効にする要件を追加
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Swagger UI の有効化
app.UseSwagger();
app.UseSwaggerUI();

// データベースの初期化
await DbInitializer.EnsureSeedDataAsync(authConnectionString);

// テナントIDのAPI層での事前バリデーション用
var tenantIdPattern = new Regex(@"^[a-z][a-z0-9_]{2,62}$", RegexOptions.Compiled);

// =========================================================================
// 🔒 動的トークンチェック ＆ ログ出力ミドルウェア
// =========================================================================
app.Use(async (context, next) =>
{
    // Swagger UI および関連ドキュメントへのアクセスは認証をスキップ
    if (context.Request.Path.StartsWithSegments("/swagger"))
    {
        await next();
        return;
    }

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
        string maskedToken = rawToken.Length > 25 ? $"{rawToken[..25]}..." : rawToken;
        app.Logger.LogInformation("[MNG-AUTH] トークンを受信しました: {Token}", maskedToken);

        var (isValid, reason) = ValidateDynamicTokenWithReason(rawToken, apiSecret, TimeSpan.FromMinutes(5));

        if (!isValid)
        {
            app.Logger.LogWarning("[MNG-AUTH-FAIL] トークン検証に失敗しました。理由: {Reason}", reason);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "認証トークンが無効、または有効期限切れです。" });
            return;
        }

        app.Logger.LogInformation("[MNG-AUTH-SUCCESS] トークン検証に成功しました。処理を継続します。");
    }
    await next();
});

// 🛠️ 管理エンドポイント1: テナントの新規作成
app.MapPost("/api/mng/tenants", async (TenantCreationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.TenantId)) return Results.BadRequest(new { error = "TenantId は必須です。" });
    string normalizedTenantId = req.TenantId.ToLower();
    if (!tenantIdPattern.IsMatch(normalizedTenantId)) return Results.BadRequest(new { error = "TenantId は小文字英数字とアンダースコアのみ、3〜63文字で指定してください。" });

    try
    {
        await mngService.CreateTenantAsync(normalizedTenantId);
        return Results.Ok(new { message = $"テナント '{normalizedTenantId}' のスキーマ隔離環境を構築しました。" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "テナント作成エラー: {TenantId}", normalizedTenantId);
        return Results.Problem("処理中にエラーが発生しました。", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("CreateTenant")
.WithOpenApi();

// 🛠️ 管理エンドポイント2: ユーザーの新規登録
app.MapPost("/api/mng/users", async (UserRegistrationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.LoginId) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.TenantId))
        return Results.BadRequest(new { error = "すべての項目を入力してください。" });

    string normalizedTenantId = req.TenantId.ToLower();
    if (!tenantIdPattern.IsMatch(normalizedTenantId)) return Results.BadRequest(new { error = "TenantId の形式が不正です。" });

    try
    {
        await mngService.CreateTenantAsync(normalizedTenantId);
        await mngService.RegisterUserAsync(req.LoginId, req.Password, normalizedTenantId);
        return Results.Ok(new { message = $"テナント '{normalizedTenantId}' にユーザー '{req.LoginId}' を登録しました。" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "ユーザー登録エラー: {LoginId}", req.LoginId);
        return Results.Problem("処理中にエラーが発生しました。", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("RegisterUser")
.WithOpenApi();

app.Run();

// トークン検証ロジック
static (bool IsValid, string Reason) ValidateDynamicTokenWithReason(string rawToken, string secretKey, TimeSpan validDuration)
{
    // (既存のロジックをそのまま記載してください)
    if (string.IsNullOrWhiteSpace(rawToken)) return (false, "トークンが空です。");
    if (rawToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) rawToken = rawToken[7..].Trim();
    var parts = rawToken.Split(':');
    if (parts.Length != 2) return (false, "トークンのフォーマットが不正です。");

    try
    {
        var secretBytes = Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new HMACSHA256(secretBytes);
        var expectedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
        var providedSignatureBytes = Convert.FromBase64String(parts[1]);
        if (!CryptographicOperations.FixedTimeEquals(providedSignatureBytes, expectedSignatureBytes)) return (false, "署名が一致しません。");

        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])).Split('|');
        if (!long.TryParse(payload[0], out long unixTimestamp)) return (false, "タイムスタンプ不正。");
        
        var tokenTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        if (tokenTime > DateTimeOffset.UtcNow.AddMinutes(5) || DateTimeOffset.UtcNow - tokenTime > validDuration) return (false, "期限切れまたは時刻不正。");

        return (true, "成功");
    }
    catch { return (false, "検証中に例外が発生しました。"); }
}

public record TenantCreationRequest(string TenantId);
public record UserRegistrationRequest(string LoginId, string Password, string TenantId);