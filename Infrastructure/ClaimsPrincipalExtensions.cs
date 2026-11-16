using System.Security.Claims;
using Folio.Web.Data;
using Folio.Web.Models;

namespace Folio.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static string? UserId(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true ? principal.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true && principal.IsInRole("admin");

    public static string AvatarImageUrl(this ClaimsPrincipal principal) => principal.AvatarUrl() ?? string.Empty;

    public static string DisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? "Reader";

    public static string? AvatarUrl(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("avatar");
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Loads the current user from the database so role changes take effect immediately.</summary>
    public static async Task<UserRecord?> CurrentUserAsync(this ClaimsPrincipal principal, Repository repository)
    {
        var id = principal.UserId();
        return id is null ? null : await repository.FindUserAsync(id);
    }

    public static async Task<UserRecord> RequireUserAsync(this ClaimsPrincipal principal, Repository repository)
    {
        var user = await principal.CurrentUserAsync(repository);
        if (user is null) throw ApiException.Unauthorized();
        return user;
    }

    public static async Task<UserRecord> RequireAdminAsync(this ClaimsPrincipal principal, Repository repository)
    {
        var user = await principal.RequireUserAsync(repository);
        if (user.Role != "admin") throw ApiException.Forbidden("This area is limited to editors.");
        return user;
    }
}

/// <summary>
/// Public bylines for marginalia. A note always belongs to the reader who wrote it, so the reader
/// account name is shown. Editorial accounts are never published as "admin" on a reader page.
/// </summary>
public static class EditorialAttribution
{
    public static string For(CommentRow note) =>
        string.Equals(note.UserRole, "admin", StringComparison.OrdinalIgnoreCase)
            ? "Folio editors"
            : note.Username;

    public static string Initials(string name) =>
        string.IsNullOrWhiteSpace(name) ? "F" : name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
}
