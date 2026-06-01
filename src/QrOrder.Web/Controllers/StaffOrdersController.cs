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
    [Authorize]
    public class StaffOrdersController : ControllerBase
    {
        private readonly IStaffOrderService _orders;
        private readonly IHubContext<StaffOrdersHub> _staffHub;
        private readonly IHubContext<PublicOrdersHub> _publicHub;

        public StaffOrdersController(
            IStaffOrderService orders,
            IHubContext<StaffOrdersHub> staffHub,
            IHubContext<PublicOrdersHub> publicHub)
        {
            _orders = orders;
            _staffHub = staffHub;
            _publicHub = publicHub;
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

            var tenantId = User.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrWhiteSpace(tenantId))
                await _staffHub.Clients.Group($"tenant:{tenantId}").SendAsync("OrderStatusChanged", payload);

            await _publicHub.Clients.Group($"order:{result.OrderId}").SendAsync("OrderStatusChanged", payload);

            return NoContent();
        }
    }
}
