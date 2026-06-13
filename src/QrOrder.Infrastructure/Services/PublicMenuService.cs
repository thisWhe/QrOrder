using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Application.Public;
using QrOrder.Domain.Enums;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Infrastructure.Services
{
    public class PublicMenuService : IPublicMenuService
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;
        private readonly IBusinessHoursService _businessHours;

        public PublicMenuService(
            AppDbContext db,
            ITenantContext tenantContext,
            IBusinessHoursService businessHours)
        {
            _db = db;
            _tenantContext = tenantContext;
            _businessHours = businessHours;
        }

        public async Task<PublicMenuDto?> GetMenuAsync(string tenantSlug, CancellationToken cancellationToken = default)
        {
            var tenant = await _db.Tenants
                .SingleOrDefaultAsync(t => t.Slug == tenantSlug && t.IsActive, cancellationToken);

            if (tenant == null) return null;

            _tenantContext.TenantId = tenant.Id;
            var businessStatus = await _businessHours.EvaluateAsync(
                tenant.Id,
                tenant.TimeZoneId,
                cancellationToken);

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
                    Product = new PublicMenuProductDto(
                        p.Id,
                        p.Name,
                        p.Description,
                        p.ImageUrl,
                        p.Price,
                        p.IsAvailable,
                        p.Ingredients,
                        p.PortionInfo,
                        p.Calories,
                        (int)p.AllergenFlags,
                        p.ContainsAlcohol,
                        p.ContainsPork,
                        p.IsVegetarian,
                        p.IsVegan,
                        (int)p.ServingTemperature)
                })
                .ToListAsync(cancellationToken);

            var productsByCategory = products
                .GroupBy(p => p.CategoryId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Product).ToList());

            var productLookup = products
                .Select(x => x.Product)
                .ToDictionary(x => x.Id);
            var publicProductIds = productLookup.Keys.ToList();
            var bestSellerIds = await _db.OrderItems
                .Where(item => item.Order.Status != OrderStatus.Canceled && publicProductIds.Contains(item.ProductId))
                .GroupBy(item => item.ProductId)
                .OrderByDescending(group => group.Sum(item => item.Quantity))
                .Select(group => group.Key)
                .Take(6)
                .ToListAsync(cancellationToken);
            var bestSellers = bestSellerIds
                .Where(productLookup.ContainsKey)
                .Select(id => productLookup[id])
                .ToList();

            if (bestSellers.Count == 0)
                bestSellers = products.Select(x => x.Product).Take(6).ToList();

            var menuCategories = categories
                .Select(c => new PublicMenuCategoryDto(
                    c.Id,
                    c.Name,
                    productsByCategory.TryGetValue(c.Id, out var categoryProducts)
                        ? categoryProducts
                        : new List<PublicMenuProductDto>()))
                .Where(c => c.Products.Count > 0)
                .ToList();

            var orderingEnabled = tenant.IsOrderingEnabled && businessStatus.IsOpen;
            var orderingMessage = !tenant.IsOrderingEnabled
                ? "Su anda online siparis alinmiyor. Menuyu inceleyebilirsiniz."
                : businessStatus.ClosedMessage;

            return new PublicMenuDto(
                tenant.Name,
                tenant.Slug,
                orderingEnabled,
                tenant.ShowProductDetails,
                orderingMessage,
                tenant.PrimaryColor,
                tenant.AccentColor,
                tenant.LogoUrl,
                tenant.HeroImageUrl,
                bestSellers,
                menuCategories);
        }
    }
}
