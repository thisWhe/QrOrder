using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Application.Public;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Infrastructure.Services
{
    public class PublicMenuService : IPublicMenuService
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;

        public PublicMenuService(AppDbContext db, ITenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async Task<PublicMenuDto?> GetMenuAsync(string tenantSlug, CancellationToken cancellationToken = default)
        {
            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive, cancellationToken);

            if (tenant == null) return null;

            _tenantContext.TenantId = tenant.Id;

            var categories = await _db.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync(cancellationToken);

            var categoryIds = categories.Select(c => c.Id).ToList();
            var products = await _db.Products
                .Where(p => p.IsActive && categoryIds.Contains(p.CategoryId))
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .Select(p => new
                {
                    p.CategoryId,
                    Product = new PublicMenuProductDto(p.Id, p.Name, p.Description, p.Price, p.IsAvailable)
                })
                .ToListAsync(cancellationToken);

            var productsByCategory = products
                .GroupBy(p => p.CategoryId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Product).ToList());

            var menuCategories = categories
                .Select(c => new PublicMenuCategoryDto(
                    c.Id,
                    c.Name,
                    productsByCategory.TryGetValue(c.Id, out var categoryProducts)
                        ? categoryProducts
                        : new List<PublicMenuProductDto>()))
                .Where(c => c.Products.Count > 0)
                .ToList();

            return new PublicMenuDto(tenant.Name, tenant.Slug, tenant.IsOrderingEnabled, menuCategories);
        }
    }
}
