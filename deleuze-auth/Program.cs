using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

var builder = WebApplication.CreateBuilder(args);

// 1. レイヤー化された各サービスの依存注入設定 (DI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA鍵維持のためシングルトン

// ★ Swagger / OpenAPI の登録
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ★ Swagger UI のミドルウェア設定 (開発・確認環境で有効化)
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Deleuze Auth API v1");
        c.RoutePrefix = "swagger"; // http://<host>:<port>/swagger でアクセス可能
    });
}

// ★ OIDCディスカバリドキュメント（案内所エンドポイントの動的修正）
app.MapGet("/.well-known/openid-configuration", () =>
{
    var externalUrl = Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") ?? "http://deleuze-auth:8080";
    var internalUrl = Environment.GetEnvironmentVariable("AUTH_INTERNAL_URL") ?? "http://127.0.0.1:5001";

    return Results.Ok(new
    {
        issuer = externalUrl,                             
        token_endpoint = $"{externalUrl}/connect/token",   
        jwks_uri = $"{internalUrl}/.well-known/jwks",     
        subject_types_supported = new[] { "public" },
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