using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using DeleuzeAuth.Data;
using DeleuzeAuth.Services;
using DeleuzeAuth.Services.Authentication;
using DeleuzeAuth.Services.Tenant;

using Deleuze.Shared.Authentication;
using Deleuze.Shared.Constants;
using Deleuze.Shared.Infrastructure;
using Deleuze.Shared.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

// ==========================================================
// CORS
// ==========================================================

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ==========================================================
// Database
// ==========================================================

var connectionString =
    builder.Configuration.GetConnectionString(
        "DefaultConnection")
    ?? throw new InvalidOperationException(
        "接続文字列 'DefaultConnection' が設定されていません。");

// ==========================================================
// AuthDbContext
// ==========================================================
//
// 共通DBへの接続。
// テナント固有のユーザー情報は auth_{tenantId} Schemaを使用する。
// 実際のテナントSchemaの切り替えは UserService 側で行う。
// ==========================================================

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// ==========================================================
// Tenant Schema
// Provisioning / Migration / Deprovisioning
// ==========================================================

builder.Services.AddScoped<TenantSchemaManager>(_ =>
{
    return new TenantSchemaManager(
        connectionString,
        "auth");
});

builder.Services.AddScoped<ITenantSchemaProvisioner>(
    sp => sp.GetRequiredService<TenantSchemaManager>());

builder.Services.AddScoped<ITenantSchemaMigrator>(
    sp => sp.GetRequiredService<TenantSchemaManager>());

builder.Services.AddScoped<ITenantSchemaDeprovisioner>(
    sp => sp.GetRequiredService<TenantSchemaManager>());

// Auth固有の薄いサービス
builder.Services.AddScoped<
    ITenantProvisioningService,
    TenantProvisioningService>();

builder.Services.AddScoped<
    ITenantMigrationService,
    TenantMigrationService>();

builder.Services.AddScoped<
    ITenantDeprovisioningService,
    TenantDeprovisioningService>();

// ==========================================================
// Authentication Services
// ==========================================================
//
// token/connect で使用する認証サービス。
// UserService は Auth DB の auth_{tenantId} Schemaから
// ユーザーを検索する。
// ==========================================================

builder.Services.AddScoped<
    IPasswordHasher,
    BCryptPasswordHasher>();

builder.Services.AddScoped<IUserService>(sp =>
{
    var passwordHasher =
        sp.GetRequiredService<IPasswordHasher>();

    return new UserService(
        connectionString,
        passwordHasher);
});

builder.Services.AddSingleton<TokenGenerator>();

// ==========================================================
// Auth Internal URL
// ==========================================================

var authInternalUrl =
    builder.Configuration["AUTH_INTERNAL_URL"]
    ?? "http://192.168.8.112:5001/api/auth";

Console.WriteLine(
    $"[DeleuzeAuth] Internal URL = {authInternalUrl}");

// ==========================================================
// HttpClient
// ==========================================================

builder.Services.AddHttpClient();

// ==========================================================
// Authentication
// ==========================================================

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "SmartAuth";
    options.DefaultChallengeScheme = "SmartAuth";
})
.AddPolicyScheme(
    "SmartAuth",
    "JWT or ApiKey",
    options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            if (context.Request.Headers.ContainsKey("X-Api-Key"))
            {
                return "ApiKey";
            }

            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
.AddScheme<
    AuthenticationSchemeOptions,
    ApiKeyAuthenticationHandler>(
    "ApiKey",
    _ => { })
.AddJwtBearer(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        options.Authority = authInternalUrl;

        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero
            };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine(
                    $"[DeleuzeAuth][JWT] Authentication FAILED: {context.Exception}");

                return Task.CompletedTask;
            },

            OnTokenValidated = context =>
            {
                Console.WriteLine(
                    "[DeleuzeAuth][JWT] Token VALIDATED");

                foreach (var claim in context.Principal!.Claims)
                {
                    Console.WriteLine(
                        $"[DeleuzeAuth][JWT] Claim: {claim.Type} = {claim.Value}");
                }

                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                Console.WriteLine(
                    $"[DeleuzeAuth][JWT] Challenge: {context.Error} / {context.ErrorDescription}");

                return Task.CompletedTask;
            }
        };
    });

// ==========================================================
// MVC / Swagger
// ==========================================================

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "deleuze-auth API",
            Version = "v1"
        });

    c.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Description = "deleuze-auth JWT",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer"
        });

    c.AddSecurityDefinition(
        "ApiKey",
        new OpenApiSecurityScheme
        {
            Description = "deleuze-mng X-Api-Key",
            Name = "X-Api-Key",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey
        });

    c.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference =
                        new OpenApiReference
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
                    Reference =
                        new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "ApiKey"
                        }
                },
                Array.Empty<string>()
            }
        });
});

// ==========================================================
// Forwarded Headers
// ==========================================================

builder.Services.Configure<ForwardedHeadersOptions>(
    options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedPrefix;

        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

// ==========================================================
// Health Check
// ==========================================================

builder.Services.AddHealthChecks();

// ==========================================================
// Build
// ==========================================================

var app = builder.Build();

app.UseForwardedHeaders();

app.UseCors();

// ==========================================================
// Swagger
// ==========================================================

app.UseDeleuzeSwagger(
    app.Environment,
    builder.Configuration,
    ApiRoutes.Auth.Base,
    "deleuze-auth API");

// ==========================================================
// Authentication / Authorization
// ==========================================================

app.UseAuthentication();
app.UseAuthorization();

// ==========================================================
// Controllers
// ==========================================================

app.MapControllers();

// ==========================================================
// Health Check
// ==========================================================

app.MapHealthChecks("/health");

app.Run();