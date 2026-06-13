namespace QrOrder.Application.Public
{
    public record PublicMenuDto(
        string Tenant,
        string TenantSlug,
        bool IsOrderingEnabled,
        bool ShowProductDetails,
        string? OrderingStatusMessage,
        string PrimaryColor,
        string AccentColor,
        string? LogoUrl,
        string? HeroImageUrl,
        List<PublicMenuProductDto> BestSellers,
        List<PublicMenuCategoryDto> Categories);

    public record PublicMenuCategoryDto(Guid Id, string Name, List<PublicMenuProductDto> Products);

    public record PublicMenuProductDto(
        Guid Id,
        string Name,
        string? Description,
        string? ImageUrl,
        decimal Price,
        bool IsAvailable,
        string? Ingredients,
        string? PortionInfo,
        int? Calories,
        int AllergenFlags,
        bool ContainsAlcohol,
        bool ContainsPork,
        bool IsVegetarian,
        bool IsVegan,
        int ServingTemperature);
}
