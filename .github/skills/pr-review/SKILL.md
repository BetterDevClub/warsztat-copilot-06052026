---
name: pr-review
description: Principal-level PR review pass for BookSlot — verify diff, run mandates, post material findings only.
---

# PR Review Skill for BookSlot

This skill performs a comprehensive, Principal-level review of a GitHub pull request against BookSlot's architectural mandates and .NET 10 conventions. It focuses on **material findings** only — architectural integrity, maintainability, performance, and long-term health.

## Core Mandates for Review

Follow these mandates to review every changed file between the verified source and target.

### 0. General Principles

- Always consider the **Principal-level perspective**: focus on architectural integrity, maintainability, performance, and long-term health of the codebase.
- Be proactive in identifying **potential issues** relevant to BookSlot conventions (VSA, Result<T>, tenant isolation, auth, outbox).
- Be positive and constructive; provide **concrete suggestions** for fixes with code snippets.
- Verify that **test cases** are meaningful and not just for coverage. If a class changed but tests didn't, verify if they're still relevant.
- Skip style/formatting (EditorConfig handles this). Skip nits. Material findings only.

### 1. Architecture & Dependency Injection

**What to flag:**

- **Optional constructor parameters** (`Type? type = null`) on DI-registered services — dependencies must be explicit; use method overloads or default values in factory methods, not in constructors.
- **Manual checks** replaceable by native .NET primitives (e.g., custom URL validation when `Uri.TryCreate` + `Uri.IsLoopback` exist).
- **DI scope mismatch** (e.g., Scoped service consumed by Singleton, Singleton holding `AppDbContext`). BookSlot uses standard ASP.NET DI scoping: `Scoped` per HTTP request for `AppDbContext`, `Singleton` for stateless helpers, `Transient` only when explicitly required.
- Any **`.Result` or `.Wait()`** on async methods — use `await` consistently. Flag synchronous blocking in async contexts.
- Any **`HttpClient` created directly** (`new HttpClient()`) — use `IHttpClientFactory` to avoid port exhaustion.
- Missing **`CancellationToken`** parameters on async methods. All async handlers/operations should accept `CancellationToken ct` and pass it through the call chain.
- Unnecessary layers/indirections: if an interface has only one implementation and no test mocking benefit, use the concrete class directly.

**Severity:** Major (DI scope mismatch, `.Result/.Wait`, missing `CancellationToken`, direct `HttpClient`). Minor (unnecessary interface abstraction).

### 2. Concurrency & Memory Management

**What to flag:**

- **ConcurrentDictionary / locks** without cleanup strategy — prefer `SemaphoreSlim` over `lock` for async operations; ensure disposal.
- **Shared mutable state** in singleton service fields (static fields, mutable dictionaries). Singletons must be thread-safe or immutable. Use `HttpContext.Items` for per-request state.
- **`IDisposable` / `IAsyncDisposable`** not wrapped in `using` / `await using` (MemoryStream, StreamWriter, manual DbContext creation). See `docs/code-review/ai-code-review-checklist.md` §2 for details.
- **Unbounded collections** in long-lived services (memory leaks in cache, event handlers not unsubscribed).

**Severity:** Major (shared mutable state in singleton, IDisposable not disposed, unbounded growth). Minor (lock instead of SemaphoreSlim in async code).

### 3. Testing Anti-Patterns

**What to flag:**

- **File I/O in tests** without abstraction — use `System.IO.Abstractions.IFileSystem` for unit tests, or use integration tests with Testcontainers.
- **`DateTime.UtcNow`** in code under test — inject `TimeProvider` (or `ISystemClock` abstraction) for deterministic tests.
- **Real HTTP calls** in tests — mock `IHttpClientFactory` for unit tests; use `WebApplicationFactory` + Testcontainers Postgres for integration tests.
- **Circular mock verification** (mock verifying its own setup).
- **Happy-path-only tests** (see `docs/code-review/ai-code-review-checklist.md` §4) — every new public operation must have at least one success test **and** one failure test (404/400/validation error).
- **Integration tests missing Testcontainers setup** — BookSlot uses `BookSlotIntegrationTestFixture` with Postgres Testcontainer; don't reinvent with in-memory SQLite (breaks EF Core advanced features).

