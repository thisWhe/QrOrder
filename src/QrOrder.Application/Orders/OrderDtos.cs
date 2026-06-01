using QrOrder.Domain.Enums;

namespace QrOrder.Application.Orders
{
    public record CreateOrderItemRequest(Guid ProductId, int Quantity, string? Note);

    public record CreateOrderRequest(
        string TenantSlug,
        string SessionToken,
        List<CreateOrderItemRequest> Items,
        string? CustomerNote);

    public record OrderItemDto(string ProductNameSnapshot, int Quantity, string? ItemNote);

    public record PublicOrderStatusDto(
        Guid Id,
        Guid TenantId,
        string Status,
        int TableNumber,
        decimal TotalAmount,
        DateTimeOffset CreatedAt,
        List<OrderItemDto> Items);

    public record OrderCreatedResult(
        Guid OrderId,
        Guid TenantId,
        decimal Total,
        int TableNumber,
        string Status,
        DateTimeOffset CreatedAt);

    public record StaffOrderDto(
        Guid Id,
        OrderStatus Status,
        int TableNumber,
        decimal TotalAmount,
        DateTimeOffset CreatedAt,
        string? CustomerNote,
        List<OrderItemDto> Items);

    public record StaffOrderStatusChangedResult(Guid OrderId, string Status, int TableNumber);
}
