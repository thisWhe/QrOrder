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
            int TableCount,
            List<string> AdminEmails);

        public record CreateTenantReq(
            string Name,
            string Slug,
            string AdminEmail,
            string AdminPassword,
            int TableCount = 0,
            string? KitchenEmail = null,
            string? KitchenPassword = null,
            string? ServiceEmail = null,
            string? ServicePassword = null);

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

            var tableCounts = await _db.Tables
                .IgnoreQueryFilters()
                .GroupBy(t => t.TenantId)
                .Select(g => new { TenantId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TenantId, x => x.Count);

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
                    tableCounts.TryGetValue(tenant.Id, out var tableCount) ? tableCount : 0,
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

            if (req.TableCount < 0 || req.TableCount > 300)
                return BadRequest("Table count must be between 0 and 300.");

            var staffValidationError = ValidateOptionalStaff(req);
            if (staffValidationError is not null)
                return BadRequest(staffValidationError);

            var staffRequests = BuildStaffRequests(req);
            var requestedEmails = new[] { adminEmail }
                .Concat(staffRequests.Select(x => x.Email))
                .ToList();
            var normalizedRequestedEmails = requestedEmails
                .Select(x => x.ToUpperInvariant())
                .ToList();

            if (requestedEmails.Count != requestedEmails.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                return BadRequest("User emails must be unique.");

            var slugExists = await _db.Tenants
                .IgnoreQueryFilters()
                .AnyAsync(t => t.Slug == slug);
            if (slugExists) return Conflict("Tenant slug already exists.");

            var userExists = await _users.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => normalizedRequestedEmails.Contains(u.NormalizedEmail!));
            if (userExists) return Conflict("User email already exists.");

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

            var admin = await CreateStaffUserAsync(tenant.Id, adminEmail, req.AdminPassword, "Admin");
            if (admin.Result is not null)
                return admin.Result;

            foreach (var staff in staffRequests)
            {
                var created = await CreateStaffUserAsync(tenant.Id, staff.Email, staff.Password, staff.Role);
                if (created.Result is not null)
                    return created.Result;
            }

            if (req.TableCount > 0)
            {
                for (var displayNumber = 1; displayNumber <= req.TableCount; displayNumber++)
                {
                    _db.Tables.Add(new Table
                    {
                        TenantId = tenant.Id,
                        DisplayNumber = displayNumber,
                        TableCode = await NewUniqueTableCodeAsync(),
                        IsActive = true
                    });
                }

                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();

            return Ok(new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                AdminEmail = admin.User?.Email,
                req.TableCount,
                StaffUsers = staffRequests.Select(x => new { x.Email, x.Role }).ToList()
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

        private async Task<string> NewUniqueTableCodeAsync()
        {
            var tableCode = Guid.NewGuid().ToString("N");
            while (await _db.Tables.IgnoreQueryFilters().AnyAsync(t => t.TableCode == tableCode))
                tableCode = Guid.NewGuid().ToString("N");

            return tableCode;
        }

        private async Task<(ApplicationUser? User, IActionResult? Result)> CreateStaffUserAsync(
            Guid tenantId,
            string email,
            string password,
            string role)
        {
            var user = new ApplicationUser
            {
                TenantId = tenantId,
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createUser = await _users.CreateAsync(user, password);
            if (!createUser.Succeeded)
                return (null, BadRequest(createUser.Errors));

            var addRole = await _users.AddToRoleAsync(user, role);
            if (!addRole.Succeeded)
                return (null, BadRequest(addRole.Errors));

            return (user, null);
        }

        private static List<StaffUserRequest> BuildStaffRequests(CreateTenantReq req)
        {
            var staff = new List<StaffUserRequest>();

            AddOptionalStaff(staff, req.KitchenEmail, req.KitchenPassword, "Kitchen");
            AddOptionalStaff(staff, req.ServiceEmail, req.ServicePassword, "Service");

            return staff;
        }

        private static string? ValidateOptionalStaff(CreateTenantReq req)
        {
            if (HasPartialStaff(req.KitchenEmail, req.KitchenPassword))
                return "Kitchen email and password must be provided together.";

            if (HasPartialStaff(req.ServiceEmail, req.ServicePassword))
                return "Service email and password must be provided together.";

            return null;
        }

        private static bool HasPartialStaff(string? email, string? password)
        {
            var hasEmail = !string.IsNullOrWhiteSpace(email);
            var hasPassword = !string.IsNullOrWhiteSpace(password);
            return hasEmail != hasPassword;
        }

        private static void AddOptionalStaff(
            List<StaffUserRequest> staff,
            string? email,
            string? password,
            string role)
        {
            var hasEmail = !string.IsNullOrWhiteSpace(email);
            var hasPassword = !string.IsNullOrWhiteSpace(password);

            if (!hasEmail && !hasPassword)
                return;

            if (!hasEmail || !hasPassword)
                return;

            staff.Add(new StaffUserRequest(email!.Trim().ToLowerInvariant(), password!, role));
        }

        private record StaffUserRequest(string Email, string Password, string Role);
    }
}
