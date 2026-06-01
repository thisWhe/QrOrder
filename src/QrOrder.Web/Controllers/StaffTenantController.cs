using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/tenant")]
    [Authorize]
    public class StaffTenantController : ControllerBase
    {
        private readonly AppDbContext _db;
        public StaffTenantController(AppDbContext db) => _db = db;

        public record UpdateTenantSettingsRequest(string Name, bool IsOrderingEnabled, int TableSessionHours);

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var tenantIdStr = User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Unauthorized();

            // Tenant tablosu ITenantEntity değil, global filter yok -> direkt ID ile çekiyoruz
            var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId && t.IsActive);
            if (tenant == null) return NotFound();

            return Ok(new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                tenant.IsOrderingEnabled,
                tenant.TableSessionHours
            });
        }

        [HttpPut("settings")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateSettings(UpdateTenantSettingsRequest req)
        {
            var tenantIdStr = User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Unauthorized();

            var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId && t.IsActive);
            if (tenant == null) return NotFound();

            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest("Name is required.");

            if (req.TableSessionHours < 1 || req.TableSessionHours > 24)
                return BadRequest("TableSessionHours must be between 1 and 24.");

            tenant.Name = req.Name.Trim();
            tenant.IsOrderingEnabled = req.IsOrderingEnabled;
            tenant.TableSessionHours = req.TableSessionHours;

            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
