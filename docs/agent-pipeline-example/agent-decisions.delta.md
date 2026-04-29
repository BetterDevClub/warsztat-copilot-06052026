# Delta to be appended to `docs/agent-decisions.md`

> This is the exact block `pr-commit` would append after the run. The "Generalize as rule" lines are the long-term memory that future `planner` and `implementer` runs will read.

```markdown
## 2026-04-26 — Add staff note endpoint (`feature/staff-add-note`)

### Plan corrections (HITL #1)
- **Was:** Authorization on `POST /api/staff/{id}/notes` set to `RequireOwner`.
- **Corrected to:** `RequireStaff` (Owner OR Staff).
- **Reason:** Matches existing internal-note slice (`AddBookingInternalNote`) — staff members are expected to leave context notes; restricting to Owner contradicts established convention.
- **Generalize as rule:** When adding "internal note"-style write endpoints, default to `RequireStaff`, not `RequireOwner`. Verify with the existing `AddBookingInternalNote` slice as the canonical example.

- **Was:** Test case `Post_AsStaffRole_Returns403`.
- **Corrected to:** `Post_AsAnonymous_Returns401`.
- **Reason:** Direct consequence of the auth change above.
- **Generalize as rule:** When changing endpoint authorization, regenerate the negative-path test names so they describe the actual rejected role/state.

### Review corrections (HITL #2)
### No corrections — pipeline output accepted as-is.

(Reviewer's three findings were addressed in implementer iter 2; human approved the review as written.)

### Iterations
- implementer↔verifier cycles: 2
- final verifier status: PASS

### Files (summary)
- created: 9, modified: 2, deleted: 0
```
