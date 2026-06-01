using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using QrOrder.Application.ServiceCalls;
using QrOrder.Web.Realtime;

namespace QrOrder.Web.Controllers
{
    [Route("public/service-calls")]
    [ApiController]
    public class PublicServiceCallsController : ControllerBase
    {
        private readonly IPublicServiceCallService _serviceCalls;
        private readonly IHubContext<StaffOrdersHub> _staffHub;

        public PublicServiceCallsController(
            IPublicServiceCallService serviceCalls,
            IHubContext<StaffOrdersHub> staffHub)
        {
            _serviceCalls = serviceCalls;
            _staffHub = staffHub;
        }

        public record CreateServiceCallRequest(string TenantSlug, string SessionToken, string? Message);

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateServiceCallRequest req)
        {
            try
            {
                var call = await _serviceCalls.CreateAsync(new QrOrder.Application.ServiceCalls.CreateServiceCallRequest(
                    req.TenantSlug,
                    req.SessionToken,
                    req.Message));

                await _staffHub.Clients.Group($"tenant:{call.TenantId}")
                    .SendAsync("ServiceCallCreated", call);

                return Ok(call);
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

    }
}
