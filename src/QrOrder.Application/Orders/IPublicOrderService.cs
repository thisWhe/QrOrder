namespace QrOrder.Application.Orders
{
    public interface IPublicOrderService
    {
        Task<OrderCreatedResult> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

        Task<PublicOrderStatusDto?> GetStatusAsync(
            string tenantSlug,
            Guid orderId,
            string sessionToken,
            CancellationToken cancellationToken = default);

        Task<PublicOrderStatusDto?> CancelAsync(
            string tenantSlug,
            Guid orderId,
            string sessionToken,
            CancellationToken cancellationToken = default);
    }
}
