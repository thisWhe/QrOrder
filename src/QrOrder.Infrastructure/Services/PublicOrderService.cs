using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Application.Orders;
using QrOrder.Application.Security;
using QrOrder.Domain.Entities;
using QrOrder.Domain.Enums;
using QrOrder.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace QrOrder.Infrastructure.Services
{
    public class PublicOrderService : IPublicOrderService
    {
        private const int MaxQuantityPerProduct = 50;
        private const int MaxCustomerNoteLength = 500;
        private const int MaxItemNoteLength = 250;

        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;
        private readonly IBusinessHoursService _businessHours;

        public PublicOrderService(
            AppDbContext db,
            ITenantContext tenantContext,
            IBusinessHoursService businessHours)
        {
            _db = db;
            _tenantContext = tenantContext;
            _businessHours = businessHours;
        }

        public async Task<OrderCreatedResult> CreateAsync(
            CreateOrderRequest request,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == request.TenantSlug && t.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("Tenant not found.");

            _tenantContext.TenantId = tenant.Id;

            if (string.IsNullOrWhiteSpace(request.SessionToken))
                throw new UnauthorizedAccessException("Session token is required.");

            if (request.RequestId == Guid.Empty)
                throw new InvalidOperationException("RequestId is required.");

            if (request.Items.Count == 0)
                throw new InvalidOperationException("Empty order.");

            if (!string.IsNullOrWhiteSpace(request.CustomerNote) && request.CustomerNote.Length > MaxCustomerNoteLength)
                throw new InvalidOperationException("Customer note is too long.");

            if (request.Items.Any(i => i.Quantity <= 0))
                throw new InvalidOperationException("Quantity must be > 0.");

            if (request.Items.Any(i => !string.IsNullOrWhiteSpace(i.Note) && i.Note.Length > MaxItemNoteLength))
                throw new InvalidOperationException("Item note is too long.");

            var normalizedItems = request.Items
                .GroupBy(i => i.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(x => x.Quantity),
                    Note = string.Join(" | ", g.Select(x => x.Note?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)))
                })
                .ToList();

            if (normalizedItems.Any(i => i.Quantity > MaxQuantityPerProduct))
                throw new InvalidOperationException("Quantity is too high.");

            var tokenHash = SessionTokenUtil.Sha256(request.SessionToken);
            var session = await _db.TableSessions
                .Include(s => s.Table)
                .SingleOrDefaultAsync(s => s.SessionTokenHash == tokenHash && s.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

            if (session == null)
                throw new UnauthorizedAccessException("Invalid or expired session.");

            if (!session.Table.IsActive)
                throw new UnauthorizedAccessException("Table is inactive.");

            var existingOrder = await FindExistingOrderAsync(
                request.RequestId,
                cancellationToken);
            if (existingOrder != null)
            {
                EnsureRequestBelongsToTable(existingOrder, session.TableId);
                return ToCreatedResult(existingOrder, false);
            }

            if (!tenant.IsOrderingEnabled)
                throw new InvalidOperationException("Online ordering is currently disabled.");

            var businessStatus = await _businessHours.EvaluateAsync(
                tenant.Id,
                tenant.TimeZoneId,
                cancellationToken);
            if (!businessStatus.IsOpen)
                throw new InvalidOperationException("Business is currently closed.");

            var productIds = normalizedItems.Select(i => i.ProductId).ToList();
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id) && p.IsActive && p.IsAvailable)
                .ToListAsync(cancellationToken);

            if (products.Count != productIds.Count)
                throw new InvalidOperationException("Some products are invalid, inactive, or unavailable.");

            var order = new Order
            {
                TenantId = tenant.Id,
                ClientRequestId = request.RequestId,
                TableId = session.TableId,
                TableSessionId = session.Id,
                Status = OrderStatus.New,
                CustomerNote = request.CustomerNote?.Trim()
            };

            foreach (var item in normalizedItems)
            {
                var product = products.Single(p => p.Id == item.ProductId);
                order.Items.Add(new OrderItem
                {
                    ProductId = product.Id,
                    ProductNameSnapshot = product.Name,
                    UnitPriceSnapshot = product.Price,
                    Quantity = item.Quantity,
                    ItemNote = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note
                });
            }

            order.TotalAmount = order.Items.Sum(i => i.UnitPriceSnapshot * i.Quantity);

            _db.Orders.Add(order);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException error) when (IsUniqueConstraintViolation(error))
            {
                foreach (var item in order.Items)
                    _db.Entry(item).State = EntityState.Detached;
                _db.Entry(order).State = EntityState.Detached;

                existingOrder = await FindExistingOrderAsync(
                    request.RequestId,
                    cancellationToken);
                if (existingOrder != null)
                {
                    EnsureRequestBelongsToTable(existingOrder, session.TableId);
                    return ToCreatedResult(existingOrder, false);
                }

                throw;
            }

            order.Table = session.Table;
            return ToCreatedResult(order, true);
        }

        private async Task<Order?> FindExistingOrderAsync(
            Guid requestId,
            CancellationToken cancellationToken)
        {
            return await _db.Orders
                .AsNoTracking()
                .Include(x => x.Table)
                .SingleOrDefaultAsync(
                    x => x.ClientRequestId == requestId,
                    cancellationToken);
        }

        private static void EnsureRequestBelongsToTable(Order order, Guid tableId)
        {
            if (order.TableId != tableId)
                throw new InvalidOperationException("RequestId was already used for another table.");
        }

        private static OrderCreatedResult ToCreatedResult(Order order, bool isNew) =>
            new(
                order.Id,
                order.TenantId,
                order.TotalAmount,
                order.Table.DisplayNumber,
                order.Status.ToString(),
                order.CreatedAt,
                isNew);

        private static bool IsUniqueConstraintViolation(DbUpdateException error) =>
            error.InnerException is SqlException { Number: 2601 or 2627 };

        public async Task<PublicOrderStatusDto?> GetStatusAsync(
            string tenantSlug,
            Guid orderId,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive, cancellationToken);

            if (tenant == null) return null;

            _tenantContext.TenantId = tenant.Id;

            var tokenHash = SessionTokenUtil.Sha256(sessionToken);
            var order = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .Include(o => o.TableSession)
                .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order == null || order.TableSession.SessionTokenHash != tokenHash)
                return null;

            return new PublicOrderStatusDto(
                order.Id,
                tenant.Id,
                order.Status.ToString(),
                order.Table.DisplayNumber,
                order.TotalAmount,
                order.CreatedAt,
                order.Items.Select(i => new OrderItemDto(i.ProductNameSnapshot, i.Quantity, i.ItemNote)).ToList());
        }

        public async Task<PublicOrderStatusDto?> CancelAsync(
            string tenantSlug,
            Guid orderId,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive, cancellationToken);

            if (tenant == null) return null;

            _tenantContext.TenantId = tenant.Id;

            if (string.IsNullOrWhiteSpace(sessionToken))
                throw new UnauthorizedAccessException("Session token is required.");

            var tokenHash = SessionTokenUtil.Sha256(sessionToken);
            var order = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .Include(o => o.TableSession)
                .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order == null || order.TableSession.SessionTokenHash != tokenHash)
                return null;

            if (order.Status != OrderStatus.New)
                throw new InvalidOperationException("Only new orders can be canceled by the customer.");

            order.Status = OrderStatus.Canceled;
            await _db.SaveChangesAsync(cancellationToken);

            return new PublicOrderStatusDto(
                order.Id,
                tenant.Id,
                order.Status.ToString(),
                order.Table.DisplayNumber,
                order.TotalAmount,
                order.CreatedAt,
                order.Items.Select(i => new OrderItemDto(i.ProductNameSnapshot, i.Quantity, i.ItemNote)).ToList());

        }
    }
}
