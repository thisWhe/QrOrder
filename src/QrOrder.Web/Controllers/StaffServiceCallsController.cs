using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using QrOrder.Application.ServiceCalls;
using QrOrder.Web.Realtime;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/service-calls")]
    [Authorize(Roles = "Admin,Service")]
    public class StaffServiceCallsController : ControllerBase
    {
        private readonly IStaffServiceCallService _serviceCalls;
        private readonly IHubContext<StaffOrdersHub> _staffHub;

        public StaffServiceCallsController(
            IStaffServiceCallService serviceCalls,
            IHubContext<StaffOrdersHub> staffHub)
        {
            _serviceCalls = serviceCalls;
            _staffHub = staffHub;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool activeOnly = true)
        {
            return Ok(await _serviceCalls.ListAsync(activeOnly));
        }

        [HttpPatch("{id:guid}/complete")]
        public async Task<IActionResult> Complete(Guid id)
        {
            var call = await _serviceCalls.CompleteAsync(id);
            if (call == null) return NotFound();

            var tenantId = User.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                await _staffHub.Clients.Group($"tenant:{tenantId}").SendAsync("ServiceCallCompleted", call);
            }

            return Ok(call);
        }
    }
}
