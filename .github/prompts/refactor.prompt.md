---
mode: agent
description: Safe refactoring of legacy code with behavior preservation, tests, and VSA BookSlot compliance
---

# Behavior-preserving refactoring

You are performing **safe refactoring** of code in the BookSlot project (VSA .NET 10 SaaS). Refactoring means changing the code structure **without changing the observed behavior** of the system.

## Mandatory context

Before starting the refactoring, review:
- `.github/copilot-instructions.md` — VSA invariants, Result<T>, tenant filter, slice conventions
- `docs/agent-decisions.md` — long-term agent memory with human-approved corrections

If these files contain rules about the refactored area (e.g., "don't extract Repository from a slice", "use Result<T>"), **strictly respect them**.

## Refactoring workflow

### 1. Behavior identification

Before any change:
- **Understand current behavior**: what does the refactored code do for valid data, invalid data, edge cases (null, empty string, no tenant, no authorization, duplicates, etc.).
- **Find existing tests**: search `tests/BookSlot.UnitTests/` and `tests/BookSlot.IntegrationTests/` for tests covering this code.
- **If tests are missing or coverage is incomplete**: STOP. First write a **characterization test** (test describing current "as-is" behavior). Don't refactor code without tests — it's not safe.

### 2. Test characterization

For each area, verify:
- ✅ Happy-path test (valid data → success)
- ✅ Invalid data test (incorrect values → returns `Result.Failure` with appropriate error)
- ✅ Edge case tests (null, empty, no permissions, no tenant, duplicates)
- ✅ Side-effect tests (database writes, outbox, cache invalidation) — are they idempotent?
- ✅ Concurrency tests (if applicable) — optimistic concurrency token, unique constraint

**If anything is missing**: add tests BEFORE refactoring.

### 3. Small-step changes

Refactoring = series of small, verifiable steps. After **each** step:
```powershell
dotnet build BookSlot.slnx --nologo
dotnet test tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj --nologo --no-build
dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj --nologo --no-build
```

If test fails → revert step, fix, try again. **Don't land changes with broken tests.**

Examples of small steps:
- Extract helper method (Extract Method)
- Rename variable/method to be more descriptive (Rename)
- Replace magic number with constant (Introduce Constant)
- Move validation to `*Validator` (FluentValidation)
- Replace imperative flow with LINQ (Refactor Loop to Query)

### 4. VSA compliance (BookSlot invariants)

During refactoring **PROHIBITED**:
- ❌ Extracting `Repository` / `Service` classes from inside a slice — slice uses `AppDbContext` directly.
- ❌ Bypassing tenant query filter (`Where(x => x.TenantId == _ctx.TenantId)`) — global filter on `AppDbContext` does this automatically. If cross-tenant read needed (Admin), use `IgnoreQueryFilters()` + add comment.
- ❌ Throwing exceptions for domain errors — use `Result<T>` (from `src/BookSlot.Domain/Shared/Result.cs`).
- ❌ Validation in handler — validation MUST be in separate `*Validator` (FluentValidation).
- ❌ Ignoring nullable warnings (CS8600/CS8602/CS8603) — `<Nullable>enable</Nullable>` is treat-as-error.
- ❌ Ignoring `IDisposable` — use `using` / `await using`.

**REQUIRED**:
- ✅ Slice = one folder under `src/BookSlot.Features/Features/<Area>/<Operation>/`.
- ✅ Handler returns `Result<TResponse>` or `Result` (not `TResponse` directly).
- ✅ Endpoint maps `result.ToHttpResult(...)` (from `src/BookSlot.Infrastructure/Http/ResultExtensions.cs`).
- ✅ Handle all error paths (not just happy-path) — maintain edge case coverage after refactoring.
- ✅ Side-effects idempotent (outbox instead of synchronous external API call inside transaction).

### 5. Null safety and IDisposable

- If refactoring code with `string` → use `string?` for nullable, add null check or `??`.
- If refactoring code with `DbContext` / `HttpClient` / `Stream` → ensure they're in `using` / `await using` (or dependency injection).
- Don't ignore nullable warnings — fix them where you're refactoring.

### 6. Side-effect idempotency

If refactored code calls side-effects (database write, webhook, email):
- Check if it's idempotent (repeated call doesn't cause duplicates).
- If not — add outbox pattern (enqueue `OutboxMessage` in same transaction, `BookSlot.Worker` delivers later) or unique constraint.

### 7. Error path handling (not just happy-path)

Check if refactored code:
- Returns sensible error when input invalid (`Result.Failure(new ValidationError(...))`).
- Returns sensible error when resource doesn't exist (`Result.Failure(new NotFoundError(...))`).
- Returns sensible error when lacking permissions (`Result.Failure(new ForbiddenError(...))`).
- Returns sensible error on conflict (duplicate unique key → `Result.Failure(new ConflictError(...))`).

**Don't remove** error handling during refactoring — preserve or improve.

## Output format

After completing refactoring, provide:

### 📝 Change list (per step)
```
1. [Extract Method] Extracted `CalculateAvailableSlots` from handler to separate method.
2. [Rename] Changed `data` → `appointmentRequest` for readability.
3. [Introduce Validator] Moved validation to `CreateAppointmentValidator`.
4. [Fix Nullable] Added null check on `appointmentRequest.CustomerId`.
```

