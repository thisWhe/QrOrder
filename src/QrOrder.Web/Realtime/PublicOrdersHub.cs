using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Application.Security;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Realtime
{
    public class PublicOrdersHub : Hub
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;

        public PublicOrdersHub(AppDbContext db, ITenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async Task JoinOrderStatus(Guid orderId, string tenantSlug, string sessionToken)
        {
            var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive);
            if (tenant == null)
                throw new HubException("Tenant not found.");

            _tenantContext.TenantId = tenant.Id;

            var tokenHash = SessionTokenUtil.Sha256(sessionToken);
            var order = await _db.Orders
                .Include(o => o.TableSession)
                .SingleOrDefaultAsync(o => o.Id == orderId);

            if (order == null || order.TableSession.SessionTokenHash != tokenHash)
                throw new HubException("Invalid order session.");

            await Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderId}");
        }
    }
}
