using Microsoft.AspNetCore.Mvc;
using QrOrder.Application.Public;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("public/sessions")]
    public class PublicSessionsController : ControllerBase
    {
        private readonly IPublicTableSessionService _sessions;

        public PublicSessionsController(IPublicTableSessionService sessions)
        {
            _sessions = sessions;
        }

        public record CreateSessionRequest(string TenantSlug, string TableCode);
        public record ValidateSessionRequest(string TenantSlug, string TableCode, string SessionToken);

        [HttpPost]
        public async Task<IActionResult> Create(CreateSessionRequest req)
        {
            var session = await _sessions.CreateAsync(req.TenantSlug, req.TableCode);
            if (session == null) return NotFound("Tenant or table not found.");

            return Ok(new
            {
                sessionToken = session.SessionToken,
                expiresAt = session.ExpiresAt,
                tableNumber = session.TableNumber
            });
        }

        [HttpPost("validate")]
        public async Task<IActionResult> Validate(ValidateSessionRequest req)
        {
            var session = await _sessions.ValidateAsync(req.TenantSlug, req.TableCode, req.SessionToken);
            if (session == null) return NotFound("Tenant, table, or session not found.");

            return Ok(new
            {
                sessionToken = session.SessionToken,
                expiresAt = session.ExpiresAt,
                tableNumber = session.TableNumber
            });
        }
    }
}
