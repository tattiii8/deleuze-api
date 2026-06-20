using DeleuzeMng.Services;
using Npgsql;
using Dapper;

var builder = WebApplication.CreateBuilder(args);

// 💡 1. 接続文字列の取得を強化（構成ファイル or Nomadの環境変数から直接取得）
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException("Connection string not found.");

// 管理用サービスの登録
builder.Services.AddScoped<TenantManagementService>(_ => new TenantManagementService(connectionString));

var app = builder.Build();

// =========================================================================
// 🚀 物理インラインによるDB初期化（コンパイル対象から漏れるリスクをゼロにする）
// =========================================================================
await (async () => 
{
    // コンテナ起動直後に必ず標準出力に痕跡を残す
    Console.WriteLine($"[INIT-START] Database initialization triggered. Target Host: {new NpgsqlConnectionStringBuilder(connectionString).Host}");
    
    try 
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS public.""Users"" (
                ""Id"" SERIAL PRIMARY KEY,
                ""LoginId"" VARCHAR(100) NOT NULL UNIQUE,
                ""Password"" VARCHAR(255) NOT NULL,
                ""TenantId"" VARCHAR(100) NOT NULL,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );";

        await connection.ExecuteAsync(createTableSql);
        Console.WriteLine("[INIT-SUCCESS] public.\"Users\" テーブルの整合性を確認・自動生成しました。");
    }
    catch (Exception ex) 
    {
        // 失敗した場合、Nomadのログに確実にエラーを残す
        Console.Error.WriteLine($"[INIT-FATAL-ERROR] データベースの初期化中に致命的な例外が発生しました: {ex.Message}");
        Console.Error.WriteLine(ex.StackTrace);
    }
})();
// =========================================================================

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