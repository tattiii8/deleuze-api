namespace DeleuzeMng.Models;

public record TenantManagementDto(
    string Id,
    string Name,
    string Status,
    DateTime CreatedAt
);