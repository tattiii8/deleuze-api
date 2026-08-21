using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using DeleuzeAuth.Data;
using DeleuzeAuth.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. レイヤー化された各サービスの依存注入設定 (DI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA鍵維持のためシングルトン

// Swagger / OpenAPI の登録
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-auth API", Version = "v1" });
});

// リバースプロキシ（Nginx）からの Forwarded ヘッダー対応設定
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor 
                             | ForwardedHeaders.XForwardedProto 
                             | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// リバースプロキシのヘッダー処理を有効化
app.UseForwardedHeaders();

// Nginx から送られてくる `/api/auth` プレフィックスを自動除去・認識させる
app.UsePathBase("/api/auth");

// Swagger UI のミドルウェア設定 (開発・確認環境で有効化)
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger(c =>
    {
        c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
        {
            // スキームやホストを含めず、/api/auth の相対パスのみを指定（他のマイクロサービスと表示を統一）
            var pathBase = string.IsNullOrEmpty(httpReq.PathBase.Value) 
                ? "/api/auth" 
                : httpReq.PathBase.Value;

            swaggerDoc.Servers = new System.Collections.Generic.List<OpenApiServer>
            {
                new OpenApiServer { Url = pathBase }
            };
        });
    });

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("v1/swagger.json", "deleuze-auth API v1");
        c.RoutePrefix = "swagger";
    });
}

// OIDCディスカバリドキュメント
app.MapGet("/.well-known/openid-configuration", () =>
{
    var externalUrl = (Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") ?? "https://deleuze.lesure.net/api/auth").TrimEnd('/');

    return Results.Ok(new
    {
        issuer = externalUrl,
        token_endpoint = $"{externalUrl}/connect/token",
        jwks_uri = $"{externalUrl}/.well-known/jwks",
        id_token_signing_alg_values_supported = new[] { "RS256" }
    });
});

// JWKSエンドポイント
app.MapGet("/.well-known/jwks", (TokenGenerator tokenGenerator) => 
    Results.Ok(tokenGenerator.GetJwks()));

// トークン発行エンドポイント（[FromForm] DTO バインドにより Swagger UI にフォーム入力欄を表示）
app.MapPost("/connect/token", async (
    [FromForm] TokenRequest request,
    IUserService userService, 
    TokenGenerator tokenGenerator) =>
{
    if (string.IsNullOrEmpty(request.user_id) || string.IsNullOrEmpty(request.password))
    {
        return Results.Json(new { error = "invalid_request", message = "IDとパスワードは必須です。" }, statusCode: 400);
    }

    var tenantId = await userService.AuthenticateAndGetTenantAsync(request.user_id, request.password);

    if (tenantId == null)
    {
        return Results.Json(new { error = "invalid_grant", message = "認証情報が正しくありません！" }, statusCode: 400);
    }

    var token = tokenGenerator.GenerateJwt(request.user_id, tenantId);
    
    return Results.Ok(new { 
        access_token = token, 
        token_type = "Bearer", 
        expires_in = 7200 
    });
})
.DisableAntiforgery()
.Accepts<TokenRequest>("application/x-www-form-urlencoded");

app.Run();

// Swagger フォーム入力バインド用 DTO
public class TokenRequest
{
    public string user_id { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
}