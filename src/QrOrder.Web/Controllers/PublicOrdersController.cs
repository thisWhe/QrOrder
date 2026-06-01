using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using QrOrder.Application.Orders;
using QrOrder.Web.Realtime;

namespace QrOrder.Web.Controllers
{
    [Route("public/orders")]
    [ApiController]
    public class PublicOrdersController : ControllerBase
    {
        private readonly IPublicOrderService _orders;
        private readonly IHubContext<StaffOrdersHub> _staffHub;
        private readonly IHubContext<PublicOrdersHub> _publicHub;

        public PublicOrdersController(
            IPublicOrderService orders,
            IHubContext<StaffOrdersHub> staffHub,
            IHubContext<PublicOrdersHub> publicHub)
        {
            _orders = orders;
            _staffHub = staffHub;
            _publicHub = publicHub;
        }

        public record CreateOrderItem(Guid ProductId, int Quantity, string? Note);
        public record CreateOrderRequest(string TenantSlug, string SessionToken, List<CreateOrderItem> Items, string? CustomerNote);
        public record CancelOrderRequest(string SessionToken);

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
        {
            try
            {
                var result = await _orders.CreateAsync(new QrOrder.Application.Orders.CreateOrderRequest(
                    req.TenantSlug,
                    req.SessionToken,
                    req.Items.Select(i => new CreateOrderItemRequest(i.ProductId, i.Quantity, i.Note)).ToList(),
                    req.CustomerNote));

                await _staffHub.Clients.Group($"tenant:{result.TenantId}")
                    .SendAsync("OrderCreated", new
                    {
                        orderId = result.OrderId,
                        tableNumber = result.TableNumber,
                        total = result.Total,
                        status = result.Status,
                        createdAt = result.CreatedAt
                    });

                return Ok(new { orderId = result.OrderId, total = result.Total });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{tenantSlug}/{orderId:guid}")]
        public async Task<IActionResult> GetStatus(string tenantSlug, Guid orderId, [FromQuery] string sessionToken)
        {
            var status = await _orders.GetStatusAsync(tenantSlug, orderId, sessionToken);
            return status == null ? NotFound() : Ok(status);
        }

        [HttpPost("{tenantSlug}/{orderId:guid}/cancel")]
        public async Task<IActionResult> Cancel(string tenantSlug, Guid orderId, [FromBody] CancelOrderRequest req)
        {
            try
            {
                var status = await _orders.CancelAsync(tenantSlug, orderId, req.SessionToken);
                if (status == null) return NotFound();

                var payload = new
                {
                    orderId = status.Id,
                    status = status.Status,
                    tableNumber = status.TableNumber
                };

                await _staffHub.Clients.Group($"tenant:{status.TenantId}").SendAsync("OrderStatusChanged", payload);
                await _publicHub.Clients.Group($"order:{status.Id}").SendAsync("OrderStatusChanged", payload);

                return Ok(status);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
        }
    }
}
