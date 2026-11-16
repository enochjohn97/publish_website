CREATE DATABASE IF NOT EXISTS folio_db 
    CHARACTER SET utf8mb4 
    COLLATE utf8mb4_unicode_ci;

-- 1.2 User Management
DROP USER IF EXISTS 'folio'@'localhost';
DROP USER IF EXISTS 'folio'@'127.0.0.1';

CREATE USER 'folio'@'localhost' IDENTIFIED BY '';
CREATE USER 'folio'@'127.0.0.1' IDENTIFIED BY '';

GRANT ALL PRIVILEGES ON folio_db.* TO 'folio'@'localhost';
GRANT ALL PRIVILEGES ON folio_db.* TO 'folio'@'127.0.0.1';
FLUSH PRIVILEGES;

USE folio_db;

CREATE TABLE IF NOT EXISTS users (
    id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    email VARCHAR(255) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NULL,
    google_sub VARCHAR(255) NULL UNIQUE,
    avatar_url TEXT NULL,
    role ENUM('reader','author','admin') NOT NULL DEFAULT 'reader',
    reputation_score INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_users_role (role)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS admins (
    id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
    username VARCHAR(50) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_admins_user FOREIGN KEY (id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 1.3 Default Administrator Account
-- Username: admin
-- Password: admin12345
INSERT INTO users (id, username, email, password_hash, role, reputation_score)
VALUES ('00000000-0000-4000-8000-000000000001', 'admin', 'admin@folio.press', '$2b$12$cP3YJ97Go1TESi4BkUNc1uFeNwuXh3L6fH37frHQGKhkk288tRwoO', 'admin', 0)
ON DUPLICATE KEY UPDATE password_hash = VALUES(password_hash), role = VALUES(role);

INSERT INTO admins (id, username, password_hash)
VALUES ('00000000-0000-4000-8000-000000000001', 'admin', '$2b$12$cP3YJ97Go1TESi4BkUNc1uFeNwuXh3L6fH37frHQGKhkk288tRwoO')
ON DUPLICATE KEY UPDATE username = VALUES(username), password_hash = VALUES(password_hash);

CREATE TABLE IF NOT EXISTS books (
    id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    slug VARCHAR(255) NOT NULL UNIQUE,
    category VARCHAR(60) NOT NULL,
    synopsis TEXT NOT NULL,
    cover_image_url TEXT NULL,
    author_id CHAR(36) NULL COLLATE utf8mb4_unicode_ci,
    author_name VARCHAR(120) NOT NULL,
    status ENUM('draft','ongoing','completed') NOT NULL DEFAULT 'draft',
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_books_author FOREIGN KEY (author_id) REFERENCES users(id) ON DELETE SET NULL,
    INDEX idx_books_status (status),
    INDEX idx_books_category (category),
    FULLTEXT KEY ft_books (title, author_name, synopsis)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS chapters (
    id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
    book_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    chapter_number INT NOT NULL,
    title VARCHAR(255) NOT NULL,
    content JSON NOT NULL,
    audio_url TEXT NULL,
    status ENUM('draft','published') NOT NULL DEFAULT 'draft',
    published_at TIMESTAMP NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_chapters_book FOREIGN KEY (book_id) REFERENCES books(id) ON DELETE CASCADE,
    UNIQUE KEY uq_chapter_number (book_id, chapter_number),
    INDEX idx_chapters_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS comments (
    id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
    chapter_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    user_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    parent_id CHAR(36) NULL COLLATE utf8mb4_unicode_ci,
    paragraph_id VARCHAR(100) NULL,
    page_number INT NULL,
    highlighted_text TEXT NULL,
    kind ENUM('comment','correction') NOT NULL DEFAULT 'comment',
    is_correction BOOLEAN GENERATED ALWAYS AS (kind = 'correction') STORED,
    content TEXT NOT NULL,
    status ENUM('pending','accepted','rejected') NOT NULL DEFAULT 'pending',
    is_contributor_submission TINYINT(1) NOT NULL DEFAULT 0,
    is_spoiler TINYINT(1) NOT NULL DEFAULT 0,
    upvotes INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_comments_chapter FOREIGN KEY (chapter_id) REFERENCES chapters(id) ON DELETE CASCADE,
    CONSTRAINT fk_comments_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    CONSTRAINT fk_comments_parent FOREIGN KEY (parent_id) REFERENCES comments(id) ON DELETE CASCADE,
    INDEX idx_comments_chapter (chapter_id, status),
    INDEX idx_comments_status (status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS comment_votes (
    comment_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    user_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (comment_id, user_id),
    CONSTRAINT fk_cv_comment FOREIGN KEY (comment_id) REFERENCES comments(id) ON DELETE CASCADE,
    CONSTRAINT fk_cv_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS badges (
    id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
    code VARCHAR(50) NOT NULL UNIQUE,
    name VARCHAR(100) NOT NULL,
    description TEXT NOT NULL,
    icon_key VARCHAR(50) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_badges (
    user_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    badge_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    awarded_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, badge_id),
    CONSTRAINT fk_ub_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    CONSTRAINT fk_ub_badge FOREIGN KEY (badge_id) REFERENCES badges(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS book_contributors (
    book_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    user_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    role_label VARCHAR(60) NOT NULL DEFAULT 'Contributor',
    credited_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (book_id, user_id),
    CONSTRAINT fk_bc_book FOREIGN KEY (book_id) REFERENCES books(id) ON DELETE CASCADE,
    CONSTRAINT fk_bc_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS reading_history (
    user_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    chapter_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    book_id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci,
    progress INT NOT NULL DEFAULT 0,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, chapter_id),
    CONSTRAINT fk_rh_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    CONSTRAINT fk_rh_chapter FOREIGN KEY (chapter_id) REFERENCES chapters(id) ON DELETE CASCADE,
    CONSTRAINT fk_rh_book FOREIGN KEY (book_id) REFERENCES books(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- In-app notifications for editors (new contributions/notes) and readers (moderation, credits, new chapters).
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS uploads (
    id CHAR(36) NOT NULL COLLATE utf8mb4_unicode_ci PRIMARY KEY,
    mime VARCHAR(100) NOT NULL,
    size_bytes INT NOT NULL,
    data LONGBLOB NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Badge catalogue (real definitions, not sample content). The application re-syncs these on start.
INSERT INTO badges (id, code, name, description, icon_key) VALUES
    (UUID(), 'first_voice', 'First Voice', 'Shared a comment that was accepted by the editors.', 'message'),
    (UUID(), 'contributor', 'Contributor', 'Officially credited as a contributor on a published book.', 'pen'),
    (UUID(), 'marginalia', 'Marginalia', 'Had ten or more contributions accepted by the editors.', 'bookmark'),
    (UUID(), 'bibliophile', 'Bibliophile', 'Read and tracked progress across five chapters.', 'glasses')
ON DUPLICATE KEY UPDATE name = VALUES(name), description = VALUES(description), icon_key = VALUES(icon_key);
