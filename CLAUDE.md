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

This is an ASP.NET Core 10.0 minimal API project (C#). It uses the single-file `Program.cs` approach (no controllers).

- **recipe-book.sln** — Solution file containing one project
- **recipe-book-api/** — The API project (`net10.0`, root namespace: `recipe_book_api`)
  - `Program.cs` — Entry point, service registration, middleware pipeline, and route definitions
  - OpenAPI/Swagger enabled in Development environment via `MapOpenApi()`

## Database

Uses **Entity Framework Core** with **SQLite**. The DbContext is `recipe_book_api.Data.RecipeBookContext`.

Connection string is in `appsettings.json` (`DefaultConnection`), defaulting to a local `recipe-book.db` file.

```bash
# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project recipe-book-api
dotnet ef database update --project recipe-book-api
```

Note: Requires `dotnet-ef` tool installed globally (`dotnet tool install --global dotnet-ef`).
