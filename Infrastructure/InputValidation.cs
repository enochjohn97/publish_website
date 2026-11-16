using System.ComponentModel.DataAnnotations;
using Folio.Web.Services;

namespace Folio.Web.Infrastructure;

public static class InputValidation
{
    /// <summary>Runs DataAnnotations over an already-sanitized model and throws a friendly 400.</summary>
    public static void EnsureValid(object model, string message = "Please correct the highlighted fields.")
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        if (Validator.TryValidateObject(model, context, results, validateAllProperties: true)) return;

        var fields = new Dictionary<string, string>();
        foreach (var result in results)
        {
            var key = result.MemberNames.FirstOrDefault() ?? "form";
            fields.TryAdd(char.ToLowerInvariant(key[0]) + key[1..], result.ErrorMessage ?? "This field isn't valid.");
        }
        throw ApiException.Validation(message, fields);
    }

    public static string CleanCategory(string? value, IReadOnlyList<string> allowed)
    {
        var cleaned = Sanitizer.CleanText(value, 60);
        if (!allowed.Contains(cleaned))
        {
            throw ApiException.Validation("Choose a category for this book.",
                new() { ["category"] = "Choose a category for this book." });
        }
        return cleaned;
    }

    public static string CleanStatus(string? value)
    {
        var cleaned = Sanitizer.CleanText(value, 20);
        return cleaned is "draft" or "ongoing" or "completed" ? cleaned : "ongoing";
    }

    public static DateTime? ParseIsoDate(string? value) =>
        DateTime.TryParse(value, out var date) ? date.ToUniversalTime() : null;
}
