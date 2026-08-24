using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Deleuze.Shared.Infrastructure;

namespace DeleuzeMng.Services
{
    public interface IServiceProvisioningCoordinator
    {
        Task ProvisionAsync(string tenantId, string serviceKey);
        Task DeprovisionAsync(string tenantId, string serviceKey);
        Task DeprovisionAllAsync(string tenantId);
        Task MigrateAsync(string tenantId, string serviceKey);
        Task<bool> MigrateAllAsync(string tenantId);
        IEnumerable<string> RegisteredServiceKeys { get; }
    }

    public class ServiceProvisioningCoordinator : IServiceProvisioningCoordinator
    {
        private readonly Dictionary<string, IServiceProvisioningClient> _clients;

        public ServiceProvisioningCoordinator(IEnumerable<IServiceProvisioningClient> clients)
        {
            _clients = clients.ToDictionary(c => c.ServiceKey, StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<string> RegisteredServiceKeys => _clients.Keys;

        public async Task ProvisionAsync(string tenantId, string serviceKey)
        {
            var client = GetClientOrThrow(serviceKey);
            await client.ProvisionTenantAsync(tenantId);
        }

        public async Task DeprovisionAsync(string tenantId, string serviceKey)
        {
            if (_clients.TryGetValue(serviceKey, out var client))
            {
                await client.DeprovisionTenantAsync(tenantId);
            }
        }

        public async Task DeprovisionAllAsync(string tenantId)
        {
            foreach (var client in _clients.Values)
            {
                try 
                { 
                    await client.DeprovisionTenantAsync(tenantId); 
                } 
                catch 
                { 
                    // 全削除処理時の個別の例外は後続処理を阻害しないよう捕捉
                }
            }
        }

        public async Task MigrateAsync(string tenantId, string serviceKey)
        {
            var client = GetClientOrThrow(serviceKey);
            await client.MigrateTenantAsync(tenantId);
        }

        public async Task<bool> MigrateAllAsync(string tenantId)
        {
            bool success = true;
            foreach (var client in _clients.Values)
            {
                try 
                { 
                    await client.MigrateTenantAsync(tenantId); 
                }
                catch 
                { 
                    success = false; 
                }
            }
            return success;
        }

        private IServiceProvisioningClient GetClientOrThrow(string serviceKey)
        {
            if (!_clients.TryGetValue(serviceKey, out var client))
            {
                throw new ArgumentException($"未対応のサービスキーです: {serviceKey}");
            }
            return client;
        }
    }
}