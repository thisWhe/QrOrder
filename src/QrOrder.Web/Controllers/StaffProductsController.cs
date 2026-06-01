using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Domain.Entities;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Controllers
{
    [Route("staff/products")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class StaffProductsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public StaffProductsController(AppDbContext db) => _db = db;

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

            _db.Products.Remove(p);
            await _db.SaveChangesAsync();
            return NoContent();
        }

    }
}