**Severity:** Blocker (zero error-path tests for new public API). Major (DateTime.UtcNow not abstracted, real HTTP calls, missing integration fixture).

### 4. BookSlot-Specific Rules (VSA / Result<T> / Tenant / Auth / Outbox)

**What to flag:**

#### Slice Isolation (NetArchTest enforced)

- **Cross-slice dependencies** — `Features/<Area>/<Operation>/` must not reference types from `Features/<AnotherArea>/`. Shared logic goes to `Features/Shared/` or duplicate the DTO.
- **Repository/Service classes inside a slice** — handlers call `AppDbContext` directly; no `IAppointmentRepository`.
- **Domain layer importing EF Core or ASP.NET Core** — `BookSlot.Domain` must be pure; no `Microsoft.EntityFrameworkCore`, no `Microsoft.AspNetCore.*`.

**Severity:** Blocker (violates NetArchTest guardrails).

#### Result<T> Pattern

- **Business exceptions thrown instead of Result.Failure** — domain logic errors return `Result<T>` with an error code; only throw for programmer errors (ArgumentNullException) or infrastructure failures (DbUpdateException).
- **Endpoint not mapping Result** — every handler returning `Result<TResponse>` must be mapped via `.ToHttpResult(...)` in the endpoint; don't manually convert to `IResult`.

**Severity:** Major (business exception thrown). Minor (manual Result mapping when `.ToHttpResult` exists).

#### Tenant Filter

- **Manual `Where(x => x.TenantId == _ctx.TenantId)`** — the global query filter on `AppDbContext` enforces tenant isolation automatically. Manual filters duplicate logic and create risk.
- **Cross-tenant reads without comment** — if using `IgnoreQueryFilters()` (Admin-only scenarios), add an explicit comment explaining why.

**Severity:** Blocker (manual tenant filter in non-admin code). Major (missing comment on IgnoreQueryFilters).

#### Auth & Authorization

- **Missing `.RequireAuthorization()`** — endpoints default to `RequireAuthorization` + role policy (`Owner` / `Staff` / `Admin`). Public endpoints must call `.AllowAnonymous()` explicitly.
- **Sensitive auth endpoints** (login, password reset, MFA) without rate limiting — use `.RequireRateLimiting("auth-sensitive")`.

**Severity:** Blocker (missing auth on sensitive endpoint). Major (missing rate limit on auth-sensitive).

#### Outbox Pattern

- **External I/O in handler before SaveChangesAsync** — calling webhooks, external APIs, sending emails inside a handler creates distributed transaction risk. Enqueue an outbox message; let `BookSlot.Worker` deliver it.
- **Domain events not going through outbox** — if the plan called for event-driven integration, verify the handler writes to the outbox table.

**Severity:** Major (external I/O in handler synchronously). Minor (event bypassing outbox when plan required it).

#### Observability

- **Logging bypassing ILogger<T>** — use structured logging via `ILogger<T>` (Serilog under the hood); no `Console.WriteLine`, no direct Serilog `Log.` calls.
- **Tracing not using BookSlotActivitySource** — distributed traces should use `BookSlotActivitySource.StartActivity(...)` for consistency.

**Severity:** Minor (logging/tracing bypassing conventions).

#### Serialization

- **Newtonsoft.Json** — this project uses `System.Text.Json`. If a library forces Newtonsoft, isolate to that boundary.

**Severity:** Minor (Newtonsoft outside forced boundary).

#### Secrets

- **Hardcoded secrets/API keys/connection strings** — never commit secrets. Use User Secrets (dev), Azure Key Vault (prod), or environment variables.

**Severity:** Blocker (secret in code).

### 5. Null Safety & Side Effects

See `docs/code-review/ai-code-review-checklist.md` §1, §3 for detailed examples. Key flags:

- **Null dereference without guard** — every `FirstOrDefaultAsync` / `FindAsync` / `T?` return must have `if (x is null) return Result.Failure(...)` before dereference.
- **I/O in constructors** — constructors must not call database, HTTP, filesystem. Use factory methods or lazy initialization.
- **Public setters on domain entities** — business logic requires encapsulation; use methods (`Cancel()`, `Reschedule()`) instead of `appointment.Status = X`.

**Severity:** Major.

---

## Workflow

