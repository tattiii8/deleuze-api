
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using DeleuzeMng.Data;
using DeleuzeMng.Services;
using Deleuze.Shared.Constants;
using Deleuze.Shared.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Dapper;


var builder = WebApplication.CreateBuilder(args);

var authInternalUrl = builder.Configuration["AUTH_INTERNAL_URL"] ?? "http://localhost:5001";

// AuthApiClient の登録
builder.Services.AddHttpClient("AuthApiClient", client =>
{
    client.BaseAddress = new Uri(authInternalUrl);
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

// 1. CORS の登録
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});





var mngConnectionString = builder.Configuration.GetConnectionString("MngConnection")
    ?? throw new InvalidOperationException("接続文字列 'MngConnection' が設定されていません。");

    // Program.cs の builder.Services 付近
builder.Services.AddDbContext<MngDbContext>(options =>
    options.UseNpgsql(mngConnectionString));

builder.Services.AddScoped<IDbInitializerService, DbInitializerService>();

var enableMngAuth = builder.Configuration.GetValue<bool>("ENABLE_MNG_AUTH", true);
var apiSecret = builder.Configuration["MANAGEMENT_API_SECRET"];
if (enableMngAuth && string.IsNullOrEmpty(apiSecret))
{
    throw new InvalidOperationException("認証が有効ですが、環境変数 'MANAGEMENT_API_SECRET' が設定されていません。");
}

// 各サービスのベースURL設定
// Nomad等の環境変数から "http://<プライベートIP>:<ポート>" を取得（未設定の場合はデフォルト値へフォールバック）
var authServiceUrl = builder.Configuration["AUTH_SERVICE_URL"] ?? "http://192.168.8.112:5001";
var driveServiceUrl = builder.Configuration["DRIVE_SERVICE_URL"] ?? "http://192.168.8.112:5004";

builder.Services.AddHttpClient();

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze- API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "シークレットキーを入力してください。",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddDbContext<MngDbContext>(options =>
    options.UseNpgsql(mngConnectionString));

var app = builder.Build();

app.UseForwardedHeaders();

// CORS を有効化
app.UseCors();

if (!enableMngAuth)
{
    app.Logger.LogWarning("[MNG-AUTH] 管理APIのワンタイムトークン認証は無効化されています。");
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger 設定
if (app.Environment.IsDevelopment())
{
    // 開発環境では標準の Swagger / Swagger UI を有効化
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "deleuze-mng API v1");
        c.RoutePrefix = "swagger"; // http://localhost:5002/swagger でアクセス可能
    });
}
else
{
    // 本番・Nomad環境用（プレフィックス付きルーティング）
    app.UseDeleuzeSwagger(app.Environment, builder.Configuration, ApiRoutes.Management.Base, "deleuze-mng API");
}


app.MapControllers();

app.Run();