using QrOrder.Domain.Abstractions;
using QrOrder.Domain.Enums;

namespace QrOrder.Domain.Entities
{
    public class ServiceCall : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }

        public Guid TableId { get; set; }
        public Table Table { get; set; } = default!;

        public Guid TableSessionId { get; set; }
        public TableSession TableSession { get; set; } = default!;

        public ServiceCallStatus Status { get; set; } = ServiceCallStatus.Open;
        public string? Message { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
