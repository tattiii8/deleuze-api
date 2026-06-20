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
        // 💡 鍵（JWKS）を取得しにいくコンテナ内バックエンドルート
        options.Authority = "http://deleuze-auth:8080";
        options.RequireHttpsMetadata = false; // 開発・ローカルクラスター環境のためHTTPを許可
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            
            // ★ 本番仕様の厳格検証をすべて有効化
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true, // 認証サーバーから取得した公開鍵による署名検証を必須にする
            ValidateLifetime = true,         // トークンの有効期限チェックを必須にする

            // ★ 認証サーバーの環境変数 (AUTH_EXTERNAL_URL) から払い出される、
            // 外側の正規URLを「正当な発行元」としてホワイトリストに指定
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