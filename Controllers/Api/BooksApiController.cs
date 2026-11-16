using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Folio.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Web.Controllers.Api;

[Route("api/books")]
public sealed class BooksApiController : ControllerBase
{
    private readonly Repository _repository;

    public BooksApiController(Repository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? category, [FromQuery] string? status)
    {
        var user = await User.CurrentUserAsync(_repository);
        var books = await _repository.ListBooksAsync(new SearchFilters
        {
            Search = Sanitizer.CleanText(q, 120),
            Category = Sanitizer.CleanText(category, 60),
            Status = Sanitizer.CleanText(status, 20),
            IncludeDrafts = user?.Role == "admin",
            Limit = 24,
        });

        return new JsonResult(ApiResponse.Success(new { books, total = books.Count }));
    }
}
