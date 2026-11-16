using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Folio.Web.Models;

namespace Folio.Web.Services;

/// <summary>Normalises untrusted input before it is validated, stored or compared.</summary>
public static partial class Sanitizer
{
    [GeneratedRegex("<[^>]*>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex("[\\u0000-\\u0008\\u000B\\u000C\\u000E-\\u001F\\u007F]", RegexOptions.Compiled)]
    private static partial Regex ControlRegex();

    [GeneratedRegex("[^a-zA-Z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonSlugRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    public static string CleanText(string? input, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var decoded = WebUtility.HtmlDecode(input);
        var stripped = TagRegex().Replace(decoded, " ");
        var withoutControl = ControlRegex().Replace(stripped, " ");
        var normalized = WhitespaceRegex().Replace(withoutControl, " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public static string CleanMultiline(string? input, int maxLength = 20000)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var decoded = WebUtility.HtmlDecode(input).Replace("\r\n", "\n");
        var stripped = TagRegex().Replace(decoded, " ");
        var cleaned = ControlRegex().Replace(stripped, " ").Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    public static string Slugify(string input)
    {
        var clean = CleanText(input, 160).ToLowerInvariant();
        var slug = NonSlugRegex().Replace(clean, "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "untitled" : slug[..Math.Min(80, slug.Length)];
    }

    public static List<ChapterBlock> ToBlocks(string input)
    {
        var blocks = new List<ChapterBlock>();
        foreach (var paragraph in input.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = CleanText(paragraph, 5000);
            if (string.IsNullOrWhiteSpace(text)) continue;
            blocks.Add(new ChapterBlock { Id = $"b{blocks.Count + 1}", Text = text });
        }
        return blocks;
    }

    public static string Mask(string value) => string.IsNullOrEmpty(value) ? "—" : new string('•', Math.Min(value.Length, 8));

    public static string HashToken(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..24];
    }
}
