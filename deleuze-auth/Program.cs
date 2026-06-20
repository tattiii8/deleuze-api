using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

var builder = WebApplication.CreateBuilder(args);

// 1. レイヤー化された各サービスの依存注入設定 (DI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA鍵維持のためシングルトン

var app = builder.Build();

// ★ OIDCディスカボリドキュメント（案内所エンドポイントの動的修正）
app.MapGet("/.well-known/openid-configuration", () =>
{
    // TokenGenerator.cs と同一の環境変数から、外側の正規URLを取得（フォールバック付き）
    // スクリプトが叩いている「http://192.168.8.112:5002」がここに入ります。
    var externalUrl = Environment.GetEnvironmentVariable("AUTH_EXTERNAL_URL") ?? "http://deleuze-auth:8080";
    
    // 業務アプリ（deleuze-app）が同じNomadのネットワーク空間（localhost）から確実に鍵を取得できるURLを指定
    var internalUrl = Environment.GetEnvironmentVariable("AUTH_INTERNAL_URL") ?? "http://127.0.0.1:5002";

    return Results.Ok(new
    {
        issuer = externalUrl,                             // トークンの iss と完全一致させる（超重要）
        token_endpoint = $"{externalUrl}/connect/token",   // 外側スクリプトが叩くログインURL
        jwks_uri = $"{internalUrl}/.well-known/jwks",     // 業務アプリが内部から確実に鍵を抜くためのURL
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
    var password = form["password"].ToString(); // クライアントからの生パスワード

    if (string.IsNullOrEmpty(loginId) || string.IsNullOrEmpty(password))
    {
        return Results.Json(new { error = "invalid_request", message = "IDとパスワードは必須です。" }, statusCode: 400);
    }

    // サービス層を通じて、認証DBのハッシュチェックと所属テナントの引き出しを実行
    var tenantId = await userService.AuthenticateAndGetTenantAsync(loginId, password);

    if (tenantId == null)
    {
        // セキュリティのため、IDとパスワードのどちらが間違っているかは明かさない
        return Results.Json(new { error = "invalid_grant", message = "認証情報が正しくありません。" }, statusCode: 400);
    }

    // 認証に成功したユーザー情報と正確な所属テナントでJWTを署名発行
    var token = tokenGenerator.GenerateJwt(loginId, tenantId);
    
    return Results.Ok(new { 
        access_token = token, 
        token_type = "Bearer", 
        expires_in = 7200 
    });
});

app.Run();