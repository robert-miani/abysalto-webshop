# Abysalto Web Shop – Architecture Proposal and Cart API

This repository contains the solution to the Abysalto Senior Backend Developer technical task.

| Part | Location | Status |
| --- | --- | --- |
| High-level architecture and implementation strategy for a multi-channel retail platform (EU market, Croatia first) | [docs/architecture.md](docs/architecture.md) | Complete |
| Cart Web API (reference implementation of one service from the architecture) | `src/` | Not started |

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
- Docker (the integration tests start PostgreSQL, Redis, and the Azure Service Bus emulator with Testcontainers)

### Build and test

```bash
dotnet build CartService.slnx
dotnet test --solution CartService.slnx
```

### Run locally

```bash
dotnet run --project src/CartService.Api
```

The liveness endpoint is at http://localhost:5080/health/live.

### Solution structure

| Project | Responsibility |
| --- | --- |
| `src/CartService.Domain` | Cart aggregate and business rules, no dependencies |
| `src/CartService.Application` | Use cases (command and query handlers) and ports |
| `src/CartService.Infrastructure` | PostgreSQL, Redis, outbox relay, Azure Service Bus |
| `src/CartService.Api` | Host, Minimal API endpoints, authentication, health checks |
| `tests/CartService.Architecture.Tests` | Enforces the allowed dependencies between layers |
| `tests/CartService.Api.IntegrationTests` | End-to-end tests against real containers |
