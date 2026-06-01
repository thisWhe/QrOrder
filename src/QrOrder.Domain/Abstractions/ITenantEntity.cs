using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Domain.Abstractions
{
    public interface ITenantEntity
    {
        Guid TenantId { get; set; }
    }
}
