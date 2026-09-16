# Agentic Software Engineering System

Gate 1 establishes a .NET 10 modular monolith foundation for the URL-shortener workload.

## Run locally

```powershell
dotnet restore AgenticSoftwareEngineering.slnx
dotnet run --project AgenticSoftwareEngineering.Api
```

The API applies pending EF Core migrations to one SQLite database at `agentic-sdlc.db` and exposes Swagger UI at `/swagger`. Startup migration is a prototype convenience for local review; production deployments should apply migrations through a governed release process rather than application startup.

Create a baseline short link with `POST /api/short-links`. The returned `ShortUrl` is directly usable through the public `GET /{shortCode}` redirect route.

## Test

```powershell
dotnet test AgenticSoftwareEngineering.slnx
```

This increment includes only the greenfield baseline: URL validation, generated short codes, redirect resolution, and privacy-minimized click analytics. Custom aliases, expiration, and orchestration execution are deliberately deferred to later controlled increments.
