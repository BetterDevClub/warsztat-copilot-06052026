# BookSlot

Multi-tenant SaaS appointment booking platform built on **.NET 10**, **PostgreSQL**, **Redis** and **Blazor Web App**, organized as **Vertical Slice Architecture (VSA)**.

> Built iteratively across 35 phases. The full plan lives in `project-brief.md` and the phase-by-phase progress in the session checkpoint folder.

---

## Stack

| Layer            | Tech                                                                                     |
| ---------------- | ---------------------------------------------------------------------------------------- |
| Domain           | .NET 10, Result/Error primitives, value objects, domain events                           |
| Features (VSA)   | One folder per use-case (`*Endpoint`, `*Handler`, `*Validator`, `*Command`/`*Query`)     |
| Infrastructure   | EF Core 10 (Npgsql + naming conventions), Identity, Redis, outbox, security, observability |
| API              | ASP.NET Core 10 Minimal APIs + JWT bearer, rate limiting, OpenAPI                        |
| Web              | Blazor Web App (Server + WASM auto), MudBlazor + blazor-bootstrap, cookie auth, antiforgery |
| Worker           | .NET Worker Service — outbox dispatcher, reminders, webhook delivery, leader election     |
| Auth             | ASP.NET Core Identity + JWT (access + refresh) + roles (`Owner`, `Staff`, `Admin`)        |
| Observability    | Serilog → Seq, OpenTelemetry (OTLP), HealthChecks (`/health/live`, `/ready`)              |
| Testing          | xUnit, FluentAssertions, NSubstitute, Testcontainers (Postgres), NetArchTest              |

## Solution layout

```
BookSlot.slnx
src/
├── BookSlot.Domain/            entities, value objects, domain events, Result<T>
├── BookSlot.Infrastructure/    EF Core, Identity, Redis, observability, security
├── BookSlot.Features/          VSA slices — Features/<Area>/<Operation>/*.cs + Shared/
├── BookSlot.Api/               REST API host (Minimal APIs, JWT)
├── BookSlot.Web/               Blazor Web App server host (cookie auth + SignalR hub)
├── BookSlot.Web.Client/        Blazor WASM client assembly (interactive auto)
└── BookSlot.Worker/            Background jobs (outbox, reminders, webhooks)
tests/
├── BookSlot.UnitTests/         pure unit tests, no I/O
├── BookSlot.IntegrationTests/  Testcontainers Postgres, end-to-end slice tests
└── BookSlot.ArchitectureTests/ NetArchTest VSA guardrails
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the runtime diagram and slice rules.

## Prerequisites

- **.NET 10 SDK** (`10.0.201` or newer — pinned in `global.json`)
- **Docker Desktop** (WSL2 backend on Windows) — for the dev stack

## Dev stack

```powershell
# optional: copy env defaults
Copy-Item .env.example .env -ErrorAction SilentlyContinue

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

| Service           | URL / Port                                  | Purpose                                  |
| ----------------- | ------------------------------------------- | ---------------------------------------- |
| Postgres          | `localhost:5432` (`bookslot`/`bookslot`)    | Main DB                                  |
| Redis             | `localhost:6379`                            | SignalR backplane + distributed cache    |
| MailHog SMTP      | `localhost:1025`                            | App SMTP target (dev email)              |
| MailHog UI        | <http://localhost:8025>                     | Captured email inbox                     |
| Seq               | <http://localhost:8081>                     | Structured log UI + OTLP receiver        |
| Aspire Dashboard  | <http://localhost:18888>                    | Optional OTel UI (image pull may fail)   |

Connection strings and OTLP endpoints are pre-wired in each app's `appsettings.Development.json`.

## Quick start

```powershell
# 1. dev stack
docker compose up -d postgres redis mailhog seq

# 2. solution
dotnet restore BookSlot.slnx
dotnet build BookSlot.slnx
dotnet test tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj
dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj

# 3. database — apply migrations + seed roles + demo data (one-shot)
dotnet run --project src/BookSlot.MigrationRunner

# 4. run the apps (separate terminals)
dotnet run --project src/BookSlot.Api
dotnet run --project src/BookSlot.Web
dotnet run --project src/BookSlot.Worker
```

