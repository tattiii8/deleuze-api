using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeleuzeMng.Services;
using DeleuzeMng.Services.Clients;
using DeleuzeMng.Services.Infrastructure;
using DeleuzeMng.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var authConnectionString = builder.Configuration.GetConnectionString("AuthConnection")
    ?? throw new InvalidOperationException("接続文字列 'AuthConnection' が設定されていません。");

if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("AppConnection")))
{
    throw new InvalidOperationException("接続文字列 'AppConnection' が設定されていません。");
}

var enableMngAuth = builder.Configuration.GetValue<bool>("ENABLE_MNG_AUTH", true);
var apiSecret = builder.Configuration["MANAGEMENT_API_SECRET"];
if (enableMngAuth && string.IsNullOrEmpty(apiSecret))
{
    throw new InvalidOperationException("認証が有効ですが、環境変数 'MANAGEMENT_API_SECRET' が設定されていません。");
}

// 💡 マイクロサービス連携用 Client の DI 登録
builder.Services.AddHttpClient<IServiceProvisioningClient, DriveProvisioningClient>();

builder.Services.AddScoped<TenantManagementService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DeleuzeMng API", Version = "v1" });

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

// リバースプロキシ（Nginx）からの Forwarded ヘッダー対応設定を追加
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// リバースプロキシのヘッダー処理を有効化
app.UseForwardedHeaders();

// ★ 核心部分: Nginx から送られてくる `/api/mng` プレフィックスを自動除去・認識させる
app.UsePathBase("/api/mng");

if (!enableMngAuth)
{
    app.Logger.LogWarning("[MNG-AUTH] 🔥 管理APIのワンタイムトークン認証 (ENABLE_MNG_AUTH) は無効化されています。");
}

app.UseDefaultFiles();
app.UseStaticFiles();

// ★ Swagger UI の設定（UsePathBase を考慮し、パスを相対指定に変更）
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        // 修正: パスから /api/mng を削除
        c.SwaggerEndpoint("/api/mng/swagger/v1/swagger.json", "deleuze-mng API v1");
        c.RoutePrefix = "swagger"; // https://<host>/api/mng/swagger でアクセス可能
    });
}

await DbInitializer.EnsureSeedDataAsync(authConnectionString);

var tenantIdPattern = new Regex(@"^[a-z][a-z0-9_]{2,62}$", RegexOptions.Compiled);

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

// 🛠️ API エンドポイント
// 修正: パスから /api/mng を削除（UsePathBase があるため）

app.MapPost("/tenants", async (TenantCreationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.TenantId)) 
        return Results.BadRequest(new { error = "TenantId は必須です。" });

    string normalizedTenantId = req.TenantId.ToLower();
    if (!tenantIdPattern.IsMatch(normalizedTenantId)) 
        return Results.BadRequest(new { error = "TenantId の形式が不正です。" });

    try
    {
        await mngService.CreateTenantAsync(normalizedTenantId, req.EnabledServices);
        return Results.Ok(new { message = $"テナント '{normalizedTenantId}' の構築処理が完了しました。" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "テナント作成エラー: {TenantId}", normalizedTenantId);
        return Results.Problem("処理中にエラーが発生しました。", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("CreateTenant")
.WithOpenApi();

app.MapPost("/tenants/{tenantId}/services", async (string tenantId, EnableServiceRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.ServiceKey))
        return Results.BadRequest(new { error = "ServiceKey は必須です。" });

    string normalizedTenantId = tenantId.ToLower();

    try
    {
        await mngService.EnableServiceForTenantAsync(normalizedTenantId, req.ServiceKey);
        return Results.Ok(new { message = $"テナント '{normalizedTenantId}' にサービス '{req.ServiceKey}' を追加有効化しました。" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
})
.WithName("EnableServiceForTenant")
.WithOpenApi();

app.MapGet("/tenants", async (TenantManagementService mngService) =>
{
    var tenants = await mngService.GetTenantsAsync();
    return Results.Ok(tenants);
})
.WithName("GetTenants")
.WithOpenApi();

app.MapDelete("/tenants/{tenantId}", async (string tenantId, TenantManagementService mngService) =>
{
    try
    {
        await mngService.DeleteTenantAsync(tenantId.ToLower());
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "テナント削除エラー: {TenantId}", tenantId);
        return Results.Problem("削除処理中にエラーが発生しました。");
    }
})
.WithName("DeleteTenant")
.WithOpenApi();

app.MapPost("/users", async (UserRegistrationRequest req, TenantManagementService mngService) =>
{
    if (string.IsNullOrWhiteSpace(req.LoginId) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.TenantId))
        return Results.BadRequest(new { error = "すべての項目を入力してください。" });

    string normalizedTenantId = req.TenantId.ToLower();

    try
    {
        var existingTenants = await mngService.GetTenantsAsync();
        if (!existingTenants.Any(t => t.TenantId == normalizedTenantId))
        {
            await mngService.CreateTenantAsync(normalizedTenantId);
        }

        await mngService.RegisterUserAsync(req.LoginId, req.Password, normalizedTenantId);
        return Results.Ok(new { message = $"テナント '{normalizedTenantId}' にユーザー '{req.LoginId}' を登録しました。" });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
})
.WithName("RegisterUser")
.WithOpenApi();

app.MapGet("/users", async (TenantManagementService mngService) =>
{
    var users = await mngService.GetUsersAsync();
    return Results.Ok(users);
})
.WithName("GetUsers")
.WithOpenApi();

app.MapDelete("/users/{id:int}", async (int id, TenantManagementService mngService) =>
{
    bool deleted = await mngService.DeleteUserAsync(id);
    return deleted ? Results.NoContent() : Results.NotFound(new { error = "指定されたユーザーが見つかりません。" });
})
.WithName("DeleteUser")
.WithOpenApi();

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

public record TenantCreationRequest(string TenantId, List<string>? EnabledServices = null);
public record EnableServiceRequest(string ServiceKey);
public record UserRegistrationRequest(string LoginId, string Password, string TenantId);