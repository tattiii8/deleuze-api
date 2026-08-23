using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using DeleuzeMng.Data;
using DeleuzeMng.Services;
using Deleuze.Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// HttpClient & Services
builder.Services.AddHttpClient<IServiceProvisioningClient, GenericHttpProvisioningClient>();
builder.Services.AddScoped<ITenantManagementService, TenantManagementService>();

builder.Services.AddHttpContextAccessor();

// JWT 認証
var jwtSecret = builder.Configuration["JWT_SECRET"] ?? "your-default-jwt-secret-key-at-least-32-bytes";
var key = System.Text.Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "deleuze-mng API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
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
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedPrefix;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// 1. リバースプロキシのヘッダー解析を最優先
app.UseForwardedHeaders();

// 2. 組み込みの機能でパスのプレフィックスを剥離
app.UsePathBase("/api/mng");

// 3. Swagger のパスは相対パス
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("v1/swagger.json", "deleuze-mng API v1");
        c.RoutePrefix = "swagger";
    });
}

// 4. ここでルーティングを確定させる (MapControllers などの前であること)
app.UseRouting();

// 5. 認証・認可
app.UseAuthentication();
app.UseAuthorization();

// 6. エンドポイントのマッピング
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();