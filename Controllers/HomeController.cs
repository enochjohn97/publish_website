using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly Repository _repository;
    private readonly ILogger<HomeController> _logger;

    public HomeController(Repository repository, ILogger<HomeController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var user = await User.CurrentUserAsync(_repository);
        var books = await _repository.ListBooksAsync(new SearchFilters { Limit = 24 });
        var viewModel = new HomeViewModel
        {
            Books = books,
            Categories = await _repository.ListCategoriesAsync(),
            Stats = await _repository.GetCountsAsync(admin: false),
            RecentNotes = await _repository.ListRecentAcceptedAsync(3),
            User = user,
        };
        ViewData["Notice"] = Request.Query["notice"].ToString() is { Length: > 0 } notice ? notice : null;
        ViewData["Error"] = Request.Query["error"].ToString() is { Length: > 0 } error ? error : null;
        return View(viewModel);
    }

    [HttpGet("/error/{code:int?}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error(int? code)
    {
        var status = code ?? 500;
        Response.StatusCode = status;
        ViewData["Status"] = status;
        if (status >= 500)
        {
            _logger.LogError("Request {Path} failed with status {Status}", Request.Path, status);
        }
        return View();
    }

    [HttpGet("/health")]
    public async Task<IActionResult> Health()
    {
        try
        {
            await _repository.GetCountsAsync(admin: false);
            return Json(new { ok = true, database = "up", timestamp = DateTime.UtcNow });
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Health check failed");
            return StatusCode(503, new { ok = false, database = "down" });
        }
    }
}
