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

## Agentic workflow

Non-trivial changes go through the 5-stage pipeline defined under `.github/agents/`:

```
prompt → planner → ⏸ HITL #1 ⏸ → implementer ↔ verifier (≤3 cycles) → code-reviewer → ⏸ HITL #2 ⏸ → pr-commit
```

- **5 agents:** `planner`, `implementer`, `verifier`, `code-reviewer`, `pr-commit` (+ `orchestrator`). Each is mirrored in two locations with platform-native frontmatter: `.github/agents/<name>.agent.md` (GitHub Copilot) and `.claude/agents/<name>.md` (Claude Code). Bodies are kept identical; verify with `pwsh ./scripts/agents-check-drift.ps1`.
- **Two mandatory HITL checkpoints:** after `planner` (approve `plan.md`) and after `code-reviewer` (approve `review.md` + diff). The orchestrator **never auto-approves on the human's behalf** — there is no `--yolo` flag, no fallback "default approve" on timeout. `awaiting_human` is a hard wait.
- **Scope limiting:** `implementer` may write only under `src/BookSlot.Features/**`, `src/BookSlot.Domain/**`, `src/BookSlot.Infrastructure/**`, `tests/**` and the two doc files. It is explicitly denied write access to `.github/workflows/**`, `.github/agents/**`, `.claude/agents/**`, `Directory.*.props`, `global.json`, `BookSlot.slnx`, `.editorconfig`. `verifier` and `code-reviewer` are read-only. Only `pr-commit` may touch `docs/agent-decisions.md`. Enforced deterministically by `scripts/scope-check.ps1` (called from `verify.ps1` and the optional git pre-commit hook in `scripts/githooks/`).
- **Iteration guard:** two independent budgets — `iterations.verifier` capped at **3** verifier FAIL bounce-backs, `iterations.review` capped at **2** HITL #2 `REQUEST_CHANGES` rounds. Either cap → `BLOCKED` (`blocked` / `blocked:review_loop`) and escalation to a human with the full context dump. Each agent also has a 10-minute timeout per iteration.
- **Hybrid LLM/script design:** mechanical work (state machine, build/test, scope-check, plan/review lint, slice scaffolding, git/gh, repo digest) lives in PowerShell scripts under `scripts/`; LLM agents do only judgment. Canonical pipeline driver: `scripts/agent-run.ps1`.
- **Model matrix (per usage-based billing):**

  | Agent           | Copilot                                  | Claude   |
  |-----------------|------------------------------------------|----------|
  | `orchestrator`  | `gpt-4.1`                                | `haiku`  |
  | `planner`       | `Claude Opus 4.7`                        | `opus`   |
  | `implementer`   | `[Claude Sonnet 4.6, gpt-5.5]`           | `sonnet` |
  | `verifier`      | (script — no LLM)                        | (script) |
  | `code-reviewer` | `Claude Haiku 4.5`                       | `haiku`  |
  | `pr-commit`     | `gpt-4.1`                                | `haiku`  |

- **Run artifacts** live under `./.agent-run/<run-id>/` (gitignored): `prompt.md`, `plan.md`, `plan-context.md`, `plan.approved.md`, `implementation/`, `verify-report.md`, `review-input.md`, `review.md`, `review.approved.md`, `plan.delta.txt`, `review.delta.txt`, `pr-body.md`, `state.json`.
- **Long-term memory — `docs/agent-decisions.md`:** an append-only log written by `pr-commit` after each run. It captures the deltas between every agent's output (`plan.md`, `review.md`) and the human-approved version (`plan.approved.md`, `review.approved.md`). Sections labeled `### Generalize as rule:` are loaded into the system prompts of `planner` and `implementer` on every subsequent run, so each correction permanently improves the agents' behavior for this repo.
- **CI gate:** `.github/workflows/ci.yml` runs `agents-check-drift.ps1` on every push/PR — both mirrors must stay byte-identical.

Operator quickstart: [`.github/AGENTS.md`](AGENTS.md).
