# Worked example: agentic pipeline run

This folder is a **read-only reference** showing what every artifact in a real run looks like. Use it as a template when you wonder "what should `plan.md` contain?".

The example task is the live demo specified in the bootstrap session:

> **`POST /api/staff/{staffId}/notes`** — add a tenant-scoped note for a staff member with FluentValidation, audit via outbox, EF Core migration, unit + integration tests.

A real run would write all of these into `./.agent-run/<run-id>/` (gitignored). They live here only to demonstrate the contract between agents.

## Files in this example

| File | Producing agent | Description |
|------|-----------------|-------------|
| [`prompt.md`](prompt.md) | (human) | Original user request. |
| [`plan.md`](plan.md) | `planner` | Implementation plan, awaiting HITL #1. |
| [`plan.approved.md`](plan.approved.md) | (human) | What HITL #1 looks like — `APPROVE-WITH-EDITS` form with one inline correction. |
| [`verify-report.md`](verify-report.md) | `verifier` | What a green verifier report looks like. |
| [`review.md`](review.md) | `code-reviewer` | Sample review with one blocking + one warning. |
| [`review.approved.md`](review.approved.md) | (human) | HITL #2 verdict. |
| [`agent-decisions.delta.md`](agent-decisions.delta.md) | `pr-commit` | The exact entry that would be appended to `docs/agent-decisions.md`. |

> ⚠️ **No code from this example is committed to the actual `src/`.** The endpoint itself will be implemented in a separate PR by re-running the pipeline for real. This folder is documentation only.
