# BookSlot agentic workflow — quickstart

This repo ships with a 5-stage agentic pipeline plus two **mandatory Human-in-the-Loop (HITL)** checkpoints. The orchestrator never auto-approves on your behalf.

```
prompt → planner → ⏸ HITL #1 ⏸ → implementer ↔ verifier (≤3) → code-reviewer → ⏸ HITL #2 ⏸ → pr-commit → PR
```

## File layout

The pipeline ships **two parallel mirror sets** of agent definitions — one per host platform. The body (system prompt) of each pair is bit-identical; only the YAML frontmatter differs (different field names, tool ids, model selectors).

```
.github/
├── copilot-instructions.md          # repo conventions (VSA, Result<T>, tenant scope, ...)
├── AGENTS.md                        # ← you are here
└── agents/                          # ── GitHub Copilot mirror (.agent.md convention)
    ├── _shared/
    │   ├── repo-context.md          # invariants + scope-allow + tool-name mapping (§9), loaded by every agent
    │   └── max-iterations.md        # iteration guard policy
    ├── orchestrator.agent.md        # user-invocable; agents: [planner, ...]
    ├── planner.agent.md             # handoffs → implementer
    ├── implementer.agent.md         # handoffs → verifier
    ├── verifier.agent.md            # handoffs → code-reviewer / implementer
    ├── code-reviewer.agent.md       # handoffs → pr-commit / implementer
    └── pr-commit.agent.md

.claude/agents/                      # ── Claude Code mirror (Claude-native frontmatter)
├── orchestrator.md                  # tools: Agent(planner, ...), Read, Grep, Bash
├── planner.md                       # permissionMode: plan, maxTurns: 8
├── implementer.md                   # maxTurns: 30
├── verifier.md                      # tools: Read, Grep, Bash
├── code-reviewer.md                 # permissionMode: plan, maxTurns: 10
└── pr-commit.md                     # tools: Read, Grep, Edit, Bash

scripts/
└── agents-check-drift.ps1           # SHA256-compares bodies between the two mirrors

docs/
└── agent-decisions.md               # long-term memory: human corrections become future rules
```

## Run artifacts

Every run writes to `./.agent-run/<run-id>/` (gitignored):

```
./.agent-run/2026-04-26T20-30-00-staff-add-note/
├── prompt.md
├── plan.md                # planner output
├── plan.approved.md       # ← you write this in HITL #1
├── implementation/
│   ├── summary.md
│   └── diff.patch
├── verify-report.md
├── review.md              # code-reviewer output
├── review.approved.md     # ← you write this in HITL #2
├── pr-body.md
└── state.json
```

Only `docs/agent-decisions.md` and the feature code are committed; the run folder is local.

## Running on Claude Code vs GitHub Copilot

The same pipeline works on both platforms — pick the mirror your client reads from.

|                            | **Claude Code** (`.claude/agents/`)                                   | **GitHub Copilot** (`.github/agents/*.agent.md`)                |
|----------------------------|------------------------------------------------------------------------|------------------------------------------------------------------|
| Source folder              | `.claude/agents/<name>.md`                                             | `.github/agents/<name>.agent.md`                                 |
| Tool ids                   | `Read, Grep, Glob, Edit, Write, Bash, WebFetch, Task, Agent(name,…)`   | `codebase, search, editFiles, runCommands, fetch, agent`         |
| Model selector             | `model: inherit` / `sonnet` / `opus` / `haiku` / full id               | `model: GPT-5.2 (copilot)` (or priority list `[A, B]`)           |
| Iteration cap              | `maxTurns: N` (native frontmatter)                                     | enforced via prompt rules in body (no native field)              |
| Read-only mode             | `permissionMode: plan` (planner, code-reviewer)                        | enforced via tool allowlist + body rules                         |
| Subagent invocation        | `Task` tool + `Agent(planner, …)` allowlist on orchestrator            | `agents: [planner, …]` field + `agent` tool                      |
| HITL flow                  | Pipeline stops; you edit `*.approved.md` artifact, then resume         | Same — plus per-agent `handoffs:` buttons surface gates as UI    |
| User-visible vs subagent   | every agent invocable                                                  | `user-invocable: true` only on orchestrator + planner            |

Fields like `scope_allow`, `max_iterations`, `timeout_minutes`, `hitl` are **not natively supported on either platform** — they live in the body of every agent (`## Twarde zasady` / `Hard rules`) and are enforced via prompt instructions, not the runtime.

### Drift control

When you edit an agent, edit **both** mirrors. To verify they match:

```powershell
pwsh ./scripts/agents-check-drift.ps1
```

The script compares the SHA256 of each pair's body (everything after the second `---`). Frontmatters intentionally differ; bodies must not. Run before every commit that touches `.github/agents/*.agent.md` or `.claude/agents/*.md`.

## Running the pipeline

