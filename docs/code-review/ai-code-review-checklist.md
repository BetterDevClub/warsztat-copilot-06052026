# AI Code Review Checklist — BookSlot

## Introduction

This checklist was created as part of the **Copilot for .NET workshop** (Piotr Stapp module) and focuses on four categories of errors most often missed in routine reviews:

1. **Null safety** — uncontrolled dereferences, unhandled `null` where it shouldn't occur.
2. **IDisposable / resource leaks** — missing `using`, forgotten `Dispose`, streams/connections without `DisposeAsync` calls.
3. **Side effects** — hidden state mutations, I/O in constructors, ambiguous side effects (methods returning a value + mutating state).
4. **Happy-path-only tests** — tests cover only success paths, ignoring validation errors, edge cases, `Result.Failure`.

**When to use:**  
- During PR reviews (manual or AI).
- After completing a feature (self-review).
- As a reference point for the code-reviewer agent in the BookSlot pipeline.

**How to read severity:**  
| Severity | Definition |
|----------|-----------|
| **Blocker** | Critical bug or architecture invariant violation — blocks merge. |
| **Major** | Potential production bug (null ref, resource leak, side effect) — requires fix. |
| **Minor** | Technical debt / suboptimal solution — worth fixing, but doesn't block. |
| **Nit** | Stylistic / cosmetic suggestion — optional. |

---

## 1. Null Safety

### Definition
Code does not assume that reference values can be `null` without explicit handling. In .NET 10 with nullable reference types (NRT) enabled by default, the compiler warns about potential dereferences — but only if we use annotations consistently.

### Control Questions
1. Do all public method parameters declared as `string`, `T?` have a guard (`ArgumentNullException.ThrowIfNull` or `Result.Failure` pattern)?
2. Do methods returning `T?` have XML doc with `<returns>null if...</returns>`?
3. Does EF Core/Dapper code assume that a `NOT NULL` column in the database = `T` in C#, and nullable = `T?`?
4. Is there an `if (x is not null)` or guard before calling `.Value` on `T?`?
5. In LINQ, do we use `.FirstOrDefault()` + `null` handling instead of `.First()` without try-catch?

### BAD vs GOOD Example

**❌ BAD:**
```csharp
// src/BookSlot.Features/Features/Appointments/Cancel/CancelAppointmentHandler.cs
public async Task<Result<CancelAppointmentResponse>> Handle(CancelAppointmentCommand cmd, CancellationToken ct)
{
    var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == cmd.AppointmentId, ct);
    // no check — if not found, .Status will throw NullReferenceException
    appt.Status = AppointmentStatus.Cancelled;
    await _db.SaveChangesAsync(ct);
    return Result.Success(new CancelAppointmentResponse(appt.Id));
}
```

**✅ GOOD:**
```csharp
public async Task<Result<CancelAppointmentResponse>> Handle(CancelAppointmentCommand cmd, CancellationToken ct)
{
    var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == cmd.AppointmentId, ct);
    if (appt is null)
        return Result.Failure<CancelAppointmentResponse>(AppointmentErrors.NotFound);
    
    appt.Status = AppointmentStatus.Cancelled;
    await _db.SaveChangesAsync(ct);
    return Result.Success(new CancelAppointmentResponse(appt.Id));
}
```

**Severity:** Major (if no guard → potential production crash).

---

## 2. IDisposable / Resource Leaks

### Definition
Unmanaged resources (`DbConnection`, `HttpClient`, `Stream`, `MemoryStream`, EF Core `DbContext` objects in manual scopes) must be released via `Dispose()` / `DisposeAsync()`. In C# 10+ we use `using` / `await using`.

### Control Questions
1. Is every local variable of type `IDisposable` in a `using` or `await using` block?
2. Does the DI handler receive `DbContext` via dependency injection (lifecycle managed by framework) instead of manually creating `new AppDbContext(...)`?
3. Does the code avoid storing `DbContext` / `HttpClient` as private fields in a class without implementing `IDisposable` in that class?
4. In integration tests, is `WebApplicationFactory<T>` used with `await using` or `using`?
5. Does the code avoid returning `IQueryable<T>` from a method that disposes the context (context must outlive the queryable)?

### BAD vs GOOD Example

**❌ BAD:**
```csharp
// src/BookSlot.Features/Features/Reports/Export/ExportReportHandler.cs
public async Task<Result<byte[]>> Handle(ExportReportCommand cmd, CancellationToken ct)
{
    var ms = new MemoryStream();
    var writer = new StreamWriter(ms);
    await writer.WriteLineAsync("Report data...");
    await writer.FlushAsync(ct);
    return Result.Success(ms.ToArray());
    // MemoryStream + StreamWriter are never disposed → leak
}
```

**✅ GOOD:**
```csharp
public async Task<Result<byte[]>> Handle(ExportReportCommand cmd, CancellationToken ct)
{
    await using var ms = new MemoryStream();
    await using var writer = new StreamWriter(ms);
    await writer.WriteLineAsync("Report data...");
    await writer.FlushAsync(ct);
    return Result.Success(ms.ToArray());
}
```

