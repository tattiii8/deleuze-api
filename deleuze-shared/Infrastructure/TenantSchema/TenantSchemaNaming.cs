using System;
using System.Linq;

namespace Deleuze.Shared.Infrastructure;

public static class TenantSchemaNaming
{
    public static string GetSchemaName(
        string serviceName,
        string tenantId)
    {
        ValidatePart(serviceName, nameof(serviceName));
        ValidatePart(tenantId, nameof(tenantId));

        return $"{serviceName}_{tenantId}";
    }

    private static void ValidatePart(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{parameterName} is required.",
                parameterName);
        }

        if (!value.All(c =>
                char.IsLetterOrDigit(c) ||
                c == '_' ||
                c == '-'))
        {
            throw new ArgumentException(
                $"Invalid {parameterName}: {value}",
                parameterName);
        }
    }
}