using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using QRCoder;
using QrOrder.Application.Common;
using QrOrder.Domain.Entities;
using QrOrder.Domain.Enums;
using QrOrder.Infrastructure.Data;
using System.Drawing.Imaging;
using TableEntity = QrOrder.Domain.Entities.Table;


namespace QrOrder.Web.Controllers
{
    [ApiController]
    [Route("staff/tables")]
    [Authorize(Roles = "Admin")]
    public class StaffTablesController : ControllerBase
    {

        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;
        private readonly ITenantContext _tenant;

        public StaffTablesController(AppDbContext db, ITenantContext tenant, IConfiguration cfg)
        {
            _db = db;
            _tenant = tenant;
            _cfg = cfg;
        }

        private static string NewTableCode() => Guid.NewGuid().ToString("N");

        // Prod ortamında appsettings'ten vermek daha doğru
        private string PublicBaseUrl => _cfg["PublicBaseUrl"] ?? "http://localhost:5140";
        private static readonly OrderStatus[] ActiveOrderStatuses =
        [
            OrderStatus.New,
            OrderStatus.Preparing,
            OrderStatus.Ready
        ];

        public record CreateTableRequest(int DisplayNumber);
        public record UpdateTableStatusRequest(bool IsActive);
        public record UpdateDisplayNumberRequest(int DisplayNumber);
        public record TableDto(Guid Id, int DisplayNumber, string TableCode, bool IsActive, bool HasActiveOrder);

        [HttpGet]
        public async Task<ActionResult<List<TableDto>>> List()
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            var rows = await _db.Set<TableEntity>()
                .Where(t => t.TenantId == tenantId.Value)
                .OrderBy(t => t.DisplayNumber)
                .Select(t => new TableDto(
                    t.Id,
                    t.DisplayNumber,
                    t.TableCode,
                    t.IsActive,
                    _db.Orders.Any(o => o.TableId == t.Id && ActiveOrderStatuses.Contains(o.Status))))
                .ToListAsync();

            return Ok(rows);
        }

        [HttpPost]
        public async Task<ActionResult<TableDto>> Create(CreateTableRequest req)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            if (req.DisplayNumber <= 0)
                return BadRequest("DisplayNumber must be > 0");

            // Aynı tenant içinde DisplayNumber unique olsun (istersen kaldırabilirsin)
            var numberExists = await _db.Set<TableEntity>().AnyAsync(t =>
                t.TenantId == tenantId.Value && t.DisplayNumber == req.DisplayNumber);

            if (numberExists)
                return Conflict("DisplayNumber already exists");

            // TableCode üret (QR için gizli kod)
            var tableCode = NewTableCode();

            // Aynı tenant içinde TableCode unique garanti
            while (await _db.Set<TableEntity>().AnyAsync(t =>
                t.TenantId == tenantId.Value && t.TableCode == tableCode))
            {
                tableCode = NewTableCode();
            }

            var table = new TableEntity
            {
                TenantId = tenantId.Value,
                DisplayNumber = req.DisplayNumber,
                TableCode = tableCode,
                IsActive = true
            };

            _db.Add(table);
            await _db.SaveChangesAsync();

            var dto = new TableDto(table.Id, table.DisplayNumber, table.TableCode, table.IsActive, false);
            return CreatedAtAction(nameof(GetById), new { id = table.Id }, dto);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<TableDto>> GetById(Guid id)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            var row = await _db.Set<TableEntity>()
                .Where(t => t.TenantId == tenantId.Value && t.Id == id)
                .Select(t => new TableDto(
                    t.Id,
                    t.DisplayNumber,
                    t.TableCode,
                    t.IsActive,
                    _db.Orders.Any(o => o.TableId == t.Id && ActiveOrderStatuses.Contains(o.Status))))
                .SingleOrDefaultAsync();

            if (row == null) return NotFound();
            return Ok(row);
        }

        [HttpPatch("{id:guid}/deactivate")]
        public async Task<IActionResult> Deactivate(Guid id)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            var table = await _db.Set<TableEntity>()
                .SingleOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == id);

            if (table == null) return NotFound();

            if (await HasActiveOrdersAsync(table.Id))
                return Conflict("This table has active orders and cannot be deactivated.");

            table.IsActive = false;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, UpdateTableStatusRequest req)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            var table = await _db.Set<TableEntity>()
                .SingleOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == id);

            if (table == null) return NotFound();

            if (!req.IsActive && await HasActiveOrdersAsync(table.Id))
                return Conflict("This table has active orders and cannot be deactivated.");

            table.IsActive = req.IsActive;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpPatch("{id:guid}/display-number")]
        public async Task<IActionResult> UpdateDisplayNumber(Guid id, UpdateDisplayNumberRequest req)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            if (req.DisplayNumber <= 0)
                return BadRequest("DisplayNumber must be > 0");

            var table = await _db.Set<TableEntity>()
                .SingleOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == id);

            if (table == null) return NotFound();

            var numberExists = await _db.Set<TableEntity>().AnyAsync(t =>
                t.TenantId == tenantId.Value &&
                t.Id != id &&
                t.DisplayNumber == req.DisplayNumber);

            if (numberExists)
                return Conflict("DisplayNumber already exists");

            table.DisplayNumber = req.DisplayNumber;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // ✅ QR URL
        // /staff/tables/{id}/qr-url?tenantSlug=demo-cafe
        [HttpGet("{id:guid}/qr-url")]
        public async Task<IActionResult> GetQrUrl(Guid id, [FromQuery] string tenantSlug)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            if (string.IsNullOrWhiteSpace(tenantSlug))
                return BadRequest("tenantSlug is required.");

            var table = await _db.Set<TableEntity>()
                .SingleOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == id);

            if (table == null) return NotFound();

            var url = $"{PublicBaseUrl}/v/{tenantSlug}/t/{table.TableCode}";
            return Ok(new { url });
        }

        // ✅ QR PNG
        // /staff/tables/{id}/qr-png?tenantSlug=demo-cafe
        [HttpGet("{id:guid}/qr-png")]
        public async Task<IActionResult> GetQrPng(Guid id, [FromQuery] string tenantSlug)
        {
            var tenantId = _tenant.TenantId;
            if (tenantId == null || tenantId == Guid.Empty)
                return Unauthorized("Tenant not set");

            if (string.IsNullOrWhiteSpace(tenantSlug))
                return BadRequest("tenantSlug is required.");

            var table = await _db.Set<TableEntity>()
                .SingleOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == id);

            if (table == null) return NotFound();

            var url = $"{PublicBaseUrl}/v/{tenantSlug}/t/{table.TableCode}";

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);

            var pngQr = new PngByteQRCode(data);
            byte[] bytes = pngQr.GetGraphic(20);

            return File(bytes, "image/png", $"table-{table.DisplayNumber}.png");
        }

        private async Task<bool> HasActiveOrdersAsync(Guid tableId)
        {
            return await _db.Orders.AnyAsync(o =>
                o.TableId == tableId &&
                ActiveOrderStatuses.Contains(o.Status));
        }
    }
}
