using QrOrder.Domain.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Domain.Entities
{
    public class Table : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }

        public int DisplayNumber { get; set; }      // Masada yazan: 1,2,3 numara
        public string TableCode { get; set; } = default!; // QR içindeki gizli kod (unique)
        public bool IsActive { get; set; } = true;
    }
}
