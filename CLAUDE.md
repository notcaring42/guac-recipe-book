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

The project is freshly scaffolded from the ASP.NET Core template with no domain logic implemented yet. There is no database, authentication, or Docker configuration.
