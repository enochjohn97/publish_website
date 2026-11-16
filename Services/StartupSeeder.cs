using Folio.Web.Data;
using Folio.Web.Models;
using Microsoft.Extensions.Configuration;

namespace Folio.Web.Services;

/// <summary>
/// Creates the minimum real data the platform needs to operate: the editorial account
/// (from configuration) and the badge catalogue. Never inserts books, readers or notes.
/// </summary>
public static class StartupSeeder
{
    private static readonly (string Code, string Name, string Description, string IconKey)[] BadgeCatalog =
    {
        ("first_voice", "First Voice", "Shared a comment that was accepted by the editors.", "message"),
        ("contributor", "Contributor", "Officially credited as a contributor on a published book.", "pen"),
        ("marginalia", "Marginalia", "Had ten or more contributions accepted by the editors.", "bookmark"),
        ("bibliophile", "Bibliophile", "Read and tracked progress across five chapters.", "glasses"),
    };

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Db>();

        await db.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS admins (
                id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
                username VARCHAR(50) NOT NULL UNIQUE,
                password_hash VARCHAR(255) NOT NULL,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT fk_admins_user FOREIGN KEY (id) REFERENCES users(id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);

        // Idempotent schema upgrades for deployments created before paged marginalia existed.
        await EnsureColumnAsync(db, logger, "page_number", "INT DEFAULT NULL AFTER paragraph_id");
        await EnsureColumnAsync(db, logger, "kind", "VARCHAR(20) DEFAULT 'comment' AFTER content");
        await EnsureColumnAsync(db, logger, "is_correction", "BOOLEAN GENERATED ALWAYS AS (kind = 'correction') STORED");

        // In-app notifications for editors and readers (idempotent for existing databases).
        await db.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS notifications (
                id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
                user_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
                kind VARCHAR(40) NOT NULL DEFAULT 'info',
                title VARCHAR(160) NOT NULL,
                body TEXT NOT NULL,
                url VARCHAR(255) NULL,
                is_read TINYINT(1) NOT NULL DEFAULT 0,
                created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT fk_notifications_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
                INDEX idx_notifications_user (user_id, is_read, created_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """);

        foreach (var badge in BadgeCatalog)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO badges (id, code, name, description, icon_key)
                VALUES (@id, @code, @name, @description, @iconKey)
                ON DUPLICATE KEY UPDATE name = VALUES(name), description = VALUES(description), icon_key = VALUES(icon_key)
                """,
                new { id = Guid.NewGuid().ToString(), code = badge.Code, name = badge.Name, description = badge.Description, iconKey = badge.IconKey });
        }

        var username = configuration["Admin:Username"] ?? "admin";
        var password = configuration["Admin:Password"] ?? "admin12345";
        var existingAdmin = await db.FirstAsync<AdminLoginRow>(
            "SELECT id, username, password_hash, role FROM users WHERE username = @username LIMIT 1",
            new { username });
        var existingInAdmins = await db.FirstAsync<AdminLoginRow>(
            "SELECT id, username, password_hash FROM admins WHERE username = @username LIMIT 1",
            new { username });

        if (existingAdmin is not null && existingAdmin.Role != "admin")
        {
            throw new InvalidOperationException($"Configured admin username '{username}' is already used by a non-admin account.");
        }

        var hash = existingAdmin?.PasswordHash;
        var needsUpdate = string.IsNullOrWhiteSpace(hash) || !BCrypt.Net.BCrypt.Verify(password, hash);
        if (needsUpdate)
        {
            hash = BCrypt.Net.BCrypt.HashPassword(password, 12);
        }

        if (existingAdmin is null)
        {
            var id = Guid.NewGuid().ToString();
            await db.ExecuteAsync(
                """
                INSERT INTO users (id, username, email, password_hash, role, reputation_score)
                VALUES (@id, @username, @email, @hash, 'admin', 0)
                """,
                new
                {
                    id,
                    username,
                    email = configuration["Admin:Email"] ?? "admin@folio.press",
                    hash,
                });
            existingAdmin = new AdminLoginRow { Id = id, Username = username, PasswordHash = hash };
        }
        else if (needsUpdate)
        {
            await db.ExecuteAsync("UPDATE users SET password_hash = @hash, role = 'admin' WHERE id = @id", new { hash, existingAdmin.Id });
        }

        if (existingInAdmins is null)
        {
            await db.ExecuteAsync(
                "INSERT INTO admins (id, username, password_hash) VALUES (@id, @username, @hash)",
                new { id = existingAdmin.Id, username, hash });
        }
        else if (string.IsNullOrWhiteSpace(existingInAdmins.PasswordHash) || !BCrypt.Net.BCrypt.Verify(password, existingInAdmins.PasswordHash))
        {
            await db.ExecuteAsync(
                "UPDATE admins SET password_hash = @hash WHERE username = @username",
                new { hash, username });
        }

        logger.LogInformation("Bootstrapped editorial account {Username}", username);
    }

    private static async Task EnsureColumnAsync(Db db, ILogger logger, string column, string definition)
    {
        // Identifiers are fixed literals here; values supplied through configuration are not used in SQL text.
        var exists = await db.ScalarAsync<long>(
            """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'comments' AND COLUMN_NAME = @column
            """, new { column });
        if (exists > 0) return;

        await db.ExecuteAsync($"ALTER TABLE comments ADD COLUMN `{column}` {definition}");
        logger.LogInformation("Applied schema upgrade: comments.{Column}", column);
    }
}
