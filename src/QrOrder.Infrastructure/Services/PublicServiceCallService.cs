using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Application.Security;
using QrOrder.Application.ServiceCalls;
using QrOrder.Domain.Entities;
using QrOrder.Domain.Enums;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Infrastructure.Services
{
    public class PublicServiceCallService : IPublicServiceCallService
    {
        private const int MaxMessageLength = 250;

        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;

        public PublicServiceCallService(AppDbContext db, ITenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async Task<ServiceCallDto> CreateAsync(
            CreateServiceCallRequest request,
            CancellationToken cancellationToken = default)
        {
            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == request.TenantSlug && t.IsActive, cancellationToken)
                ?? throw new InvalidOperationException("Tenant not found.");

            _tenantContext.TenantId = tenant.Id;

            if (string.IsNullOrWhiteSpace(request.SessionToken))
                throw new UnauthorizedAccessException("Session token is required.");

            if (!string.IsNullOrWhiteSpace(request.Message) && request.Message.Length > MaxMessageLength)
                throw new InvalidOperationException("Service call message is too long.");

            var tokenHash = SessionTokenUtil.Sha256(request.SessionToken);
            var session = await _db.TableSessions
                .Include(s => s.Table)
                .SingleOrDefaultAsync(s => s.SessionTokenHash == tokenHash && s.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

            if (session == null)
                throw new UnauthorizedAccessException("Invalid or expired session.");

            if (!session.Table.IsActive)
                throw new UnauthorizedAccessException("Table is inactive.");

            var existingOpenCall = await _db.ServiceCalls
                .Include(c => c.Table)
                .Where(c =>
                    c.TableSessionId == session.Id &&
                    c.Status == ServiceCallStatus.Open)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingOpenCall != null)
                return ToDto(existingOpenCall);

            var call = new ServiceCall
            {
                TenantId = tenant.Id,
                TableId = session.TableId,
                TableSessionId = session.Id,
                Status = ServiceCallStatus.Open,
                Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim()
            };

            _db.ServiceCalls.Add(call);
            await _db.SaveChangesAsync(cancellationToken);

            call.Table = session.Table;
            return ToDto(call);
        }

        private static ServiceCallDto ToDto(ServiceCall call)
        {
            return new ServiceCallDto(
                call.Id,
                call.TenantId,
                call.Status,
                call.Table.DisplayNumber,
                call.Message,
                call.CreatedAt,
                call.CompletedAt);
        }
    }
}
