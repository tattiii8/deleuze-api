using System;
using System.Text;
using Deleuze.Shared.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Deleuze.Shared.Authentication
{
    public static class AuthenticationExtensions
    {
        public static IServiceCollection AddDeleuzeAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var jwtSecret = configuration["JWT_SECRET"] ?? "YourSuperSecretKeyHereWhichIsAtLeast32BytesLong!";
            var key = Encoding.UTF8.GetBytes(jwtSecret);

            services.AddHttpContextAccessor();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "SmartAuth";
                options.DefaultChallengeScheme = "SmartAuth";
            })
            .AddPolicyScheme("SmartAuth", "Bearer or ApiKey", options =>
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
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", options => { });

            services.AddScoped<ITenantProvider, JwtTenantProvider>();

            return services;
        }
    }
}