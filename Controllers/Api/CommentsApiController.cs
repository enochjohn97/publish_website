using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Folio.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Web.Controllers.Api;

[Route("api/comments")]
public sealed class CommentsApiController : ControllerBase
{
    private readonly Repository _repository;
    private readonly BadgeService _badges;
    private readonly RateLimiter _limiter;
    private readonly ILogger<CommentsApiController> _logger;

    public CommentsApiController(Repository repository, BadgeService badges, RateLimiter limiter, ILogger<CommentsApiController> logger)
    {
        _repository = repository;
        _badges = badges;
        _limiter = limiter;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string chapterId)
    {
        if (!Guid.TryParse(chapterId, out _)) throw ApiException.NotFound("We couldn't find that chapter.");
        var user = await User.CurrentUserAsync(_repository);
        var comments = await _repository.ListChapterCommentsAsync(chapterId, includePending: user?.Role == "admin", viewerUserId: user?.Id);
        return new JsonResult(ApiResponse.Success(new { comments }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CommentFormInput? input)
    {
        if (input is null) throw ApiException.Validation("We couldn't read that form. Please try again.");
        var user = await User.RequireUserAsync(_repository);
        var ip = Sanitizer.HashToken(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local");
        _limiter.Check($"comment:{ip}:{user.Id}", 8, TimeSpan.FromMinutes(10));

        input.ChapterId = Sanitizer.CleanText(input.ChapterId, 40);
        input.Content = Sanitizer.CleanMultiline(input.Content, 2000);
        input.HighlightedText = string.IsNullOrWhiteSpace(input.HighlightedText) ? null : Sanitizer.CleanText(input.HighlightedText, 400);
        input.ParentId = string.IsNullOrWhiteSpace(input.ParentId) ? null : Sanitizer.CleanText(input.ParentId, 40);
        input.ParagraphId = string.IsNullOrWhiteSpace(input.ParagraphId) ? null : Sanitizer.CleanText(input.ParagraphId, 20);
        input.Kind = Sanitizer.CleanText(input.Kind, 20).ToLowerInvariant() is "correction" ? "correction" : "comment";
        if (input.PageNumber is < 1 or > 999) input.PageNumber = null;

        if (!Guid.TryParse(input.ChapterId, out _)) throw ApiException.NotFound("We couldn't find that chapter.");
        if (input.ParentId is not null && !Guid.TryParse(input.ParentId, out _)) throw ApiException.Validation("That reply target isn't valid.");
        if (input.ParagraphId is not null && !System.Text.RegularExpressions.Regex.IsMatch(input.ParagraphId, "^b[0-9]{1,4}$"))
        {
            throw ApiException.Validation("Invalid paragraph anchor.");
        }
        InputValidation.EnsureValid(input, "Please correct the highlighted fields.");

        var id = await _repository.CreateCommentAsync(input, user.Id);
        _logger.LogInformation("Comment {CommentId} queued for moderation by {UserId}; page={Page}, kind={Kind}", id, user.Id, input.PageNumber, input.Kind);

        return new JsonResult(ApiResponse.Success(new
        {
            id,
            status = "pending",
            message = "Thank you — your note is with our editors for review.",
        }));
    }

    [HttpPost("{id}/upvote")]
    public async Task<IActionResult> Upvote(string id)
    {
        var user = await User.RequireUserAsync(_repository);
        if (!Guid.TryParse(id, out _)) throw ApiException.NotFound("We couldn't find that note.");
        var ip = Sanitizer.HashToken(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "local");
        _limiter.Check($"upvote:{ip}:{user.Id}", 30, TimeSpan.FromMinutes(1));
        var upvotes = await _repository.UpvoteAsync(id, user.Id);
        return new JsonResult(ApiResponse.Success(new { upvotes }));
    }

    [HttpPost("/api/reading-progress")]
    public async Task<IActionResult> Progress([FromBody] ProgressRequest? request)
    {
        if (request is null || !Guid.TryParse(request.ChapterId, out _))
        {
            throw ApiException.Validation("We couldn't save that reading position.");
        }
        var user = await User.RequireUserAsync(_repository);
        await _repository.SaveReadingProgressAsync(user.Id, request.ChapterId, Math.Clamp(request.Progress, 0, 100));
        await _badges.SyncEarnedBadgesAsync(user.Id);
        return new JsonResult(ApiResponse.Success(new { saved = true }));
    }
}
