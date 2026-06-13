using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Domain.Entities;
using QrOrder.Domain.Enums;
using QrOrder.Infrastructure.Data;
using QrOrder.Web.Storage;

namespace QrOrder.Web.Controllers
{
    [Route("staff/products")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class StaffProductsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenant;
        private readonly IProductImageStorage _imageStorage;

        public StaffProductsController(
            AppDbContext db,
            ITenantContext tenant,
            IProductImageStorage imageStorage)
        {
            _db = db;
            _tenant = tenant;
            _imageStorage = imageStorage;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var data = await _db.Products
                .Include(p => p.Category)
                .OrderBy(p => p.Category.SortOrder)
                .ThenBy(p => p.Category.Name)
                .ThenBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.Ingredients,
                    p.PortionInfo,
                    p.Calories,
                    AllergenFlags = (int)p.AllergenFlags,
                    p.ContainsAlcohol,
                    p.ContainsPork,
                    p.IsVegetarian,
                    p.IsVegan,
                    ServingTemperature = (int)p.ServingTemperature,
                    p.ImageUrl,
                    p.Price,
                    p.SortOrder,
                    p.IsActive,
                    p.IsAvailable,
                    p.CategoryId,
                    CategoryName = p.Category.Name
                })
                .ToListAsync();

            return Ok(data);
        }

        public record CreateReq(
            Guid CategoryId,
            string Name,
            string? Description,
            decimal Price,
            int SortOrder,
            string? Ingredients,
            string? PortionInfo,
            int? Calories,
            int AllergenFlags,
            bool ContainsAlcohol,
            bool ContainsPork,
            bool IsVegetarian,
            bool IsVegan,
            int ServingTemperature);

        [HttpPost]
        public async Task<IActionResult> Create(CreateReq req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
            if (req.Price <= 0) return BadRequest("Price must be > 0.");
            var validationError = ValidateProductDetails(req.Ingredients, req.PortionInfo, req.Calories, req.AllergenFlags);
            if (validationError != null) return BadRequest(validationError);
            if (!Enum.IsDefined((ServingTemperature)req.ServingTemperature)) return BadRequest("Serving temperature is invalid.");

            var cat = await _db.Categories.SingleOrDefaultAsync(c => c.Id == req.CategoryId && c.IsActive);
            if (cat == null) return BadRequest("Invalid category.");

            var p = new Product
            {
                CategoryId = req.CategoryId,
                Name = req.Name.Trim(),
                Description = req.Description?.Trim(),
                Ingredients = NormalizeOptional(req.Ingredients),
                PortionInfo = NormalizeOptional(req.PortionInfo),
                Calories = req.Calories,
                AllergenFlags = (AllergenFlags)req.AllergenFlags,
                ContainsAlcohol = req.ContainsAlcohol,
                ContainsPork = req.ContainsPork,
                IsVegetarian = req.IsVegetarian || req.IsVegan,
                IsVegan = req.IsVegan,
                ServingTemperature = (ServingTemperature)req.ServingTemperature,
                Price = req.Price,
                SortOrder = req.SortOrder,
                IsActive = true,
                IsAvailable = true
            };

            _db.Products.Add(p);
            await _db.SaveChangesAsync();

            return Ok(new { p.Id });
        }

        public record UpdateReq(
            Guid CategoryId,
            string Name,
            string? Description,
            decimal Price,
            int SortOrder,
            bool IsActive,
            bool IsAvailable,
            string? Ingredients,
            string? PortionInfo,
            int? Calories,
            int AllergenFlags,
            bool ContainsAlcohol,
            bool ContainsPork,
            bool IsVegetarian,
            bool IsVegan,
            int ServingTemperature);
        public record UpdateStatusReq(bool IsActive);
        public record UpdateAvailabilityReq(bool IsAvailable);

        [HttpPost("{id:guid}/image")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(LocalProductImageStorage.MaxFileSize + 64 * 1024)]
        public async Task<IActionResult> UploadImage(
            Guid id,
            [FromForm] IFormFile image,
            CancellationToken cancellationToken)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId is null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set.");

            var product = await _db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (product == null) return NotFound();

            if (image == null || image.Length == 0)
                return BadRequest("Image is required.");

            if (image.Length > LocalProductImageStorage.MaxFileSize)
                return BadRequest("Image size cannot exceed 5 MB.");

            string newImageUrl;
            try
            {
                await using var content = image.OpenReadStream();
                newImageUrl = await _imageStorage.SaveAsync(
                    tenantId.Value,
                    product.Id,
                    content,
                    image.FileName,
                    image.ContentType,
                    cancellationToken);
            }
            catch (InvalidDataException error)
            {
                return BadRequest(error.Message);
            }

            var oldImageUrl = product.ImageUrl;
            product.ImageUrl = newImageUrl;

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                await _imageStorage.DeleteAsync(newImageUrl, cancellationToken);
                throw;
            }

            await _imageStorage.DeleteAsync(oldImageUrl, cancellationToken);
            return Ok(new { imageUrl = newImageUrl });
        }

        [HttpDelete("{id:guid}/image")]
        public async Task<IActionResult> DeleteImage(Guid id, CancellationToken cancellationToken)
        {
            var product = await _db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (product == null) return NotFound();

            var imageUrl = product.ImageUrl;
            product.ImageUrl = null;
            await _db.SaveChangesAsync(cancellationToken);
            await _imageStorage.DeleteAsync(imageUrl, cancellationToken);

            return NoContent();
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, UpdateReq req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
            if (req.Price <= 0) return BadRequest("Price must be > 0.");
            var validationError = ValidateProductDetails(req.Ingredients, req.PortionInfo, req.Calories, req.AllergenFlags);
            if (validationError != null) return BadRequest(validationError);
            if (!Enum.IsDefined((ServingTemperature)req.ServingTemperature)) return BadRequest("Serving temperature is invalid.");

            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();

            var cat = await _db.Categories.SingleOrDefaultAsync(c => c.Id == req.CategoryId && c.IsActive);
            if (cat == null) return BadRequest("Invalid category.");

            p.CategoryId = req.CategoryId;
            p.Name = req.Name.Trim();
            p.Description = req.Description?.Trim();
            p.Ingredients = NormalizeOptional(req.Ingredients);
            p.PortionInfo = NormalizeOptional(req.PortionInfo);
            p.Calories = req.Calories;
            p.AllergenFlags = (AllergenFlags)req.AllergenFlags;
            p.ContainsAlcohol = req.ContainsAlcohol;
            p.ContainsPork = req.ContainsPork;
            p.IsVegetarian = req.IsVegetarian || req.IsVegan;
            p.IsVegan = req.IsVegan;
            p.ServingTemperature = (ServingTemperature)req.ServingTemperature;
            p.Price = req.Price;
            p.SortOrder = req.SortOrder;
            p.IsActive = req.IsActive;
            p.IsAvailable = req.IsAvailable;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateStatusReq req)
        {
            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();

            p.IsActive = req.IsActive;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("{id:guid}/availability")]
        public async Task<IActionResult> UpdateAvailability(Guid id, UpdateAvailabilityReq req)
        {
            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();

            p.IsAvailable = req.IsAvailable;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("{id:guid}/restore")]
        public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
        {
            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (p == null) return NotFound();

            var categoryIsActive = await _db.Categories
                .AnyAsync(x => x.Id == p.CategoryId && x.IsActive, cancellationToken);
            if (!categoryIsActive)
                return Conflict("Product category is inactive.");

            p.IsActive = true;
            p.IsAvailable = true;
            await _db.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        [HttpPatch("{id:guid}/archive")]
        public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
        {
            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (p == null) return NotFound();

            p.IsActive = false;
            p.IsAvailable = false;
            await _db.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (p == null) return NotFound();

            var hasOrderHistory = await _db.OrderItems
                .AnyAsync(x => x.ProductId == id, cancellationToken);

            if (hasOrderHistory)
            {
                p.IsActive = false;
                p.IsAvailable = false;
                await _db.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    deleted = false,
                    archived = true
                });
            }

            var imageUrl = p.ImageUrl;
            _db.Products.Remove(p);
            await _db.SaveChangesAsync(cancellationToken);
            await _imageStorage.DeleteAsync(imageUrl, cancellationToken);

            return Ok(new
            {
                deleted = true,
                archived = false
            });
        }

        private static string? ValidateProductDetails(string? ingredients, string? portionInfo, int? calories, int allergenFlags)
        {
            if (ingredients?.Length > 1500) return "Ingredients cannot exceed 1500 characters.";
            if (portionInfo?.Length > 150) return "Portion information cannot exceed 150 characters.";
            if (calories is < 0 or > 10000) return "Calories must be between 0 and 10000.";

            const int supportedFlags = (1 << 14) - 1;
            if (allergenFlags < 0 || (allergenFlags & ~supportedFlags) != 0)
                return "Allergen selection is invalid.";

            return null;
        }

        private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    }
}
