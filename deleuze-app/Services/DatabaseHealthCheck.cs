using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using deleuze_app.Models;
using System.Threading;
using System.Threading.Tasks;

namespace deleuze_app.Services;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // 非同期で安全にデータベースの接続確認を行う
        var canConnect = await _db.Database.CanConnectAsync(cancellationToken);

        return canConnect 
            ? HealthCheckResult.Healthy("Database is connected.") 
            : HealthCheckResult.Unhealthy("Database connection failed.");
    }
}