**Severity:** Major (memory / descriptor leak → production performance degradation).

---

## 3. Side Effects

### Definition
A method has a **hidden side effect** if:
- It changes state outside its scope (modifies class field, global state, calls I/O) without explicit declaration in name/signature.
- It performs I/O in constructor (database, network, filesystem).
- It returns a value + mutates a parameter or field (ambiguous intent).

In VSA, we expect a handler to call `_db.SaveChangesAsync` — this is an expected side effect. An **unexpected** side effect is, for example, a handler calling an external webhook in the middle of logic + logging to a local file in the constructor.

### Control Questions
1. Does the constructor **not** call I/O (database, HTTP, filesystem)? If it must initialize a connection, use lazy init or an `InitializeAsync` method.
2. Do methods `GetX()` / `CalculateY()` avoid modifying state (class fields, database, cache)?
3. Is the side effect in the name: `SaveAndNotify(...)`, `ProcessAndLog(...)` vs just `Process(...)`?
4. In domain entities, is the setter `private` / `init` where mutation should go through a business method (`Cancel()`, `Reschedule(...)`) instead of `appointment.Status = X`?
5. Does the handler avoid calling an external API synchronously inside a transaction (use instead: outbox + worker)?

### BAD vs GOOD Example

**❌ BAD:**
```csharp
// src/BookSlot.Domain/Appointments/Appointment.cs
public class Appointment
{
    public Guid Id { get; set; }
    public AppointmentStatus Status { get; set; } // public setter → anyone can change
    public DateTime ScheduledAt { get; set; }
}

// src/BookSlot.Features/Features/Appointments/Reschedule/RescheduleAppointmentHandler.cs
public async Task<Result<Unit>> Handle(RescheduleAppointmentCommand cmd, CancellationToken ct)
{
    var appt = await _db.Appointments.FindAsync(cmd.AppointmentId);
    if (appt is null) return Result.Failure<Unit>(AppointmentErrors.NotFound);
    
    appt.ScheduledAt = cmd.NewTime; // direct mutation, no business rule
    await _db.SaveChangesAsync(ct);
    return Result.Success(Unit.Value);
}
```

**✅ GOOD:**
```csharp
// src/BookSlot.Domain/Appointments/Appointment.cs
public class Appointment
{
    public Guid Id { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTime ScheduledAt { get; private set; }

    public Result<Unit> Reschedule(DateTime newTime)
    {
        if (newTime < DateTime.UtcNow)
            return Result.Failure<Unit>(AppointmentErrors.PastDate);
        if (Status == AppointmentStatus.Cancelled)
            return Result.Failure<Unit>(AppointmentErrors.CannotReschedule);
        
        ScheduledAt = newTime;
        return Result.Success(Unit.Value);
    }
}

// handler calls the domain method:
var result = appt.Reschedule(cmd.NewTime);
if (result.IsFailure) return result;
await _db.SaveChangesAsync(ct);
```

**Severity:** Major (hidden mutations → difficult debugging, DDD / encapsulation violation).

---

## 4. Happy-Path-Only Tests

### Definition
Tests cover only the success scenario (`Result.Success`, HTTP 200). Missing tests for:
- Validation errors (`Result.Failure`, HTTP 400).
- Edge cases (empty string, `Guid.Empty`, past date, duplicate key).
- Race conditions (in concurrency tests).
- `null` handling (if API accepts `T?`).

**Why this is a blocker:** Public API without error-path tests is unreliable in production — we don't know if the validator catches edge cases, if Result.Failure propagates correctly to HTTP 400, if tenant filter isolates errors between tenants.

### Control Questions
1. Does every public operation (endpoint) have **at least 1 success test + 1 failure test**?
2. Do validators have tests for every error returned by `RuleFor(...).Must(...).WithErrorCode("X")`?
3. Do integration tests check HTTP status codes 400/404/403 (not just 200)?
4. Do handler unit tests mock `DbContext.FindAsync` returning `null` (NotFound scenario)?
5. For logic with `if (condition) return Failure(...)`, does a test exist that triggers this branch?

### BAD vs GOOD Example

**❌ BAD:**
```csharp
// tests/BookSlot.UnitTests/Appointments/CreateAppointmentHandlerTests.cs
[Fact]
public async Task Handle_ValidCommand_ReturnsSuccess()
{
    // Arrange
    var handler = new CreateAppointmentHandler(_dbMock.Object, _tenantMock.Object);
    var cmd = new CreateAppointmentCommand(Guid.NewGuid(), DateTime.UtcNow.AddDays(1));
    
    // Act
    var result = await handler.Handle(cmd, CancellationToken.None);
    
    // Assert
    Assert.True(result.IsSuccess);
}

// missing tests: what if ClientId doesn't exist? What if ScheduledAt is in the past? What if validator rejects?
```

