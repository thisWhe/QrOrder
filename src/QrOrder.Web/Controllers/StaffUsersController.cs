using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Infrastructure.Auth;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/users")]
    [Authorize(Roles = "Admin")]
    public class StaffUsersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly UserManager<ApplicationUser> _users;

        public StaffUsersController(AppDbContext db, ITenantContext tenant, UserManager<ApplicationUser> users)
        {
            _db = db;
            _tenant = tenant;
            _users = users;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            // Tenant filter Identity userlarında da çalışıyor (ApplicationUser ITenantEntity)
            var users = await _users.Users
                .OrderBy(u => u.Email)
                .ToListAsync();

            var data = new List<UserDto>();
            foreach (var user in users)
            {
                var roles = await _users.GetRolesAsync(user);
                data.Add(new UserDto(user.Id, user.Email ?? "", roles.OrderBy(x => x).ToList()));
            }

            return Ok(data);
        }

        public record UserDto(Guid Id, string Email, List<string> Roles);
        public record CreateReq(string Email, string Password, string Role); // Role: Kitchen | Service
        public record ResetPasswordReq(string Password);

        [HttpPost]
        public async Task<IActionResult> Create(CreateReq req)
        {
            if (_tenant.TenantId is null) return Unauthorized("Tenant not resolved.");
            var role = req.Role.Trim();

            if (role != "Kitchen" && role != "Service")
                return BadRequest("Role must be Kitchen or Service.");

            var email = req.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email)) return BadRequest("Email required.");

            var exists = await _users.FindByEmailAsync(email);
            if (exists != null) return BadRequest("User already exists.");

            var user = new ApplicationUser
            {
                TenantId = _tenant.TenantId.Value,
                Email = email,
                UserName = email,
                EmailConfirmed = true
            };

            var res = await _users.CreateAsync(user, req.Password);
            if (!res.Succeeded) return BadRequest(res.Errors);

            await _users.AddToRoleAsync(user, role);
            return Ok(new { user.Id, user.Email, role });
        }

        [HttpPatch("{id:guid}/password")]
        public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordReq req)
        {
            var user = await _users.Users.SingleOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            if (string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Password required.");

            var passwordValidationErrors = new List<IdentityError>();
            foreach (var validator in _users.PasswordValidators)
            {
                var validation = await validator.ValidateAsync(_users, user, req.Password);
                if (!validation.Succeeded)
                    passwordValidationErrors.AddRange(validation.Errors);
            }

            if (passwordValidationErrors.Count > 0)
                return BadRequest(passwordValidationErrors);

            user.PasswordHash = _users.PasswordHasher.HashPassword(user, req.Password);
            user.SecurityStamp = Guid.NewGuid().ToString();

            var result = await _users.UpdateAsync(user);
            if (!result.Succeeded) return BadRequest(result.Errors);

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var user = await _users.Users.SingleOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            var roles = await _users.GetRolesAsync(user);
            if (roles.Contains("Admin"))
                return BadRequest("Admin user cannot be deleted.");

            await _users.DeleteAsync(user);
            return NoContent();
        }
    }
}
