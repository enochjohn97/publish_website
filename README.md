# Folio Press — publishing & printing platform

An ASP.NET Core 9 (MVC + Razor) publishing platform: a public catalogue and immersive reader, a
marginalia/moderation workflow, and an editorial dashboard that publishes real manuscripts into MySQL.

There is **no seed fiction, sample book, or hard-coded content** anywhere in the app. The database in
this checkout contains exactly one account (the editor) and the badge catalogue; everything else is
created through the UI.

## Stack

| Layer | Choice |
| --- | --- |
| Web | ASP.NET Core 9 MVC, Razor views, custom CSS design system |
| Data | MySQL 8, `MySqlConnector` + Dapper, fully parameterized SQL |
| Auth | Cookie authentication (HttpOnly, SameSite=Lax), Google OAuth 2.0 via `Microsoft.AspNetCore.Authentication.Google`, bcrypt for the editor account |
| Uploads | Covers stored as validated `LONGBLOB` rows and served from `/api/images/{id}` |
| Client | Vanilla JS modules: live catalogue search, reader narration, admin modals |

## Run it

```bash
# 1. A MySQL 8 server with a database + user, then load the schema:
mysql -u root -p -e "CREATE DATABASE folio CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
mysql -u root -p folio < db/schema.sql

# 2. Point the app at it (appsettings.json → ConnectionStrings:Default)
#    Development checkout uses a project-local MySQL instance on 127.0.0.1:3307.

# 3. Start
dotnet run --urls http://localhost:3100
```

On first start the app creates the editorial account from `Admin:Username` / `Admin:Password`
(default `admin` / `admin12345`) and re-syncs the badge catalogue. Change both before exposing the
site; the password is hashed with bcrypt at startup, never stored in plaintext.

### Google sign-in (readers)

1. Google Cloud Console → APIs & Services → Credentials → OAuth client ID (Web application).
2. Authorised redirect URI: `https://your-host/signin-google` (and `http://localhost:3100/signin-google` for local work).
3. Set the credentials, preferably as environment variables rather than committed files:

```bash
export Authentication__Google__ClientId="…apps.googleusercontent.com"
export Authentication__Google__ClientSecret="…"
```

Without those values the login page shows a setup notice and reader sign-in is disabled; editorial
credentials still work. On first Google sign-in a reader row is created from the Google `sub`, email,
display name and profile photo, and the photo/name are refreshed on every later sign-in.

## Feature map

**Public**
- Home with a featured title, catalogue stats and a live search field. Search queries title, author,
  genre and synopsis with genre/completion filters and client-side sorting (`/api/books`).
- Book page: cover, synopsis, status, chapter list and a public credits page for readers promoted to
  contributors.
- Immersive reader (`/books/{slug}/{chapter}`): light/sepia/dark themes, font size, serif/sans,
  adjustable line height, margin notes toggle, and synchronized narration. The TTS engine speaks one
  sentence at a time and highlights the matching paragraph as it goes, with play/pause/stop, previous
  and next sentence, and speed control. Optional recorded audio appears when `audio_url` is set.
- Paragraph-anchored marginalia: select text inside a paragraph to open the composer with the quote and
  block id attached. End-of-chapter notes support replies, spoiler flags, “submit as contribution” and
  one upvote per account. All published notes show author name, Google avatar and exact date/time.
- Profile modal from the nav: Google photo, name, email, role, reputation, badges, reading history,
  credited books and recent notes, plus sign out.

**Editorial dashboard** (`/admin`, cookie role `admin`)
- Stats, moderation queue and recent decisions.
- Book upload modal with mandatory category, name, author, synopsis, cover and auto creation date.
  Cover is checked client-side and re-checked server-side (declared MIME, magic bytes, ≤ 4 MB).
  Empty/invalid fields return field-level messages.
- Chapter editor: title, number, plain-text manuscript converted to addressable paragraph blocks,
  optional audio URL, publish/draft.
- Status changes (draft/ongoing/completed), book deletion, and per-book filters.
- Accept/reject with one click. Accepting a **contribution** opens the prompt: “Would you like to
  officially add [User Name] as a contributor to this book?” Confirming credits the reader on the
  public page, promotes `reader` → `author`, adds 25 reputation and awards the Contributor badge.

## Security notes

- Passwords: bcrypt (cost 12) for the editor account; Google accounts never store a password.
- Sessions: encrypted authentication cookie, `HttpOnly`, `SameSite=Lax`, 7-day absolute expiry
  (8-hour for editor sign-in), `Secure` on HTTPS.
- CSRF: ASP.NET antiforgery tokens on HTML forms (login/logout) and a double-submit
  cookie/header pair (`folio_api_csrf` + `RequestVerificationToken`) for every JSON/multipart API
  mutation, with same-origin checks. Failures return a friendly 403 JSON message.
- SQL injection: every statement is parameterized through Dapper; LIKE wildcards in search are escaped.
- XSS: all user text is stripped of tags on the server (`Sanitizer`), rendered through Razor’s encoder,
  and encoded again in JavaScript-rendered fragments. CSP, `X-Frame-Options: DENY`, `nosniff`,
  referrer policy and permissions policy are applied to every response.
- Upload safety: extension-independent magic-byte validation, size cap, dedicated `/api/images` route
  with immutable caching and `nosniff`.
- Authorization: role read from the database on each request; admin pages and APIs require the admin
  role; JSON APIs return 401/403 instead of redirects.
- Rate limits: editor login (6 per 5 min per IP + username), comments (8 per 10 min per user) and
  upvotes (30 per minute).
- Errors: users see short, non-technical messages with an HTTP status reference; the server logs
  structured, timestamped entries with stack traces and request context, never secrets.

## Project layout

```
Controllers/            Pages + /api endpoints (books, comments, profile, images, admin)
Data/                   Db gateway (Dapper) and Repository with all parameterized queries
Models/                 Domain rows, form models with DataAnnotations, view models
Services/               Sanitizer, ImageService, BadgeService, CsrfToken, StartupSeeder
Infrastructure/         API envelopes, friendly exceptions, claims helpers, rate limiter
Views/                  Razor views (Home, Books, Auth, Admin, Shared)
wwwroot/css|js          Design system and progressive-enhancement scripts
db/schema.sql           MySQL schema for deployment
```

## Verification performed

- `dotnet build` clean; app started on `http://localhost:3100`; `/health` returns database status.
- Editor login: wrong password returns the friendly inline error; correct password redirects to `/admin`.
- Book upload with a real PNG (validated, stored, served back at 200 `image/png`); missing cover returns
  400 with a `cover` field error.
- Two chapters created, reader rendered all paragraphs, notes posted, queue accepted, contributor
  credited and shown on the public credits page; profile API returned badges and history.
- CSRF: valid double-submit token accepted; missing token returns 403 JSON. Anonymous admin API
  returns 401 JSON; anonymous `/admin` redirects to `/login`.
- Verification data was removed afterwards: the database holds 0 books, 0 chapters, 0 comments, 1
  editor account and 4 badge definitions.

## Known limitations

- Google OAuth cannot be exercised here because no client ID/secret exists in this checkout; the UI
  reports the missing configuration instead of faking a login. Add credentials to enable reader sign-in.
- The development checkout runs its own MySQL instance on port 3307 under `.data/` (git-ignored). For
  production, point `ConnectionStrings:Default` at a managed MySQL server and set secrets through the
  environment, not `appsettings.json`.
- Speech narration uses the browser Speech Synthesis API, so voice quality depends on the installed
  system voices; no third-party AI voice service is required.
