using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Domain.Entities;
using QrOrder.Infrastructure.Auth;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("super-admin/tenants")]
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminTenantsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _users;

        public SuperAdminTenantsController(AppDbContext db, UserManager<ApplicationUser> users)
        {
            _db = db;
            _users = users;
        }

        public record TenantDto(
            Guid Id,
            string Name,
            string Slug,
            bool IsActive,
            bool IsOrderingEnabled,
            int TableSessionHours,
            DateTimeOffset CreatedAt,
            int UserCount,
            List<string> AdminEmails);

        public record CreateTenantReq(
            string Name,
            string Slug,
            string AdminEmail,
            string AdminPassword);

        public record UpdateTenantStatusReq(bool IsActive);

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var tenants = await _db.Tenants
                .IgnoreQueryFilters()
                .OrderBy(t => t.Name)
                .ToListAsync();

            var users = await _users.Users
                .IgnoreQueryFilters()
                .ToListAsync();

            var result = new List<TenantDto>();

            foreach (var tenant in tenants)
            {
                var tenantUsers = users.Where(u => u.TenantId == tenant.Id).ToList();
                var admins = new List<string>();

                foreach (var user in tenantUsers)
                {
                    if (await _users.IsInRoleAsync(user, "Admin"))
                        admins.Add(user.Email ?? user.UserName ?? "");
                }

                result.Add(new TenantDto(
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.IsActive,
                    tenant.IsOrderingEnabled,
                    tenant.TableSessionHours,
                    tenant.CreatedAt,
                    tenantUsers.Count,
                    admins.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList()));
            }

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateTenantReq req)
        {
            var name = req.Name.Trim();
            var slug = NormalizeSlug(req.Slug);
            var adminEmail = req.AdminEmail.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Tenant name is required.");

            if (string.IsNullOrWhiteSpace(slug))
                return BadRequest("Tenant slug is required.");

            if (string.IsNullOrWhiteSpace(adminEmail))
                return BadRequest("Admin email is required.");

            if (string.IsNullOrWhiteSpace(req.AdminPassword))
                return BadRequest("Admin password is required.");

            var slugExists = await _db.Tenants
                .IgnoreQueryFilters()
                .AnyAsync(t => t.Slug == slug);
            if (slugExists) return Conflict("Tenant slug already exists.");

            var userExists = await _users.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.NormalizedEmail == adminEmail.ToUpperInvariant());
            if (userExists) return Conflict("Admin email already exists.");

            await using var tx = await _db.Database.BeginTransactionAsync();

            var tenant = new Tenant
            {
                Name = name,
                Slug = slug,
                IsActive = true,
                IsOrderingEnabled = true,
                TableSessionHours = 12
            };

            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            var admin = new ApplicationUser
            {
                TenantId = tenant.Id,
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };

            var createUser = await _users.CreateAsync(admin, req.AdminPassword);
            if (!createUser.Succeeded)
            {
                return BadRequest(createUser.Errors);
            }

            var addRole = await _users.AddToRoleAsync(admin, "Admin");
            if (!addRole.Succeeded)
                return BadRequest(addRole.Errors);

            await tx.CommitAsync();

            return Ok(new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                AdminEmail = admin.Email
            });
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateTenantStatusReq req)
        {
            var tenant = await _db.Tenants
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound();
            if (tenant.Slug == "platform" && !req.IsActive)
                return BadRequest("Platform tenant cannot be deactivated.");

            tenant.IsActive = req.IsActive;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private static string NormalizeSlug(string value)
        {
            var chars = value
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray();

            return string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
