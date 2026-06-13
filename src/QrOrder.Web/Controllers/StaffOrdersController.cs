using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using QrOrder.Application.Orders;
using QrOrder.Domain.Enums;
using QrOrder.Web.Realtime;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/orders")]
    [Authorize(Roles = "Admin,Kitchen,Service")]
    public class StaffOrdersController : ControllerBase
    {
        private readonly IStaffOrderService _orders;
        private readonly IHubContext<StaffOrdersHub> _staffHub;
        private readonly IHubContext<PublicOrdersHub> _publicHub;
        private readonly ILogger<StaffOrdersController> _logger;

        public StaffOrdersController(
            IStaffOrderService orders,
            IHubContext<StaffOrdersHub> staffHub,
            IHubContext<PublicOrdersHub> publicHub,
            ILogger<StaffOrdersController> logger)
        {
            _orders = orders;
            _staffHub = staffHub;
            _publicHub = publicHub;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] OrderStatus? status, [FromQuery] bool includeClosed = false)
        {
            return Ok(await _orders.ListAsync(status, includeClosed));
        }

        public record UpdateStatusRequest(OrderStatus Status);

        [HttpPatch("{id:guid}/status")]
        [Authorize(Roles = "Admin,Kitchen,Service")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateStatusRequest req)
        {
            var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(claim => claim.Value);
            if (!StaffOrderPolicy.CanRoleSetStatus(roles, req.Status))
                return Forbid();

            StaffOrderStatusChangedResult? result;
            try
            {
                result = await _orders.UpdateStatusAsync(id, req.Status);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            if (result == null) return NotFound();

            var payload = new
            {
                orderId = result.OrderId,
                status = result.Status,
                tableNumber = result.TableNumber
            };

            try
            {
                var tenantId = User.FindFirst("tenant_id")?.Value;
                if (!string.IsNullOrWhiteSpace(tenantId))
                    await _staffHub.Clients.Group($"tenant:{tenantId}").SendAsync("OrderStatusChanged", payload);

                await _publicHub.Clients.Group($"order:{result.OrderId}").SendAsync("OrderStatusChanged", payload);
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "Order {OrderId} status was saved but its SignalR notification failed.", result.OrderId);
            }

            return NoContent();
        }

    }
}
