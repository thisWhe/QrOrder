using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Domain.Entities;
using QrOrder.Infrastructure.Data;
using QrOrder.Web.Storage;
using System.Text.RegularExpressions;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/tenant")]
    [Authorize]
    public class StaffTenantController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ITenantBrandingStorage _brandingStorage;

        public StaffTenantController(AppDbContext db, ITenantBrandingStorage brandingStorage)
        {
            _db = db;
            _brandingStorage = brandingStorage;
        }

        public record BusinessHourRequest(DayOfWeek DayOfWeek, bool IsOpen, TimeOnly OpenTime, TimeOnly CloseTime);
        public record UpdateTenantSettingsRequest(
            string Name,
            bool IsOrderingEnabled,
            bool ShowProductDetails,
            int TableSessionHours,
            string PrimaryColor,
            string AccentColor,
            List<BusinessHourRequest> BusinessHours);

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var tenantIdStr = User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Unauthorized();

            // Tenant tablosu ITenantEntity değil, global filter yok -> direkt ID ile çekiyoruz
            var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId && t.IsActive);
            if (tenant == null) return NotFound();

            var savedHours = await _db.TenantBusinessHours
                .Where(x => x.TenantId == tenantId)
                .ToListAsync();
            var businessHours = Enumerable.Range(0, 7)
                .Select(day =>
                {
                    var saved = savedHours.SingleOrDefault(x => (int)x.DayOfWeek == day);
                    return new
                    {
                        DayOfWeek = day,
                        IsOpen = saved?.IsOpen ?? true,
                        OpenTime = (saved?.OpenTime ?? new TimeOnly(0, 0)).ToString("HH:mm"),
                        CloseTime = (saved?.CloseTime ?? new TimeOnly(0, 0)).ToString("HH:mm")
                    };
                })
                .ToList();

            return Ok(new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                tenant.IsOrderingEnabled,
                tenant.ShowProductDetails,
                tenant.TableSessionHours,
                tenant.TimeZoneId,
                tenant.PrimaryColor,
                tenant.AccentColor,
                tenant.LogoUrl,
                tenant.HeroImageUrl,
                BusinessHours = businessHours
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

            if (req.BusinessHours == null || req.BusinessHours.Count != 7 ||
                req.BusinessHours.Select(x => x.DayOfWeek).Distinct().Count() != 7 ||
                req.BusinessHours.Any(x => !Enum.IsDefined(x.DayOfWeek)))
                return BadRequest("BusinessHours must contain each day exactly once.");

            if (!IsHexColor(req.PrimaryColor) || !IsHexColor(req.AccentColor))
                return BadRequest("Theme colors must use #RRGGBB format.");

            tenant.Name = req.Name.Trim();
            tenant.IsOrderingEnabled = req.IsOrderingEnabled;
            tenant.ShowProductDetails = req.ShowProductDetails;
            tenant.TableSessionHours = req.TableSessionHours;
            tenant.PrimaryColor = req.PrimaryColor.ToUpperInvariant();
            tenant.AccentColor = req.AccentColor.ToUpperInvariant();

            var savedHours = await _db.TenantBusinessHours
                .Where(x => x.TenantId == tenantId)
                .ToListAsync();

            foreach (var requested in req.BusinessHours)
            {
                var hour = savedHours.SingleOrDefault(x => x.DayOfWeek == requested.DayOfWeek);
                if (hour == null)
                {
                    hour = new TenantBusinessHour
                    {
                        TenantId = tenantId,
                        DayOfWeek = requested.DayOfWeek
                    };
                    _db.TenantBusinessHours.Add(hour);
                }

                hour.IsOpen = requested.IsOpen;
                hour.OpenTime = requested.OpenTime;
                hour.CloseTime = requested.CloseTime;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("branding/{imageType}")]
        [Authorize(Roles = "Admin")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(LocalTenantBrandingStorage.MaxFileSize + 64 * 1024)]
        public async Task<IActionResult> UploadBrandingImage(
            string imageType,
            [FromForm] IFormFile image,
            CancellationToken cancellationToken)
        {
            if (imageType is not ("logo" or "hero")) return NotFound();
            if (!TryGetTenantId(out var tenantId)) return Unauthorized();

            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(x => x.Id == tenantId && x.IsActive, cancellationToken);
            if (tenant == null) return NotFound();
            if (image == null || image.Length == 0) return BadRequest("Image is required.");

            string newImageUrl;
            try
            {
                await using var content = image.OpenReadStream();
                newImageUrl = await _brandingStorage.SaveAsync(
                    tenantId,
                    imageType,
                    content,
                    image.FileName,
                    image.ContentType,
                    cancellationToken);
            }
            catch (InvalidDataException error)
            {
                return BadRequest(error.Message);
            }

            var oldImageUrl = imageType == "logo" ? tenant.LogoUrl : tenant.HeroImageUrl;
            if (imageType == "logo") tenant.LogoUrl = newImageUrl;
            else tenant.HeroImageUrl = newImageUrl;

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await _brandingStorage.DeleteAsync(newImageUrl, cancellationToken);
                throw;
            }

            await _brandingStorage.DeleteAsync(oldImageUrl, cancellationToken);
            return Ok(new { imageUrl = newImageUrl });
        }

        [HttpDelete("branding/{imageType}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteBrandingImage(
            string imageType,
            CancellationToken cancellationToken)
        {
            if (imageType is not ("logo" or "hero")) return NotFound();
            if (!TryGetTenantId(out var tenantId)) return Unauthorized();

            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(x => x.Id == tenantId && x.IsActive, cancellationToken);
            if (tenant == null) return NotFound();

            var imageUrl = imageType == "logo" ? tenant.LogoUrl : tenant.HeroImageUrl;
            if (imageType == "logo") tenant.LogoUrl = null;
            else tenant.HeroImageUrl = null;

            await _db.SaveChangesAsync(cancellationToken);
            await _brandingStorage.DeleteAsync(imageUrl, cancellationToken);
            return NoContent();
        }

        private bool TryGetTenantId(out Guid tenantId) =>
            Guid.TryParse(User.FindFirst("tenant_id")?.Value, out tenantId);

        private static bool IsHexColor(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);
    }
}
