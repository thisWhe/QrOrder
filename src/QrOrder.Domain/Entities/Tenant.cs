using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Domain.Entities
{
    public class Tenant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = default!;
        public string Slug { get; set; } = default!; // URL'de kullanılacak (unique)
        public bool IsActive { get; set; } = true;
        public bool IsOrderingEnabled { get; set; } = true;
        public int TableSessionHours { get; set; } = 12;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
