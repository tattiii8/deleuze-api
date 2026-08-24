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
using Deleuze.Shared.MultiTenancy; 

var builder = WebApplication.CreateBuilder(args); 

builder.Logging.ClearProviders(); 
builder.Logging.AddConsole(); 
builder.Logging.SetMinimumLevel(LogLevel.Information); 
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning); 

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

// SQL Migration サービス
builder.Services.AddScoped<ITenantMigrationService, TenantMigrationService>(); 

// AWS S3 Storage Service
builder.Services.AddSingleton<IAmazonS3>(_ => 
    new AmazonS3Client(
        new Amazon.Runtime.EnvironmentVariablesAWSCredentials(), 
        Amazon.RegionEndpoint.APNortheast1
    )); 
builder.Services.AddScoped<IStorageService, S3StorageService>(); 

// deleuze-auth  
var authAuthority = builder.Configuration["AUTH_INTERNAL_URL"] 
    ?? "http://192.168.8.112:5001/api/auth"; 

// deleuze-auth API HttpClient  
builder.Services.AddHttpClient("AuthService", client => {
    var baseUrl = authAuthority.EndsWith("/") ? authAuthority : authAuthority + "/"; 
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
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options => {
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

// Swagger UI  
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