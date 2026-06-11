using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Domain.Abstractions;
using QrOrder.Domain.Entities;
using QrOrder.Infrastructure.Auth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QrOrder.Infrastructure.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {
        private readonly ITenantContext _tenantContext;

        public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        // QueryFilter için property (EF bunu kullanabiliyor)
        public Guid CurrentTenantId => _tenantContext.TenantId ?? Guid.Empty;

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<Table> Tables => Set<Table>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<TableSession> TableSessions => Set<TableSession>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<ServiceCall> ServiceCalls => Set<ServiceCall>();



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);


            modelBuilder.Entity<Order>()
                .HasOne(o => o.Table)
                .WithMany()
                .HasForeignKey(o => o.TableId)
                .OnDelete(DeleteBehavior.NoAction); // veya NoAction

            modelBuilder.Entity<Order>()
                .HasOne(o => o.TableSession)
                .WithMany()
                .HasForeignKey(o => o.TableSessionId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ServiceCall>()
                .HasOne(c => c.Table)
                .WithMany()
                .HasForeignKey(c => c.TableId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ServiceCall>()
                .HasOne(c => c.TableSession)
                .WithMany()
                .HasForeignKey(c => c.TableSessionId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ServiceCall>()
                .Property(c => c.Message)
                .HasMaxLength(250);

            modelBuilder.Entity<ServiceCall>()
                .HasIndex(c => new { c.TenantId, c.Status, c.CreatedAt });

            // Tenant unique
            modelBuilder.Entity<Tenant>()
                .HasIndex(x => x.Slug)
                .IsUnique();

            // Table
            modelBuilder.Entity<Table>()
                .HasIndex(x => x.TableCode)
                .IsUnique();

            modelBuilder.Entity<Table>()
                .HasIndex(x => new { x.TenantId, x.DisplayNumber })
                .IsUnique();

            // Decimal precision
            modelBuilder.Entity<Product>()
                .Property(x => x.Price)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Product>()
                .Property(x => x.ImageUrl)
                .HasMaxLength(500);

            modelBuilder.Entity<Order>()
                .Property(x => x.TotalAmount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<OrderItem>()
                .Property(x => x.UnitPriceSnapshot)
                .HasPrecision(18, 2);

            // Concurrency
            modelBuilder.Entity<Order>()
                .Property(x => x.RowVersion)
                .IsRowVersion();

            // Global tenant filter: ITenantEntity olan her tablo tenant ile filtrelensin
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                {
                    var method = typeof(AppDbContext)
                        .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .MakeGenericMethod(entityType.ClrType);

                    method.Invoke(this, new object[] { modelBuilder });
                }
            }
        }

        private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder)
            where TEntity : class, ITenantEntity
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        }


    }
}
