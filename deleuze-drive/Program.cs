using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddSwaggerGen();

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

// ★ 核心部分: Nginx から送られてくる `/api/drive` プレフィックスを自動除去・認識させる
app.UsePathBase("/api/drive");

// ★ Swagger UI のミドルウェア設定 (開発環境または設定値で有効化)
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        // PathBase (/api/drive) を含めた JSON 取得先を指定
        c.SwaggerEndpoint("/api/drive/swagger/v1/swagger.json", "DeleuzeDrive API v1");
        c.RoutePrefix = "swagger"; // https://<host>/api/drive/swagger でアクセス可能
    });
}

app.UseAuthorization();

app.MapControllers();

// https://<host>/api/drive/health でヘルスチェック可能に
app.MapHealthChecks("/health");

app.Run();