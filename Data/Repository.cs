using Folio.Web.Infrastructure;
using Folio.Web.Models;
using Folio.Web.Services;

namespace Folio.Web.Data;

public sealed class Repository
{
    private readonly Db _db;
    private readonly ILogger<Repository> _logger;

    public Repository(Db db, ILogger<Repository> logger)
    {
        _db = db;
        _logger = logger;
    }

    private const string CommentSelect = """
        SELECT c.id, c.parent_id, c.paragraph_id, c.page_number, c.kind, c.highlighted_text, c.content, c.status,
               c.is_contributor_submission, c.is_spoiler, c.upvotes, c.created_at,
               u.id AS UserId, u.username, u.avatar_url, u.role AS UserRole,
               b.id AS BookId, b.title AS BookTitle, b.slug AS BookSlug,
               ch.id AS ChapterId, ch.title AS ChapterTitle, ch.chapter_number
        FROM comments c
        JOIN users u ON u.id = c.user_id
        JOIN chapters ch ON ch.id = c.chapter_id
        JOIN books b ON b.id = ch.book_id
        """;

    private const string BookSelect = """
        SELECT b.id, b.title, b.slug, b.category, b.synopsis, b.cover_image_url, b.author_id, b.author_name,
               b.status, b.created_at, b.updated_at,
               (SELECT COUNT(*) FROM chapters c WHERE c.book_id = b.id AND c.status = 'published') AS ChapterCount,
               (SELECT MIN(c.chapter_number) FROM chapters c WHERE c.book_id = b.id AND c.status = 'published') AS FirstChapterNumber,
               (SELECT COALESCE(SUM(CHAR_LENGTH(c.content)), 0) / 6 FROM chapters c WHERE c.book_id = b.id AND c.status = 'published') AS WordCount,
               (SELECT COUNT(*) FROM book_contributors bc WHERE bc.book_id = b.id) AS ContributorCount
        FROM books b
        """;

    // ---------------------------------------------------------------- users

    public Task<UserRecord?> FindUserAsync(string id) =>
        _db.FirstAsync<UserRecord>(
            """
            SELECT id, username, email, avatar_url, role, reputation_score, created_at
            FROM users WHERE id = @id LIMIT 1
            """, new { id });

    public Task<AdminLoginRow?> FindAdminForLoginAsync(string username) =>
        _db.FirstAsync<AdminLoginRow>(
            """
            SELECT a.id, a.username, a.password_hash, u.avatar_url, 'admin' AS role
            FROM admins a
            JOIN users u ON u.id = a.id
            WHERE a.username = @username LIMIT 1
            """, new { username });

    public Task<UserRecord?> FindGoogleUserAsync(string googleSub) =>
        _db.FirstAsync<UserRecord>(
            """
            SELECT id, username, email, avatar_url, role, reputation_score, created_at
            FROM users WHERE google_sub = @googleSub LIMIT 1
            """, new { googleSub });

    public async Task<UpsertGoogleResult> UpsertGoogleUserAsync(string googleSub, string email, string name, string? picture)
    {
        var existing = await _db.FirstAsync<UserRecord>(
            """
            SELECT id, username, email, avatar_url, role, reputation_score, created_at
            FROM users WHERE google_sub = @googleSub OR email = @email LIMIT 1
            """, new { googleSub, email });

        if (existing is not null)
        {
            var avatar = picture ?? existing.AvatarUrl;
            await _db.ExecuteAsync(
                "UPDATE users SET google_sub = @googleSub, avatar_url = @avatar, email = @email WHERE id = @id",
                new { googleSub, avatar, email, id = existing.Id });
            existing.AvatarUrl = avatar;
            existing.Email = email;
            return new UpsertGoogleResult { User = existing, WasCreated = false };
        }

        var username = await UniqueUsernameAsync(name, email);
        var id = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(
            """
            INSERT INTO users (id, username, email, password_hash, google_sub, avatar_url, role)
            VALUES (@id, @username, @email, NULL, @googleSub, @avatar, 'reader')
            """, new { id, username, email, googleSub, avatar = picture });

        _logger.LogInformation("New Google reader {Username} ({UserId})", username, id);
        return new UpsertGoogleResult
        {
            User = new UserRecord
            {
                Id = id,
                Username = username,
                Email = email,
                AvatarUrl = picture,
                Role = "reader",
                ReputationScore = 0,
                CreatedAt = DateTime.UtcNow,
            },
            WasCreated = true,
        };
    }

