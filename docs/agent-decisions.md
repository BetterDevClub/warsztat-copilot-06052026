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

### Files (skrót)
- created: <n>, modified: <n>, deleted: 0
```

If a run had **no human corrections**, the entry still gets logged with the line:

```
### No corrections — pipeline output accepted as-is.
```

so we keep a complete audit trail of every run.

## Rules (consolidated)

> When a `### Generalize as rule:` line shows up in 3+ entries with the same wording, promote it to this consolidated list and drop the duplicate from individual entries.

- _(empty — populated as the pipeline runs)_

---

# Log

<!-- New entries are appended below, newest first. Format above. -->

## 2026-05-04 — Internal booking notes (`feature/booking-add-note`)

### Plan corrections (HITL #1)

- **Was:** FK delete behavior `ON DELETE CASCADE` (planner's default — if a booking is deleted, its notes are deleted too).
- **Corrected to:** `ON DELETE RESTRICT` — booking cannot be deleted while notes exist.
- **Reason:** Human explicitly chose RESTRICT to preserve audit trail integrity; notes are internal records that should not be silently dropped when a booking is removed.
- **Generalize as rule:** When adding a child collection that represents an audit trail or internal record, default the FK to `ON DELETE RESTRICT`, not `CASCADE`. Use CASCADE only when the child is strictly presentational or disposable.

### Review corrections (HITL #2)

- **Was:** Code-reviewer flagged FK RESTRICT as a BLOCKING finding (mismatch with plan.md).
- **Corrected to:** False alarm — reviewer read `plan.md` instead of `plan.approved.md`; the implementation correctly matches the human-approved plan.
- **Reason:** The reviewer agent must always diff against `plan.approved.md`, not `plan.md`.
- **Generalize as rule:** The code-reviewer must validate the implementation against `plan.approved.md` (the human-approved version), never against the raw `plan.md`. Divergence from `plan.md` that aligns with `plan.approved.md` is correct, not a finding.

### Iterations

- implementer↔verifier cycles: 1 (verifier failed on pre-existing NU1902 build errors in test projects not covered by implementer's NuGetAudit fix; resolved by extending fix to all test `.csproj` files)
- final verifier status: PASS

### Files (skrót)

- created: 7 (BookingNote.cs, BookingNoteConfiguration.cs, migration × 2, AddBookingNote.cs, AddBookingNoteHandlerTests.cs, pr-body.md)
- modified: 10 (BookingErrors.cs, BookingFeatureErrors.cs, FeaturesAssemblyMarker.cs, AppDbContext.cs, ModelSnapshot.cs, 5 × NuGetAudit .csproj fix)
- deleted: 0

## 2026-01-01 — pipeline bootstrap (placeholder)

### No corrections — pipeline output accepted as-is.

This is a seed entry so the file is non-empty for the first real run.
The first genuine entry will be appended by `pr-commit` after the live demo
on `POST /api/staff/{id}/notes`.

### Iterations
- implementer↔verifier cycles: 0
- final verifier status: N/A

### Files (skrót)
- created: 0, modified: 0, deleted: 0
