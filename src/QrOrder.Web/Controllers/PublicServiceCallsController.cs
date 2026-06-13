using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
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
        private readonly ILogger<PublicServiceCallsController> _logger;

        public PublicServiceCallsController(
            IPublicServiceCallService serviceCalls,
            IHubContext<StaffOrdersHub> staffHub,
            ILogger<PublicServiceCallsController> logger)
        {
            _serviceCalls = serviceCalls;
            _staffHub = staffHub;
            _logger = logger;
        }

        public record CreateServiceCallRequest(string TenantSlug, string SessionToken, string? Message);

        [HttpPost]
        [EnableRateLimiting("public-write")]
        public async Task<IActionResult> Create([FromBody] CreateServiceCallRequest req)
        {
            try
            {
                var call = await _serviceCalls.CreateAsync(new QrOrder.Application.ServiceCalls.CreateServiceCallRequest(
                    req.TenantSlug,
                    req.SessionToken,
                    req.Message));

                try
                {
                    await _staffHub.Clients.Group($"tenant:{call.TenantId}")
                        .SendAsync("ServiceCallCreated", call);
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Service call {ServiceCallId} was saved but its SignalR notification failed.", call.Id);
                }

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
