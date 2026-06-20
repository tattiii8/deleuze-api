using DeleuzeMng.Services;
using DeleuzeMng.Data; // 💡 追加

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string not found.");

// 管理用サービスの登録
builder.Services.AddScoped<TenantManagementService>(_ => new TenantManagementService(connectionString));

var app = builder.Build();

// =========================================================================
// 🚀 データベースの初期化（別クラスから非同期で1行だけ呼び出す）
// =========================================================================
await DbInitializer.EnsureSeedDataAsync(connectionString);
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