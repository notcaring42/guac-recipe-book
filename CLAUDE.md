# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build the solution
dotnet build

# Run the API (from repo root)
dotnet run --project recipe-book-api

# Run with a specific launch profile
dotnet run --project recipe-book-api --launch-profile http   # http://localhost:5007
dotnet run --project recipe-book-api --launch-profile https  # https://localhost:7092

# Run the tests
dotnet test
```

Tests live in `recipe-book-api.Tests/` (xUnit). Unit tests cover `User`, the auth DTOs, `AuthCookie` and `AdminSeeder` against an in-memory SQLite database (`TestDb`); `AuthEndpointTests` runs the real app via `WebApplicationFactory` against a temporary SQLite file. Tests run serially because they set the `GUAC_ADMIN_PWD` environment variable. No linter or formatter is configured.

## Architecture

This is an ASP.NET Core 11.0 minimal API project (C#). It uses the single-file `Program.cs` approach (no controllers).

- **recipe-book.sln** — Solution file containing the API and its test project
- **recipe-book-api/** — The API project (`net11.0`, root namespace: `recipe_book_api`)
  - `Program.cs` — Entry point, service registration, middleware pipeline, and route definitions
  - `Models/` — EF Core entities (e.g. `Food`)
  - `Data/` — `RecipeBookContext` (the DbContext)
  - `Dtos/` — Request/response records. Endpoints accept and return DTOs, never EF entities directly. Request DTOs carry their own `Validate()` method; endpoints return `Results.ValidationProblem(...)` on failure.
  - `Migrations/` — EF Core migrations
  - OpenAPI/Swagger enabled in Development environment via `MapOpenApi()`

## Authentication

Cookie authentication (`recipe-book.auth` cookie); passwords are hashed with ASP.NET Core Identity's `PasswordHasher<User>`.

- A **fallback authorization policy** requires a logged-in user on every endpoint. Endpoints that must be public (e.g. `/auth/login`) opt out with `.AllowAnonymous()`.
- Admin-only endpoints use `.RequireAuthorization("Admin")` (role claim `Admin`, from `User.IsAdmin`).
- Endpoints: `POST /auth/login`, `POST /auth/logout`, `GET /auth/me`, `POST /users` and `GET /users/{id}` (admin only).
- On startup, `Data/AdminSeeder.cs` ensures an `admin` user exists with the password from the **`GUAC_ADMIN_PWD`** environment variable. The variable is required on first run (startup fails without it); afterwards, if it's set to a new value, the admin password is updated to match.
- Usernames may use any script. Lookups and the unique index use `User.NormalizedUsername` (NFKC + `ToUpperInvariant`), never `Username` directly; create users with `User.Create(...)` so both columns are set.
- **Session revocation:** each user has a `SecurityStamp`, copied into the cookie at login. `Auth/AuthCookie.ValidateAsync` checks it against the database on every request, so **any code that changes a user's password or admin flag must also set `SecurityStamp = User.NewSecurityStamp()`** to log out that user's existing sessions. Sessions also expire 7 days after login regardless of activity.
- The cookie is `Secure` (HTTPS-only) outside Development.
- `POST /auth/login` is rate-limited per client IP (default 10 attempts per 60 seconds, then `429`). Configure with `RateLimiting:Login:PermitLimit` and `RateLimiting:Login:WindowSeconds`.

```bash
GUAC_ADMIN_PWD='...' dotnet run --project recipe-book-api
```

## Database

Uses **Entity Framework Core** with **SQLite**. The DbContext is `recipe_book_api.Data.RecipeBookContext`.

Connection string is in `appsettings.json` (`DefaultConnection`), defaulting to `recipe-book.db`. A relative path is resolved against the project's content root, so the file always lives at `recipe-book-api/recipe-book.db` regardless of the working directory. The app fails at startup if the connection string is missing.

Pending migrations are applied automatically on startup (`Database.Migrate()` in `Program.cs`), so `dotnet ef database update` is optional. After changing a model, you still need to add a migration:

```bash
# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project recipe-book-api
dotnet ef database update --project recipe-book-api
```

Note: Requires `dotnet-ef` tool installed globally (`dotnet tool install --global dotnet-ef`).
