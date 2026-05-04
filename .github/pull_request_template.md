# Pull Request

## What and why

<!-- Brief: what problem does this PR solve? Which issue/task does it address? -->

Fixes: #

## How it was tested

<!-- Check what was verified: -->

- [ ] Unit tests: `dotnet test tests/BookSlot.UnitTests/`
- [ ] Architecture tests: `dotnet test tests/BookSlot.ArchitectureTests/`
- [ ] Integration tests: `dotnet test tests/BookSlot.IntegrationTests/`
- [ ] Manual testing in local environment
- [ ] Verified in Swagger UI / Postman / curl

**Test details:**

<!-- Optional: add screenshots, logs, reproduction steps -->

## AI code review checklist

Before approving the PR, run **AI code review** according to [`docs/code-review/ai-code-review-checklist.md`](../docs/code-review/ai-code-review-checklist.md).

- [ ] AI review completed — no blocking issues or all fixed

## Self-review checklist before submitting the PR:

Before submitting the PR for review, use the `.github/skills/pr-review/` skill for automated verification:

```bash
# In Copilot CLI or via skill invocation:
# "Run pr-review skill on my staged changes"
```

- [ ] Self-review: no violations of VSA, tenant scope, Result<T>, naming conventions
- [ ] Self-review: tests cover happy-path + at least 1 error scenario

## Breaking changes

<!-- Does the change break API / contracts / migrations? -->

- [ ] No breaking changes
- [ ] **Breaking:** _(describe what changes and how to migrate)_

## Observability / feature flags impact

<!-- Do the changes require new metrics, logs, flags? -->

- [ ] No observability impact
- [ ] Added new logs / metrics / traces — verified in Seq / Prometheus
- [ ] Feature flag: `____` (name)

## Additional notes

<!-- Links, screenshots, diagrams, comments for reviewers -->
