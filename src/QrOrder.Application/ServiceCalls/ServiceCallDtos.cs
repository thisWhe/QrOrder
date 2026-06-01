using QrOrder.Domain.Enums;

namespace QrOrder.Application.ServiceCalls
{
    public record CreateServiceCallRequest(
        string TenantSlug,
        string SessionToken,
        string? Message);

    public record ServiceCallDto(
        Guid Id,
        Guid TenantId,
        ServiceCallStatus Status,
        int TableNumber,
        string? Message,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt);
}