### 🗺️ Before/after map
```
BEFORE:
- Handler had 150 lines, inline validation, no null checks.
- Tenant filter bypassed with `Where(x => x.TenantId == _ctx.TenantId)`.

AFTER:
- Handler has 60 lines, validation in `*Validator`, null safety preserved.
- Tenant filter works automatically (removed manual `Where`).
```

### ✅ Behavior preservation checklist
```
- [x] Happy-path (valid data) — preserved, test green.
- [x] Invalid data (missing required field) — preserved, test green.
- [x] Edge case (null customerId) — preserved, test green.
- [x] Edge case (duplicate slot) — preserved, test green.
- [x] Tenant isolation — preserved, test green.
- [x] Build success (`dotnet build BookSlot.slnx`).
- [x] All tests green (unit + architecture).
```

### 🧪 Tests run list
```
dotnet test tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj --nologo --no-build
  Passed: BookSlot.UnitTests.Features.Appointments.CreateAppointmentHandlerTests.Handle_ValidRequest_ReturnsSuccess [12ms]
  Passed: BookSlot.UnitTests.Features.Appointments.CreateAppointmentHandlerTests.Handle_InvalidData_ReturnsFailure [8ms]
  Passed: BookSlot.UnitTests.Features.Appointments.CreateAppointmentHandlerTests.Handle_NullCustomerId_ReturnsFailure [6ms]
  Passed: BookSlot.UnitTests.Features.Appointments.CreateAppointmentHandlerTests.Handle_DuplicateSlot_ReturnsFailure [10ms]

dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj --nologo --no-build
  Passed: BookSlot.ArchitectureTests.SliceIsolationTests.Slices_ShouldNotReferenceOtherSlices [45ms]
  Passed: BookSlot.ArchitectureTests.LayeringTests.Features_ShouldNotReferenceDomain [38ms]
```

## Red flags (STOP refactoring)

If you encounter any:
- ❌ Missing tests for refactored area.
- ❌ Test red after refactoring step (revert, fix, try again).
- ❌ Attempt to bypass VSA invariants (e.g., extracting `Repository` from slice).
- ❌ Attempt to ignore nullable warning (`#nullable disable` / `!` without justification).
- ❌ Attempt to replace `Result<T>` with exception for domain error.

→ **Report problem to human and wait for decision.**

## Example: safe handler refactoring

**BEFORE** (`CreateAppointmentHandler.cs`, 150 lines):
```csharp
public async Task<Result<AppointmentResponse>> Handle(CreateAppointmentCommand command, CancellationToken ct)
{
    // Inline validation
    if (string.IsNullOrWhiteSpace(command.CustomerId)) return Result.Failure<AppointmentResponse>(new ValidationError("CustomerId is required"));
    if (command.StartTime < DateTime.UtcNow) return Result.Failure<AppointmentResponse>(new ValidationError("StartTime must be in future"));

    // Manual tenant filter (redundant)
    var slot = await _db.Slots.Where(s => s.TenantId == _ctx.TenantId && s.Id == command.SlotId).FirstOrDefaultAsync(ct);
    if (slot == null) return Result.Failure<AppointmentResponse>(new NotFoundError("Slot"));

    // Happy-path only (no duplicate check)
    var appointment = new Appointment { CustomerId = command.CustomerId, SlotId = slot.Id, TenantId = _ctx.TenantId };
    _db.Appointments.Add(appointment);
    await _db.SaveChangesAsync(ct);

    return Result.Success(new AppointmentResponse { Id = appointment.Id });
}
```

**AFTER** (60 lines, validation in `CreateAppointmentValidator`, null safety, duplicate check):
```csharp
public async Task<Result<AppointmentResponse>> Handle(CreateAppointmentCommand command, CancellationToken ct)
{
    // Validation moved to CreateAppointmentValidator (FluentValidation)

    // Automatic tenant filter (removed manual Where)
    var slot = await _db.Slots.FirstOrDefaultAsync(s => s.Id == command.SlotId, ct);
    if (slot is null) return Result.Failure<AppointmentResponse>(new NotFoundError("Slot"));

    // Duplicate check (edge case)
    var existingAppointment = await _db.Appointments
        .FirstOrDefaultAsync(a => a.SlotId == command.SlotId && a.CustomerId == command.CustomerId, ct);
    if (existingAppointment is not null) return Result.Failure<AppointmentResponse>(new ConflictError("Appointment already exists for this slot"));

    var appointment = new Appointment
    {
        CustomerId = command.CustomerId,
        SlotId = slot.Id,
        TenantId = _ctx.TenantId
    };

    _db.Appointments.Add(appointment);
    await _db.SaveChangesAsync(ct);

    return Result.Success(new AppointmentResponse { Id = appointment.Id });
}
```

Changes:
1. ✅ Validation moved to `CreateAppointmentValidator`.
2. ✅ Removed redundant `Where(s => s.TenantId == _ctx.TenantId)` — global filter does this.
3. ✅ Added duplicate check (edge case).
4. ✅ Null safety: `if (slot is null)` instead of `if (slot == null)`.
5. ✅ All tests green after refactoring.

---

**Remember**: refactoring is structure change **without behavior change**. If behavior changes (new functionality, new edge case) — that's not refactoring, it's a feature. Then use full agentic workflow (planner → implementer → verifier → code-reviewer).
