using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using DeleuzeDrive.Data;
using DeleuzeDrive.Services;
using Swashbuckle.AspNetCore.SwaggerGen;

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

// ★ 各 API の Parameters 欄に X-Tenant-Id の入力欄を表示する設定
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DeleuzeDrive API", Version = "v1" });

    // OperationFilter を追加して入力パラメータ欄に固定追加する
    c.OperationFilter<TenantHeaderOperationFilter>();
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

// ★ テナントヘッダー未指定時の直落ち防止ミドルウェア
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

// ★ 各 API エンドポイントの Parameters 欄へ X-Tenant-Id を追加するフィルタークラス
public class TenantHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // 内部 API (/internal) のコントローラーにはパラメータを追加しない
        if (context.ApiDescription.ActionDescriptor is ControllerActionDescriptor descriptor)
        {
            if (descriptor.ControllerName.Equals("TenantInternal", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        // ヘルスチェック等のパスもスキップ
        var relativePath = context.ApiDescription.RelativePath?.ToLower() ?? "";
        if (relativePath.StartsWith("health"))
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();

        // GUI の Parameters 欄に入力フォームを追加
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Tenant-Id",
            In = ParameterLocation.Header,
            Required = true, // 必須マーク (*)
            Description = "対象のテナントID (例: acme_corp)",
            Schema = new OpenApiSchema
            {
                Type = "string"
            }
        });
    }
}