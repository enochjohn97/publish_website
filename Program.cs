using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;

// Schema follows the specification's snake_case column names; map them to PascalCase models.
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection().SetApplicationName("Folio.Web");
builder.Services.AddSingleton<Db>();
builder.Services.AddScoped<Repository>();
builder.Services.AddScoped<BadgeService>();
builder.Services.AddScoped<ImageService>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddScoped<CsrfToken>();
builder.Services.AddScoped<GoogleAuthService>();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "folio_csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.IsEssential = true;
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
var googleEnabled = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

var authBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/adminLogin";
        options.AccessDeniedPath = "/?error=admin_only";
        options.Cookie.Name = "folio_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("");
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync("");
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

if (googleEnabled)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
        options.Events.OnCreatingTicket = async context =>
        {
            var repository = context.HttpContext.RequestServices.GetRequiredService<Repository>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleSignIn");

            using var document = JsonDocument.Parse(context.User.GetRawText());
            var root = document.RootElement;
            string? Read(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            var sub = Read("sub") ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = Read("email");
            var name = Read("name");
            var picture = Read("picture");

            if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Google did not return an email address for this account.");
            }

            var googleAuth = context.HttpContext.RequestServices.GetRequiredService<GoogleAuthService>();
            var existingUser = await repository.FindGoogleUserAsync(sub);
            var wasNew = string.IsNullOrEmpty(existingUser?.Id);
            var user = await googleAuth.UpsertReaderAsync(sub, email!, name, picture, wasNew);
            var logId = user.Id ?? (existingUser?.Id ?? "new");
            logger.LogInformation("Google sign-in {Outcome} for user {UserId}", wasNew ? "created" : "resumed", logId);

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id ?? throw new InvalidOperationException("The signed-in user has no ID.")),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("avatar", user.AvatarUrl ?? string.Empty),
            }, "Google");

            context.Principal = new ClaimsPrincipal(identity);
        };
        options.Events.OnRemoteFailure = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleSignIn");
            logger.LogError(context.Failure, "Google authentication failed");
            context.Response.Redirect("/?error=google_sign_in_failed");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddSession(options =>
{
    options.Cookie.Name = "folio_flash";
    options.Cookie.HttpOnly = true;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: blob: https:; media-src 'self' https:; " +
        "script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' https://accounts.google.com";
    headers.Remove("X-Powered-By");
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers["Cache-Control"] = context.Context.Request.Path.StartsWithSegments("/img")
            ? "public, max-age=2592000"
            : "public, max-age=3600";
    }
});
app.UseRouting();

// Double-submit CSRF for JSON/API mutations. The token lives in a readable cookie and must
// echo in the RequestVerificationToken header; cross-origin pages cannot read it. Same-origin
// checks add defence in depth. HTML forms keep ASP.NET Core's antiforgery tokens.
app.Use(async (context, next) =>
{
    var token = context.Request.Cookies[ApiCsrf.CookieName];
    if (string.IsNullOrEmpty(token))
    {
        token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(ApiCsrf.CookieName, token, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromHours(12),
            IsEssential = true,
        });
    }
    context.Items["ApiCsrfToken"] = token;

    var isMutation = !(HttpMethods.IsGet(context.Request.Method)
        || HttpMethods.IsHead(context.Request.Method)
        || HttpMethods.IsOptions(context.Request.Method));

    if (isMutation && context.Request.Path.StartsWithSegments("/api"))
    {
        var headerToken = context.Request.Headers["RequestVerificationToken"].ToString();
        var origin = context.Request.Headers.Origin.ToString();
        var originAllowed = string.IsNullOrEmpty(origin)
            || Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                && string.Equals(originUri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(headerToken)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(headerToken), System.Text.Encoding.UTF8.GetBytes(token))
            || !originAllowed)
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiCsrf");
            logger.LogWarning("Blocked {Method} {Path}: CSRF token missing or mismatched",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(ApiResponse.Fail(new ApiException(403, "FORBIDDEN",
                "Your security token expired. Refresh the page and try again.")));
            return;
        }
    }

    await next();
});
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Prefer the X-Forwarded-For header when reverse proxies/CDNs forward traffic to the app,
// so admin-ip logging and rate limits resolve the real visitor address.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { },
    KnownProxies = { },
});

// Friendly, non-technical failures for unhandled exceptions; full detail stays in the log.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ApiException apiError)
    {
        if (context.Response.HasStarted) throw;            context.Response.Clear();
        context.Response.StatusCode = apiError.Status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(ApiResponse.Fail(apiError).ToJson() ?? "");
    }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
    {
        // Expired/absent antiforgery token: protect the request without leaking internals.
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Antiforgery");
        logger.LogWarning("Rejected request to {Path}: antiforgery validation failed", context.Request.Path);
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(ApiResponse.Fail(new ApiException(403, "FORBIDDEN",
            "Your security token expired. Refresh the page and try again.")).ToJson() ?? "");
    }
    catch (Exception error)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
        logger.LogError(error, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("");
        }
        else
        {
            context.Response.Redirect("/error/500");
        }
    }
});

try
{
    await StartupSeeder.SeedAsync(app.Services, app.Configuration, app.Logger);
}
catch (Exception error)
{
    app.Logger.LogError(error, "Startup seeding failed; check the database connection string");
}

app.MapControllerRoute(
    name: "chapter",
    pattern: "books/{slug}/{chapterNumber:int}",
    defaults: new { controller = "Books", action = "Read" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapFallback("{*path}", async (context) =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(builder.Environment.WebRootPath, "error.html"));
});

app.Run();
