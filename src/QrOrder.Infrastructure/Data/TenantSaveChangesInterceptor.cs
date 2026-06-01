using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QrOrder.Application.Common;
using QrOrder.Domain.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Infrastructure.Data
{
    public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly ITenantContext _tenant;

        public TenantSaveChangesInterceptor(ITenantContext tenant) => _tenant = tenant;

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            ApplyTenantIds(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ApplyTenantIds(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ApplyTenantIds(DbContext? db)
        {
            if (db == null) return;
            if (_tenant.TenantId is null) return;

            var tenantId = _tenant.TenantId.Value;

            foreach (var entry in db.ChangeTracker.Entries<ITenantEntity>())
            {
                // Sadece Added için set ediyoruz (Update'de tenant değiştirilmesin)
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity.TenantId == Guid.Empty)
                        entry.Entity.TenantId = tenantId;
                }
            }
        }
    }
}
