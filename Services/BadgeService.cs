using Folio.Web.Data;
using Folio.Web.Models;

namespace Folio.Web.Services;

public sealed class BadgeService
{
    private readonly Db _db;
    private readonly ILogger<BadgeService> _logger;

    public BadgeService(Db db, ILogger<BadgeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SyncEarnedBadgesAsync(string userId)
    {
        var stats = await _db.FirstAsync<BadgeStats>(
            """
            SELECT
              (SELECT COUNT(*) FROM comments WHERE user_id = @userId AND status = 'accepted') AS Accepted,
              (SELECT COUNT(*) FROM book_contributors WHERE user_id = @userId) AS Credited,
              (SELECT COUNT(*) FROM reading_history WHERE user_id = @userId) AS Chapters
            """, new { userId });
        if (stats is null) return;

        if (stats.Accepted >= 1) await AwardBadgeAsync(userId, "first_voice");
        if (stats.Accepted >= 10) await AwardBadgeAsync(userId, "marginalia");
        if (stats.Credited >= 1) await AwardBadgeAsync(userId, "contributor");
        if (stats.Chapters >= 5) await AwardBadgeAsync(userId, "bibliophile");
    }

    public async Task<bool> AwardBadgeAsync(string userId, string code)
    {
        var badgeId = await _db.FirstAsync<string>("SELECT id FROM badges WHERE code = @code LIMIT 1", new { code });
        if (string.IsNullOrEmpty(badgeId)) return false;
        var inserted = await _db.ExecuteAsync(
            "INSERT IGNORE INTO user_badges (user_id, badge_id) VALUES (@userId, @badgeId)", new { userId, badgeId });
        if (inserted > 0) _logger.LogInformation("Awarded badge {Code} to {UserId}", code, userId);
        return inserted > 0;
    }

    public async Task<IReadOnlyList<BadgeRow>> ListAsync(string userId) =>
        await _db.QueryAsync<BadgeRow>(
            """
            SELECT b.id, b.name, b.description, b.icon_key, ub.awarded_at
            FROM user_badges ub JOIN badges b ON b.id = ub.badge_id
            WHERE ub.user_id = @userId ORDER BY ub.awarded_at DESC
            """, new { userId });

    private sealed class BadgeStats
    {
        public int Accepted { get; set; }
        public int Credited { get; set; }
        public int Chapters { get; set; }
    }
}
