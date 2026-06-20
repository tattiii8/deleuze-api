using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. HTTPコンテキストにアクセスするためのサービス群の登録
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpHeaderTenantProvider>();

// 2. OIDC / JWT 認証設定
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "http://deleuze-auth:8080"; // 単一Issuerのアドレス
        options.RequireHttpsMetadata = false;           // 開発環境のためHTTP検証を許可
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true
        };
    });

builder.Services.AddAuthorization();

// 3. PostgreSQLの接続設定
// ★ テナントごとにキャッシュを分離する独自のIModelCacheKeyFactoryを登録
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString)
           .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
});

var app = builder.Build();

// ★ 修正ポイント: 起動時のスキーマ作成ループおよび実データの挿入ロジックはすべて撤廃。
// 実データやスキーマ構造は DB 起動時に init.sql で手動（自動）作成されるため、
// アプリは起動時にDBへ余計なアクセスを一切行いません。

// 4. 認証・認可ミドルウェアの適用
app.UseAuthentication();
app.UseAuthorization();

// 5. テナント別データ取得API (トークン・ヘッダー一致で安全にガード)
app.MapGet("/api/products", async (AppDbContext db, ITenantProvider tenantProvider) =>
{
    try 
    {
        // ヘッダーとJWTクレームのチェックを通過した現在のテナントIDを取得
        var currentTenant = tenantProvider.GetTenantId();
        
        // EF Coreにより、このリクエストのテナント専用のスキーマからデータが引き出されます
        var products = await db.Products.ToListAsync();
        
        return Results.Ok(new { 
            DetectedTenant = currentTenant, 
            Data = products 
        });
    }
    catch (UnauthorizedAccessException ex)
    {
        // テナントのなりすまし等の不正アクセスを検知した場合
        return Results.Json(new { error = ex.Message }, statusCode: 403);
    }
}).RequireAuthorization(); // JWTによる認証を強制

app.Run();

// ─── 以下、マルチテナント（スキーマ分離）に必要なクラス群 ───

public interface ITenantProvider 
{ 
    string GetTenantId(); 
}

public class HttpHeaderTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public HttpHeaderTenantProvider(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public string GetTenantId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return "public";

        // ① リクエストヘッダーからテナントIDを取得
        string? headerTenant = context.Request.Headers.TryGetValue("X-Tenant-ID", out var tId) ? tId.ToString() : null;

        // ② OIDCのJWTトークンのクレームからテナントIDを取得
        var tokenTenant = context.User.FindFirst("tenant_id")?.Value;

        // ③ セキュリティ防衛線: テナントのすり替え（偽装リクエスト）をブロック
        if (headerTenant != null && tokenTenant != null && headerTenant != tokenTenant)
        {
            throw new UnauthorizedAccessException("ヘッダーのテナントIDとJWT内のテナントIDが一致しません。");
        }

        // 基本的には偽装不可能なJWT内のテナントIDを優先して信頼する
        return tokenTenant ?? headerTenant ?? "public";
    }
}

public class Product 
{ 
    public int Id { get; set; } 
    public string Name { get; set; } = string.Empty; 
}

public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;
    
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider) 
        : base(options) => _tenantProvider = tenantProvider;
        
    public DbSet<Product> Products => Set<Product>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // ★リクエストごとに取得したテナントIDを、そのまま接続先スキーマ名として動的に指定
        modelBuilder.HasDefaultSchema(_tenantProvider.GetTenantId()); 
    }
}

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    // EF Coreが特定のスキーマにモデル設定を固定（キャッシュ）してしまうのを防ぐため、
    // テナントID（スキーマ名）をキャッシュキーの一部にしてキャッシュをテナント別に分離する
    public object Create(DbContext context, bool designTime) => 
        context is AppDbContext db ? (context.GetType(), db.GetService<ITenantProvider>().GetTenantId(), designTime) : context.GetType();
}