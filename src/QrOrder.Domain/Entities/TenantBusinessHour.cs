using QrOrder.Domain.Abstractions;

namespace QrOrder.Domain.Entities
{
    public class TenantBusinessHour : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public bool IsOpen { get; set; } = true;
        public TimeOnly OpenTime { get; set; } = new(0, 0);
        public TimeOnly CloseTime { get; set; } = new(0, 0);
    }
}
