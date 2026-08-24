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

var builder = WebApplication.CreateBuilder(args);

// 1. データベース・サービスの登録 (DI)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<TokenGenerator>(); // RSA 鍵の生成

// 2. コントローラーの有効化
builder.Services.AddControllers();

// Swagger / OpenAPI の設定
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

// 💡 削除: コントローラー側で ApiRoutes (例: "api/auth/internal") を直接定義しているため、
// UsePathBase を指定すると Swagger やルーティングでパスが重複する原因になります。
// app.UsePathBase("/api/auth");

// Swagger UI
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger(c =>
    {
        c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
        {
            var host = httpReq.Host.Value;
            var scheme = httpReq.Scheme;
            swaggerDoc.Servers = new System.Collections.Generic.List<OpenApiServer>
            {
                new OpenApiServer { Url = $"{scheme}://{host}" }
            };
        });
    });

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "deleuze-auth API v1");
        c.RoutePrefix = "swagger";
    });
}

// 3. コントローラーへのルーティングを有効化
app.MapControllers();

app.Run();