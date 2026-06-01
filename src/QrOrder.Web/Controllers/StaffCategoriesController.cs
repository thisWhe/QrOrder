using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QrOrder.Domain.Entities;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/categories")]
    [Authorize(Roles = "Admin")]
    public class StaffCategoriesController : ControllerBase
    {
        private readonly AppDbContext _db;
        public StaffCategoriesController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var data = await _db.Categories
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, c.SortOrder, c.IsActive })
                .ToListAsync();

            return Ok(data);
        }

        public record CreateReq(string Name, int SortOrder);

        [HttpPost]
        public async Task<IActionResult> Create(CreateReq req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");

            var c = new Category
            {
                Name = req.Name.Trim(),
                SortOrder = req.SortOrder,
                IsActive = true
            };

            _db.Categories.Add(c);
            await _db.SaveChangesAsync();

            return Ok(new { c.Id });
        }

        public record UpdateReq(string Name, int SortOrder, bool IsActive);
        public record UpdateStatusReq(bool IsActive);

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, UpdateReq req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");

            var c = await _db.Categories.SingleOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            c.Name = req.Name.Trim();
            c.SortOrder = req.SortOrder;
            c.IsActive = req.IsActive;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateStatusReq req)
        {
            var c = await _db.Categories.SingleOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            c.IsActive = req.IsActive;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var c = await _db.Categories.SingleOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();

            _db.Categories.Remove(c);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
