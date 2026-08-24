using Deleuze.Shared.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Deleuze.Shared.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddSharedAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, JwtTenantProvider>();
        return services;
    }
}