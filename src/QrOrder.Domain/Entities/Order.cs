using QrOrder.Domain.Abstractions;
using QrOrder.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Domain.Entities
{
    public class Order : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }

        public Guid TableId { get; set; }
        public Table Table { get; set; } = default!;

        public Guid TableSessionId { get; set; }
        public TableSession TableSession { get; set; } = default!;

        public OrderStatus Status { get; set; } = OrderStatus.New;
        public string? CustomerNote { get; set; }
        public decimal TotalAmount { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        // optimistic concurrency için
        public byte[] RowVersion { get; set; } = default!;

        public List<OrderItem> Items { get; set; } = new();
    }
}
