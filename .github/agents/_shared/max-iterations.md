# Max iterations & escalation policy

## Default budgets

| Loop / agent                   | Max iterations | What happens on overflow |
|--------------------------------|----------------|--------------------------|
| planner                        | 1              | One plan per run. Corrections = HITL #1. |
| implementer ↔ verifier         | **3**          | Stop, status `BLOCKED`, escalate to a human with a full dump. |
| code-reviewer                  | 1              | One review per diff. Re-review after fixes is a new iteration. |
| pr-commit                      | 1              | Single invocation. Rebase conflict = STOP, escalate. |

Every agent also has a **per-iteration timeout: 10 min**. On overflow — `TIMEOUT`, escalate.

## Definition of an "iteration" in the implementer ↔ verifier loop

1. **Iteration N:** implementer touches files → verifier runs build/tests.
2. If verifier reports **PASS** → exit the loop, hand over to code-reviewer.
3. If verifier reports **FAIL** → implementer receives `verify-report.md` + the last 200 lines of logs and goes back to editing.
4. After the 3rd failed iteration → STOP.

## `BLOCKED` escalation format

`pr-commit` **does not run** in this scenario. Instead, `./.agent-run/<id>/blocked.md` is written:

```markdown
# BLOCKED — <run-id>

**Phase:** implementer↔verifier
**Reason:** max_iterations(3) exceeded
**Last verifier status:** FAIL — 4 unit tests, 1 build error

## Iteration 1
- diff (summary): ...
- verifier output (tail): ...

## Iteration 2
...

## Iteration 3
...

## Hypotheses (from the last agent)
- ...

## Questions for the human
- Do you want to change the strategy? (e.g. add a helper in Shared)
- Was the plan too ambitious? Split into 2 PRs?
```

The `BLOCKED` state is visible in the UI and in the orchestrator's stdout. The human decides: **resume** (with a corrected plan), **abort**, or **edit & retry**.

## `SCOPE_VIOLATION` escalation format

When an agent tries to write outside its scope-allow-list:

```markdown
# SCOPE VIOLATION — <run-id>

**Agent:** implementer
**Attempted path:** .github/workflows/ci.yml
**Allowed paths:** src/BookSlot.Features/**, src/BookSlot.Domain/**, ...

**Reason the agent gave:** "Need to add a CI job for the new endpoint."

**Action:** STOP. Operation not executed. Pipeline waits for the human's decision.
```

The human can: update the scope in `plan.md` (and the scope-allow list in the relevant agent) or force the agent toward a different solution.

## Hard rule: the orchestrator never auto-bypasses

- There is no `--yolo`, `--auto-approve`, `--skip-hitl` flag.
- There is no "default = approve" on timeout.
- `awaiting_human` is a persistent state until the human responds.