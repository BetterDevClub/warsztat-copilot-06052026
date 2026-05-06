---
name: ci-yaml-author
description: Author or modify GitHub Actions workflows — verify triggers, concurrency, permissions, branch protection integration, secrets best practices, OIDC over PAT.
---

# CI/CD YAML Author Skill for BookSlot

This skill assists with creating or modifying GitHub Actions workflows (`.github/workflows/*.yml`) while enforcing best practices for security, performance, and reliability.

## When to Use

- User requests a new workflow (e.g., "Add a workflow for E2E tests", "Create a deployment pipeline").
- User asks to modify an existing workflow (e.g., "Add a step to publish test results", "Change the trigger to run on push to develop").
- You need to verify a workflow against BookSlot conventions before committing.

## Verification Checklist

When authoring or reviewing a workflow, verify:

### 1. Triggers

- **Appropriate events**: `push` (for CI on protected branches), `pull_request` (for PR validation), `workflow_dispatch` (for manual runs), `schedule` (for nightly/weekly jobs).
- **Branch filters**: Limit `push` triggers to `master` / `develop` to avoid redundant runs on feature branches (use `pull_request` for those).
- **Path filters** (optional): Use `paths` to skip workflows when only docs change (e.g., `paths-ignore: ['docs/**', '*.md']`).

**Example:**
```yaml
on:
  push:
    branches: [master, develop]
  pull_request:
    branches: [master, develop]
  workflow_dispatch:
```

### 2. Concurrency

- **Concurrency groups**: Use `concurrency` to cancel in-progress runs when a new commit is pushed (saves CI minutes, reduces queue time).
- **Per-PR grouping**: For PR workflows, group by `github.ref` (each PR gets its own queue).

**Example:**
```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```

### 3. Permissions

- **Principle of least privilege**: Set `permissions` at workflow or job level. Default is `read-all` (too permissive); narrow to only what's needed.
- **Common permissions**:
  - `contents: read` (checkout code)
  - `pull-requests: write` (post comments, update status)
  - `checks: write` (create check runs)
  - `id-token: write` (OIDC for Azure/AWS/GCP)

**Example:**
```yaml
permissions:
  contents: read
  pull-requests: write
  checks: write
```

### 4. Status Checks & Branch Protection

- **Required checks**: After adding a new workflow or job, update GitHub branch protection rules to require the job as a status check before merge.
- **Job naming**: Use descriptive job IDs (e.g., `build-and-test`, `integration-tests`) so they're clear in branch protection settings.

**Reminder output:**
```
[ci-yaml-author] ⚠️  New job 'integration-tests' added.
[ci-yaml-author] Remember to add 'integration-tests' to required status checks in GitHub branch protection settings.
```

### 5. Secrets & Environment Variables

- **Secrets from GitHub Secrets**: Use `${{ secrets.SECRET_NAME }}` — never hardcode secrets in YAML.
- **Environment-specific secrets**: Use GitHub Environments (dev, staging, prod) with protection rules for sensitive deployments.
- **OIDC over PAT**: For cloud deployments (Azure, AWS), prefer OIDC (`id-token: write` + federated credentials) over Personal Access Tokens (no secret rotation needed).

**Example (Azure OIDC):**
```yaml
- name: Azure Login
  uses: azure/login@v1
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
```

### 6. Caching & Performance

- **Cache dependencies**: Use `actions/cache` for NuGet packages, npm modules, Docker layers.
- **Matrix builds**: For multi-target (e.g., .NET 8 + .NET 10, Windows + Linux), use `strategy.matrix`.

**Example (NuGet cache):**
```yaml
- uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
    restore-keys: ${{ runner.os }}-nuget-
```

### 7. Testcontainers & Service Dependencies

- **Docker-in-Docker**: Testcontainers (used in BookSlot integration tests) requires Docker. Use `ubuntu-latest` runner (has Docker pre-installed).
- **No matrix on Windows** for Testcontainers (Windows runners don't support Linux containers in GHA).

**Example:**
```yaml
jobs:
  integration-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - name: Run integration tests (Testcontainers)
        run: dotnet test tests/BookSlot.IntegrationTests/BookSlot.IntegrationTests.csproj --nologo
```

### 8. Artifacts & Reporting

- **Upload test results**: Use `actions/upload-artifact` for test result XML, coverage reports, logs.
- **Publish test report**: Use `dorny/test-reporter@v1` or similar to surface failures in PR checks.

**Example:**
```yaml
- name: Upload test results
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: test-results
    path: '**/TestResults/*.trx'
```

## Reference Workflow

Use `.github/workflows/dotnet-ci.yml` (if it exists) as a template. Key structure:

```yaml
name: .NET CI

on:
  push:
    branches: [master, develop]
  pull_request:
    branches: [master, develop]

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read
  pull-requests: write

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - name: Restore dependencies
        run: dotnet restore BookSlot.slnx
      - name: Build
        run: dotnet build BookSlot.slnx --no-restore --nologo
      - name: Unit tests
        run: dotnet test tests/BookSlot.UnitTests/BookSlot.UnitTests.csproj --no-build --nologo
      - name: Architecture tests
        run: dotnet test tests/BookSlot.ArchitectureTests/BookSlot.ArchitectureTests.csproj --no-build --nologo
      - name: Integration tests (Testcontainers)
        run: dotnet test tests/BookSlot.IntegrationTests/BookSlot.IntegrationTests.csproj --no-build --nologo
```

## Workflow

1. **User request**: e.g., "Add a workflow to run integration tests on every PR".
2. **Verify existing workflows**: `ls .github/workflows/` — check for duplicates, identify the correct trigger.
3. **Draft the workflow**: Use the checklist above + reference workflow structure.
4. **Output the YAML**: Present the full workflow file.
5. **Remind about branch protection**: If adding a new job, remind user to add it to required status checks.

## Example Output

**User:** "Add a workflow for integration tests with Testcontainers on PRs."

**Skill response:**

```yaml
# .github/workflows/integration-tests.yml
name: Integration Tests

on:
  pull_request:
    branches: [master, develop]

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read
  pull-requests: write

jobs:
  integration-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: ${{ runner.os }}-nuget-
      - name: Restore dependencies
        run: dotnet restore BookSlot.slnx
      - name: Build
        run: dotnet build BookSlot.slnx --no-restore --nologo
      - name: Run integration tests (Testcontainers)
        run: dotnet test tests/BookSlot.IntegrationTests/BookSlot.IntegrationTests.csproj --no-build --nologo --logger "trx;LogFileName=integration-tests.trx"
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: integration-test-results
          path: '**/TestResults/integration-tests.trx'
```

**Reminder:**
- ⚠️  New workflow created. Add the `integration-tests` job to required status checks in GitHub branch protection settings for `master` and `develop`.
- 🔒 No secrets needed for this workflow (Testcontainers uses ephemeral local containers).
- 🚀 Concurrency group set to cancel in-progress runs on new commits.

---

## Security Best Practices

- **Never log secrets**: Mask secrets via `::add-mask::` or rely on GitHub's automatic masking.
- **Pin action versions**: Use `@v4` or commit SHA (`@abc123`) instead of `@latest` to prevent supply chain attacks.
- **Review third-party actions**: Prefer GitHub-official actions (`actions/*`, `azure/*`) over unverified community actions.
- **Environment protection**: For production deployments, use GitHub Environments with required reviewers.

---

**Note:** This skill generates workflow YAML but does NOT commit it. The user or `pr-commit` agent handles the commit/PR.
