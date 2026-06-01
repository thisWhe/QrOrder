using Microsoft.EntityFrameworkCore;
using QrOrder.Application.ServiceCalls;
using QrOrder.Domain.Entities;
using QrOrder.Domain.Enums;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Infrastructure.Services
{
    public class StaffServiceCallService : IStaffServiceCallService
    {
        private readonly AppDbContext _db;

        public StaffServiceCallService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<ServiceCallDto>> ListAsync(
            bool activeOnly,
            CancellationToken cancellationToken = default)
        {
            var query = _db.ServiceCalls
                .Include(c => c.Table)
                .OrderBy(c => c.CreatedAt)
                .AsQueryable();

            if (activeOnly)
            {
                query = query.Where(c => c.Status == ServiceCallStatus.Open);
            }

            return await query
                .Take(100)
                .Select(c => new ServiceCallDto(
                    c.Id,
                    c.TenantId,
                    c.Status,
                    c.Table.DisplayNumber,
                    c.Message,
                    c.CreatedAt,
                    c.CompletedAt))
                .ToListAsync(cancellationToken);
        }

        public async Task<ServiceCallDto?> CompleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var call = await _db.ServiceCalls
                .Include(c => c.Table)
                .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

            if (call == null) return null;

            if (call.Status == ServiceCallStatus.Completed)
            {
                return ToDto(call);
            }

            call.Status = ServiceCallStatus.Completed;
            call.CompletedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
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
