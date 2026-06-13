using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Orders;
using QrOrder.Domain.Enums;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Infrastructure.Services
{
    public class StaffOrderService : IStaffOrderService
    {
        private readonly AppDbContext _db;

        public StaffOrderService(AppDbContext db) => _db = db;

        public async Task<List<StaffOrderDto>> ListAsync(
            OrderStatus? status,
            bool includeClosed,
            CancellationToken cancellationToken = default)
        {
            var query = _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .OrderByDescending(o => o.CreatedAt)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(o => o.Status == status.Value);
            }
            else if (!includeClosed)
            {
                query = query.Where(o =>
                    o.Status == OrderStatus.New ||
                    o.Status == OrderStatus.Preparing ||
                    o.Status == OrderStatus.Ready);
            }

            return await query
                .Take(200)
                .Select(o => new StaffOrderDto(
                    o.Id,
                    o.Status,
                    o.Table.DisplayNumber,
                    o.TotalAmount,
                    o.CreatedAt,
                    o.CustomerNote,
                    o.Items.Select(i => new OrderItemDto(i.ProductNameSnapshot, i.Quantity, i.ItemNote)).ToList()))
                .ToListAsync(cancellationToken);
        }

        public async Task<StaffOrderStatusChangedResult?> UpdateStatusAsync(
            Guid orderId,
            OrderStatus status,
            CancellationToken cancellationToken = default)
        {
            var order = await _db.Orders
                .Include(o => o.Table)
                .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order == null) return null;

            if (!StaffOrderPolicy.IsValidTransition(order.Status, status))
                throw new InvalidOperationException("Invalid order status transition.");

            order.Status = status;
            await _db.SaveChangesAsync(cancellationToken);

            return new StaffOrderStatusChangedResult(order.Id, order.Status.ToString(), order.Table.DisplayNumber);
        }

    }
}
