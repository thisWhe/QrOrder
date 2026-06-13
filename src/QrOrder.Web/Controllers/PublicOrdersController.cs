using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
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
        private readonly ILogger<PublicOrdersController> _logger;

        public PublicOrdersController(
            IPublicOrderService orders,
            IHubContext<StaffOrdersHub> staffHub,
            IHubContext<PublicOrdersHub> publicHub,
            ILogger<PublicOrdersController> logger)
        {
            _orders = orders;
            _staffHub = staffHub;
            _publicHub = publicHub;
            _logger = logger;
        }

        public record CreateOrderItem(Guid ProductId, int Quantity, string? Note);
        public record CreateOrderRequest(string TenantSlug, string SessionToken, Guid RequestId, List<CreateOrderItem> Items, string? CustomerNote);
        public record CancelOrderRequest(string SessionToken);

        [HttpPost]
        [EnableRateLimiting("public-write")]
        public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
        {
            try
            {
                var result = await _orders.CreateAsync(new QrOrder.Application.Orders.CreateOrderRequest(
                    req.TenantSlug,
                    req.SessionToken,
                    req.RequestId,
                    req.Items.Select(i => new CreateOrderItemRequest(i.ProductId, i.Quantity, i.Note)).ToList(),
                    req.CustomerNote));

                if (result.IsNew)
                {
                    _logger.LogInformation(
                        "Order {OrderId} created for tenant {TenantId}, table {TableNumber}, request {RequestId}.",
                        result.OrderId,
                        result.TenantId,
                        result.TableNumber,
                        req.RequestId);

                    try
                    {
                        await _staffHub.Clients.Group($"tenant:{result.TenantId}")
                            .SendAsync("OrderCreated", new
                            {
                                orderId = result.OrderId,
                                tableNumber = result.TableNumber,
                                total = result.Total,
                                status = result.Status,
                                createdAt = result.CreatedAt
                            });
                    }
                    catch (Exception error)
                    {
                        _logger.LogWarning(
                            error,
                            "Order {OrderId} was saved but the SignalR notification failed for tenant {TenantId}.",
                            result.OrderId,
                            result.TenantId);
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "Duplicate order request {RequestId} returned existing order {OrderId} for tenant {TenantId}.",
                        req.RequestId,
                        result.OrderId,
                        result.TenantId);
                }

                return Ok(new
                {
                    orderId = result.OrderId,
                    total = result.Total,
                    duplicate = !result.IsNew
                });
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
        [EnableRateLimiting("public-write")]
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

                try
                {
                    await _staffHub.Clients.Group($"tenant:{status.TenantId}").SendAsync("OrderStatusChanged", payload);
                    await _publicHub.Clients.Group($"order:{status.Id}").SendAsync("OrderStatusChanged", payload);
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Canceled order {OrderId} was saved but its SignalR notification failed.", status.Id);
                }

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
