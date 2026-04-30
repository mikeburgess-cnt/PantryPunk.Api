using SixLabors.ImageSharp;

namespace PantryPunk.Api.Services;

public sealed record ImageValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public byte[]? Bytes { get; init; }
    public string? MediaType { get; init; }
    public string? Extension { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public static ImageValidationResult Fail(string error) =>
        new() { IsValid = false, Error = error };

    public static ImageValidationResult Ok(byte[] bytes, string mediaType, string extension, int width, int height) =>
        new()
        {
            IsValid = true,
            Bytes = bytes,
            MediaType = mediaType,
            Extension = extension,
            Width = width,
            Height = height
        };
}

public sealed class ImageFileValidator
{
    private const int MaxBytes = 3 * 1024 * 1024;
    private const int MaxDimension = 8192;

    private static readonly HashSet<string> AllowedMediaTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public async Task<ImageValidationResult> ValidateAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ImageValidationResult.Fail("No image provided.");

        if (file.Length > MaxBytes)
            return ImageValidationResult.Fail("Image must be under 3MB.");

        if (string.IsNullOrWhiteSpace(file.ContentType) || !AllowedMediaTypes.Contains(file.ContentType))
            return ImageValidationResult.Fail("Unsupported image format.");

        byte[] bytes;
        using (var ms = new MemoryStream((int)file.Length))
        {
            await using var stream = file.OpenReadStream();
            await stream.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var sniffed = SniffFormat(bytes);
        if (sniffed is null)
            return ImageValidationResult.Fail("Unsupported image format.");

        if (!string.Equals(sniffed.Value.MediaType, file.ContentType, StringComparison.OrdinalIgnoreCase))
            return ImageValidationResult.Fail("Content-Type does not match image contents.");

        ImageInfo info;
        try
        {
            info = Image.Identify(bytes);
        }
        catch
        {
            return ImageValidationResult.Fail("Image could not be decoded.");
        }

        if (info is null)
            return ImageValidationResult.Fail("Image could not be decoded.");

        var decodedMime = info.Metadata?.DecodedImageFormat?.DefaultMimeType;
        if (!string.Equals(decodedMime, sniffed.Value.MediaType, StringComparison.OrdinalIgnoreCase))
            return ImageValidationResult.Fail("Image format mismatch.");

        if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension)
            return ImageValidationResult.Fail("Image dimensions exceed limit.");

        return ImageValidationResult.Ok(bytes, sniffed.Value.MediaType, sniffed.Value.Extension, info.Width, info.Height);
    }

    private static (string MediaType, string Extension)? SniffFormat(byte[] bytes)
    {
        if (bytes.Length < 12) return null;

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", "jpg");

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return ("image/png", "png");

        // WebP: "RIFF" ???? "WEBP"
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return ("image/webp", "webp");

        return null;
    }
}
