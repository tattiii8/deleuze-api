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

                // 1. JSON (swagger.json) の配信エンドポイントを api/{service}/swagger 配下に合わせる
                app.UseSwagger(c =>
                {
                    c.RouteTemplate = $"{cleanBase}/swagger/{{documentName}}/swagger.json";

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

                // 2. Swagger UI (HTML画面) の設定
                app.UseSwaggerUI(c =>
                {
                    // JSON の相対パス (index.html から見た同階層の v1/swagger.json)
                    c.SwaggerEndpoint("v1/swagger.json", $"{title} v1");
                    
                    // UI のアクセス URL (例: api/auth/swagger)
                    c.RoutePrefix = $"{cleanBase}/swagger";
                });
            }

            return app;
        }
    }
}