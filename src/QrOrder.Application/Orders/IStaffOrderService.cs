using QrOrder.Domain.Enums;

namespace QrOrder.Application.Orders
{
    public interface IStaffOrderService
    {
        Task<List<StaffOrderDto>> ListAsync(
            OrderStatus? status,
            bool includeClosed,
            CancellationToken cancellationToken = default);

        Task<StaffOrderStatusChangedResult?> UpdateStatusAsync(
            Guid orderId,
            OrderStatus status,
            CancellationToken cancellationToken = default);
    }
}
