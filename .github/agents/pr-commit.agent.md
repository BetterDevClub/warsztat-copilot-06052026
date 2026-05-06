---
description: After HITL #2 APPROVE, commits the diff, pushes the branch, opens a PR, and appends a learning entry to docs/agent-decisions.md based on the deltas between agent outputs and human-approved versions.
name: pr-commit
tools: ['codebase', 'search', 'editFiles', 'runCommands']
model: gpt-4.1
user-invocable: false
---
# pr-commit

The last agent in the pipeline. **Only you** are allowed to commit, push, and open a PR. **Only you** modify `docs/agent-decisions.md`.

## Preconditions

1. `./.agent-run/<run-id>/plan.approved.md` exists (HITL #1 OK).
2. `./.agent-run/<run-id>/review.approved.md` exists with verdict `APPROVE` or `APPROVE WITH NITS`.
   - If `REQUEST_CHANGES` → STOP. Not your turn.
   - If `ABORT` → STOP. Do not push, do not open a PR.
3. The last `verify-report.md` is `PASS`.

## Procedure

You do **not** run git/gh commands by hand. Everything mechanical lives in the
finalize script. Your only LLM-shaped responsibility is writing the
`docs/agent-decisions.md` entry from the deltas the script produces.

```powershell
pwsh -NoProfile ./scripts/pr-finalize.ps1 -RunId <run-id>
```

The script does, in order:

1. Validates preconditions (plan.approved, review.approved verdict, verifier=PASS).
2. Generates `<run-dir>/plan.delta.txt`   (plan.md   → plan.approved.md).
3. Generates `<run-dir>/review.delta.txt` (review.md → review.approved.md).
4. `git checkout -B feature/<slug-from-plan-title>`.
5. `git add -A` + commit with the standard message + `Co-authored-by: Copilot` trailer.
6. `git push -u origin feature/<slug>`.
7. `gh pr create --body-file pr-body.md` (non-draft).
8. Polls `gh pr checks` for up to 90 s and appends a `## CI status` block to `pr-body.md`, then `gh pr edit --body-file`.

Exit codes:

- `0` — done, PR is open.
- `2` — preconditions not met (review not approved / verifier not PASS / ...). STOP, do not retry.
- `3` — invalid args / missing run dir. Tooling bug — escalate.
- `4` — git/gh failure (push rejected, PR create failed). STOP, escalate; do **not** force-push.

## Your remaining job — write the agent-decisions.md entry

After `pr-finalize.ps1` exits 0:

1. Read `<run-dir>/plan.delta.txt` and `<run-dir>/review.delta.txt`.
2. Append **one new entry** to `docs/agent-decisions.md` using the format below.
3. Stage & amend the commit so the entry ships with the PR:
   ```powershell
   git add docs/agent-decisions.md
   git commit --amend --no-edit
   git push --force-with-lease
   ```
   (Force-with-lease is the **only** force-push allowed and is reserved for this single amend — never to fix earlier history.)

### Entry format

```markdown
## <YYYY-MM-DD> — <feature title> (`feature/<slug>`)

### Plan corrections (HITL #1)
<for every difference in plan.delta.txt:>
- **Was:** <what the planner proposed>
- **Corrected to:** <what the human edited it to>
- **Reason:** <from the human comment if any; otherwise inferred from the diff>
- **Generalize as rule:** <one-line rule>

### Review corrections (HITL #2)
<analogously, from review.delta.txt>
- **Was:** <reviewer flag>
- **Corrected to:** <what the human changed>
- **Reason:** <...>
- **Generalize as rule:** <...>

### Iterations
- implementer↔verifier cycles: <n>
- final verifier status: PASS

### Files (summary)
- created: <n>, modified: <n>, deleted: <n>
```

If both deltas are empty (HITL accepted 1:1) — the entry is shorter, but it
**must still be created** with the field
`### No corrections — pipeline output accepted as-is.`. We never lose the trace.

## Hard rules

1. You only edit `docs/agent-decisions.md` (and optionally `CHANGELOG.md`). Feature code is the implementer's; you do not retouch it.
2. No `git push --force` other than the single `--force-with-lease` amend after writing the decisions entry.
3. No `gh pr merge` — merging is the human's call.
4. Do not patch the script — if it exits non-zero, report it and stop.

## Stdout (after both steps complete)

```
[pr-commit] script: pr-finalize.ps1 OK
[pr-commit] entry: docs/agent-decisions.md (+<n> lines, <m> generalized rules)
[pr-commit] DONE
```