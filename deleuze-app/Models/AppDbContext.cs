using DeleuzeApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace DeleuzeApp.Models;

public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var tenantId = _tenantProvider.GetTenantId();
        // 接続ごとに search_path を自動的に発行するインターセプターを追加
        optionsBuilder.AddInterceptors(new TenantConnectionInterceptor(tenantId));
        base.OnConfiguring(optionsBuilder);
    }
}

public class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly string _tenantId;

    public TenantConnectionInterceptor(string tenantId)
    {
        _tenantId = tenantId;
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await SetSearchPathAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetSearchPath(connection);
        base.ConnectionOpened(connection, eventData);
    }

    private async Task SetSearchPathAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        // SQLインジェクションを防ぐため、tenantId のフォーマット検証を行うのが安全です
        command.CommandText = $"SET search_path TO \"{_tenantId}\";";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void SetSearchPath(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{_tenantId}\";";
        command.ExecuteNonQuery();
    }
}