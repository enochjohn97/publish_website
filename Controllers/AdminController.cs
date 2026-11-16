using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Folio.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Folio.Web.Controllers;

[Authorize(Roles = "admin")]
public sealed class AdminController : Controller
{
    private readonly Repository _repository;
    private readonly ILogger<AdminController> _logger;

    public AdminController(Repository repository, ILogger<AdminController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("/admin")]
    public async Task<IActionResult> Index()
    {
        // Defence in depth: the [Authorize] role filter plus a live database role check. A reader
        // or unauthorised visitor never sees the dashboard, even with a stale cookie.
        var current = await User.CurrentUserAsync(_repository);
        if (current is null) return Redirect("/adminLogin");
        if (current.Role != "admin")
        {
            _logger.LogWarning("Non-admin {UserId} attempted to reach /admin from {Ip}", current.Id, Sanitizer.HashToken(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local"));
            return Redirect("/?error=admin_only");
        }

        var admin = current;
        var books = await _repository.ListBooksAsync(new SearchFilters { IncludeDrafts = true, Limit = 60 });

        ViewData["Notice"] = Request.Query["notice"].ToString() is { Length: > 0 } notice ? notice : null;

        return View(new AdminViewModel
        {
            Admin = admin,
            Stats = await _repository.GetCountsAsync(admin: true),
            Books = books,
            Pending = await _repository.ListPendingAsync(),
            Recent = await _repository.ListRecentModeratedAsync(8),
            QueueCount = await _repository.GetPendingCountAsync(),
            NextChapterNumber = books.Count == 0 ? 1 : 1,
        });
    }
}
