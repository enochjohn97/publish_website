using Microsoft.AspNetCore.Antiforgery;

namespace Folio.Web.Services;

/// <summary>
/// Issues exactly one antiforgery token per request. Generating several tokens in a single
/// response rotates the cookie and invalidates earlier tokens, so every form/meta tag shares this one.
/// </summary>
public sealed class CsrfToken
{
    private readonly IAntiforgery _antiforgery;
    private readonly IHttpContextAccessor _accessor;
    private string? _token;

    public CsrfToken(IAntiforgery antiforgery, IHttpContextAccessor accessor)
    {
        _antiforgery = antiforgery;
        _accessor = accessor;
    }

    public string Value
    {
        get
        {
            if (_token is not null) return _token;
            var context = _accessor.HttpContext
                ?? throw new InvalidOperationException("No active HTTP context for antiforgery token generation.");
            _token = _antiforgery.GetAndStoreTokens(context).RequestToken ?? string.Empty;
            return _token;
        }
    }
}
