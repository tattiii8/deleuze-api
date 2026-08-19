using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models; // ★ 追加
using DeleuzeAuth.Data;
using DeleuzeAuth.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. レイヤー化された各サービスの依存注入設定 (DI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA鍵維持のためシングルトン

// ★ Swagger / OpenAPI の登録（AddSwaggerGenの内部に SwaggerDoc を移動）
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-auth API", Version = "v1" });
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

// ★ 核心部分: Nginx から送られてくる `/api/auth` プレフィックスを自動除去・認識させる
app.UsePathBase("/api/auth");

// ★ Swagger UI のミドルウェア設定 (開発・確認環境で有効化)
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger(c =>
    {
        // プロキシ配下の PathBase を考慮した Server 属性を自動生成
        c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
        {
            swaggerDoc.Servers = new System.Collections.Generic.List<OpenApiServer>
            {
                new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}{httpReq.PathBase.Value}" }
            };
        });
    });

    app.UseSwaggerUI(c =>
    {
        // 修正: 先頭の '/' を削除し、PathBase 内部の相対パスとして指定
        c.SwaggerEndpoint("v1/swagger.json", "deleuze-auth API v1");
        c.RoutePrefix = "swagger"; // https://<host>/api/auth/swagger でアクセス可能
    });
}

// ★ OIDCディスカバリドキュメント（案内所エンドポイント）
app.MapGet("/.well-known/openid-configuration", () =>
{
    // 末尾のスラッシュを除去して統一
    var externalUrl = (Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") ?? "https://deleuze.lesure.net/api/auth").TrimEnd('/');

    return Results.Ok(new
    {
        issuer = externalUrl,                                 // https://deleuze.lesure.net/api/auth
        token_endpoint = $"{externalUrl}/connect/token",       // https://deleuze.lesure.net/api/auth/connect/token
        jwks_uri = $"{externalUrl}/.well-known/jwks",         
        id_token_signing_alg_values_supported = new[] { "RS256" }
    });
});

// JWKSエンドポイント（APIへの公開鍵配布所）
app.MapGet("/.well-known/jwks", (TokenGenerator tokenGenerator) => 
    Results.Ok(tokenGenerator.GetJwks()));

// 本格仕様になったトークン発行エンドポイント（DB検証・生パスワード対応）
app.MapPost("/connect/token", async (HttpContext context, IUserService userService, TokenGenerator tokenGenerator) =>
{
    var form = await context.Request.ReadFormAsync();
    var loginId = form["user_id"].ToString();
    var password = form["password"].ToString(); 

    if (string.IsNullOrEmpty(loginId) || string.IsNullOrEmpty(password))
    {
        return Results.Json(new { error = "invalid_request", message = "IDとパスワードは必須です。" }, statusCode: 400);
    }

    var tenantId = await userService.AuthenticateAndGetTenantAsync(loginId, password);

    if (tenantId == null)
    {
        return Results.Json(new { error = "invalid_grant", message = "認証情報が正しくありません！" }, statusCode: 400);
    }

    var token = tokenGenerator.GenerateJwt(loginId, tenantId);
    
    return Results.Ok(new { 
        access_token = token, 
        token_type = "Bearer", 
        expires_in = 7200 
    });
});

app.Run();