`BookSlot.MigrationRunner` is a console app that owns the database lifecycle (migrations + Identity roles + demo data). Hosts never migrate or seed at startup — run the runner once before booting any host. Re-run any time it's safe (idempotent).

### Default dev credentials

| Role  | Email              | Password    |
| ----- | ------------------ | ----------- |
| Owner | `admin@demo.local` | `Admin123!` |
| Staff | `staff@demo.local` | `Staff123!` |

## Docker images

Production images are built per service via `.github/workflows/docker.yml` and published to GHCR:

```powershell
docker build -f src/BookSlot.Api/Dockerfile    -t bookslot-api:local    .
docker build -f src/BookSlot.Web/Dockerfile    -t bookslot-web:local    .
docker build -f src/BookSlot.Worker/Dockerfile -t bookslot-worker:local .
```

All three run as non-root, expose `:8080`, and have a `wget`-based liveness probe against `/health/live`.

## Tests

```powershell
# Unit + arch (fast, no infra)
dotnet test tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj
dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj

# Integration (Testcontainers spins up Postgres - needs Docker)
dotnet test tests/BookSlot.IntegrationTests/BookSlot.IntegrationTests.csproj
```

Coverage settings live in `coverlet.runsettings` (cobertura+opencover).

## Operations

- [`docs/RUNBOOK.md`](docs/RUNBOOK.md) — startup, health checks, common ops tasks, troubleshooting.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — request flow diagram, slice anatomy, multi-tenant model.
- [`.github/copilot-instructions.md`](.github/copilot-instructions.md) — guidance for AI agents working on this repo.
- [`.github/AGENTS.md`](.github/AGENTS.md) — 5-stage agentic pipeline (planner → HITL → implementer ↔ verifier → code-reviewer → HITL → pr-commit) with mandatory human checkpoints and long-term memory in [`docs/agent-decisions.md`](docs/agent-decisions.md).

## Workshop Materials

Ready-to-use tools, checklists, and recipes for workshop participants:

- [`.github/prompts/refactor.prompt.md`](.github/prompts/refactor.prompt.md) — structured prompt for safe legacy code refactoring (explicit pre-conditions, rollback strategy, side-effect audit).
- [`docs/code-review/ai-code-review-checklist.md`](docs/code-review/ai-code-review-checklist.md) — AI code review checklist: null safety, IDisposable leaks, side effects in pure functions, happy-path-only testing, tenant scope violations.
- [`.github/skills/pr-review/SKILL.md`](.github/skills/pr-review/SKILL.md) — PR review skill (Principal-level mandates: VSA guardrails, Result<T>, tenant filter, no Repository in slice). Run before submitting PR.
- [`.github/skills/safe-refactor/SKILL.md`](.github/skills/safe-refactor/SKILL.md) — safe refactor skill (phase-by-phase refactoring workflow with explicit rollback points). Use when rebuilding complex modules.
- [`.github/skills/ci-yaml-author/SKILL.md`](.github/skills/ci-yaml-author/SKILL.md) — CI YAML author skill (generates/modifies GitHub Actions workflows with best practices: caching, matrix, secrets, OIDC). Use when adding new CI/CD jobs.
- [`.github/pull_request_template.md`](.github/pull_request_template.md) — PR template (sections: what and why, how tested, AI review checklist, self-review, breaking changes, observability). Fill out before submitting.
- [`.github/CODEOWNERS`](.github/CODEOWNERS) — code owners (placeholder teams: `@BetterDevClub/maintainers`, `@BetterDevClub/domain-owners`, etc.). Replace with your teams/users before production.
- [`.github/workflows/dotnet-ci.yml`](.github/workflows/dotnet-ci.yml) — reference CI for .NET 10 (restore, build, unit tests, architecture tests, integration tests with Testcontainers). Fork/adapt for your own repo.
- [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml) — CD skeleton (build Docker images, push to GHCR, deploy to staging/prod with approval gate). Extend with your target (AKS, ECS, App Service).
- [`tests/BookSlot.ArchitectureTests/ArchitectureTests.cs`](tests/BookSlot.ArchitectureTests/ArchitectureTests.cs) — architecture test index (NetArchTest): layering, slice isolation, naming conventions, no cross-slice dependencies. Extend for your own guardrails.
