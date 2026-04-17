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
- Docker Desktop (for later phases: PostgreSQL, Redis, MailHog, Seq, Aspire Dashboard)

## Quick start

```powershell
dotnet restore
dotnet build
dotnet test
```

## Current status

Phase 0 — solution bootstrap. No runtime behavior yet.
