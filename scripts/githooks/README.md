# BookSlot git hooks

Lightweight repo-managed hooks that share the same scope rules the agent
pipeline uses (`scripts/scope-check.ps1`).

## Install

```bash
git config core.hooksPath scripts/githooks
```

From now on `git commit` will run `scope-check.ps1 -Agent implementer -Diff
<staged>` and refuse the commit if any staged file falls on the implementer's
deny-list (`.github/workflows/**`, `Directory.*.props`, etc.).

## Override per-commit

```bash
BOOKSLOT_AGENT=pr-commit git commit -m "docs: append agent-decisions entry"
```

Useful when the human is committing on behalf of `pr-commit` (only
`docs/agent-decisions.md` and `CHANGELOG.md` are allowed).

## Bypass (rare)

```bash
git commit --no-verify
```

Reserved for emergencies. The CI drift gate (`agents-check-drift.ps1`) will
still flag agent-mirror divergence on push, so bypassing locally does not skip
the safety net.