1. **Verify PR metadata**:
   ```bash
   gh pr view <num> --json number,title,baseRefName,headRefName,files,additions,deletions
   ```
   Confirm the PR is targeting the correct base branch (typically `master` or `develop`).

2. **Get the diff**:
   ```bash
   gh pr diff <num>
   ```
   Review every changed file against the mandates above.

3. **Apply mandates per file**:
   - For each file in the diff, check sections 0-5.
   - If a mandate is violated, note the file, line number, mandate category, and suggested fix.

4. **Post material findings**:
   - Each material finding (Blocker/Major severity) must be posted as a **separate inline comment** on the PR:
     ```bash
     gh pr review <num> --comment -b "**[Blocker] Manual tenant filter**\n\nFile: src/BookSlot.Features/Features/Appointments/Cancel/CancelAppointmentHandler.cs:42\n\nManual \`Where(a => a.TenantId == tenant.TenantId)\` — the global query filter handles this; risk of duplication.\n\n**Suggested fix:**\nRemove the Where clause; rely on the filter:\n\`\`\`csharp\nvar appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == cmd.AppointmentId, ct);\n\`\`\`"
     ```
   - Use `gh api repos/{owner}/{repo}/pulls/{num}/comments` for inline file comments (requires diff position).

5. **Skip style-only / nit feedback**:
   - Do not comment on whitespace, member ordering, or subjective naming if EditorConfig/analyzers passed.
   - Do not comment on pre-existing code outside the PR scope unless it's a critical bug introduced by the change.

6. **If no material findings**:
   ```bash
   gh pr review <num> --approve -b "✅ No material findings. All mandates verified:\n- Architecture & DI: PASS\n- Concurrency & memory: PASS\n- Testing: PASS\n- VSA/Result<T>/Tenant: PASS\n- Null safety & side effects: PASS"
   ```

---

## Severity Convention

Aligned with `.github/agents/code-reviewer.agent.md`:

| Severity | Definition | Action |
|----------|------------|--------|
| **Blocker** | Violates architectural invariant (slice cross-dependency, domain → EF/AspNetCore, missing auth, manual tenant filter, secret in code, zero error-path tests for new public API). | Blocks merge. Request changes. |
| **Major** | Potential production bug (null dereference, resource leak, `.Result/.Wait`, DI scope mismatch, external I/O in handler, happy-path-only tests incomplete). | Requires fix before merge. |
| **Minor** | Technical debt (unnecessary abstraction, logging bypassing convention, Newtonsoft outside boundary). | Worth fixing, but not a hard blocker. |
| **Nit** | Style/cosmetic suggestion. | Optional; typically skip in this skill. |

---

## Example Output

When material findings exist:

```
[pr-review] Reviewing PR #123: Add appointment cancellation feature
[pr-review] Base: master | Head: feature/cancel-appointment
[pr-review] Files changed: 8

[pr-review] FINDING 1/3 (Blocker)
  File: src/BookSlot.Features/Features/Appointments/Cancel/CancelAppointmentHandler.cs:42
  Mandate: §4 Tenant Filter
  Issue: Manual `Where(a => a.TenantId == tenant.TenantId)` — global filter handles this.
  Fix: Remove the Where; rely on the filter.

[pr-review] FINDING 2/3 (Major)
  File: src/BookSlot.Features/Features/Appointments/Cancel/CancelAppointmentHandler.cs:35
  Mandate: §5 Null Safety
  Issue: `FirstOrDefaultAsync` without null check before dereference.
  Fix: Add `if (appt is null) return Result.Failure(AppointmentErrors.NotFound);`

[pr-review] FINDING 3/3 (Major)
  File: tests/BookSlot.UnitTests/Appointments/CancelAppointmentHandlerTests.cs
  Mandate: §3 Happy-path-only
  Issue: Only success test exists; no test for NotFound / already cancelled.
  Fix: Add tests for error paths.

[pr-review] Verdict: REQUEST CHANGES (1 blocker, 2 major)
[pr-review] Posted 3 inline comments via `gh pr review`.
```

When no material findings:

```
[pr-review] Reviewing PR #124: Update appointment reminder template
[pr-review] Base: master | Head: feature/reminder-template
[pr-review] Files changed: 2

[pr-review] ✅ No material findings. All mandates verified.
[pr-review] Posted approval via `gh pr review --approve`.
```
