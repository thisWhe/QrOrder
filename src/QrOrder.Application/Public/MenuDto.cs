namespace QrOrder.Application.Public
{
    public record PublicMenuDto(
        string Tenant,
        string TenantSlug,
        bool IsOrderingEnabled,
        List<PublicMenuCategoryDto> Categories);

    public record PublicMenuCategoryDto(Guid Id, string Name, List<PublicMenuProductDto> Products);

    public record PublicMenuProductDto(Guid Id, string Name, string? Description, decimal Price, bool IsAvailable);
}
