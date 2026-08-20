using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using DeleuzeApp.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DeleuzeApp.Services;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var canConnect = await _db.Database.CanConnectAsync(cancellationToken);

        return canConnect 
            ? HealthCheckResult.Healthy("Database is connected.") 
            : HealthCheckResult.Unhealthy("Database connection failed.");
    }
}