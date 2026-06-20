using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System;
using deleuze_app.Models;
using deleuze_app.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// 1. テナントプロバイダーの登録
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HttpHeaderTenantProvider>();

// 2. 認証・認可設定
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // ★ 修正ポイント: 環境変数から「内部の鍵取得用URL」を動的に読み込む
        var internalAuthUrl = Environment.GetEnvironmentVariable("AUTH_INTERNAL_URL") ?? "http://127.0.0.1:5002";
        
        options.Authority = internalAuthUrl; 
        options.RequireHttpsMetadata = false; 

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true, 
            ValidateLifetime = true,

            // トークンに刻まれている「外側の本名」を検証する
            ValidIssuers = new[]
            {
                "http://192.168.8.112:5002"
            }
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

// 4. ヘルスチェックの登録（カスタムクラスを使って型安全にDI解決）
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres-db");

var app = builder.Build();

// ミドルウェアパイプライン
app.UseAuthentication();
app.UseAuthorization();

// 5. ヘルスチェックエンドポイント（スクリプトの /healthz と同期）
app.MapHealthChecks("/health");

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