> The pipeline is designed for [Claude Code](https://docs.anthropic.com/en/docs/claude-code) subagents and [GitHub Copilot custom agents](https://code.visualstudio.com/docs/copilot/customization/custom-agents). Every agent file is plain markdown with a YAML frontmatter — you can also drive it manually by feeding each agent its prompt + the predecessor's artifact.

### 1. Kick off

Drop your prompt into `./.agent-run/<run-id>/prompt.md` and invoke the `orchestrator` agent. It will:

1. Call `planner` — produces `plan.md`.
2. Pause on **HITL #1**.

### 2. HITL #1 — approve plan

Read `plan.md`. Then create `plan.approved.md` with one of:

| Verdict | What it means | What to put in plan.approved.md |
|---------|---------------|---------------------------------|
| **APPROVE** | Plan is good as-is | Copy `plan.md` verbatim and add a top line `APPROVE`. |
| **APPROVE-WITH-EDITS** | Plan is mostly good, you tweaked it | Edit the plan inline, save, prepend `APPROVE-WITH-EDITS`. The deltas vs `plan.md` will be auto-logged to `agent-decisions.md` by `pr-commit`. |
| **REJECT** | Plan is wrong direction | Write `REJECT: <reason>` only. Pipeline ends. |

### 3. Implementer ↔ verifier (auto, capped at 3)

Implementer writes code/tests; verifier runs `dotnet build` + tests. On FAIL, it bounces back. After 3 failures, the run goes `BLOCKED` and waits for you.

### 4. HITL #2 — approve diff

Once verifier is green, `code-reviewer` produces `review.md`. Read it next to the diff. Then create `review.approved.md`:

| Verdict | Effect |
|---------|--------|
| **APPROVE** | Goes to `pr-commit`. |
| **REQUEST_CHANGES: <list>** | Goes back to implementer (counts as another iteration). |
| **ABORT: <reason>** | Pipeline ends, no commit, no PR. |

### 5. pr-commit

If HITL #2 was `APPROVE`:
- Diffs `plan.md ↔ plan.approved.md` and `review.md ↔ review.approved.md`.
- Appends an entry to `docs/agent-decisions.md` summarizing your corrections, including any `### Generalize as rule:` lines that planner/implementer will read on future runs.
- Commits, pushes, opens PR. Does **not** merge — that's still your call.

## Iteration & scope guards

- **Pipeline cap:** 3 iterations between implementer ↔ verifier; per-agent timeout 10 min; whole run timeout 60 min.
- **Scope:** every agent has an explicit `scope_allow` / `deny_write` list in its frontmatter. The implementer cannot edit `.github/workflows/**`, `.github/agents/**`, `Directory.*.props`, `global.json`, etc. The reviewer and verifier cannot edit anything in the repo. Only `pr-commit` can touch `docs/agent-decisions.md`.
- **No auto-approve:** there is no flag that bypasses HITL. The orchestrator stays in `awaiting_human` until you write the approval artifact.

## Long-term memory — `docs/agent-decisions.md`

This file is the project's *living rulebook from agent corrections*. Workflow:

1. Run finishes (with or without your edits).
2. `pr-commit` appends a structured entry.
3. On the next run, `planner` and `implementer` re-read the file. Sections labelled `### Generalize as rule:` are appended to their system prompts as additional invariants.
4. When the same rule appears 3+ times, promote it to the **Rules (consolidated)** section at the top.

The result: every HITL correction is permanent and reduces future corrections on similar tasks.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `BLOCKED` after 3 verifier failures | implementer can't satisfy plan with current scope | Edit `plan.approved.md`, re-run from PHASE 2 |
| `SCOPE_VIOLATION` | agent tried to write outside its allow-list | Either expand scope in `plan.approved.md` (and the agent's frontmatter), or change the approach |
| `awaiting_human:hitl-*` forever | pipeline correctly paused — it does **not** auto-progress | Write the `*.approved.md` artifact |
| `verify-report.md` says PASS but reviewer reports `wait` | shouldn't happen — file a bug; the gate is "verifier=PASS implies reviewer can proceed" |
| `pr-commit` refuses to commit | review verdict ≠ APPROVE, or verifier ≠ PASS | Re-check `review.approved.md` and last `verify-report.md` |

## Live demo task

A worked example of the full pipeline lives in [`docs/agent-pipeline-example/`](../docs/agent-pipeline-example/) — every artifact (`prompt.md`, `plan.md`, `plan.approved.md`, `verify-report.md`, `review.md`, `review.approved.md`, `agent-decisions.delta.md`) is shown as it would look in a real `./.agent-run/<run-id>/` folder.

---

## Production module quick links

Workshop materials:

- `.github/prompts/refactor.prompt.md` — safe refactor prompt
- `docs/code-review/ai-code-review-checklist.md` — AI review checklist
- `.github/skills/pr-review/SKILL.md` — PR review skill
- `.github/skills/safe-refactor/SKILL.md` — safe refactor skill
- `.github/skills/ci-yaml-author/SKILL.md` — CI YAML skill
- `.github/pull_request_template.md` — PR template
- `.github/CODEOWNERS` — code owners
- `.github/workflows/dotnet-ci.yml` — CI workflow
- `.github/workflows/deploy.yml` — CD workflow
- `tests/BookSlot.ArchitectureTests/ArchitectureTests.cs` — architecture tests index
