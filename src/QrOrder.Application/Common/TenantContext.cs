using QrOrder.Domain.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Application.Common
{
    public class TenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
    }
}
