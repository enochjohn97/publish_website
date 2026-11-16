using Folio.Web.Infrastructure;

namespace Folio.Web.Services;

public sealed class ImageService
{
    public const long MaxBytes = 4 * 1024 * 1024;
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
    };

    public async Task<(byte[] Bytes, string Mime)> ReadCoverAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiException.Validation("A cover image is required.", new() { ["cover"] = "A cover image is required." });
        }
        if (file.Length > MaxBytes)
        {
            throw new ApiException(413, "PAYLOAD_TOO_LARGE", "That image is too large. Please upload one under 4 MB.",
                new() { ["cover"] = "Cover image must be 4 MB or smaller." });
        }
        if (!AllowedTypes.Contains(file.ContentType))
        {
            throw ApiException.Validation("Covers must be a JPG, PNG or WebP image.",
                new() { ["cover"] = "Only JPG, PNG or WebP images are allowed." });
        }

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory);
        var bytes = memory.ToArray();
        if (bytes.Length > MaxBytes)
        {
            throw new ApiException(413, "PAYLOAD_TOO_LARGE", "That image is too large. Please upload one under 4 MB.");
        }

        var detected = Detect(bytes);
        if (detected is null || !string.Equals(detected, file.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("That file doesn't look like a valid image.",
                new() { ["cover"] = "The file content doesn't match a JPG, PNG or WebP image." });
        }
        return (bytes, detected);
    }

    private static string? Detect(byte[] bytes)
    {
        if (bytes.Length < 12) return null;
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
            bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P') return "image/webp";
        return null;
    }
}
