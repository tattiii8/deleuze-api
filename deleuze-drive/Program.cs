using System; 
using Amazon.S3; 
using Microsoft.AspNetCore.Authentication; 
using Microsoft.AspNetCore.Authentication.JwtBearer; 
using Microsoft.AspNetCore.Builder; 
using Microsoft.AspNetCore.HttpOverrides; 
using Microsoft.EntityFrameworkCore; 
using Microsoft.EntityFrameworkCore.Infrastructure; 
using Microsoft.Extensions.Configuration; 
using Microsoft.Extensions.DependencyInjection; 
using Microsoft.Extensions.Hosting; 
using Microsoft.Extensions.Logging; 
using Microsoft.IdentityModel.Tokens; 
using Microsoft.OpenApi.Models; 
using DeleuzeDrive.Data; 
using DeleuzeDrive.Services; 
using Deleuze.Shared.Authentication; 
using Deleuze.Shared.Infrastructure; 
using Deleuze.Shared.Constants;
using Deleuze.Shared.MultiTenancy; 
using Deleuze.Shared.Swagger;

var builder = WebApplication.CreateBuilder(args); 

builder.Logging.ClearProviders(); 
builder.Logging.AddConsole(); 
builder.Logging.SetMinimumLevel(LogLevel.Information); 
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning); 

// 💡 1. CORS ポリシーの登録を追加
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// HttpContextAccessor と ITenantProvider の DI 登録
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, JwtTenantProvider>();

// DbContext (PostgreSQL)  
builder.Services.AddDbContext<DriveDbContext>((sp, options) => {
    var tenantProvider = sp.GetRequiredService<ITenantProvider>();
    var tenantId = tenantProvider.GetTenantId();

    var baseConnectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                               ?? "Host=deleuze-db;Database=deleuze_drive;Username=postgres;Password=postgres";

    // テナント ID に応じたスキーマ検索パスを設定
    var connectionString = $"{baseConnectionString};SearchPath={tenantId},public";
    options.UseNpgsql(connectionString); 
    options.ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>(); 
});

// --------------------------------------------------
// Tenant Schema Provisioning / Migration
// --------------------------------------------------

builder.Services.AddScoped<TenantSchemaMigrationRunner>(sp =>
{
    var connectionString =
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=deleuze-db;Database=deleuze_drive;Username=postgres;Password=postgres";

    return new TenantSchemaMigrationRunner(connectionString);
});

builder.Services.AddScoped<ITenantSchemaProvisioner>(
    sp => sp.GetRequiredService<TenantSchemaMigrationRunner>());

builder.Services.AddScoped<ITenantSchemaMigrator>(
    sp => sp.GetRequiredService<TenantSchemaMigrationRunner>());

// Drive固有の薄いサービス
builder.Services.AddScoped<
    ITenantProvisioningService,
    TenantProvisioningService>();

builder.Services.AddScoped<
    ITenantMigrationService,
    TenantMigrationService>();
// AWS S3 Storage Service
builder.Services.AddSingleton<IAmazonS3>(_ => 
    new AmazonS3Client(
        new Amazon.Runtime.EnvironmentVariablesAWSCredentials(), 
        Amazon.RegionEndpoint.APNortheast1
    )); 
builder.Services.AddScoped<IStorageService, S3StorageService>(); 

// deleuze-auth 
// --------------------------------------------------
// DeleuzeAuth URL
// --------------------------------------------------

// Docker内部からDeleuzeAuthへアクセスするURL
var authInternalUrl =
    builder.Configuration["AUTH_INTERNAL_URL"]
    ?? "http://192.168.8.112:5001/api/auth";

// JWTのAuthority
// Discovery / JWKS取得に使用
var authAuthority =
    builder.Configuration["AUTH_AUTHORITY"]
    ?? authInternalUrl;

Console.WriteLine(
    $"[DeleuzeDrive] Auth Internal URL = {authInternalUrl}");

Console.WriteLine(
    $"[DeleuzeDrive] Auth Authority = {authAuthority}");

// deleuze-auth API HttpClient  
builder.Services.AddHttpClient("AuthService", client =>
{
    var baseUrl =
        authInternalUrl.EndsWith("/")
            ? authInternalUrl
            : authInternalUrl + "/";

    client.BaseAddress = new Uri(baseUrl);
});

// SmartAuth (PolicyScheme) で統合管理
builder.Services.AddAuthentication(options => {
    options.DefaultScheme = "SmartAuth"; 
    options.DefaultChallengeScheme = "SmartAuth"; 
})
.AddPolicyScheme("SmartAuth", "JWT or ApiKey", options => {
    options.ForwardDefaultSelector = context => 
    { 
        if (context.Request.Headers.ContainsKey("X-Api-Key")) 
        { 
            return "ApiKey"; 
        } 
        return JwtBearerDefaults.AuthenticationScheme; 
    }; 
})
// Deleuze.Shared ApiKeyAuthenticationHandler  

.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>( 
    "ApiKey", _ => { }) 
.AddJwtBearer(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        options.Authority = authAuthority;

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
                    $"[DeleuzeDrive][JWT] Authentication FAILED: {context.Exception}");

                return Task.CompletedTask;
            },

            OnTokenValidated = context =>
            {
                Console.WriteLine(
                    "[DeleuzeDrive][JWT] Token VALIDATED");

                foreach (var claim in context.Principal!.Claims)
                {
                    Console.WriteLine(
                        $"[DeleuzeDrive][JWT] Claim: {claim.Type} = {claim.Value}");
                }

                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                Console.WriteLine(
                    $"[DeleuzeDrive][JWT] Challenge: {context.Error} / {context.ErrorDescription}");

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddControllers(); 
builder.Services.AddEndpointsApiExplorer(); 

// Swagger Generator の設定
builder.Services.AddSwaggerGen(c => {
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-drive API", Version = "v1" }); 
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme 
    { 
        Description = "deleuze-auth JWT",
        Name = "Authorization", 
        In = ParameterLocation.Header, 
        Type = SecuritySchemeType.Http, 
        Scheme = "Bearer" 
    }); 
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme 
    { 
        Description = "deleuze-mng X-Api-Key",
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

// Forwarded Headers
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix; 
    options.KnownNetworks.Clear(); 
    options.KnownProxies.Clear(); 
});

builder.Services.AddHealthChecks().AddDbContextCheck<DriveDbContext>("Database"); 

var app = builder.Build(); 

app.UseForwardedHeaders(); 

// 💡 2. ミドルウェア パイプラインの先頭で UseCors を適用
app.UseCors();

// 共通 Swagger 拡張呼び出し
app.UseDeleuzeSwagger(app.Environment, builder.Configuration, ApiRoutes.Drive.Base, "deleuze-drive API");

app.UseAuthentication(); 
app.UseAuthorization(); 

app.MapControllers(); 
app.MapHealthChecks("/health"); 

app.Run();