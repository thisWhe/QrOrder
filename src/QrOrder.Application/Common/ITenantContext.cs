using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Application.Common
{
    public interface ITenantContext
    {
        Guid? TenantId { get; set; }
    }
}