    private async Task<string> UniqueUsernameAsync(string name, string email)
    {
        var baseName = Sanitizer.CleanText(name, 30);
        if (string.IsNullOrWhiteSpace(baseName) || baseName.Length < 2)
        {
            baseName = Sanitizer.CleanText(email.Split('@')[0], 30);
        }
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "reader";

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var candidate = attempt == 0 ? baseName : $"{baseName} {attempt + 1}";
            var taken = await _db.ScalarAsync<long>("SELECT COUNT(*) FROM users WHERE username = @candidate", new { candidate });
            if (taken == 0) return candidate;
        }
        return $"{baseName} {Guid.NewGuid().ToString("N")[..4]}";
    }

    // ---------------------------------------------------------------- books

    public async Task<IReadOnlyList<BookRow>> ListBooksAsync(SearchFilters filters)
    {
        var where = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!filters.IncludeDrafts) where.Add("b.status <> 'draft'");
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            where.Add("(b.title LIKE @term OR b.author_name LIKE @term OR b.synopsis LIKE @term OR b.category LIKE @term)");
            parameters["term"] = $"%{filters.Search.Replace("%", "\\%").Replace("_", "\\_")}%";
        }
        if (!string.IsNullOrWhiteSpace(filters.Category) && filters.Category != "all")
        {
            where.Add("b.category = @category");
            parameters["category"] = filters.Category;
        }
        if (!string.IsNullOrWhiteSpace(filters.Status) && filters.Status != "all")
        {
            if (filters.Status is not ("draft" or "ongoing" or "completed"))
            {
                throw ApiException.Validation("That status filter isn't supported.");
            }
            where.Add("b.status = @status");
            parameters["status"] = filters.Status;
        }

        var limit = Math.Clamp(filters.Limit, 1, 60);
        // Newest first, with a stable tie-break so two books created in the same second
        // never swap places between requests (the newest upload must lead the list).
        var sql = $"{BookSelect} {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY b.created_at DESC, b.updated_at DESC, b.title ASC LIMIT {limit}";
        return await _db.QueryAsync<BookRow>(sql, parameters);
    }

    public Task<BookRow?> GetBookBySlugAsync(string slug) =>
        _db.FirstAsync<BookRow>($"{BookSelect} WHERE b.slug = @slug LIMIT 1", new { slug });

    public Task<BookRow?> GetBookByIdAsync(string id) =>
        _db.FirstAsync<BookRow>($"{BookSelect} WHERE b.id = @id LIMIT 1", new { id });

    public async Task<IReadOnlyList<string>> ListCategoriesAsync()
    {
        var rows = await _db.QueryAsync<string>(
            "SELECT DISTINCT category FROM books WHERE status <> 'draft' ORDER BY category");
        return rows;
    }

    public async Task<string> UniqueSlugAsync(string title)
    {
        var baseSlug = Sanitizer.Slugify(title);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = attempt == 0 ? baseSlug : $"{baseSlug}-{attempt + 1}";
            var taken = await _db.ScalarAsync<long>("SELECT COUNT(*) FROM books WHERE slug = @candidate", new { candidate });
            if (taken == 0) return candidate;
        }
        return $"{baseSlug}-{DateTime.UtcNow.Ticks.ToString()[10..]}";
    }

    public async Task<BookRow> CreateBookAsync(BookFormInput input, byte[] cover, string mime, string adminId)
    {
        var id = Guid.NewGuid().ToString();
        var uploadId = Guid.NewGuid().ToString();
        var slug = await UniqueSlugAsync(input.Title);

        await _db.TransactionAsync(async (connection, transaction) =>
        {
            await using (var insertUpload = new MySqlConnector.MySqlCommand(
                "INSERT INTO uploads (id, mime, size_bytes, data) VALUES (@id, @mime, @size, @data)", connection, transaction))
            {
                insertUpload.Parameters.AddWithValue("@id", uploadId);
                insertUpload.Parameters.AddWithValue("@mime", mime);
                insertUpload.Parameters.AddWithValue("@size", cover.Length);
                insertUpload.Parameters.AddWithValue("@data", cover);
                await insertUpload.ExecuteNonQueryAsync();
            }

            await using (var insertBook = new MySqlConnector.MySqlCommand(
                """
                INSERT INTO books (id, title, slug, category, synopsis, cover_image_url, author_id, author_name, status)
                VALUES (@id, @title, @slug, @category, @synopsis, @cover, @authorId, @authorName, @status)
                """, connection, transaction))
            {
                insertBook.Parameters.AddWithValue("@id", id);
                insertBook.Parameters.AddWithValue("@title", input.Title);
                insertBook.Parameters.AddWithValue("@slug", slug);
                insertBook.Parameters.AddWithValue("@category", input.Category);
                insertBook.Parameters.AddWithValue("@synopsis", input.Synopsis);
                insertBook.Parameters.AddWithValue("@cover", $"/api/images/{uploadId}");
                insertBook.Parameters.AddWithValue("@authorId", adminId);
                insertBook.Parameters.AddWithValue("@authorName", input.AuthorName);
                insertBook.Parameters.AddWithValue("@status", input.Status);
                await insertBook.ExecuteNonQueryAsync();
            }
            return 0;
        });

        return await GetBookByIdAsync(id) ?? throw new ApiException(500, "INTERNAL_ERROR", "The book could not be saved.");
    }

    public async Task UpdateBookStatusAsync(string id, string status)
    {
        var rows = await _db.ExecuteAsync("UPDATE books SET status = @status WHERE id = @id", new { status, id });
        if (rows == 0) throw ApiException.NotFound("We couldn't find that book.");

        if (status == "draft") return;
        var book = await _db.FirstAsync<BookRow>("SELECT id, title, slug FROM books WHERE id = @id LIMIT 1", new { id });
        if (book is null) return;
        var recipients = await _db.QueryAsync<string>(
            "SELECT user_id FROM book_contributors WHERE book_id = @id", new { id });
        foreach (var recipient in recipients.Distinct())
        {
            await NotifyUserAsync(recipient, "book", "A book you helped with is live",
                $"“{book.Title}” is now available in the catalogue.", $"/books/{book.Slug}");
        }
    }

    public async Task DeleteBookAsync(string id)
    {
        var rows = await _db.ExecuteAsync("DELETE FROM books WHERE id = @id", new { id });
        if (rows == 0) throw ApiException.NotFound("We couldn't find that book.");
    }

    public async Task<IReadOnlyList<ContributorRow>> ListContributorsAsync(string bookId) =>
        await _db.QueryAsync<ContributorRow>(
            """
            SELECT bc.user_id, u.username, u.avatar_url, bc.role_label, bc.credited_at
            FROM book_contributors bc JOIN users u ON u.id = bc.user_id
            WHERE bc.book_id = @bookId ORDER BY bc.credited_at
            """, new { bookId });

    // ---------------------------------------------------------------- chapters

    public async Task<IReadOnlyList<ChapterRow>> ListChaptersAsync(string bookId, bool includeDrafts)
    {
        var sql = $"""
            SELECT id, book_id, chapter_number, title, status, published_at, created_at
            FROM chapters WHERE book_id = @bookId {(includeDrafts ? "" : "AND status = 'published'")}
            ORDER BY chapter_number
            """;
        return await _db.QueryAsync<ChapterRow>(sql, new { bookId });
    }

    public Task<ChapterRow?> GetChapterAsync(string bookId, int chapterNumber) =>
        _db.FirstAsync<ChapterRow>(
            """
            SELECT id, book_id, chapter_number, title, content AS ContentJson, audio_url, status, published_at, created_at
            FROM chapters WHERE book_id = @bookId AND chapter_number = @chapterNumber LIMIT 1
            """, new { bookId, chapterNumber });

    public async Task<string> CreateChapterAsync(ChapterFormInput input, string contentJson)
    {
        var id = Guid.NewGuid().ToString();
        try
        {
            await _db.ExecuteAsync(
                """
                INSERT INTO chapters (id, book_id, chapter_number, title, content, audio_url, status, published_at)
                VALUES (@id, @bookId, @chapterNumber, @title, @content, @audioUrl, @status, @publishedAt)
                """, new
                {
                    id,
                    bookId = input.BookId,
                    chapterNumber = input.ChapterNumber,
                    title = input.Title,
                    content = contentJson,
                    audioUrl = string.IsNullOrWhiteSpace(input.AudioUrl) ? null : input.AudioUrl,
                    status = input.Publish ? "published" : "draft",
                    publishedAt = input.Publish ? DateTime.UtcNow : (DateTime?)null,
                });
        }
        catch (MySqlConnector.MySqlException error) when (error.Number == 1062)
        {
            throw ApiException.Validation("That chapter number is already used in this book.",
                new() { ["chapterNumber"] = "This chapter number is taken. Choose another." });
        }

        if (input.Publish)
        {
            var book = await _db.FirstAsync<BookRow>(
                "SELECT id, title, slug FROM books WHERE id = @bookId LIMIT 1", new { bookId = input.BookId });
            if (book is not null)
            {
                var recipients = await _db.QueryAsync<string>(
                    """
                    SELECT bc.user_id FROM book_contributors bc WHERE bc.book_id = @bookId
                    UNION
                    SELECT c.user_id FROM comments c JOIN chapters ch ON ch.id = c.chapter_id
                      WHERE ch.book_id = @bookId AND c.status = 'accepted'
                    """, new { bookId = input.BookId });
                foreach (var recipient in recipients.Distinct())
                {
                    await NotifyUserAsync(recipient, "chapter", "New chapter published",
                        $"“{book.Title}” has a new chapter: {input.Title}.", $"/books/{book.Slug}/{input.ChapterNumber}");
                }
            }
        }
        return id;
    }

    // ---------------------------------------------------------------- comments

    public async Task<IReadOnlyList<CommentRow>> ListChapterCommentsAsync(string chapterId, bool includePending, string? viewerUserId = null)
    {
        // Published notes for everyone, plus a reader's own notes still awaiting moderation so
        // their contribution never appears to vanish after they submit it.
        var filter = includePending ? "" : "AND (c.status = 'accepted' OR c.user_id = @viewerUserId)";
        var sql = $"{CommentSelect} WHERE c.chapter_id = @chapterId {filter} ORDER BY c.created_at";
        return await _db.QueryAsync<CommentRow>(sql, new { chapterId, viewerUserId });
    }

    public async Task<IReadOnlyList<CommentRow>> ListPendingAsync() =>
        await _db.QueryAsync<CommentRow>(
            $"{CommentSelect} WHERE c.status = 'pending' ORDER BY c.created_at LIMIT 100");

    public async Task<CommentRow?> GetCommentByIdAsync(string commentId) =>
        await _db.FirstAsync<CommentRow>($"{CommentSelect} WHERE c.id = @commentId LIMIT 1", new { commentId });

    public async Task<int> GetPendingCountAsync() =>
        await _db.ScalarAsync<int>("SELECT COUNT(*) FROM comments WHERE status = 'pending'");

    public async Task<IReadOnlyList<CommentRow>> ListRecentModeratedAsync(int limit = 8) =>
        await _db.QueryAsync<CommentRow>(
            $"{CommentSelect} WHERE c.status IN ('accepted','rejected') ORDER BY c.created_at DESC LIMIT {Math.Clamp(limit, 1, 50)}");

    public async Task<IReadOnlyList<CommentRow>> ListRecentAcceptedAsync(int limit = 3) =>
        await _db.QueryAsync<CommentRow>(
            $"{CommentSelect} WHERE c.status = 'accepted' ORDER BY c.created_at DESC LIMIT {Math.Clamp(limit, 1, 12)}");

    public Task<CommentRow?> GetCommentAsync(string commentId) =>
        _db.FirstAsync<CommentRow>($"{CommentSelect} WHERE c.id = @commentId LIMIT 1", new { commentId });

    public async Task<string> CreateCommentAsync(CommentFormInput input, string userId)
    {
        var chapter = await _db.FirstAsync<ChapterRow>(
            """
            SELECT id, book_id, chapter_number, title, content AS ContentJson, status
            FROM chapters WHERE id = @id AND status = 'published' LIMIT 1
            """, new { id = input.ChapterId });
        if (chapter is null) throw ApiException.NotFound("That chapter is no longer available.");

        string? parentId = null;
        if (!string.IsNullOrWhiteSpace(input.ParentId))
        {
            var parent = await _db.FirstAsync<CommentRow>(
                "SELECT id, chapter_id, content, created_at FROM comments WHERE id = @id AND status = 'accepted' LIMIT 1",
                new { id = input.ParentId });
            if (parent is null || parent.ChapterId != input.ChapterId)
            {
                throw ApiException.Validation("You can only reply to a published note on this chapter.");
            }
            parentId = parent.Id;
        }

        var id = Guid.NewGuid().ToString();
        await _db.ExecuteAsync(
            """
            INSERT INTO comments (id, chapter_id, user_id, parent_id, paragraph_id, page_number, kind,
                                  highlighted_text, content, status, is_contributor_submission, is_spoiler)
            VALUES (@id, @chapterId, @userId, @parentId, @paragraphId, @pageNumber, @kind,
                    @highlight, @content, 'pending', @contribution, @spoiler)
            """, new
            {
                id,
                chapterId = input.ChapterId,
                userId,
                parentId,
                paragraphId = string.IsNullOrWhiteSpace(input.ParagraphId) ? null : input.ParagraphId,
                pageNumber = input.PageNumber,
                kind = input.Kind == "correction" ? "correction" : "comment",
                highlight = string.IsNullOrWhiteSpace(input.HighlightedText) ? null : input.HighlightedText,
                content = input.Content,
                contribution = input.IsContributorSubmission,
                spoiler = input.IsSpoiler,
            });

        var username = await _db.ScalarAsync<string>("SELECT username FROM users WHERE id = @userId LIMIT 1", new { userId });
        var book = await _db.FirstAsync<BookRow>("SELECT id, title FROM books WHERE id = @bookId LIMIT 1", new { bookId = chapter.BookId });
        await NotifyAdminsAsync(
            input.IsContributorSubmission ? "contribution" : "comment",
            input.IsContributorSubmission ? "New contribution to review" : "New note awaiting review",
            $"{username} {(input.IsContributorSubmission ? "submitted a contribution" : "left a note")} on “{book?.Title}” · Ch. {chapter.ChapterNumber}.",
            "/admin");
        return id;
    }

    public async Task<CommentRow> ModerateAsync(string commentId, string action)
    {
        var existing = await _db.FirstAsync<CommentRow>(
            "SELECT id, user_id, status, created_at FROM comments WHERE id = @commentId LIMIT 1", new { commentId });
        if (existing is null) throw ApiException.NotFound("We couldn't find that comment.");

        var status = action == "accept" ? "accepted" : "rejected";
        if (existing.Status == status) throw ApiException.Conflict($"This note has already been {status}.");

        await _db.ExecuteAsync("UPDATE comments SET status = @status WHERE id = @commentId", new { status, commentId });
        if (status == "accepted")
        {
            await _db.ExecuteAsync("UPDATE users SET reputation_score = reputation_score + 5 WHERE id = @userId",
                new { userId = existing.UserId });
        }
        else if (existing.Status == "accepted")
        {
            await _db.ExecuteAsync("UPDATE users SET reputation_score = GREATEST(reputation_score - 5, 0) WHERE id = @userId",
                new { userId = existing.UserId });
        }

        var moderated = await GetCommentAsync(commentId) ?? throw ApiException.NotFound("We couldn't find that comment.");
        var accepted = status == "accepted";
        await NotifyUserAsync(
            existing.UserId,
            accepted ? "accepted" : "rejected",
            accepted ? "Your note was published" : "Your note wasn't published",
            accepted
                ? $"Your note on “{moderated.BookTitle}” is now visible to readers."
                : $"A note you left on “{moderated.BookTitle}” wasn't accepted this time.",
            accepted ? $"/books/{moderated.BookSlug}/{moderated.ChapterNumber}" : null);
        return moderated;
    }

    public async Task<int> UpvoteAsync(string commentId, string userId)
    {
        var comment = await _db.FirstAsync<CommentRow>(
            "SELECT id, status, created_at FROM comments WHERE id = @commentId LIMIT 1", new { commentId });
        if (comment is null || comment.Status != "accepted") throw ApiException.NotFound("We couldn't find that note.");

        var inserted = await _db.ExecuteAsync(
            "INSERT IGNORE INTO comment_votes (comment_id, user_id) VALUES (@commentId, @userId)", new { commentId, userId });
        if (inserted == 0) throw ApiException.Conflict("You've already upvoted this note.");

        await _db.ExecuteAsync("UPDATE comments SET upvotes = upvotes + 1 WHERE id = @commentId", new { commentId });
        return await _db.ScalarAsync<int>("SELECT upvotes FROM comments WHERE id = @commentId", new { commentId });
    }

    public async Task<(string Username, string BookTitle, string UserId, string BookId)> AddContributorAsync(string bookId, string userId)
    {
        var book = await _db.FirstAsync<BookRow>("SELECT id, title, slug FROM books WHERE id = @bookId LIMIT 1", new { bookId });
        var user = await _db.FirstAsync<UserRecord>(
            "SELECT id, username, role, reputation_score, created_at FROM users WHERE id = @userId LIMIT 1", new { userId });
        if (book is null) throw ApiException.NotFound("We couldn't find that book.");
        if (user is null) throw ApiException.NotFound("We couldn't find that reader.");

        var inserted = await _db.ExecuteAsync(
            "INSERT IGNORE INTO book_contributors (book_id, user_id, role_label) VALUES (@bookId, @userId, 'Contributor')",
            new { bookId, userId });
        if (inserted == 0) throw ApiException.Conflict($"{user.Username} is already credited on this book.");

        if (user.Role == "reader")
        {
            await _db.ExecuteAsync(
                "UPDATE users SET role = 'author', reputation_score = reputation_score + 25 WHERE id = @userId", new { userId });
        }
        else
        {
            await _db.ExecuteAsync(
                "UPDATE users SET reputation_score = reputation_score + 25 WHERE id = @userId", new { userId });
        }

        await NotifyUserAsync(userId, "contributor", "You're now a credited contributor",
            $"You're credited on “{book.Title}”. Thank you for helping shape it.", $"/books/{book.Slug}");

        return (user.Username, book.Title, userId, bookId);
    }

    public async Task<IReadOnlyList<CommentRow>> ListUserCommentsAsync(string userId, int limit = 20) =>
        await _db.QueryAsync<CommentRow>(
            $"{CommentSelect} WHERE c.user_id = @userId ORDER BY c.created_at DESC LIMIT {Math.Clamp(limit, 1, 50)}",
            new { userId });

    // ---------------------------------------------------------------- book details

    public async Task<BookRow> UpdateBookInfoAsync(string id, string title, string category, string authorName, string synopsis, string status)
    {
        var rows = await _db.ExecuteAsync(
            """
            UPDATE books SET title = @title, category = @category, author_name = @authorName,
                             synopsis = @synopsis, status = @status
            WHERE id = @id
            """, new { id, title, category, authorName, synopsis, status });
        if (rows == 0) throw ApiException.NotFound("We couldn't find that book.");
        return await GetBookByIdAsync(id) ?? throw ApiException.NotFound("We couldn't find that book.");
    }

    public Task<int> NextChapterNumberAsync(string bookId) =>
        _db.ScalarAsync<int>("SELECT COALESCE(MAX(chapter_number), 0) + 1 FROM chapters WHERE book_id = @bookId", new { bookId });

    // ---------------------------------------------------------------- notifications

    public async Task NotifyUserAsync(string userId, string kind, string title, string body, string? url = null)
    {
        try
        {
            await _db.ExecuteAsync(
                """
                INSERT INTO notifications (id, user_id, kind, title, body, url)
                VALUES (@id, @userId, @kind, @title, @body, @url)
                """, new { id = Guid.NewGuid().ToString(), userId, kind, title, body, url });
        }
        catch (Exception error)
        {
            // A notification must never break the action it describes (e.g. an older schema).
            _logger.LogWarning(error, "Could not record notification for {UserId}", userId);
        }
    }

    public async Task NotifyAdminsAsync(string kind, string title, string body, string? url = null)
    {
        try
        {
            await _db.ExecuteAsync(
                """
                INSERT INTO notifications (id, user_id, kind, title, body, url)
                SELECT UUID(), u.id, @kind, @title, @body, @url FROM users u WHERE u.role = 'admin'
                """, new { kind, title, body, url });
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Could not record an admin notification");
        }
    }

    public async Task<IReadOnlyList<NotificationRow>> ListNotificationsAsync(string userId, int limit = 20) =>
        await _db.QueryAsync<NotificationRow>(
            $"""
            SELECT id, kind, title, body, url, is_read, created_at
            FROM notifications WHERE user_id = @userId
            ORDER BY created_at DESC LIMIT {Math.Clamp(limit, 1, 50)}
            """, new { userId });

    public async Task<long> CountUnreadNotificationsAsync(string userId) =>
        await _db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM notifications WHERE user_id = @userId AND is_read = 0", new { userId });

    public async Task MarkNotificationsReadAsync(string userId, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            await _db.ExecuteAsync(
                "UPDATE notifications SET is_read = 1 WHERE user_id = @userId AND is_read = 0", new { userId });
            return;
        }
        await _db.ExecuteAsync(
            "UPDATE notifications SET is_read = 1 WHERE user_id = @userId AND id = @id", new { userId, id });
    }

    // ---------------------------------------------------------------- history, stats, uploads

    public async Task SaveReadingProgressAsync(string userId, string chapterId, int progress)
    {
        var chapter = await _db.FirstAsync<ChapterRow>(
            "SELECT id, book_id, created_at FROM chapters WHERE id = @chapterId LIMIT 1", new { chapterId });
        if (chapter is null) throw ApiException.NotFound("We couldn't find that chapter.");

        await _db.ExecuteAsync(
            """
            INSERT INTO reading_history (user_id, chapter_id, book_id, progress) VALUES (@userId, @chapterId, @bookId, @progress)
            ON DUPLICATE KEY UPDATE progress = GREATEST(progress, VALUES(progress)), updated_at = CURRENT_TIMESTAMP
            """, new { userId, chapterId, bookId = chapter.BookId, progress });
    }

    public async Task<IReadOnlyList<ReadingHistoryRow>> ListReadingHistoryAsync(string userId) =>
        await _db.QueryAsync<ReadingHistoryRow>(
            """
            SELECT rh.chapter_id, ch.title AS ChapterTitle, b.title AS BookTitle, b.slug AS BookSlug,
                   ch.chapter_number, rh.progress, rh.updated_at
            FROM reading_history rh
            JOIN chapters ch ON ch.id = rh.chapter_id
            JOIN books b ON b.id = rh.book_id
            WHERE rh.user_id = @userId ORDER BY rh.updated_at DESC LIMIT 10
            """, new { userId });

    public async Task<IReadOnlyList<ContributionRow>> ListUserContributionsAsync(string userId) =>
        await _db.QueryAsync<ContributionRow>(
            """
            SELECT b.title AS BookTitle, b.slug AS BookSlug, bc.credited_at
            FROM book_contributors bc JOIN books b ON b.id = bc.book_id
            WHERE bc.user_id = @userId ORDER BY bc.credited_at DESC
            """, new { userId });

    public async Task<CountsRow> GetCountsAsync(bool admin)
    {
        var sql = admin
            ? """
              SELECT
                (SELECT COUNT(*) FROM books) AS Books,
                (SELECT COUNT(*) FROM books WHERE status <> 'draft') AS PublishedBooks,
                (SELECT COUNT(*) FROM chapters) AS Chapters,
                (SELECT COUNT(*) FROM users WHERE role = 'reader') AS Readers,
                (SELECT COUNT(*) FROM comments WHERE status = 'pending') AS PendingComments,
                (SELECT COUNT(*) FROM comments WHERE status = 'accepted') AS AcceptedComments,
                (SELECT COUNT(*) FROM book_contributors) AS Contributors
              """
            : """
              SELECT
                (SELECT COUNT(*) FROM books WHERE status <> 'draft') AS Books,
                (SELECT COUNT(*) FROM chapters ch JOIN books b ON b.id = ch.book_id
                   WHERE ch.status = 'published' AND b.status <> 'draft') AS Chapters,
                (SELECT COUNT(*) FROM book_contributors) AS Contributors,
                (SELECT COUNT(*) FROM comments WHERE status = 'accepted') AS Notes
              """;
        return await _db.FirstAsync<CountsRow>(sql) ?? new CountsRow();
    }

    public Task SaveUploadAsync(string id, string mime, byte[] data) =>
        _db.ExecuteAsync("INSERT INTO uploads (id, mime, size_bytes, data) VALUES (@id, @mime, @size, @data)",
            new { id, mime, size = data.Length, data });

    public async Task<(string Mime, byte[] Data)?> GetUploadAsync(string id)
    {
        var row = await _db.FirstAsync<UploadRow>("SELECT mime, data FROM uploads WHERE id = @id LIMIT 1", new { id });
        return row is null ? null : (row.Mime, row.Data);
    }

    private sealed class UploadRow
    {
        public string Mime { get; set; } = "";
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}
