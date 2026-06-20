using Microsoft.EntityFrameworkCore;
using DeleuzeAuth.Data;
using DeleuzeAuth.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. レイヤー化された各サービスの依存注入設定 (DI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA鍵維持のためシングルトン

var app = builder.Build();

// OIDCディスカボリドキュメント（案内所エンドポイント）
app.MapGet("/.well-known/openid-configuration", () => Results.Ok(new
{
    issuer = "http://deleuze-auth:8080",
    token_endpoint = "http://deleuze-auth:8080/connect/token",
    jwks_uri = "http://deleuze-auth:8080/.well-known/jwks",
    subject_types_supported = new[] { "public" },
    id_token_signing_alg_values_supported = new[] { "RS256" }
}));

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