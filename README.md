# Abysalto Web Shop – Architecture Proposal and Cart API

This repository contains the solution to the Abysalto Senior Backend Developer technical task.

| Part | Location | Status |
| --- | --- | --- |
| High-level architecture and implementation strategy for a multi-channel retail platform (EU market, Croatia first) | [docs/architecture.md](docs/architecture.md) | Complete |
| Cart Web API (reference implementation of one service from the architecture) | `src/` | In progress |

## Architecture document

The document describes the target system for a retail platform that serves millions of users a day
through a web shop, mobile apps, marketplace integrations, and B2B integrations. It covers the
architecture view, key components, scaling, security, external integrations (including Croatian
fiscalization), monitoring, and the code delivery plan. A decision log records each load-bearing
decision with its reason, the rejected alternative, and the condition to revisit it.

GitHub renders the diagrams in the Markdown file. To read the document with its diagrams outside GitHub,
open [docs/architecture.html](docs/architecture.html) in a browser (it needs an internet connection to load
the Markdown and Mermaid renderers). The Markdown file is the source; the HTML file is a rendered copy.

## Cart API

The Cart API is one service of the platform described in the architecture document, built with .NET 10.
It is under construction; this section grows with each pull request.

### Prerequisites

- .NET 10 SDK (see `global.json`)
- Docker (the integration tests start PostgreSQL and the Azure Service Bus emulator with Testcontainers)

### Run with Docker Compose

```bash
docker compose up --build
```

This starts PostgreSQL 17 and the API. The API applies the database migrations when it starts and answers on
http://localhost:8080. Check it with http://localhost:8080/health/live. Stop and remove everything, including
the database volume, with `docker compose down -v`.

On the very first start the log shows one `fail:` entry for `SELECT ... FROM "__EFMigrationsHistory"`. That is
Entity Framework Core checking its history table in a new database, just before it creates the table. It is
expected and not an error of the service.

### Run the API from the command line

```bash
docker compose up -d postgres
dotnet run --project src/CartService.Api
```

The API listens on http://localhost:5080 and uses the connection string in
`src/CartService.Api/appsettings.Development.json`, which points to the PostgreSQL container above.

### Build and test

```bash
dotnet build CartService.slnx
dotnet test --solution CartService.slnx
```

The integration tests need Docker. They start a PostgreSQL container, apply the real migrations to it, and
remove it when the tests finish.

### Database and migrations

| Topic | Decision |
| --- | --- |
| Schema | `carts` and `cart_items`, created by Entity Framework Core migrations in `src/CartService.Infrastructure/Persistence/Migrations` |
| Integrity in the database | One owner per cart, quantity between 1 and 20, and one active cart per customer (partial unique index) |
| Concurrent changes | Every change increments the cart version; a write based on an old version is rejected and becomes HTTP 409 |
| Applying migrations | `Database:ApplyMigrationsOnStartup` is on for Docker Compose and local runs, and off by default so that production applies migrations in the delivery pipeline |

Create a new migration after a model change:

```bash
dotnet tool restore
dotnet dotnet-ef migrations add <Name> --project src/CartService.Infrastructure --output-dir Persistence/Migrations
```

### Solution structure

| Project | Responsibility |
| --- | --- |
| `src/CartService.Domain` | Cart aggregate and business rules, no dependencies |
| `src/CartService.Application` | Use cases (command and query handlers) and ports |
| `src/CartService.Infrastructure` | PostgreSQL, Redis, outbox relay, Azure Service Bus |
| `src/CartService.Api` | Host, Minimal API endpoints, authentication, health checks |
| `tests/CartService.Domain.Tests` | Unit tests of the business rules |
| `tests/CartService.Architecture.Tests` | Enforces the allowed dependencies between layers |
| `tests/CartService.Api.IntegrationTests` | Tests against real containers: PostgreSQL, Service Bus emulator |
