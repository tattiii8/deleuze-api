using System;

namespace Deleuze.Shared.Infrastructure;

public static class TenantSchemaNaming
{
    public static string GetSchemaName(
        string serviceName,
        string tenantId)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException(
                "Service name is required.",
                nameof(serviceName));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        ValidateName(serviceName, nameof(serviceName));
        ValidateName(tenantId, nameof(tenantId));

        return $"{serviceName}_{tenantId}";
    }

    private static void ValidateName(
        string value,
        string parameterName)
    {
        if (!value.All(c =>
                char.IsLetterOrDigit(c) ||
                c == '_' ||
                c == '-'))
        {
            throw new ArgumentException(
                $"Invalid name: {value}",
                parameterName);
        }
    }
}