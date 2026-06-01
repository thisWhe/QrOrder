using Microsoft.AspNetCore.Identity;
using QrOrder.Domain.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Infrastructure.Auth
{
    public class ApplicationUser : IdentityUser<Guid>, ITenantEntity
    {
        public Guid TenantId { get; set; }
    }
}
