namespace QrOrder.Application.Public
{
    public interface IPublicTableSessionService
    {
        Task<CreateTableSessionResult?> CreateAsync(string tenantSlug, string tableCode, CancellationToken cancellationToken = default);
        Task<CreateTableSessionResult?> ValidateAsync(string tenantSlug, string tableCode, string sessionToken, CancellationToken cancellationToken = default);
    }
}
