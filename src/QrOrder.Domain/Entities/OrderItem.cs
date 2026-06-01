using QrOrder.Domain.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Domain.Entities
{
    public class OrderItem : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }

        public Guid OrderId { get; set; }
        public Order Order { get; set; } = default!;

        public Guid ProductId { get; set; }

        // Snapshot alanları: ürün adı/fiyatı değişse bile sipariş bozulmasın
        public string ProductNameSnapshot { get; set; } = default!;
        public decimal UnitPriceSnapshot { get; set; }

        public int Quantity { get; set; }
        public string? ItemNote { get; set; }
    }
}
