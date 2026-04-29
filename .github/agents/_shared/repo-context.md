# Shared repo context (loaded by every agent)

> Every agent (planner / implementer / verifier / code-reviewer / pr-commit) **must** include this file in its prompt as the first source of truth about the repo. This is a summary — details live in the linked documents.

## 1. About the project

- **BookSlot** — multi-tenant SaaS for appointment booking.
- Stack: **.NET 10**, EF Core 10 (Postgres + Npgsql), Blazor Web App (Server + WASM), Worker Service, Redis, Identity + JWT.
- Architecture: **Vertical Slice Architecture (VSA)** + a small Domain + a single Infrastructure.

Full description: [`docs/ARCHITECTURE.md`](../../../docs/ARCHITECTURE.md), runtime ops: [`docs/RUNBOOK.md`](../../../docs/RUNBOOK.md), AI rules: [`.github/copilot-instructions.md`](../../copilot-instructions.md).

## 2. Project map

```
src/BookSlot.Domain/            entities, value objects, Result<T>, no I/O
src/BookSlot.Infrastructure/    EF Core, Identity, Redis, observability, security
src/BookSlot.Features/          VSA slices: Features/<Area>/<Operation>/
src/BookSlot.Api/               Minimal APIs + JWT
src/BookSlot.Web/ + .Web.Client Blazor Web App
src/BookSlot.Worker/            BackgroundService jobs
src/BookSlot.MigrationRunner/   one-shot console (migrations + seed)
tests/BookSlot.UnitTests/
tests/BookSlot.IntegrationTests/    Testcontainers Postgres
tests/BookSlot.ArchitectureTests/   NetArchTest — DO NOT remove
```

## 3. Invariants the agent NEVER breaks (NetArchTest enforces these)

1. Slice = one folder under `src/BookSlot.Features/Features/<Area>/<Operation>/`. Files: `*Endpoints` (static), `*Handler` (sealed), `*Validator`, `Command`/`Query`/`Response` records.
2. No cross-slice dependencies. Shared pieces → `src/BookSlot.Features/Shared/` or duplicate the DTO.
3. No `Repository` / `Service` classes inside a slice — the handler calls `AppDbContext` directly.
4. Domain stays pure: no `Microsoft.EntityFrameworkCore`, no `Microsoft.AspNetCore`. Business failures → `Result<T>`, not exceptions.
5. Validation lives in `*Validator` (FluentValidation), not in the handler.
6. Tenant scope: entities marked `ITenantScoped` are filtered by the global query filter on `AppDbContext`. **Never write `Where(x => x.TenantId == _ctx.TenantId)` manually.** Exception (cross-tenant Admin): `IgnoreQueryFilters()` + a comment.
7. Auth: API endpoints default to `RequireAuthorization` + a role policy (`Owner` / `Staff` / `Admin`). Public → explicit `.AllowAnonymous()`. Sensitive auth → `.RequireRateLimiting("auth-sensitive")`.
8. Outbox: domain events go through the outbox, not HTTP inside `SaveChangesAsync`.
9. Webhooks: HMAC-SHA256 in `WebhookDeliveryJob` — do not reimplement.
10. Logs: `ILogger<T>` (Serilog). Trace: `BookSlotActivitySource.StartActivity(...)`. Do not bypass.
11. Naming: PascalCase, file = type name, namespace = folder path under `src/<Project>/`.
12. Analyzers: `IDE0005` is treat-as-**error** — keep `using` directives minimal.

## 4. Build/test commands (the only whitelist for the verifier)

```powershell
dotnet build BookSlot.slnx --nologo
dotnet test tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj                 --nologo --no-build
dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj --nologo --no-build
dotnet test tests/BookSlot.IntegrationTests/BookSlot.IntegrationTests.csproj   --nologo --no-build
```

`dotnet test` does not accept multiple `*.csproj` arguments in one invocation — run per project.

## 5. Scope-allow-list per agent

