# Branch protection rules — ready-to-use recipe

This file contains ready-to-copy branch protection configurations for `main` in BookSlot. Choose a method (UI or CLI) and apply.

---

## A) Configuration via UI (Settings → Branches)

1. Navigate to **Settings** → **Branches** in the GitHub repository.
2. Click **Add rule** (or edit existing rule for `main`).
3. **Branch name pattern:** `main`
4. Check the following options:

### Require a pull request before merging

- **Required approvals:** `1` (or more for critical repos)
- **Dismiss stale pull request approvals when new commits are pushed** ✅
- **Require review from Code Owners** ✅
- **Require approval of the most recent reviewable push** ✅ _(optional, blocks merge if there are new commits after the last approval)_

### Require status checks to pass before merging

- **Require branches to be up to date before merging** ✅
- **Status checks that are required:**
  - `build-test` _(job key from `.github/workflows/dotnet-ci.yml`)_
  - `copilot-pr-review` _(job key from `.github/workflows/copilot-review.yml`, optional)_

**⚠️ WARNING — Job key vs Step name:**  
GitHub branch protection **requires the exact job key name** (the YAML key in `jobs.<key>:`), **not** the `name:` field or step names inside the job. Example:
- `dotnet-ci.yml` has `jobs.build-test:` (job key) with `name: Build & Test (.NET 10)` — required check is **`build-test`**.

Case-sensitive.

_(Note: jobs must be run at least once to appear in the list. Create a dummy PR to initialize.)_

### Require conversation resolution before merging

- ✅ **Require all conversations to be resolved before merging.**

### Require signed commits

- ✅ _(optional, but **best practice** — blocks commits without GPG/SSH signature)_

### Require linear history

- ✅ **Blocks merge commits** — only squash or rebase.

### Do not allow bypassing the above settings

- **Lock branch** → prevent force push and deletion of the `main` branch ✅
- **Allow force pushes:** ❌ _(disabled)_
- **Allow deletions:** ❌ _(disabled)_

5. **Save changes.**

---

## B) Configuration via REST API / GitHub CLI

If you prefer scripts or want to version-control the configuration, use the `gh` CLI:

### Requirements

- GitHub CLI (`gh`) installed and authenticated (`gh auth login`)
- Token with `repo` scope (by default `gh auth login` provides sufficient permissions)

> **⚠️ WARNING — Job keys are case-sensitive:** `build-test` ≠ `Build-test` ≠ `BUILD-TEST`. In the commands below, you must provide the exact job key (YAML key under `jobs:`), not the `name:` field value or step names. See section D for the full reference list.

### Command (backward-compatible version, REST API `contexts`)

```bash
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  repos/{OWNER}/{REPO}/branches/main/protection \
  -f required_status_checks[strict]=true \
  -f required_status_checks[contexts][]=build-test \
  -f enforce_admins=false \
  -f required_pull_request_reviews[dismiss_stale_reviews]=true \
  -f required_pull_request_reviews[require_code_owner_reviews]=true \
  -f required_pull_request_reviews[required_approving_review_count]=1 \
  -f required_pull_request_reviews[require_last_push_approval]=false \
  -f required_conversation_resolution=true \
  -f required_linear_history=true \
  -f allow_force_pushes=false \
  -f allow_deletions=false \
  -f required_signatures=false
```

**Replace:**
- `{OWNER}` → organization or user name (e.g., `BetterDevClub`)
- `{REPO}` → repository name (e.g., `warsztat-copilot-06052026`)

**Note:**
- `required_signatures=false` — change to `true` if you want to require signed commits (GPG/SSH).
- `enforce_admins=false` — set to `true` to apply rules to administrators as well.
- **Job key:** `build-test` from `.github/workflows/dotnet-ci.yml`.

### Command (new version, recommended, REST API v3+ `checks`)

```bash
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  repos/{OWNER}/{REPO}/branches/main/protection \
  -f required_status_checks[strict]=true \
  -F required_status_checks[checks][][context]=build-test \
  -f enforce_admins=false \
  -f required_pull_request_reviews[dismiss_stale_reviews]=true \
  -f required_pull_request_reviews[require_code_owner_reviews]=true \
  -f required_pull_request_reviews[required_approving_review_count]=1 \
  -f required_pull_request_reviews[require_last_push_approval]=false \
  -f required_conversation_resolution=true \
  -f required_linear_history=true \
  -f allow_force_pushes=false \
  -f allow_deletions=false \
  -f required_signatures=false
```

**Differences from the old version:**
- `contexts` → `checks` (object with `context` field).
- `-f` → `-F` for array of objects (required by `gh` CLI for nested structures).
- `X-GitHub-Api-Version: 2022-11-28` — explicit API version (optional, but recommended).

