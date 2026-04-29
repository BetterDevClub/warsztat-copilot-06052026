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
