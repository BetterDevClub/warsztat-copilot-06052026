# BookSlot

Multi-tenant appointment booking platform (.NET 10, PostgreSQL, Blazor Web App).

> Built iteratively across 35 phases — see `projekt-startowy.md` and the session plan for details.

## Stack

| Layer | Tech |
| --- | --- |
| Domain / Application | .NET 10, MediatR, FluentValidation, Result<T> |
| Infrastructure | EF Core 10, PostgreSQL, Redis |
| API | ASP.NET Core 10 Web API |
| Web | Blazor Web App (.NET 10) — Server + WASM interactive modes |
| Worker | .NET Worker Service (custom `BackgroundService`s + DB outbox) |
| Auth | ASP.NET Core Identity + JWT (access + refresh) |
| Observability | Serilog + OpenTelemetry (OTLP) + HealthChecks |
| Testing | xUnit, FluentAssertions, NSubstitute, Testcontainers, NetArchTest |

## Solution layout

```
BookSlot.slnx
src/
├── BookSlot.Domain/          entities, value objects, domain events
├── BookSlot.Application/     CQRS (MediatR), DTOs, interfaces, pipeline behaviors
├── BookSlot.Infrastructure/  EF Core, email, external APIs
├── BookSlot.Api/             ASP.NET Core Web API
├── BookSlot.Web/             Blazor Web App — server host
├── BookSlot.Web.Client/      Blazor Web App — WASM client
└── BookSlot.Worker/          Background jobs (outbox polling, reminders, webhooks…)
tests/
├── BookSlot.UnitTests/
├── BookSlot.IntegrationTests/
└── BookSlot.ArchitectureTests/   NetArchTest guardrails
```

## Prerequisites

- .NET 10 SDK (`10.0.201` or newer — pinned in `global.json`)
- **Docker Desktop** (WSL2 backend) — for the dev stack (PostgreSQL, Redis, MailHog, Seq, Aspire Dashboard). Install from <https://www.docker.com/products/docker-desktop/>.

## Dev stack (docker compose)

```powershell
# optional: copy env defaults
Copy-Item .env.example .env

# start everything in the background
docker compose up -d

# check health
docker compose ps

# stop (keep data)
docker compose down

# stop and wipe volumes
docker compose down -v
```

### Service map

| Service | URL / Port | Purpose |
| --- | --- | --- |
| Postgres | `localhost:5432` (`bookslot`/`bookslot`/`bookslot`) | Main DB |
| Redis | `localhost:6379` | SignalR backplane + distributed cache |
| MailHog SMTP | `localhost:1025` | App SMTP target (dev email) |
| MailHog UI | <http://localhost:8025> | Captured email inbox |
| Seq | <http://localhost:8081> | Structured log UI (Serilog sink) |
| Aspire Dashboard | <http://localhost:18888> | OpenTelemetry traces/metrics/logs |
| Aspire OTLP (gRPC) | `localhost:18889` | OTLP ingest endpoint (apps ship here) |

Connection strings and OTLP endpoint are already set in each app's `appsettings.Development.json`.

## Quick start

```powershell
# 1. dev stack
docker compose up -d

# 2. solution
dotnet restore
dotnet build
dotnet test
```

## Current status

Phase 1 — dev environment (docker compose stack + app configs). No runtime behavior yet.
