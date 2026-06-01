using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Domain.Enums
{
    public enum OrderStatus
    {
        New = 0,
        Preparing = 1,
        Ready = 2,
        Delivered = 3,
        Canceled = 4
    }
}
