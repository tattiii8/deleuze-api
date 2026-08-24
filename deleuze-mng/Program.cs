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
using Deleuze.Shared.Constants;
using Deleuze.Shared.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

// 💡 1. CORS の登録
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

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

builder.Services.AddHttpClient<IServiceProvisioningClient, DriveProvisioningClient>();
builder.Services.AddHttpClient<DriveProvisioningClient>();

builder.Services.AddScoped<ITenantManagementService>(sp =>
{
    var client = sp.GetRequiredService<IServiceProvisioningClient>();
    var driveClient = sp.GetRequiredService<DriveProvisioningClient>();

    var serviceClients = new Dictionary<string, Func<string, Task<bool>>>
    {
        [client.ServiceKey] = async (tenantId) =>
        {
            await client.InitializeTenantAsync(tenantId);
            return true;
        }
    };

    var disableServiceClients = new Dictionary<string, Func<string, Task<bool>>>
    {
        [client.ServiceKey] = async (tenantId) =>
        {
            await client.RollbackTenantAsync(tenantId);
            return true;
        }
    };

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
        driveClient
    );
});

builder.Services.AddScoped<TenantManagementService>(sp => 
    (TenantManagementService)sp.GetRequiredService<ITenantManagementService>());

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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// 💡 2. パイプライン先頭で CORS を有効化
app.UseCors();

if (!enableMngAuth)
{
    app.Logger.LogWarning("[MNG-AUTH] 🔥 管理APIのワンタイムトークン認証 (ENABLE_MNG_AUTH) は無効化されています。");
}

app.UseDefaultFiles();
app.UseStaticFiles();

// 💡 3. 正しいベースプレフィックス (ApiRoutes.Management.Base = "api/mng") を指定
app.UseDeleuzeSwagger(app.Environment, builder.Configuration, ApiRoutes.Management.Base, "deleuze-mng API");

await DbInitializer.EnsureSeedDataAsync(authConnectionString);

// 🔒 トークン検証ミドルウェア
app.Use(async (context, next) =>
{
    // 💡 4. パス文字列に "swagger" が含まれていればプレフィックス位置に関わらず認証スキップ
    if (context.Request.Path.Value?.Contains("swagger", StringComparison.OrdinalIgnoreCase) == true)
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

app.MapControllers();

app.Run();

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