using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using DeleuzeMng.Data;
using DeleuzeMng.Services;
using Deleuze.Shared.Constants;
using Deleuze.Shared.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var authInternalUrl =
    builder.Configuration["AUTH_INTERNAL_URL"] ?? "http://localhost:5001";

// AuthApiClient の登録
builder.Services.AddHttpClient("AuthApiClient", client =>
{
    client.BaseAddress = new Uri(authInternalUrl);
});

// ログ設定
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

// 2. データベース設定
var mngConnectionString =
    builder.Configuration.GetConnectionString("MngConnection")
    ?? throw new InvalidOperationException(
        "接続文字列 'MngConnection' が設定されていません");

builder.Services.AddDbContext<MngDbContext>(options =>
    options.UseNpgsql(mngConnectionString));

// DB初期化サービス
builder.Services.AddScoped<IDbInitializerService, DbInitializerService>();

// 3. MNG認証設定
var enableMngAuth =
    builder.Configuration.GetValue<bool>("ENABLE_MNG_AUTH", true);

var apiSecret =
    builder.Configuration["MANAGEMENT_API_SECRET"];

if (enableMngAuth && string.IsNullOrEmpty(apiSecret))
{
    throw new InvalidOperationException(
        "認証が有効ですが、環境変数 'MANAGEMENT_API_SECRET' が設定されていません。");
}

// 各サービスのベースURL設定
var authServiceUrl =
    builder.Configuration["AUTH_SERVICE_URL"]
    ?? "http://192.168.8.112:5001";

var driveServiceUrl =
    builder.Configuration["DRIVE_SERVICE_URL"]
    ?? "http://192.168.8.112:5004";

builder.Services.AddHttpClient();

builder.Services.AddControllers();

// 4. Swagger
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "deleuze-mng API",
            Version = "v1"
        });

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

// 5. リバースプロキシ対応
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedPrefix;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// CORS
app.UseCors();

if (!enableMngAuth)
{
    app.Logger.LogWarning(
        "[MNG-AUTH] 管理APIのワンタイムトークン認証は無効化されています。");
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "deleuze-mng API v1");

        c.RoutePrefix = "swagger";
    });
}
else
{
    app.UseDeleuzeSwagger(
        app.Environment,
        builder.Configuration,
        ApiRoutes.Management.Base,
        "deleuze-mng API");
}

// Controller
app.MapControllers();

// DB初期化
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider
        .GetRequiredService<IDbInitializerService>();

    await initializer.ExecuteWithRetryAsync();
}

app.Run();