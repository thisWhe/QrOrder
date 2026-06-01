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
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
            }

            await base.OnConnectedAsync();
        }
    }
}
