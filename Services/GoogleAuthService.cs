using System.Text.Json;
using Folio.Web.Data;
using Folio.Web.Models;
using Microsoft.Extensions.Configuration;

namespace Folio.Web.Services;

/// <summary>
/// Creates or updates reader accounts from Google's OpenID Connect userinfo response
/// during the OIDC ticket created event. Kept as a small focused service so the
/// authentication callback stays readable.
/// </summary>
public sealed class GoogleAuthService
{
    private readonly Db _db;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(Db db, ILogger<GoogleAuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserRecord> UpsertReaderAsync(string googleSub, string email, string? name, string? picture, bool isNew)
    {
        if (isNew)
        {
            var username = await UniqueUsernameAsync(name ?? email.Split('@')[0], email);
            var id = Guid.NewGuid().ToString();

            await _db.ExecuteAsync(
                """
                INSERT INTO users (id, username, email, password_hash, google_sub, avatar_url, role, reputation_score)
                VALUES (@id, @username, @email, NULL, @googleSub, @avatar, 'reader', 0)
                """,
                new { id, username, email, googleSub, avatar = picture });

            _logger.LogInformation("Created Google reader {Username} ({UserId}) from {Email}", username, id, email);

            return new UserRecord
            {
                Id = id,
                Username = username,
                Email = email,
                AvatarUrl = picture,
                Role = "reader",
                ReputationScore = 0,
                CreatedAt = DateTime.UtcNow,
            };
        }

        var existing = await _db.FirstAsync<UserRecord>(
            "SELECT id, username, email, avatar_url, role, reputation_score, created_at FROM users WHERE google_sub = @googleSub LIMIT 1",
            new { googleSub });

        if (existing is null)
        {
            throw new InvalidOperationException($"Google account {email} has no local account.");
        }

        var avatar = picture ?? existing.AvatarUrl;
        if (!string.Equals(existing.AvatarUrl, avatar, StringComparison.Ordinal) ||
            !string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            await _db.ExecuteAsync(
                "UPDATE users SET avatar_url = @avatar, email = @email WHERE id = @id",
                new { avatar, email, id = existing.Id });
            existing.AvatarUrl = avatar;
            existing.Email = email;
            _logger.LogInformation("Updated Google profile for {Username} ({UserId})", existing.Username, existing.Id);
        }

        return existing;
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

        return $"{baseName} {Guid.NewGuid():N}".AsSpan(0, 36).ToString()[..30];
    }
}
