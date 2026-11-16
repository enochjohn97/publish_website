namespace Folio.Web.Infrastructure;

/// <summary>Cookie/header pair used by the JSON API double-submit CSRF check.</summary>
public static class ApiCsrf
{
    public const string CookieName = "folio_api_csrf";
    public const string HeaderName = "RequestVerificationToken";
    public const string ItemKey = "ApiCsrfToken";
}
