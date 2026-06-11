namespace QrOrder.Web.Storage;

public sealed class LocalProductImageStorage : IProductImageStorage
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp"
        };

    private readonly string _webRootPath;

    public LocalProductImageStorage(IWebHostEnvironment environment)
    {
        _webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    }

    public async Task<string> SaveAsync(
        Guid tenantId,
        Guid productId,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(extension, out var expectedContentType) ||
            !string.Equals(contentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only JPEG, PNG and WebP images are allowed.");
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length == 0 || buffer.Length > MaxFileSize)
            throw new InvalidDataException("Image size must be between 1 byte and 5 MB.");

        if (!HasValidSignature(buffer.GetBuffer(), checked((int)buffer.Length), expectedContentType))
            throw new InvalidDataException("Image content does not match its file type.");

        var relativeDirectory = Path.Combine(
            "uploads",
            "products",
            tenantId.ToString("N"),
            productId.ToString("N"));
        var directory = Path.Combine(_webRootPath, relativeDirectory);
        Directory.CreateDirectory(directory);

        var storedFileName = $"{Guid.NewGuid():N}{NormalizeExtension(extension)}";
        var absolutePath = Path.Combine(directory, storedFileName);

        buffer.Position = 0;
        await using var output = new FileStream(
            absolutePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        await buffer.CopyToAsync(output, cancellationToken);

        return "/" + Path.Combine(relativeDirectory, storedFileName).Replace('\\', '/');
    }

    public Task DeleteAsync(string? imageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) ||
            !imageUrl.StartsWith("/uploads/products/", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var relativePath = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_webRootPath, relativePath));
        var uploadsRoot = Path.GetFullPath(Path.Combine(_webRootPath, "uploads", "products"));

        if (!fullPath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private static string NormalizeExtension(string extension) =>
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : extension;

    private static bool HasValidSignature(byte[] bytes, int length, string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
            "image/webp" => length >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            _ => false
        };
    }
}
