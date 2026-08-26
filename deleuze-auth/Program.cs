using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using DeleuzeAuth.Data;
using DeleuzeAuth.Services;
using Deleuze.Shared.Constants;
using Deleuze.Shared.Swagger;

var builder = WebApplication.CreateBuilder(args);

// 1. CORS ポリシーの登録
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 2. データベース・サービスの登録 (DI)
// 変数名を authConnectionString にそろえる場合
var authConnectionString =
    builder.Configuration.GetConnectionString("AuthConnection");

if (string.IsNullOrWhiteSpace(authConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:AuthConnection が設定されていません。");
}

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(authConnectionString));

builder.Services.AddScoped<IDbInitializerService, DbInitializerService>();

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA 鍵の生成

// 3. コントローラーの有効化
builder.Services.AddControllers();

// 4. Swagger Generator の設定 (mng と統一)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-auth API", Version = "v1" });

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

// Nginx 等のリバースプロキシ対応
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                              | ForwardedHeaders.XForwardedProto
                              | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();



// ミドルウェア パイプライン
app.UseCors();

// 5. Swagger 設定 (mng と同様に IsDevelopment による分岐を導入)
if (app.Environment.IsDevelopment())
{
    // 開発環境では標準の Swagger / Swagger UI を有効化
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "deleuze-auth API v1");
        c.RoutePrefix = "swagger"; // http://localhost:5001/swagger でアクセス可能
    });
}
else
{
    // 本番・Nomad環境用（プレフィックス付きルーティング）
    app.UseDeleuzeSwagger(app.Environment, builder.Configuration, ApiRoutes.Auth.Base, "deleuze-auth API");
}

// コントローラーへのルーティングを有効化
app.MapControllers();

app.Run();