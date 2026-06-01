namespace QrOrder.Application.Public
{
    public interface IPublicMenuService
    {
        Task<PublicMenuDto?> GetMenuAsync(string tenantSlug, CancellationToken cancellationToken = default);
    }
}
