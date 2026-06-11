using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Domain.Entities;
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

        public record CreateReq(Guid CategoryId, string Name, string? Description, decimal Price, int SortOrder);

        [HttpPost]
        public async Task<IActionResult> Create(CreateReq req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
            if (req.Price <= 0) return BadRequest("Price must be > 0.");

            var cat = await _db.Categories.SingleOrDefaultAsync(c => c.Id == req.CategoryId && c.IsActive);
            if (cat == null) return BadRequest("Invalid category.");

            var p = new Product
            {
                CategoryId = req.CategoryId,
                Name = req.Name.Trim(),
                Description = req.Description?.Trim(),
                Price = req.Price,
                SortOrder = req.SortOrder,
                IsActive = true,
                IsAvailable = true
            };

            _db.Products.Add(p);
            await _db.SaveChangesAsync();

            return Ok(new { p.Id });
        }

        public record UpdateReq(Guid CategoryId, string Name, string? Description, decimal Price, int SortOrder, bool IsActive, bool IsAvailable);
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

            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();

            var cat = await _db.Categories.SingleOrDefaultAsync(c => c.Id == req.CategoryId && c.IsActive);
            if (cat == null) return BadRequest("Invalid category.");

            p.CategoryId = req.CategoryId;
            p.Name = req.Name.Trim();
            p.Description = req.Description?.Trim();
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

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var p = await _db.Products.SingleOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();

            var imageUrl = p.ImageUrl;
            _db.Products.Remove(p);
            await _db.SaveChangesAsync();
            await _imageStorage.DeleteAsync(imageUrl);
            return NoContent();
        }

    }
}
