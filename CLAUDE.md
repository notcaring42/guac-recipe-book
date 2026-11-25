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
```

No test projects exist yet. No linter or formatter is configured.

## Architecture

This is an ASP.NET Core 11.0 minimal API project (C#). It uses the single-file `Program.cs` approach (no controllers).

- **recipe-book.sln** — Solution file containing one project
- **recipe-book-api/** — The API project (`net11.0`, root namespace: `recipe_book_api`)
  - `Program.cs` — Entry point, service registration, middleware pipeline, and route definitions
  - `Models/` — EF Core entities (e.g. `Food`)
  - `Data/` — `RecipeBookContext` (the DbContext)
  - `Dtos/` — Request/response records. Endpoints accept and return DTOs, never EF entities directly. Request DTOs carry their own `Validate()` method; endpoints return `Results.ValidationProblem(...)` on failure.
  - `Migrations/` — EF Core migrations
  - OpenAPI/Swagger enabled in Development environment via `MapOpenApi()`

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