**✅ GOOD:**
```csharp
[Fact]
public async Task Handle_ValidCommand_ReturnsSuccess() { /* ... */ }

[Fact]
public async Task Handle_ClientNotFound_ReturnsFailure()
{
    // Arrange
    _dbMock.Setup(db => db.Clients.FindAsync(It.IsAny<Guid>())).ReturnsAsync((Client?)null);
    var handler = new CreateAppointmentHandler(_dbMock.Object, _tenantMock.Object);
    var cmd = new CreateAppointmentCommand(Guid.NewGuid(), DateTime.UtcNow.AddDays(1));
    
    // Act
    var result = await handler.Handle(cmd, CancellationToken.None);
    
    // Assert
    Assert.True(result.IsFailure);
    Assert.Equal(ClientErrors.NotFound.Code, result.Error.Code);
}

[Fact]
public async Task Handle_ScheduledAtInPast_ReturnsValidationFailure()
{
    var cmd = new CreateAppointmentCommand(Guid.NewGuid(), DateTime.UtcNow.AddDays(-1));
    var validator = new CreateAppointmentValidator();
    
    var validationResult = await validator.ValidateAsync(cmd);
    
    Assert.False(validationResult.IsValid);
    Assert.Contains(validationResult.Errors, e => e.ErrorCode == "PAST_DATE");
}

[Theory]
[InlineData("00000000-0000-0000-0000-000000000000")]
public async Task Handle_EmptyClientId_ReturnsValidationFailure(string guidStr)
{
    var cmd = new CreateAppointmentCommand(Guid.Parse(guidStr), DateTime.UtcNow.AddDays(1));
    var validator = new CreateAppointmentValidator();
    
    var validationResult = await validator.ValidateAsync(cmd);
    
    Assert.False(validationResult.IsValid);
}
```

**Severity:** Blocker (if a new public API operation has no error-path test) / Major (if there are several success tests, but zero failure).

---

## Mapping to Repo Guardrails

| Checklist Category | Relationship to BookSlot Guardrails |
|----------------------|-------------------------------|
| **Null safety** | `Result<T>` pattern enforces explicit handling of missing results (instead of `null` + exception). Domain entities use `NotNull` constraints in EF Core. |
| **IDisposable** | `AppDbContext` injected via DI (scoped lifetime) → framework automatically disposes. Handlers don't manually create context. `WebApplicationFactory` in integration tests must be `await using`. |
| **Side effects** | VSA + domain encapsulation: state mutations in domain entities through methods (e.g., `Cancel()`, `Reschedule()`), not through public setters. Outbox pattern (BookSlot.Worker) — no external I/O in handler before `SaveChangesAsync`. |
| **Happy-path-only tests** | NetArchTest (`tests/BookSlot.ArchitectureTests/`) enforces tests for every operation. Plan.approved.md contains "Definition of done" section → if it says "success test + failure test", code-reviewer blocks merge if either is missing. |

**Why this works:**  
Guardrails (Result<T>, tenant filter, VSA isolation, outbox) **reduce the surface area** of errors in categories 1-3, but don't eliminate them entirely. Code can, for example, return `Result.Success(appt)` where `appt` is `null` (null safety), or a handler can manually create `new MemoryStream()` instead of `await using` (IDisposable). **The checklist catches what guardrails miss.**

---

## How to Assign Copilot Review According to This Checklist

Copy to Copilot CLI / GitHub Copilot Chat:

> **Prompt:**  
> Perform a code review of my PR according to the checklist `docs/code-review/ai-code-review-checklist.md`. Check all 4 categories: (1) null safety — does every `FirstOrDefault` / `FindAsync` have a guard or `Result.Failure` before dereferencing; (2) IDisposable — is every `MemoryStream` / `StreamWriter` / manual `DbContext` in `using` / `await using`; (3) side effects — do constructors avoid I/O, are setters `private` where domain logic requires encapsulation, does the handler avoid calling external APIs synchronously; (4) happy-path-only — does the new operation have a success test **and** failure test (404/400/validation error). Report only **Major** and **Blocker** findings with `file:line` + concrete fix suggestion. Ignore style/formatting.

---

## Severity Convention

According to `.github/agents/code-reviewer.agent.md`:

| Severity | Usage |
|----------|--------|
| **blocking** | Architecture invariant violation (slice cross-dependency, domain → EF/AspNetCore, missing auth, manual tenant filter, plan required test → test missing). Blocks merge. |
| **warning** | Technical debt (missing XML doc, IDE0005 unused usings, suboptimal name). Worth fixing before merge, but not a hard blocker. |
| **info** | Suggestion without bug (refactor to `Theory`, extract helper method). Optional. |

**For the 4 categories in this checklist:**
- **Null safety:** Major (potential crash).
- **IDisposable:** Major (resource leak).
- **Side effects:** Major (hidden mutation, difficult debugging).
- **Happy-path-only:** **Blocker** if new public operation (endpoint) without any error-path test; **Major** if there are success tests, but zero tests for validation/NotFound/edge cases.

---

**Usage Example:**  
After merging the `Features/Appointments/Reschedule/` feature, the dev can:
1. Run the pipeline (planner → implementer → verifier → **code-reviewer** → pr-commit).
2. See findings from 4 categories in `review.md`.
3. HITL #2: approve or request changes.
4. The code-reviewer agent refers to this checklist as "Production code review extras (Stapp module)" — it extends baseline invariants with additional control questions.
