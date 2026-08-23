using System;
using Amazon.S3;
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
using DeleuzeDrive.Authentication;

var builder = WebApplication.CreateBuilder(args);

// DbContext (PostgreSQL) の登録 + 動的モデルキャッシュキーファクトリの設定
builder.Services.AddDbContext<DriveDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>();
});

builder.Services.AddHttpContextAccessor();

// ITenantProvider を JwtTenantProvider に登録
builder.Services.AddScoped<ITenantProvider, JwtTenantProvider>();

// AWS S3 サービスおよび IStorageService の登録
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<IStorageService, S3StorageService>();

// deleuze-auth の基本URL
var authAuthority = builder.Configuration["AUTH_INTERNAL_URL"] 
    ?? "http://192.168.8.112:5001/api/auth";

// deleuze-auth 内部検証 API 呼び出し用の HttpClient 登録
builder.Services.AddHttpClient("AuthService", client =>
{
    var baseUrl = authAuthority.EndsWith("/") ? authAuthority : authAuthority + "/";
    client.BaseAddress = new Uri(baseUrl);
});

// SmartAuth (PolicyScheme) による動的認証切替
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "SmartAuth";
    options.DefaultChallengeScheme = "SmartAuth";
})
.AddPolicyScheme("SmartAuth", "JWT or ApiKey", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        if (context.Request.Headers.ContainsKey("X-Api-Key"))
        {
            return ApiKeyAuthenticationOptions.DefaultScheme;
        }
        return JwtBearerDefaults.AuthenticationScheme;
    };
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationOptions.DefaultScheme, _ => { })
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Authority = authAuthority;
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger UI の設定
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-drive API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "deleuze-auth でログイン時に取得した JWT アクセストークンを入力してください。",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "deleuze-mng で発行された X-Api-Key を入力してください。",
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
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
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// リバースプロキシからの Forwarded ヘッダー対応
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks().AddDbContextCheck<DriveDbContext>("Database");

var app = builder.Build();

app.UseForwardedHeaders();

// 💡 修正: 内部通信用ポート（5004）でのパスズレ（404）を防ぐため、固定の UsePathBase は除外

if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "deleuze-drive API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();