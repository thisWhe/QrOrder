using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace QrOrder.Web.Realtime
{
    [Authorize]
    public class StaffOrdersHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantId, out var parsedTenantId) || parsedTenantId == Guid.Empty)
                throw new HubException("Tenant claim is missing or invalid.");

            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{parsedTenantId}");

            await base.OnConnectedAsync();
        }
    }
}