| Agent          | Allow (read+write)                                                                                           | Allow (read-only)                | Hard DENY (write) |
|----------------|---------------------------------------------------------------------------------------------------------------|----------------------------------|-------------------|
| planner        | —                                                                                                             | entire repo                      | everything (read-only) |
| implementer    | `src/BookSlot.Features/**`, `src/BookSlot.Domain/**`, `src/BookSlot.Infrastructure/**`, `tests/**`, `docs/ARCHITECTURE.md`, `docs/RUNBOOK.md` | entire repo | `.github/workflows/**`, `.github/agents/**`, `.claude/agents/**`, `Directory.*.props`, `global.json`, `BookSlot.slnx`, `.editorconfig`, `coverlet.runsettings` |
| verifier       | —                                                                                                             | entire repo + commands from §4   | everything (writes forbidden) |
| code-reviewer  | —                                                                                                             | entire repo + PR diff            | everything (writes forbidden) |
| pr-commit      | `docs/agent-decisions.md`, `CHANGELOG.md` (if it exists)                                                      | entire repo                      | everything else (production code) |

If the plan requires a modification on the **DENY** list (e.g. a new CI workflow), the agent **MUST** stop the pipeline and ask the human for explicit approval in `plan.md` — this is not an auto-bypass.

## 6. Pipeline artifacts (I/O contract between agents)

All artifacts live in `./.agent-run/<run-id>/`:

```
.agent-run/<timestamp>-<feature-slug>/
├── prompt.md                # original user prompt
├── plan.md                  # planner → input for HITL #1
├── plan.approved.md         # after HITL #1 (with human patches if any)
├── implementation/
│   ├── summary.md           # implementer: list of files + decisions
│   └── diff.patch           # diff snapshot vs the base branch
├── verify-report.md         # verifier: pass/fail per check + log tail
├── review.md                # code-reviewer: severity | file:line | rationale | suggested_fix
├── review.approved.md       # after HITL #2
└── pr.md                    # pr-commit: commit SHA + PR URL + delta into agent-decisions.md
```

`./.agent-run/` is **gitignored**. Only `docs/agent-decisions.md` and the feature code itself land in the repo.

## 7. Long-term memory: `docs/agent-decisions.md`

- **Append-only.** Every agent reads the whole file on startup.
- Sections labelled `### Generalize as rule:` are appended to the system prompts of `planner` and `implementer` (these are the "lessons learned").
- `pr-commit` auto-generates an entry from the diff between the original agent artifact (`plan.md`, `review.md`) and the post-HITL version (`plan.approved.md`, `review.approved.md`).

## 8. Behavior required of every agent

1. On startup, print **your identity**, version, scope-allow, and max_iterations (from frontmatter).
2. Print a **3-5 bullet plan** before making any tool calls.
3. When done, produce the artifact required by §6 and a short stdout summary.
4. If you exceed the scope-allow-list → STOP, status `SCOPE_VIOLATION`, escalate.
5. If you reach `max_iterations` → STOP, status `BLOCKED`, dump context.
6. Never commit yourself (only `pr-commit` may).
7. Never auto-accept HITL.

## 9. Tool name mapping (Claude Code ↔ GitHub Copilot)

The pipeline is portable. Every agent has two file versions with an **identical body** and a platform-native frontmatter:
- **GitHub Copilot:** `.github/agents/<name>.agent.md`
- **Claude Code:** `.claude/agents/<name>.md`

Tool name mapping:

| Intent                  | Claude Code              | GitHub Copilot                  |
|-------------------------|--------------------------|---------------------------------|
| Read files              | `Read`                   | `codebase`                      |
| Search text             | `Grep`                   | `search`                        |
| Glob                    | `Glob`                   | `search` (covered)              |
| Edit existing file      | `Edit`                   | `editFiles`                     |
| Create file             | `Write`                  | `editFiles`                     |
| Shell commands          | `Bash`                   | `runCommands`                   |
| Fetch URL               | `WebFetch`               | `fetch`                         |
| Invoke subagent         | `Task` / `Agent(<name>)` | `agent` tool + `agents:` field  |

**Fields supported on only one platform:**
- `maxTurns`, `permissionMode`, `disallowedTools` → Claude only (frontmatter).
- `handoffs:`, `agents:`, `user-invocable`, `disable-model-invocation` → Copilot only (frontmatter).
- `scope_allow`, `max_iterations` (as a policy), `timeout_minutes`, `hitl` → **no native support on either platform**. These rules live in the body of every agent (the `## Hard rules` section) and are enforced via prompt instructions, not frontmatter.

**Cross-platform sync:**
The body (everything after the second `---`) must be identical between the pair of files. Verify with `pwsh ./scripts/agents-check-drift.ps1`. CI can block the PR on drift.