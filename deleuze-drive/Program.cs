using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using DeleuzeDrive.Data;
using DeleuzeDrive.Services;

var builder = WebApplication.CreateBuilder(args);

// DbContext (PostgreSQL) の登録 + 動的モデルキャッシュキーファクトリの設定
builder.Services.AddDbContext<DriveDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HeaderTenantProvider>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ★ 修正点1: Swagger UI 上で X-Tenant-Id を設定できるように定義を追加
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DeleuzeDrive API", Version = "v1" });

    c.AddSecurityDefinition("TenantId", new OpenApiSecurityScheme
    {
        Description = "テナントIDを指定してください（例: acme_corp）",
        Name = "X-Tenant-Id",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "TenantId"
                }
            },
            Array.Empty<string>()
        }
    });
});

// リバースプロキシ（Nginx）からの Forwarded ヘッダー対応設定を追加
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// EF Core 用 Health Check
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DriveDbContext>("Database");

var app = builder.Build();

// リバースプロキシのヘッダー処理を有効化
app.UseForwardedHeaders();

// Nginx から送られてくる `/api/drive` プレフィックスを自動除去・認識させる
app.UsePathBase("/api/drive");

// Swagger UI のミドルウェア設定
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/api/drive/swagger/v1/swagger.json", "DeleuzeDrive API v1");
        c.RoutePrefix = "swagger";
    });
}

// ★ 修正点2: テナントヘッダー未指定時の直落ち防止ミドルウェア
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower() ?? "";

    // Swagger、Health Check、内部管理用（/internal）API はテナントID指定のチェックをバイパス
    if (path.StartsWith("/swagger") || 
        path.StartsWith("/health") || 
        path.StartsWith("/internal"))
    {
        await next();
        return;
    }

    // 通常の API リクエストで X-Tenant-Id ヘッダーが存在しない場合は 400 Bad Request
    if (!context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantId) || string.IsNullOrWhiteSpace(tenantId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "リクエストヘッダー 'X-Tenant-Id' が必要です。" });
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllers();

// https://<host>/api/drive/health でヘルスチェック可能に
app.MapHealthChecks("/health");

app.Run();