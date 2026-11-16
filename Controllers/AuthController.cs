using System.Security.Claims;
using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Folio.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Folio.Web.Controllers;

public sealed class AuthController : Controller
{
    private readonly Repository _repository;
    private readonly IConfiguration _configuration;
    private readonly RateLimiter _limiter;
    private readonly ILogger<AuthController> _logger;

    public AuthController(Repository repository, IConfiguration configuration, RateLimiter limiter, ILogger<AuthController> logger)
    {
        _repository = repository;
        _configuration = configuration;
        _limiter = limiter;
        _logger = logger;
    }

    private bool GoogleEnabled =>
        !string.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_configuration["Authentication:Google:ClientSecret"]);

    [HttpGet("/adminLogin")]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true && User.IsAdmin())
        {
            TempData["Warning"] = "You're already signed in to the editorial desk.";
            return Redirect("/admin");
        }
        return View(new AdminLoginForm());
    }

    [HttpGet("/login")]
    public IActionResult LegacyLogin() => Redirect("/adminLogin");

    [HttpPost("/adminLogin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(AdminLoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var ip = Sanitizer.HashToken(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local");
        try
        {
            _limiter.Check($"admin-login:{ip}", 6, TimeSpan.FromMinutes(5));
            _limiter.Check($"admin-login:{ip}:{Sanitizer.CleanText(form.Username, 50).ToLowerInvariant()}", 6, TimeSpan.FromMinutes(5));
        }
        catch (ApiException limited)
        {
            ModelState.AddModelError(string.Empty, limited.Message);
            return View(form);
        }

        var admin = await _repository.FindAdminForLoginAsync(Sanitizer.CleanText(form.Username, 50));
        var passwordValid = admin?.PasswordHash is not null && BCrypt.Net.BCrypt.Verify(form.Password, admin.PasswordHash);

        if (admin is null || !passwordValid)
        {
            _logger.LogWarning("Failed admin sign-in for {Username} from {Ip}", Sanitizer.CleanText(form.Username, 50), ip);
            ModelState.AddModelError(string.Empty, "We couldn't sign you in. Please check your username and password and try again.");
            return View(form);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id),
            new(ClaimTypes.Name, admin.Username),
            new(ClaimTypes.Role, admin.Role),
            new("avatar", admin.AvatarUrl ?? string.Empty),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

        _logger.LogInformation("Admin {Username} signed in", admin.Username);
        TempData["Success"] = $"Signed in as {admin.Username}. You're in the editorial desk.";
        return Redirect("/admin");
    }

    [HttpGet("/auth/google")]
    public IActionResult Google(string? returnUrl)
    {
        if (!GoogleEnabled) return Redirect("/?error=google_not_configured");
        var redirect = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) ? "/" : returnUrl;
        return Challenge(new AuthenticationProperties { RedirectUri = redirect }, "Google");
    }

    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "You've been securely signed out.";
        return Redirect("/");
    }

    [HttpGet("/denied")]
    [Authorize]
    public IActionResult Denied()
    {
        TempData["Warning"] = "That area is limited to press editors. Sign in to continue.";
        return Redirect("/adminLogin");
    }
}
