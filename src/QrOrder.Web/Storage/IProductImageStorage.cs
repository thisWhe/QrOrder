namespace QrOrder.Web.Storage;

public interface IProductImageStorage
{
    Task<string> SaveAsync(
        Guid tenantId,
        Guid productId,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string? imageUrl, CancellationToken cancellationToken = default);
}
