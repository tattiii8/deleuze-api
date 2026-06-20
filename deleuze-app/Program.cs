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
        options.Authority = "http://deleuze-auth:8080";
        options.RequireHttpsMetadata = false; // 開発環境・ローカルクラスターのためHTTPを許可
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = true, // Issuer検証は有効に維持してセキュアに保つ
            
            // ★ 修正ポイント: コンテナ間URLと外側のホストIP、どちらのトークンも正当な発行元として受け入れる
            ValidIssuers = new[]
            {
                "http://deleuze-auth:8080",
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