using System.ComponentModel.DataAnnotations;

namespace Folio.Web.Models;

public sealed class UserRecord
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "reader";
    public int ReputationScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminLoginRow
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = "reader";
}

public sealed class BookRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Category { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public string? AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Status { get; set; } = "draft";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int ChapterCount { get; set; }

    /// <summary>Lowest published chapter number, or null when the title has no published chapters.</summary>
    public int? FirstChapterNumber { get; set; }

    public long WordCount { get; set; }
    public int ContributorCount { get; set; }

    public int ReadingMinutes => Math.Max(1, (int)Math.Round(WordCount / 220.0));
}

public sealed class ChapterRow
{
    public string Id { get; set; } = "";
    public string BookId { get; set; } = "";
    public int ChapterNumber { get; set; }
    public string Title { get; set; } = "";
    public string ContentJson { get; set; } = "[]";
    public string? AudioUrl { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CommentRow
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string? ParagraphId { get; set; }
    public int? PageNumber { get; set; }
    public string Kind { get; set; } = "comment";
    public string? HighlightedText { get; set; }
    public string Content { get; set; } = "";
    public string Status { get; set; } = "pending";
    public bool IsContributorSubmission { get; set; }
    public bool IsSpoiler { get; set; }
    public int Upvotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string UserRole { get; set; } = "reader";
    public string BookId { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string BookSlug { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public int ChapterNumber { get; set; }
}

public sealed class ContributorRow
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string RoleLabel { get; set; } = "Contributor";
    public DateTime CreditedAt { get; set; }
}

public sealed class ChapterBlock
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class BadgeRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconKey { get; set; } = "";
    public DateTime AwardedAt { get; set; }
}

public sealed class ReadingHistoryRow
{
    public string ChapterId { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string BookSlug { get; set; } = "";
    public int ChapterNumber { get; set; }
    public int Progress { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ContributionRow
{
    public string BookTitle { get; set; } = "";
    public string BookSlug { get; set; } = "";
    public DateTime CreditedAt { get; set; }
}

public sealed class CountsRow
{
    public int Books { get; set; }
    public int PublishedBooks { get; set; }
    public int Chapters { get; set; }
    public int Readers { get; set; }
    public int PendingComments { get; set; }
    public int AcceptedComments { get; set; }
    public int Contributors { get; set; }
    public int Notes { get; set; }
}

public sealed class UpsertGoogleResult
{
    public required UserRecord User { get; init; }
    public bool WasCreated { get; init; }
}

public sealed class SearchFilters
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
    public bool IncludeDrafts { get; init; }
    public int Limit { get; init; } = 24;
}

public sealed class NotificationRow
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "info";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Url { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class BookFormInput
{
    [Required(ErrorMessage = "Enter the book name.")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Book name must be 2–255 characters.")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Choose a category.")]
    public string Category { get; set; } = "";

    [Required(ErrorMessage = "Enter the author's name.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Author name must be 2–120 characters.")]
    public string AuthorName { get; set; } = "";

    [Required(ErrorMessage = "Write a synopsis of at least 40 characters.")]
    [StringLength(4000, MinimumLength = 40, ErrorMessage = "Synopsis must be 40–4000 characters.")]
    public string Synopsis { get; set; } = "";

    public string Status { get; set; } = "ongoing";
}

public sealed class ChapterFormInput
{
    [Required]
    public string BookId { get; set; } = "";

    [Required(ErrorMessage = "Enter a chapter title.")]
    [StringLength(255, MinimumLength = 2, ErrorMessage = "Chapter title must be 2–255 characters.")]
    public string Title { get; set; } = "";

    [Range(1, 999, ErrorMessage = "Chapter number must be between 1 and 999.")]
    public int ChapterNumber { get; set; } = 1;

    [Required(ErrorMessage = "Write at least one paragraph.")]
    [StringLength(200000, MinimumLength = 100, ErrorMessage = "Chapter text must be at least 100 characters.")]
    public string Content { get; set; } = "";

    [Url(ErrorMessage = "Enter a valid audio URL.")]
    public string? AudioUrl { get; set; }

    public bool Publish { get; set; } = true;

    /// <summary>When set, the pasted text is split into several chapters by its "Chapter N" headings.</summary>
    public bool FullBook { get; set; }
}

public sealed class CommentFormInput
{
    [Required]
    public string ChapterId { get; set; } = "";

    [Required(ErrorMessage = "Write at least a couple of words.")]
    [StringLength(2000, MinimumLength = 2, ErrorMessage = "Comment must be 2–2000 characters.")]
    public string Content { get; set; } = "";

    public string? ParentId { get; set; }
    public string? ParagraphId { get; set; }
    [Range(1, 999, ErrorMessage = "Page number must be between 1 and 999.")]
    public int? PageNumber { get; set; }
    public string Kind { get; set; } = "comment";
    [StringLength(400, ErrorMessage = "Highlighted text must be under 400 characters.")]
    public string? HighlightedText { get; set; }
    public bool IsSpoiler { get; set; }
    public bool IsContributorSubmission { get; set; }
}

public sealed class HomeViewModel
{
    public IReadOnlyList<BookRow> Books { get; init; } = Array.Empty<BookRow>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public CountsRow Stats { get; init; } = new();
    public IReadOnlyList<CommentRow> RecentNotes { get; init; } = Array.Empty<CommentRow>();
    public UserRecord? User { get; init; }
}

public sealed class BookPageViewModel
{
    public required BookRow Book { get; init; }
    public IReadOnlyList<ChapterRow> Chapters { get; init; } = Array.Empty<ChapterRow>();
    public IReadOnlyList<ContributorRow> Contributors { get; init; } = Array.Empty<ContributorRow>();
    public UserRecord? User { get; init; }
}

public sealed class ReaderBook
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Category { get; set; } = "";
}

public sealed class ReaderPage
{
    public int Number { get; init; }
    public IReadOnlyList<ChapterBlock> Blocks { get; init; } = Array.Empty<ChapterBlock>();
}

public sealed class ReaderViewModel
{
    public required BookRow Book { get; init; }
    public required ChapterRow Chapter { get; init; }
    public required List<ChapterBlock> Blocks { get; init; }
    public IReadOnlyList<ReaderPage> Pages { get; init; } = Array.Empty<ReaderPage>();
    public IReadOnlyList<ChapterRow> Chapters { get; init; } = Array.Empty<ChapterRow>();
    public IReadOnlyList<CommentRow> Comments { get; init; } = Array.Empty<CommentRow>();
    public UserRecord? User { get; init; }
}

public sealed class AdminViewModel
{
    public required UserRecord Admin { get; init; }
    public CountsRow Stats { get; init; } = new();
    public IReadOnlyList<BookRow> Books { get; init; } = Array.Empty<BookRow>();
    public IReadOnlyList<CommentRow> Pending { get; init; } = Array.Empty<CommentRow>();
    public IReadOnlyList<CommentRow> Recent { get; init; } = Array.Empty<CommentRow>();
    public int QueueCount { get; init; }
    public int NextChapterNumber { get; init; } = 1;
}

public sealed class ProfileViewModel
{
    public required UserRecord User { get; init; }
    public IReadOnlyList<BadgeRow> Badges { get; init; } = Array.Empty<BadgeRow>();
    public IReadOnlyList<ReadingHistoryRow> ReadingHistory { get; init; } = Array.Empty<ReadingHistoryRow>();
    public IReadOnlyList<ContributionRow> Contributions { get; init; } = Array.Empty<ContributionRow>();
    public IReadOnlyList<CommentRow> Comments { get; init; } = Array.Empty<CommentRow>();
}
