using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Application.Public;
using QrOrder.Application.Security;
using QrOrder.Domain.Entities;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Infrastructure.Services
{
    public class PublicTableSessionService : IPublicTableSessionService
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;

        public PublicTableSessionService(AppDbContext db, ITenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async Task<CreateTableSessionResult?> CreateAsync(
            string tenantSlug,
            string tableCode,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive, cancellationToken);

            if (tenant == null) return null;

            _tenantContext.TenantId = tenant.Id;

            var table = await _db.Tables
                .SingleOrDefaultAsync(t => t.TableCode == tableCode && t.IsActive, cancellationToken);

            if (table == null) return null;

            var sessionHours = Math.Clamp(tenant.TableSessionHours, 1, 24);
            var token = SessionTokenUtil.NewToken();
            var session = new TableSession
            {
                TenantId = tenant.Id,
                TableId = table.Id,
                SessionTokenHash = SessionTokenUtil.Sha256(token),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(sessionHours)
            };

            _db.TableSessions.Add(session);
            await _db.SaveChangesAsync(cancellationToken);

            return new CreateTableSessionResult(token, session.ExpiresAt, table.DisplayNumber);
        }

        public async Task<CreateTableSessionResult?> ValidateAsync(
            string tenantSlug,
            string tableCode,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionToken)) return null;

            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive, cancellationToken);

            if (tenant == null) return null;

            _tenantContext.TenantId = tenant.Id;

            var tokenHash = SessionTokenUtil.Sha256(sessionToken);
            var session = await _db.TableSessions
                .Include(s => s.Table)
                .SingleOrDefaultAsync(s =>
                    s.SessionTokenHash == tokenHash &&
                    s.ExpiresAt > DateTimeOffset.UtcNow &&
                    s.Table.TableCode == tableCode &&
                    s.Table.IsActive,
                    cancellationToken);

            if (session == null) return null;

            return new CreateTableSessionResult(sessionToken, session.ExpiresAt, session.Table.DisplayNumber);
        }
    }
}
