using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DeleuzeMng.Services;
using DeleuzeMng.Services.Clients;
using DeleuzeMng.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using DeleuzeMng.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var authConnectionString = builder.Configuration.GetConnectionString("AuthConnection")
    ?? throw new InvalidOperationException("接続文字列 'AuthConnection' が設定されていません。");

var appConnectionString = builder.Configuration.GetConnectionString("AppConnection")
    ?? throw new InvalidOperationException("接続文字列 'AppConnection' が設定されていません。");

var enableMngAuth = builder.Configuration.GetValue<bool>("ENABLE_MNG_AUTH", true);
var apiSecret = builder.Configuration["MANAGEMENT_API_SECRET"];
if (enableMngAuth && string.IsNullOrEmpty(apiSecret))
{
    throw new InvalidOperationException("認証が有効ですが、環境変数 'MANAGEMENT_API_SECRET' が設定されていません。");
}

// 💡 マイクロサービス連携用 Client の DI 登録
builder.Services.AddHttpClient<IServiceProvisioningClient, DriveProvisioningClient>();
builder.Services.AddHttpClient<DriveProvisioningClient>(); // 👈 追加: 個別インジェクション用

// 💡 Service クライアント辞書の準備と TenantManagementService の DI 登録
builder.Services.AddScoped<ITenantManagementService>(sp =>
{
    var client = sp.GetRequiredService<IServiceProvisioningClient>();
    var driveClient = sp.GetRequiredService<DriveProvisioningClient>(); // 👈 追加: 履歴・ヘルスチェック用クライアントを取得

    // 💡 有効化（プロビジョニング）用クライアント辞書
    var serviceClients = new Dictionary<string, Func<string, Task<bool>>>
    {
        [client.ServiceKey] = async (tenantId) =>
        {
            await client.InitializeTenantAsync(tenantId);
            return true;
        }
    };

    // 💡 無効化（デプロビジョニング・削除）用クライアント辞書
    var disableServiceClients = new Dictionary<string, Func<string, Task<bool>>>
    {
        [client.ServiceKey] = async (tenantId) =>
        {
            await client.RollbackTenantAsync(tenantId);
            return true;
        }
    };

    // 💡 マイグレーション用クライアント辞書
    var migrateServiceClients = new Dictionary<string, Func<string, Task<bool>>>
    {
        [client.ServiceKey] = async (tenantId) =>
        {
            await client.MigrateTenantAsync(tenantId);
            return true;
        }
    };

    return new TenantManagementService(
        appConnectionString, 
        authConnectionString, 
        serviceClients, 
        disableServiceClients,
        migrateServiceClients,
        driveClient // 👈 正しく DriveProvisioningClient を渡す
    );
});

// コンストラクター直接注入や具象型解決が必要な場合のフォールバック登録
builder.Services.AddScoped<TenantManagementService>(sp => 
    (TenantManagementService)sp.GetRequiredService<ITenantManagementService>());

// 💡 コントローラーをサービスコンテナに追加
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-mng API", Version = "v1" });

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

// リバースプロキシ（Nginx）からの Forwarded ヘッダー対応設定
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// リバースプロキシのヘッダー処理を有効化
app.UseForwardedHeaders();

// Nginx から送られてくる `/api/mng` プレフィックスを認識
app.UsePathBase("/api/mng");

if (!enableMngAuth)
{
    app.Logger.LogWarning("[MNG-AUTH] 🔥 管理APIのワンタイムトークン認証 (ENABLE_MNG_AUTH) は無効化されています。");
}

app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger UI の設定
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/api/mng/swagger/v1/swagger.json", "deleuze-mng API v1");
        c.RoutePrefix = "swagger";
    });
}

await DbInitializer.EnsureSeedDataAsync(authConnectionString);

// 🔒 トークン検証ミドルウェア
app.Use(async (context, next) =>
{
    // Swagger UI 関連へのアクセスは認証をスキップ
    if (context.Request.Path.StartsWithSegments("/swagger"))
    {
        await next();
        return;
    }

    if (enableMngAuth)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var extractedToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authorization ヘッダーがありません。" });
            return;
        }

        string rawToken = extractedToken.ToString();
        var (isValid, reason) = ValidateDynamicTokenWithReason(rawToken, apiSecret!, TimeSpan.FromMinutes(5));

        if (!isValid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "認証トークンが無効、または有効期限切れです。" });
            return;
        }
    }

    await next();
});

// 🛠️ コントローラーのルーティングをマッピング
app.MapControllers();

app.Run();

// 🔑 トークン検証ロジック
static (bool IsValid, string Reason) ValidateDynamicTokenWithReason(string rawToken, string secretKey, TimeSpan validDuration)
{
    if (string.IsNullOrWhiteSpace(rawToken)) return (false, "トークンが空です。");
    if (rawToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) rawToken = rawToken[7..].Trim();
    var parts = rawToken.Split(':');
    if (parts.Length != 2) return (false, "トークンのフォーマットが不正です。");

    try
    {
        var secretBytes = Encoding.UTF8.GetBytes(secretKey);
        using var hmac = new HMACSHA256(secretBytes);
        var expectedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
        var providedSignatureBytes = Convert.FromBase64String(parts[1]);
        if (!CryptographicOperations.FixedTimeEquals(providedSignatureBytes, expectedSignatureBytes)) return (false, "署名が一致しません。");

        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])).Split('|');
        if (!long.TryParse(payload[0], out long unixTimestamp)) return (false, "タイムスタンプ不正。");
        
        var tokenTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        if (tokenTime > DateTimeOffset.UtcNow.AddMinutes(5) || DateTimeOffset.UtcNow - tokenTime > validDuration) return (false, "期限切れまたは時刻不正。");

        return (true, "成功");
    }
    catch { return (false, "検証中に例外が発生しました。"); }
}