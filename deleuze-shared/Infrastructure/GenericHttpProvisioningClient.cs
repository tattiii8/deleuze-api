using System.Net.Http;

namespace Deleuze.Shared.Infrastructure;

public class GenericHttpProvisioningClient : IServiceProvisioningClient
{
    private readonly HttpClient _httpClient;
    public string ServiceName { get; }

    public GenericHttpProvisioningClient(string serviceName, HttpClient httpClient)
    {
        ServiceName = serviceName;
        _httpClient = httpClient;
    }

    public async Task<bool> ProvisionTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"internal/tenants/{tenantId}", null, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeprovisionTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"internal/tenants/{tenantId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}