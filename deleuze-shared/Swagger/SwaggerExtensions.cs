using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Deleuze.Shared.Swagger
{
    public static class SwaggerExtensions
    {
        public static IApplicationBuilder UseDeleuzeSwagger(
            this IApplicationBuilder app,
            IWebHostEnvironment env,
            IConfiguration configuration,
            string serviceBaseRoute,
            string title)
        {
            if (env.IsDevelopment() || configuration.GetValue<bool>("EnableSwagger", true))
            {
                var cleanBase = serviceBaseRoute.Trim('/');

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
                    // 💡 先頭の '/' を外し、相対パス指定にする (または $"/{cleanBase}/swagger/v1/swagger.json")
                    // これにより Swagger UI と同じディレクトリ配下の v1/swagger.json を正しく取得できます
                    c.SwaggerEndpoint("v1/swagger.json", $"{title} v1");
                    
                    c.RoutePrefix = $"{cleanBase}/swagger";
                });
            }

            return app;
        }
    }
}