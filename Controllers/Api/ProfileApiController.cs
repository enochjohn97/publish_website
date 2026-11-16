using Folio.Web.Data;
using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Folio.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Folio.Web.Controllers.Api;

[Route("api/profile")]
public sealed class ProfileApiController : ControllerBase
{
    private readonly Repository _repository;
    private readonly BadgeService _badges;
    private readonly ILogger<ProfileApiController> _logger;

    public ProfileApiController(Repository repository, BadgeService badges, ILogger<ProfileApiController> logger)
    {
        _repository = repository;
        _badges = badges;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await User.RequireUserAsync(_repository);
        try
        {
            var viewModel = new ProfileViewModel
            {
                User = user,
                Badges = await _badges.ListAsync(user.Id),
                ReadingHistory = await _repository.ListReadingHistoryAsync(user.Id),
                Contributions = await _repository.ListUserContributionsAsync(user.Id),
                Comments = await _repository.ListUserCommentsAsync(user.Id),
            };
            return new JsonResult(ApiResponse.Success(new
            {
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatarUrl = user.AvatarUrl,
                    role = user.Role,
                    reputationScore = user.ReputationScore,
                    createdAt = user.CreatedAt,
                },
                badges = viewModel.Badges,
                readingHistory = viewModel.ReadingHistory,
                contributions = viewModel.Contributions,
                comments = viewModel.Comments.Select(comment => new
                {
                    id = comment.Id,
                    content = comment.Content,
                    status = comment.Status,
                    createdAt = comment.CreatedAt,
                    bookTitle = comment.BookTitle,
                    chapterTitle = comment.ChapterTitle,
                    chapterNumber = comment.ChapterNumber,
                    isContributorSubmission = comment.IsContributorSubmission,
                }),
            }));
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Profile load failed for {UserId}", user.Id);
            throw;
        }
    }

    [HttpGet("/api/notifications")]
    public async Task<IActionResult> Notifications([FromQuery] int limit = 20)
    {
        var user = await User.RequireUserAsync(_repository);
        var items = await _repository.ListNotificationsAsync(user.Id, limit);
        var unread = await _repository.CountUnreadNotificationsAsync(user.Id);
        return new JsonResult(ApiResponse.Success(new { notifications = items, unread }));
    }

    [HttpPost("/api/notifications/read")]
    public async Task<IActionResult> MarkNotificationsRead([FromBody] NotificationReadRequest? request)
    {
        var user = await User.RequireUserAsync(_repository);
        var id = request?.Id;
        if (!string.IsNullOrWhiteSpace(id) && !Guid.TryParse(id, out _))
        {
            throw ApiException.Validation("That notification no longer exists.");
        }
        await _repository.MarkNotificationsReadAsync(user.Id, string.IsNullOrWhiteSpace(id) ? null : id);
        return new JsonResult(ApiResponse.Success(new { unread = await _repository.CountUnreadNotificationsAsync(user.Id) }));
    }
}

public sealed class NotificationReadRequest
{
    public string? Id { get; set; }
}
