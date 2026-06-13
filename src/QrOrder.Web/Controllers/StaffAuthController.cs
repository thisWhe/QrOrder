using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using QrOrder.Application.Common;
using QrOrder.Infrastructure.Auth;
using QrOrder.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/auth")]
    public class StaffAuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly JwtTokenService _jwt;

        public StaffAuthController(
            AppDbContext db,
            ITenantContext tenantContext,
            UserManager<ApplicationUser> userManager,
            JwtTokenService jwt)
        {
            _db = db;
            _tenantContext = tenantContext;
            _userManager = userManager;
            _jwt = jwt;
        }

        public record LoginRequest(string TenantSlug, string Email, string Password);
        public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

        [HttpPost("login")]
        [EnableRateLimiting("staff-login")]
        public async Task<IActionResult> Login(LoginRequest req)
        {
            var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Slug == req.TenantSlug && t.IsActive);
            if (tenant == null) return Unauthorized();

            _tenantContext.TenantId = tenant.Id;

            var user = await _userManager.FindByEmailAsync(req.Email.Trim());
            if (user == null || user.TenantId != tenant.Id) return Unauthorized();

            if (!user.LockoutEnabled)
                await _userManager.SetLockoutEnabledAsync(user, true);

            if (await _userManager.IsLockedOutAsync(user))
                return StatusCode(StatusCodes.Status429TooManyRequests, "Account is temporarily locked.");

            var ok = await _userManager.CheckPasswordAsync(user, req.Password);
            if (!ok)
            {
                await _userManager.AccessFailedAsync(user);
                return Unauthorized();
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwt.Create(user, roles);

            return Ok(new { token });
        }

        [HttpPatch("password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
        {
            var userIdValue = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdValue, out var userId)) return Unauthorized();

            var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId);
            if (user == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
                return BadRequest("CurrentPassword and NewPassword are required.");

            var result = await _userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
            if (!result.Succeeded) return BadRequest(result.Errors);

            return NoContent();
        }
    }
}