### Example with substituted values (backward-compatible version):

```bash
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  repos/BetterDevClub/warsztat-copilot-06052026/branches/main/protection \
  -f required_status_checks[strict]=true \
  -f required_status_checks[contexts][]=build-test \
  -f enforce_admins=false \
  -f required_pull_request_reviews[dismiss_stale_reviews]=true \
  -f required_pull_request_reviews[require_code_owner_reviews]=true \
  -f required_pull_request_reviews[required_approving_review_count]=1 \
  -f required_pull_request_reviews[require_last_push_approval]=false \
  -f required_conversation_resolution=true \
  -f required_linear_history=true \
  -f allow_force_pushes=false \
  -f allow_deletions=false \
  -f required_signatures=false
```

**Verification:**

```bash
gh api repos/BetterDevClub/warsztat-copilot-06052026/branches/main/protection
```

Should return JSON with active rules.

---

## C) What each protection blocks — table

| Rule | What it blocks | Why it's important |
|------|----------------|-------------------|
| **Require pull request before merging** | Direct push to `main` | Enforces code review and audit trail |
| **Require 1+ approvals** | Merge without review | Guarantees at least one human check |
| **Dismiss stale approvals** | Merge after new commits without re-review | Prevents "approval on old version of code" situation |
| **Require Code Owners review** | Merge without approval from the team responsible for the area (`.github/CODEOWNERS`) | Ensures domain/infrastructure owners approve changes |
| **Require status checks (build, tests)** | Merge when tests fail | Blocks introduction of breaking code |
| **Require branches to be up to date** | Merge when PR is not rebased on latest `main` | Prevents conflicts and unforeseen interactions |
| **Require conversation resolution** | Merge with open comments | Enforces closure/resolution of all reviewer feedback |
| **Require signed commits** | Commit without GPG/SSH signature | Ensures authenticity of authorship (resistant to spoofing) |
| **Require linear history** | Merge commits (only squash/rebase) | Maintains clean, linear history in `main` |
| **Block force pushes** | `git push --force` to `main` | Protects against history loss and overwriting others' work |
| **Block deletions** | `git push --delete` on `main` | Prevents accidental deletion of the main branch |

---

## D) Required checks — reference list

These jobs **must** be green before merge (add them in the "Require status checks to pass" section):

| Check name (job key) | Workflow file | What it verifies |
|----------------------|---------------|------------------|
| `build-test` | `.github/workflows/dotnet-ci.yml` | `dotnet build` + unit tests + architecture tests + integration (with Testcontainers) — production workflow .NET 10, triggers on `main`/`develop` |
| `copilot-pr-review` _(optional)_ | `.github/workflows/copilot-review.yml` | Automated AI review with GitHub Copilot — additional safety net (if configured) |

**⚠️ CRITICAL WARNING — Job key (not step name):**  
GitHub branch protection uses **job key** (YAML key in `jobs.<key>:`), **not** the `name:` field or step names. Example from this repo:
- `dotnet-ci.yml` line 24: `jobs.build-test:` → required check = **`build-test`** (case-sensitive).

Step "Architecture tests" **is not** a separate job — it's a step inside `build-test`. GitHub doesn't allow enforcing individual steps; required checks work only at the job level.

**Note:**
- If you have separate jobs for `dotnet build`, `unit-tests`, `integration-tests`, add each separately to required checks.
- If you use matrix strategy (e.g., test on Linux + Windows), GitHub typically requires **all** combinations — make sure this is intended.

### How to add a required check that's not on the list yet?

1. The workflow must be run at least once (create a dummy PR or run manually).
2. GitHub will detect the job name and add it to available checks.
3. Return to **Settings → Branches → Edit rule** and check the new check.

---

## Best practices

- **Don't** set `enforce_admins=true` immediately — leave yourself an escape hatch for hotfixes in emergency situations.
- **Do** enable `required_signatures=true` if your team has configured GPG/SSH keys (increases security, blocks spoofing).
- **Do** require `require_code_owner_reviews=true` for critical areas (`/src/BookSlot.Domain/`, `/.github/agents/`).
- **Do** enforce `required_linear_history=true` — in VSA, `main` history should be linear (squash or rebase).

---

## Configuration verification

After saving rules:

```bash
# Check active protection rules
gh api repos/BetterDevClub/warsztat-copilot-06052026/branches/main/protection

# Or in UI: Settings → Branches → main rule should have all checkboxes ✅
```

Try making a dummy commit directly to `main`:

```bash
git checkout main
echo "test" >> test.txt
git add test.txt
git commit -m "test direct push"
git push origin main
```

Should return an error:

```
! [remote rejected] main -> main (protected branch hook declined)
```

If you see this error — **protection is working correctly**. 🎉
