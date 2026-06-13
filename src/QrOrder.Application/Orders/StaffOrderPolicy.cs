using QrOrder.Domain.Enums;

namespace QrOrder.Application.Orders;

public static class StaffOrderPolicy
{
    public static bool CanRoleSetStatus(IEnumerable<string> roles, OrderStatus status)
    {
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roleSet.Contains("Admin")) return true;

        if (roleSet.Contains("Kitchen"))
            return status is OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Canceled;

        if (roleSet.Contains("Service"))
            return status is OrderStatus.Delivered or OrderStatus.Canceled;

        return false;
    }

    public static bool IsValidTransition(OrderStatus current, OrderStatus next)
    {
        if (current == next) return true;
        if (current is OrderStatus.Canceled or OrderStatus.Delivered) return false;
        if (next == OrderStatus.Canceled) return true;

        return (current, next) switch
        {
            (OrderStatus.New, OrderStatus.Preparing) => true,
            (OrderStatus.Preparing, OrderStatus.Ready) => true,
            (OrderStatus.Ready, OrderStatus.Delivered) => true,
            _ => false
        };
    }
}
