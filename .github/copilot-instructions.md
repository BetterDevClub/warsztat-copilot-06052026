# Copilot instructions for BookSlot

This repo is a **Vertical Slice Architecture (VSA)** .NET 10 SaaS for multi-tenant appointment booking. Read these rules before generating code so suggestions stay consistent with what's already shipped.

## Project map

```
src/BookSlot.Domain/            // entities, value objects, Result<T>, no I/O
src/BookSlot.Infrastructure/    // EF Core, Identity, Redis, observability, security
src/BookSlot.Features/          // VSA slices: Features/<Area>/<Operation>/
src/BookSlot.Api/               // Minimal APIs + JWT
src/BookSlot.Web/               // Blazor Web App server host
src/BookSlot.Web.Client/        // Blazor WASM interactive client
src/BookSlot.Worker/            // BackgroundService jobs (outbox, reminders, webhooks)
src/BookSlot.MigrationRunner/   // one-shot console: migrations + role + demo data seeding
tests/BookSlot.UnitTests/
tests/BookSlot.IntegrationTests/    // Testcontainers Postgres
tests/BookSlot.ArchitectureTests/   // NetArchTest VSA guardrails — DO NOT remove
```

## Slice rules (NetArchTest enforces these)

- **One slice = one folder** under `src/BookSlot.Features/Features/<Area>/<Operation>/`.
- A slice contains at most: `*Endpoints` (static), `*Handler` (sealed), `*Validator`, `Command`/`Query`/`Response` records.
- **No cross-slice dependencies.** Need to share something? Put it under `src/BookSlot.Features/Shared/` or duplicate the DTO.
- **No `Repository` / `Service` classes inside a slice** — call `AppDbContext` directly from the handler.
- **Domain stays pure** — no `Microsoft.EntityFrameworkCore`, no `Microsoft.AspNetCore`, no exceptions for business failures (return `Result<T>`).
- Validation belongs in `*Validator` (FluentValidation), not in the handler.

## Conventions

- **Result pattern**: handlers return `Result<TResponse>`. Endpoints map via `result.ToHttpResult(...)`.
- **Tenant scope**: tenant-scoped entities implement `ITenantScoped`; the global query filter on `AppDbContext` enforces isolation. Never write `Where(x => x.TenantId == _ctx.TenantId)` manually — let the filter do it. If you need cross-tenant reads (Admin only), use `IgnoreQueryFilters()` and add an explicit comment.
- **Auth**:
  - API endpoints default to `RequireAuthorization` + a role policy (`Owner`/`Staff`/`Admin`). Public endpoints must call `.AllowAnonymous()` explicitly.
  - Sensitive auth endpoints use `.RequireRateLimiting("auth-sensitive")`.
- **Outbox**: domain events go through the outbox. Don't call external services from inside `SaveChangesAsync` — enqueue an outbox message and let `BookSlot.Worker` deliver it.
- **Webhooks**: signed with HMAC-SHA256 in `WebhookDeliveryJob`. Don't reinvent — reuse the helper.
- **Observability**: log via `ILogger<T>` (Serilog under the hood); traces via `BookSlotActivitySource.StartActivity(...)`. Don't bypass — Seq dashboards rely on the conventions.
- **Naming**: PascalCase types, file name matches type name, namespace = folder path under `src/<Project>/`.

## Testing rules

- **Unit tests** (`tests/BookSlot.UnitTests`): pure, no I/O, no `WebApplicationFactory`, no Docker. Use NSubstitute for collaborators.
- **Integration tests** (`tests/BookSlot.IntegrationTests`): use the existing Testcontainers Postgres fixture. New endpoints should get at least a happy-path integration test.
- **Architecture tests** (`tests/BookSlot.ArchitectureTests`): `LayeringTests`, `SliceIsolationTests`, `NamingConventionTests`. If you add a new top-level layer or naming convention, extend these.
- Don't disable analyzers (IDE0005 is treat-as-error in this repo — keep `using` directives minimal).

## Build / test commands

```powershell
dotnet build BookSlot.slnx --nologo
dotnet test  tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj         --nologo --no-build
dotnet test  tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj --nologo --no-build
```

`dotnet test` cannot accept multiple `*.csproj` arguments — invoke per project.

## Definition of done for a new feature

1. New folder under `Features/<Area>/<NewOperation>/` with the slice files.
2. FluentValidation rules where input is non-trivial.
3. Endpoint registered in the slice's `*Endpoints` class — no central router.
4. At least one unit test for the handler; at least one integration test for the endpoint if it touches the DB.
5. `dotnet build`, unit tests, and architecture tests all green.
6. If the change exposes new public surface, update `docs/ARCHITECTURE.md` or `docs/RUNBOOK.md` as appropriate.
