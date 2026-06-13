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
        private readonly ILogger<StaffServiceCallsController> _logger;

        public StaffServiceCallsController(
            IStaffServiceCallService serviceCalls,
            IHubContext<StaffOrdersHub> staffHub,
            ILogger<StaffServiceCallsController> logger)
        {
            _serviceCalls = serviceCalls;
            _staffHub = staffHub;
            _logger = logger;
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
                try
                {
                    await _staffHub.Clients.Group($"tenant:{tenantId}").SendAsync("ServiceCallCompleted", call);
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Service call {ServiceCallId} was completed but its SignalR notification failed.", call.Id);
                }
            }

            return Ok(call);
        }
    }
}
