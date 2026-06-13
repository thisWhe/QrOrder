namespace QrOrder.Web.Storage;

public interface ITenantBrandingStorage
{
    Task<string> SaveAsync(
        Guid tenantId,
        string imageType,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string? imageUrl, CancellationToken cancellationToken = default);
}
