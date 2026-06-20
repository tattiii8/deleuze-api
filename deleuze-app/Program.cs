using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. テナントプロバイダーの登録
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpHeaderTenantProvider>();

// 2. 認証・認可設定
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "http://deleuze-auth:8080";
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true
        };
    });
builder.Services.AddAuthorization();

// 3. PostgreSQL & EF Core 設定
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString)
           .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
});

// 4. ヘルスチェック（DB接続チェック含む）の登録
builder.Services.AddHealthChecks()
    .AddNpgsql(connectionString ?? throw new InvalidOperationException("Connection string not found."));

var app = builder.Build();

// ミドルウェアパイプライン
app.UseAuthentication();
app.UseAuthorization();

// 5. ヘルスチェックエンドポイント (認証不要が一般的)
app.MapHealthChecks("/healthz");

// 6. テナント別データ取得API
app.MapGet("/api/products", async (AppDbContext db, ITenantProvider tenantProvider) =>
{
    try 
    {
        var currentTenant = tenantProvider.GetTenantId();
        var products = await db.Products.ToListAsync();
        
        return Results.Ok(new { 
            DetectedTenant = currentTenant, 
            Data = products 
        });
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 403);
    }
}).RequireAuthorization();

app.Run();