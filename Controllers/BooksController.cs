using System.Text.Json;
using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Web.Controllers;

public sealed class BooksController : Controller
{
    private readonly Repository _repository;
    private readonly ILogger<BooksController> _logger;

    public BooksController(Repository repository, ILogger<BooksController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("/books/{slug}")]
    public async Task<IActionResult> Details(string slug)
    {
        var user = await User.CurrentUserAsync(_repository);
        var book = await _repository.GetBookBySlugAsync(slug);
        if (book is null) return NotFound();
        if (book.Status == "draft" && user?.Role != "admin") return NotFound();

        var chapters = await _repository.ListChaptersAsync(book.Id, includeDrafts: user?.Role == "admin");

        // Clicking a book opens it straight in the reader (page/book mode); the details
        // page is only shown when there is nothing published to read yet.
        var firstChapter = chapters.FirstOrDefault(chapter => chapter.Status == "published") ?? chapters.FirstOrDefault();
        var wantsDetails = string.Equals(Request.Query["view"], "details", StringComparison.OrdinalIgnoreCase);
        if (firstChapter is not null && firstChapter.Status == "published" && !wantsDetails)
        {
            return RedirectToAction(nameof(Read), new { slug, chapterNumber = firstChapter.ChapterNumber });
        }

        return View(new BookPageViewModel
        {
            Book = book,
            Chapters = chapters,
            Contributors = await _repository.ListContributorsAsync(book.Id),
            User = user,
        });
    }

    [HttpGet("/books/{slug}/{chapterNumber:int}")]
    public async Task<IActionResult> Read(string slug, int chapterNumber)
    {
        var user = await User.CurrentUserAsync(_repository);
        var book = await _repository.GetBookBySlugAsync(slug);
        if (book is null) return NotFound();
        if (book.Status == "draft" && user?.Role != "admin") return NotFound();
        if (chapterNumber < 1) return NotFound();

        var chapter = await _repository.GetChapterAsync(book.Id, chapterNumber);
        if (chapter is null) return NotFound();
        if (chapter.Status != "published" && user?.Role != "admin") return NotFound();

        var blocks = ParseBlocks(chapter.ContentJson, chapter.Id);
        return View(new ReaderViewModel
        {
            Book = book,
            Chapter = chapter,
            Blocks = blocks,
            Pages = Paginate(blocks),
            Chapters = await _repository.ListChaptersAsync(book.Id, includeDrafts: user?.Role == "admin"),
            // Published marginalia for everyone, plus the reader's own notes awaiting review.
            Comments = await _repository.ListChapterCommentsAsync(chapter.Id, includePending: false, viewerUserId: user?.Id),
            User = user,
        });
    }

    /// <summary>Groups paragraphs into stable reading pages (~1,100 characters, never splitting a paragraph).</summary>
    private static List<ReaderPage> Paginate(List<ChapterBlock> blocks)
    {
        const int targetCharacters = 1100;
        var pages = new List<ReaderPage>();
        var current = new List<ChapterBlock>();
        var characters = 0;

        foreach (var block in blocks)
        {
            current.Add(block);
            characters += block.Text.Length;
            if (characters < targetCharacters) continue;
            pages.Add(new ReaderPage { Number = pages.Count + 1, Blocks = current.ToArray() });
            current.Clear();
            characters = 0;
        }
        if (current.Count > 0)
        {
            pages.Add(new ReaderPage { Number = pages.Count + 1, Blocks = current.ToArray() });
        }
        if (pages.Count == 0)
        {
            pages.Add(new ReaderPage { Number = 1, Blocks = Array.Empty<ChapterBlock>() });
        }
        return pages;
    }

    private List<ChapterBlock> ParseBlocks(string json, string chapterId)
    {
        try
        {
            var blocks = JsonSerializer.Deserialize<List<ChapterBlock>>(json, JsonDefaults.Options) ?? new List<ChapterBlock>();
            for (var index = 0; index < blocks.Count; index++)
            {
                blocks[index].Id = string.IsNullOrWhiteSpace(blocks[index].Id) ? $"b{index + 1}" : blocks[index].Id;
            }
            return blocks;
        }
        catch (JsonException error)
        {
            _logger.LogError(error, "Chapter {ChapterId} has malformed content JSON", chapterId);
            return new List<ChapterBlock>();
        }
    }
}
