---
name: safe-refactor
description: Guided refactoring workflow — identify legacy hotspot, add characterization tests, apply small-step transformations, verify build+test after each step.
---

# Safe Refactor Skill for BookSlot

This skill implements a disciplined, test-driven refactoring workflow. It ensures legacy code is covered by characterization tests before transformation, applies changes in small incremental steps, and verifies build + test success after each step.

## When to Use

- User requests `/refactor <target>` or "refactor X without breaking Y".
- You identify a legacy hotspot (complex method, god class, cyclomatic complexity >10, feature envy).
- Code smells require architectural changes (slice boundary violations, domain logic in handlers, missing Result<T>).
- Before extracting a new slice from an existing monolithic feature.

## Workflow

1. **Identify the refactor target**:
   - If user specified, confirm the file/class/method.
   - If not, propose based on complexity metrics (long methods, high coupling, missing tests).

2. **Characterization tests first**:
   - If the target has zero test coverage, write **characterization tests** that lock in current behavior (even if that behavior is buggy).
   - For BookSlot: use `WebApplicationFactory` + Testcontainers for integration tests, or `NSubstitute` mocks for unit tests.
   - Run tests to confirm green baseline: `dotnet test --no-build --nologo`.

3. **Plan small steps**:
   - Break the refactor into 5-10 small transformations (extract method, move to domain entity, introduce Result<T>, split slice).
   - Each step must be independently verifiable (build + test pass).

4. **Apply one step at a time**:
   - Make the transformation (e.g., extract method, rename, move to new file).
   - Run: `dotnet build BookSlot.slnx --nologo && dotnet test --no-build --nologo`.
   - If red: revert, adjust the step, retry.
   - If green: commit (optional) or proceed to next step.

5. **Final verification**:
   - After all steps: run full suite including architecture tests:
     ```powershell
     dotnet test tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj --nologo
     dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj --nologo
     ```
   - Confirm all NetArchTest rules pass (slice isolation, domain purity, naming conventions).

6. **Clean up characterization tests**:
   - If the characterization tests locked in buggy behavior, replace them with correct tests now that the refactor is done.
   - If they're still valid, keep them.

## Example

**User:** `/refactor src/BookSlot.Features/Features/Appointments/Create/CreateAppointmentHandler.cs`

**Skill response:**

```
[safe-refactor] Target: CreateAppointmentHandler.cs
[safe-refactor] Step 1/5: Verify existing test coverage...
  → tests/BookSlot.UnitTests/Appointments/CreateAppointmentHandlerTests.cs exists (3 tests)
  → Running baseline: dotnet test --no-build
  → ✅ All green.

[safe-refactor] Step 2/5: Extract validation logic to Validator...
  → Created CreateAppointmentValidator.cs
  → Removed inline validation from handler
  → Running: dotnet build && dotnet test
  → ✅ Build + tests pass.

[safe-refactor] Step 3/5: Move domain rule (appointment conflict check) to Appointment.CanSchedule()...
  → Added CanSchedule method to src/BookSlot.Domain/Appointments/Appointment.cs
  → Handler now calls appt.CanSchedule(newTime)
  → Running: dotnet build && dotnet test
  → ✅ Build + tests pass.

[safe-refactor] Step 4/5: Replace if-throw with Result.Failure...
  → Handler returns Result<CreateAppointmentResponse>
  → Endpoint maps via .ToHttpResult()
  → Running: dotnet build && dotnet test
  → ✅ Build + tests pass.

[safe-refactor] Step 5/5: Final verification (architecture tests)...
  → dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj
  → ✅ All NetArchTest rules pass.

[safe-refactor] ✅ Refactor complete. All steps green.
```

## Integration with `/refactor` Prompt

This skill references `.github/prompts/refactor.prompt.md` (if it exists) for additional context on BookSlot-specific refactor patterns (e.g., "Extract slice", "Introduce Result<T>", "Move to domain entity").

If the prompt file is missing, the skill operates standalone with the workflow above.

## Safety Guarantees

- **No breaking changes**: every step must pass build + test.
- **Small increments**: each transformation is independently reversible.
- **Test-first**: characterization tests lock in behavior before any code change.
- **Architecture validation**: final step confirms NetArchTest rules (slice isolation, domain purity).

---

**Note:** This skill does NOT auto-commit. It produces the refactored code and runs verification, but leaves commit/PR creation to the user or to the `pr-commit` agent in the pipeline.
