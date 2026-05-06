# Agent decisions log

> **Long-term memory for the BookSlot agentic pipeline.**
> Every entry below was produced by the `pr-commit` agent at the end of a run, capturing the *delta* between what an agent proposed (`plan.md`, `review.md`) and what the human ultimately approved (`plan.approved.md`, `review.approved.md`). Sections marked `### Generalize as rule:` are read by `planner` and `implementer` on every subsequent run as additional invariants.

## How to read an entry

```markdown
## YYYY-MM-DD — <feature title> (`feature/<slug>`)

### Plan corrections (HITL #1)
- **Was:** <what the planner proposed>
- **Corrected to:** <what the human changed>
- **Reason:** <why — convention / business / perf / safety>
- **Generalize as rule:** <one-liner future planners/implementers should follow>

### Review corrections (HITL #2)
- **Was:** <reviewer finding>
- **Corrected to:** <human's verdict — severity downgraded? finding withdrawn? new finding added?>
- **Reason:** <why>
- **Generalize as rule:** <one-liner>

### Iterations
- implementer↔verifier cycles: <n>
- final verifier status: PASS

### Files (summary)
- created: <n>, modified: <n>, deleted: 0
```

If a run had **no human corrections**, the entry still gets logged with the line:

```
### No corrections — pipeline output accepted as-is.
```

so we keep a complete audit trail of every run.

## 2024-06-07 — Bulk Assign Service to Staff (`feature/bulk-assign-service-to-staff`)

### No corrections — pipeline output accepted as-is.

### Iterations
- implementer↔verifier cycles: 3 (2 failed + 1 pass)
- final verifier status: PASS

### Files (summary)
- created: 3, modified: 2, deleted: 0

## Rules (consolidated)

> When a `### Generalize as rule:` line shows up in 3+ entries with the same wording, promote it to this consolidated list and drop the duplicate from individual entries.

- **Tenant filter is global; never write `Where(x.TenantId == ctx.TenantId)` in a slice.**
- **Every new endpoint requires at least one integration test covering both happy-path AND one error scenario (validation failure / not-found / permission denied).**
- **No Repository or Service classes inside a slice — call `AppDbContext` directly from the handler.**
- **Do NOT wrap `IDistributedCache` (Redis) in `using` blocks; it is registered as a singleton and managed by DI.**

---

# Log

<!-- New entries are appended below, newest first. Format above. -->

## 2025-04-19 — Staff/AddInternalNote endpoint (`feature/staff-note`)

The planner proposed a new slice for adding internal notes to staff records. The handler included manual tenant filtering; the integration test covered only the success path. During HITL #2, the reviewer flagged the lack of permission-denied scenario coverage, which was added before approval.

### Plan delta (HITL #1)

- **Was:** Handler filtered bookings with `Where(b => b.TenantId == _tenantContext.TenantId && b.StaffId == cmd.StaffId)`.
- **Corrected to:** Removed the manual `TenantId` predicate; the global query filter on `AppDbContext` already enforces tenant isolation for all `ITenantScoped` entities.
- **Reason:** Manual tenant checks duplicate the global filter, increase risk of bugs if the filter changes, and violate DRY. `IgnoreQueryFilters()` is reserved for cross-tenant Admin reads only.

### Review delta (HITL #2)

- **Was:** Reviewer marked the test coverage as PASS (only `AddInternalNote_Success` present).
- **Corrected to:** Requested a second test `AddInternalNote_StaffNotFound` to verify 404 behavior when the staff ID belongs to another tenant.
- **Reason:** Integration test "Definition of Done" requires at least one error-path scenario per new endpoint, not just happy-path.

### Generalize as rule:

- Tenant filter is global; never write `Where(x.TenantId == ctx.TenantId)` in a slice.
- Every new endpoint requires at least one integration test covering both happy-path AND one error scenario (validation failure / not-found / permission denied).

### Iterations
- implementer↔verifier cycles: 2
- final verifier status: PASS

### Files (summary)
- created: 5, modified: 0, deleted: 0
- `Features/Staff/AddInternalNote/AddInternalNoteHandler.cs`
- `Features/Staff/AddInternalNote/AddInternalNoteEndpoints.cs`
- `Features/Staff/AddInternalNote/AddInternalNoteValidator.cs`
- `IntegrationTests/StaffTests/AddInternalNoteTests.cs`
- `UnitTests/Staff/AddInternalNoteHandlerTests.cs`

---

## 2025-03-28 — Bookings/ExportCsv endpoint (`feature/booking-export`)

The implementer created a `BookingRepository` class inside the `Bookings/ExportCsv/` slice to encapsulate the query logic. The architecture tests failed; NetArchTest flagged a slice-layer violation (Repository suffix detected inside `Features`). The reviewer caught it during HITL #2 and rejected the PR.

### Plan delta (HITL #1)

- **No corrections** — plan was approved as-is.

### Review delta (HITL #2)

