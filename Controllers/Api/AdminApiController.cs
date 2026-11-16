using System.Text.Json;
using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Folio.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Web.Controllers.Api;

[Route("api/admin")]
[Authorize(Roles = "admin")]
public sealed class AdminApiController : ControllerBase
{
    private static readonly string[] Categories =
    {
        "Fiction", "Literary Fiction", "Poetry", "Memoir", "Essay",
        "History", "Science", "Children", "Business", "Art & Design",
    };

    private readonly Repository _repository;
    private readonly BadgeService _badges;
    private readonly ImageService _images;
    private readonly ILogger<AdminApiController> _logger;

    public AdminApiController(Repository repository, BadgeService badges, ImageService images, ILogger<AdminApiController> logger)
    {
        _repository = repository;
        _badges = badges;
        _images = images;
        _logger = logger;
    }

    [HttpPost("books")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> CreateBook([FromForm] BookFormInput? input, IFormFile? cover)
    {
        if (input is null) throw ApiException.Validation("We couldn't read that form. Please try again.");
        var admin = await User.RequireAdminAsync(_repository);

        input.Title = Sanitizer.CleanText(input.Title, 255);
        input.AuthorName = Sanitizer.CleanText(input.AuthorName, 120);
        input.Synopsis = Sanitizer.CleanMultiline(input.Synopsis, 4000);
        try
        {
            input.Category = InputValidation.CleanCategory(input.Category, Categories);
        }
        catch (ApiException error)
        {
            throw ApiException.Validation("Please correct the highlighted fields.", error.Fields);
        }
        input.Status = InputValidation.CleanStatus(input.Status);
        InputValidation.EnsureValid(input);

        var image = await _images.ReadCoverAsync(cover);
        var book = await _repository.CreateBookAsync(input, image.Bytes, image.Mime, admin.Id);
        _logger.LogInformation("Book {BookId} published by {AdminId}", book.Id, admin.Id);
        return new JsonResult(ApiResponse.Success(new { book }));
    }

    [HttpPatch("books/{id}")]
    public async Task<IActionResult> UpdateBookStatus(string id, [FromBody] StatusUpdateRequest? request)
    {
        await User.RequireAdminAsync(_repository);
        if (request is null) throw ApiException.Validation("Please choose a status.");
        var status = Sanitizer.CleanText(request.Status, 20);
        if (status is not ("draft" or "ongoing" or "completed")) throw ApiException.Validation("That status isn't supported.");
        await _repository.UpdateBookStatusAsync(id, status);
        return new JsonResult(ApiResponse.Success(new { updated = true }));
    }

    [HttpDelete("books/{id}")]
    public async Task<IActionResult> DeleteBook(string id)
    {
        var admin = await User.RequireAdminAsync(_repository);
        await _repository.DeleteBookAsync(id);
        _logger.LogInformation("Book {BookId} deleted by {AdminId}", id, admin.Id);
        return new JsonResult(ApiResponse.Success(new { deleted = true }));
    }

    [HttpPost("chapters")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> CreateChapter([FromForm] ChapterFormInput? input)
    {
        if (input is null) throw ApiException.Validation("We couldn't read that form. Please try again.");
        var admin = await User.RequireAdminAsync(_repository);

        input.BookId = Sanitizer.CleanText(input.BookId, 40);
        input.Title = Sanitizer.CleanText(input.Title, 255);
        input.Content = Sanitizer.CleanMultiline(input.Content, input.FullBook ? 400000 : 200000);
        input.AudioUrl = string.IsNullOrWhiteSpace(input.AudioUrl) ? null : Sanitizer.CleanText(input.AudioUrl, 500);

        var book = await _repository.GetBookByIdAsync(input.BookId);
        if (book is null) throw ApiException.NotFound("We couldn't find that book.");

        if (input.FullBook) return await CreateFullBookAsync(input, book, admin);

        InputValidation.EnsureValid(input);

        var blocks = Sanitizer.ToBlocks(input.Content);
        if (blocks.Count == 0) throw ApiException.Validation("Chapter text can't be empty.", new() { ["content"] = "Write at least one paragraph." });

        var chapterId = await _repository.CreateChapterAsync(input, JsonSerializer.Serialize(blocks, JsonDefaults.Options));
        _logger.LogInformation("Chapter {ChapterId} added to {BookId} by {AdminId}", chapterId, input.BookId, admin.Id);
        return new JsonResult(ApiResponse.Success(new { chapterId }));
    }

    /// <summary>Splits a pasted manuscript at its "Chapter N" headings and stores each part as a chapter.</summary>
    private async Task<IActionResult> CreateFullBookAsync(ChapterFormInput input, BookRow book, UserRecord admin)
    {
        var parts = SplitManuscript(input.Content);
        if (parts.Count == 0)
        {
            throw ApiException.Validation("We couldn't find any chapter headings in that manuscript.",
                new() { ["content"] = "Start each chapter with a line such as \"Chapter 1 — The Landing\"." });
        }

        var next = await _repository.NextChapterNumberAsync(book.Id);
        var created = new List<int>();
        var skipped = 0;
        foreach (var part in parts)
        {
            var blocks = Sanitizer.ToBlocks(part.Body);
            if (blocks.Count == 0)
            {
                skipped++;
                continue;
            }

            var chapter = new ChapterFormInput
            {
                BookId = book.Id,
                Title = Sanitizer.CleanText(part.Title, 255),
                ChapterNumber = next,
                Content = part.Body,
                AudioUrl = input.AudioUrl,
                Publish = input.Publish,
            };
            if (string.IsNullOrWhiteSpace(chapter.Title)) chapter.Title = $"Chapter {next}";

            await _repository.CreateChapterAsync(chapter, JsonSerializer.Serialize(blocks, JsonDefaults.Options));
            created.Add(next);
            next++;
        }

        if (created.Count == 0)
        {
            throw ApiException.Validation("Every chapter in that manuscript is empty.",
                new() { ["content"] = "Add some text under each chapter heading." });
        }

        _logger.LogInformation("Full book uploaded for {BookId}: {Count} chapters by {AdminId}", book.Id, created.Count, admin.Id);
        return new JsonResult(ApiResponse.Success(new
        {
            chapters = created.Count,
            skipped,
            firstChapterNumber = created[0],
            message = $"{created.Count} chapters added to “{book.Title}”.",
        }));
    }

    private static List<(string Title, string Body)> SplitManuscript(string content)
    {
        var heading = new System.Text.RegularExpressions.Regex(
            @"^\s*(chapter|part|book)\s+([0-9]+|[ivxlcdm]+)\b[\s.:\-–—]*(.*)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var parts = new List<(string Title, string Body)>();
        string? title = null;
        var lines = new List<string>();

        void Flush()
        {
            if (title is null) return;
            var body = string.Join("\n", lines).Trim();
            if (body.Length > 0) parts.Add((title, body));
        }

        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            var match = heading.Match(line.Trim());
            if (match.Success)
            {
                Flush();
                var rest = match.Groups[3].Value.Trim();
                title = string.IsNullOrWhiteSpace(rest) ? match.Value.Trim() : $"{match.Groups[1].Value} {match.Groups[2].Value} — {rest}";
                lines.Clear();
                continue;
            }
            if (title is null) continue;
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(line);
        }
        Flush();
        return parts;
    }

    [HttpGet("moderation")]
    public async Task<IActionResult> Moderation()
    {
        await User.RequireAdminAsync(_repository);
        return new JsonResult(ApiResponse.Success(new
        {
            pending = await _repository.ListPendingAsync(),
            recent = await _repository.ListRecentModeratedAsync(),
        }));
    }

    [HttpPost("moderation/{id}")]
    public async Task<IActionResult> Moderate(string id, [FromBody] ModerationRequest? request)
    {
        var admin = await User.RequireAdminAsync(_repository);
        if (request is null || request.Action is not ("accept" or "reject"))
        {
            throw ApiException.Validation("Choose whether to accept or reject this note.");
        }
        var comment = await _repository.ModerateAsync(id, request.Action);
        await _badges.SyncEarnedBadgesAsync(comment.UserId);
        _logger.LogInformation("Comment {CommentId} {Action} by {AdminId}", id, request.Action, admin.Id);
        return new JsonResult(ApiResponse.Success(new { comment }));
    }

    [HttpPost("contributors")]
    public async Task<IActionResult> AddContributor([FromBody] ContributorRequest? request)
    {
        var admin = await User.RequireAdminAsync(_repository);
        if (request is null || !Guid.TryParse(request.BookId, out _) || !Guid.TryParse(request.UserId, out _))
        {
            throw ApiException.Validation("Choose a valid note, book and reader.");
        }
        var result = await _repository.AddContributorAsync(request.BookId, request.UserId);
        await _badges.SyncEarnedBadgesAsync(request.UserId);
        _logger.LogInformation("Reader {UserId} credited on {BookId} by {AdminId}", request.UserId, request.BookId, admin.Id);
        return new JsonResult(ApiResponse.Success(new { result.Username, result.BookTitle, result.BookId, result.UserId }));
    }

    [HttpPatch("books/{id}/info")]
    public async Task<IActionResult> UpdateBookInfo(string id, [FromBody] BookInfoRequest? request)
    {
        var admin = await User.RequireAdminAsync(_repository);
        if (request is null || !Guid.TryParse(id, out _))
        {
            throw ApiException.Validation("We couldn't read those book details. Please try again.");
        }

        var title = Sanitizer.CleanText(request.Title, 255);
        var authorName = Sanitizer.CleanText(request.AuthorName, 120);
        var synopsis = Sanitizer.CleanMultiline(request.Synopsis, 4000);
        var fields = new Dictionary<string, string>();
        if (title.Length < 2) fields["title"] = "Enter the book name.";
        if (authorName.Length < 2) fields["authorName"] = "Enter the author's name.";
        if (synopsis.Length < 40) fields["synopsis"] = "Write at least 40 characters of synopsis.";
        if (fields.Count > 0) throw ApiException.Validation("Please correct the highlighted fields.", fields);

        string category;
        try
        {
            category = InputValidation.CleanCategory(request.Category, Categories);
        }
        catch (ApiException error)
        {
            throw ApiException.Validation("Choose a category.", error.Fields);
        }

        var status = InputValidation.CleanStatus(request.Status);
        var book = await _repository.UpdateBookInfoAsync(id, title, category, authorName, synopsis, status);
        _logger.LogInformation("Book {BookId} details updated by {AdminId}", id, admin.Id);

        if (status != "draft")
        {
            foreach (var contributor in await _repository.ListContributorsAsync(id))
            {
                await _repository.NotifyUserAsync(contributor.UserId, "book", "A book you helped with is live",
                    $"“{book.Title}” details were updated by the editors.", $"/books/{book.Slug}");
            }
        }

        return new JsonResult(ApiResponse.Success(new { book }));
    }
}

public sealed class BookInfoRequest
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string Status { get; set; } = "ongoing";
}
