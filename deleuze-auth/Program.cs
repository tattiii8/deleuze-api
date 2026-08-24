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
using Deleuze.Shared.Swagger; // 共通 Swagger 拡張を参照

var builder = WebApplication.CreateBuilder(args);

// 1. データベース・サービスの登録 (DI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA 鍵の生成

// 2. コントローラーの有効化
builder.Services.AddControllers();

// Swagger Generator の設定
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-auth API", Version = "v1" });
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

// 💡 共通拡張メソッド呼び出し（api/auth/swagger へ自動マッピング）
app.UseDeleuzeSwagger(app.Environment, builder.Configuration, ApiRoutes.Auth.Base, "deleuze-auth API");

// 3. コントローラーへのルーティングを有効化
app.MapControllers();

app.Run();