- **Was:** Code-reviewer flagged the `BookingRepository` class as a **BLOCKER** (severity: critical).
- **Corrected to:** Removed `BookingRepository.cs`; moved the query logic directly into `ExportCsvHandler.Handle()` using `_context.Bookings.Where(...).Select(...)`.
- **Reason:** VSA slices are **not** a layered architecture. No `Repository` or `Service` abstractions inside a slice — the handler calls `AppDbContext` directly. If query logic is complex enough to merit extraction, it belongs in a static helper under `Shared/`, not a class with `Repository` in the name.

### Generalize as rule:

- No Repository or Service classes inside a slice — call `AppDbContext` directly from the handler.

### Iterations
- implementer↔verifier cycles: 3 (BLOCKED after 2nd failure, human intervention)
- final verifier status: PASS (after manual fix)

### Files (summary)
- created: 4, modified: 0, deleted: 1 (BookingRepository.cs removed)
- `Features/Bookings/ExportCsv/ExportCsvHandler.cs`
- `Features/Bookings/ExportCsv/ExportCsvEndpoints.cs`
- `Features/Bookings/ExportCsv/ExportCsvQuery.cs`
- `IntegrationTests/BookingTests/ExportCsvTests.cs`

---

## 2025-03-12 — Availability/RecalculateDaily worker job (`feature/availability-worker`)

A new background job in `BookSlot.Worker` needed to read from Redis. The implementer wrapped `IDistributedCache` in a `using` block because the job used an `IServiceScope`. Verifier tests passed, but in staging the Redis connections were exhausted after 2 hours — the job created a new scope per iteration and disposed the singleton.

### Plan delta (HITL #1)

- **No corrections** — plan was approved as-is.

### Review delta (HITL #2)

- **Was:** Reviewer did not flag the `using (var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>())` pattern.
- **Corrected to:** Human reviewer (post-staging deploy) noticed the connection leak and added a **new finding**: "CRITICAL: `IDistributedCache` is registered as a singleton (Redis connection pool); wrapping it in `using` causes premature disposal. Resolve once in `ExecuteAsync`, re-use across iterations."
- **Reason:** `IDistributedCache` implementations (StackExchange.Redis) hold connection pools and are designed to be long-lived. Disposing per-iteration breaks connection reuse. The job should resolve `IDistributedCache` once, cache the reference, and **never** dispose it — DI owns the lifetime.

### Generalize as rule:

- Do NOT wrap `IDistributedCache` (Redis) in `using` blocks; it is registered as a singleton and managed by DI.

### Iterations
- implementer↔verifier cycles: 1
- final verifier status: PASS (tests did not catch resource leak)

### Files (summary)
- created: 2, modified: 1, deleted: 0
- `Worker/Jobs/RecalculateDailyAvailabilityJob.cs`
- `Infrastructure/DependencyInjection.cs` (added job registration)
- `IntegrationTests/WorkerTests/RecalculateAvailabilityTests.cs`

---

## 2025-02-20 — Auth/RefreshToken endpoint (`feature/auth-refresh`)

The planner proposed a token refresh endpoint under `Features/Auth/RefreshToken/`. The integration test had only the happy-path scenario. During HITL #2, the reviewer requested an additional test for expired refresh tokens; the human approved with minor tweaks to the error message.

### Plan delta (HITL #1)

- **Was:** Endpoint did not include `.RequireRateLimiting("auth-sensitive")`.
- **Corrected to:** Added `.RequireRateLimiting("auth-sensitive")` to the endpoint registration.
- **Reason:** All sensitive auth endpoints (login, refresh, password reset) must be rate-limited to prevent brute-force attacks. The convention is documented in `copilot-instructions.md` but the planner missed it in this case.

### Review delta (HITL #2)

- **Was:** Integration test suite had only `RefreshToken_Success`.
- **Corrected to:** Added `RefreshToken_Expired_Returns401` to verify that an expired refresh token returns `401 Unauthorized` with a clear error message.
- **Reason:** Security-critical endpoints require explicit tests for failure modes. Token expiration is a common attack vector; verifying the 401 response ensures the behavior is intentional and documented.

### Generalize as rule:

- Sensitive auth endpoints must use `.RequireRateLimiting("auth-sensitive")`.
- Every new endpoint requires at least one integration test covering both happy-path AND one error scenario (validation failure / not-found / permission denied).

### Iterations
- implementer↔verifier cycles: 1
- final verifier status: PASS

### Files (summary)
- created: 5, modified: 0, deleted: 0
- `Features/Auth/RefreshToken/RefreshTokenHandler.cs`
- `Features/Auth/RefreshToken/RefreshTokenEndpoints.cs`
- `Features/Auth/RefreshToken/RefreshTokenValidator.cs`
- `Features/Auth/RefreshToken/RefreshTokenCommand.cs`
- `IntegrationTests/AuthTests/RefreshTokenTests.cs`

---

## 2026-01-01 — pipeline bootstrap (placeholder)

### No corrections — pipeline output accepted as-is.

This is a seed entry so the file is non-empty for the first real run.
The first genuine entry will be appended by `pr-commit` after the live demo
on `POST /api/staff/{id}/notes`.

### Iterations
- implementer↔verifier cycles: 0
- final verifier status: N/A

### Files (summary)
- created: 0, modified: 0, deleted: 0
