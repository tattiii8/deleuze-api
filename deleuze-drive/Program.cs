using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DeleuzeDrive.Data;
using DeleuzeDrive.Services;

var builder = WebApplication.CreateBuilder(args);

// DbContext (PostgreSQL) の登録 + 動的モデルキャッシュキーファクトリの設定
builder.Services.AddDbContext<DriveDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
});

builder.Services.AddHttpContextAccessor();

// ★ ITenantProvider を JwtTenantProvider に登録
builder.Services.AddScoped<ITenantProvider, JwtTenantProvider>();

// ★ JWT 認証ミドルウェアの登録
var jwtSecret = builder.Configuration["JWT_SECRET"] 
    ?? builder.Configuration["Jwt:Secret"] 
    ?? throw new InvalidOperationException("環境変数または設定値 'JWT_SECRET' が未設定です。");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ★ Swagger UI で「Authorize」ボタンから Bearer トークンを設定可能にする
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DeleuzeDrive API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "deleuze-auth でログイン時に取得した JWT アクセストークンを入力してください。",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
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

// リバースプロキシ（Nginx）からの Forwarded ヘッダー対応
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks().AddDbContextCheck<DriveDbContext>("Database");

var app = builder.Build();

app.UseForwardedHeaders();
app.UsePathBase("/api/drive");

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/api/drive/swagger/v1/swagger.json", "DeleuzeDrive API v1");
        c.RoutePrefix = "swagger";
    });
}

// ⚠️ UseAuthentication -> UseAuthorization の順序で実行
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();