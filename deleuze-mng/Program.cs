using DeleuzeMng.Services;
using DeleuzeMng.Data;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// 1. データベース接続文字列の取得
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string not found.");

// 2. 💡 Nomadの環境変数（またはappsettings）から認証トークンを取得
// ※ Nomadのジョブファイル（HCL）の env セクションで指定した値がここに紐付きます
var apiToken = builder.Configuration["MANAGEMENT_API_TOKEN"];

if (string.IsNullOrEmpty(apiToken))
{
    // 本番環境でのトークン未設定による事故を防ぐため、未設定時は起動エラーにする
    throw new InvalidOperationException("環境変数 'MANAGEMENT_API_TOKEN' が設定されていません。");
}

// 管理用サービスの登録
builder.Services.AddScoped<TenantManagementService>(_ => new TenantManagementService(connectionString));

var app = builder.Build();

// データベースの初期化
await DbInitializer.EnsureSeedDataAsync(connectionString);

// =========================================================================
// 🔒 トークンチェックを行うミドルウェア
// =========================================================================
app.Use(async (context, next) =>
{
    // マネジメントAPIのルート（/api/mng/）のみ認証を必須にする
    if (context.Request.Path.StartsWithSegments("/api/mng"))
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var extractedToken) ||
            extractedToken != $"Bearer {apiToken}")
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "認証トークンが無効または未設定です。" });
            return;
        }
    }
    await next();
});

// 🛠️ 管理エンドポイント1: テナントの新規作成（オンボーディング）
app.MapPost("/api/mng/tenants", async (TenantCreationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.TenantId)) return Results.BadRequest("TenantId は必須です。");

    await mngService.CreateTenantAsync(req.TenantId.ToLower());
    return Results.Ok(new { message = $"テナント '{req.TenantId}' のスキーマ隔離環境を動的に構築しました。" });
});

// 🛠️ 管理エンドポイント2: ユーザーの新規登録（ハッシュ化保存）
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

// DTO 定義
public record TenantCreationRequest(string TenantId);
public record UserRegistrationRequest(string LoginId, string Password, string TenantId);