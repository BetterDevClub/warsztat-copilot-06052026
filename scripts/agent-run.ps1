#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Deterministic state-machine orchestrator for the BookSlot agentic pipeline.

.DESCRIPTION
  Replaces the `orchestrator` agent's run-control logic with PowerShell so we
  do not burn LLM tokens on bookkeeping. The script owns
  `./.agent-run/<run-id>/state.json` and decides every transition between
  phases. It does NOT invoke LLMs itself — the human (or a thin host) calls
  the appropriate subcommand at the right moment, and the script tells them
  which agent should run next.

  Subcommands:
    init       <-RunId> [-PromptPath]      create run folder + state.json (state=planning)
    status     <-RunId>                    print state + next required action
    next-agent <-RunId>                    print just the next agent name (or DONE/BLOCKED)
    verify     <-RunId>                    wrap scripts/verify.ps1 + update state
    review-prep<-RunId>                    wrap scripts/review-precompute.ps1 + update state
    hitl-wait  <-RunId> -Gate hitl-1|hitl-2 [-PollSec 5] [-TimeoutMin 0]
    advance    <-RunId> -To <state>        manual transition (orchestrator override)
    record     <-RunId> -Phase planner|implementer|reviewer|pr-commit -ExitCode <n>

  Exit codes:
    0  success
    1  pipeline transitioned to BLOCKED (caller should escalate)
    2  scope violation (caller must escalate)
    3  invalid args / state corrupt
    4  HITL timeout exceeded
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('init', 'status', 'next-agent', 'verify', 'review-prep', 'hitl-wait', 'advance', 'record')]
    [string] $Command,

    [Parameter(Mandatory = $true)] [string] $RunId,

    [string] $PromptPath,
    [ValidateSet('hitl-1', 'hitl-2')] [string] $Gate,
    [int]    $PollSec    = 5,
    [int]    $TimeoutMin = 0,
    [string] $To,
    [ValidateSet('planner', 'implementer', 'reviewer', 'pr-commit')] [string] $Phase,
    [int]    $ExitCode
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..')
$runDir     = Join-Path $repoRoot ".agent-run/$RunId"
$statePath  = Join-Path $runDir 'state.json'
$verifierCap = 3
$reviewCap   = 2

function Read-State {
    if (-not (Test-Path $statePath)) {
        Write-Host "[agent-run] no state.json at $statePath; run 'init' first."
        exit 3
    }
    return Get-Content $statePath -Raw | ConvertFrom-Json
}

function Write-State($state) {
    $json = $state | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($statePath, $json, [System.Text.UTF8Encoding]::new($false))
}

function Append-History($state, [string]$event) {
    if (-not $state.history) { $state | Add-Member -NotePropertyName history -NotePropertyValue @() -Force }
    $state.history += @{ ts = (Get-Date).ToString('o'); event = $event }
}

function Set-Phase($state, [string]$phase, [string]$reason = $null) {
    $state.phase = $phase
    if ($reason) { $state.blocked_reason = $reason }
    Append-History $state "phase=$phase$(if ($reason) { " ($reason)" })"
}

# Map: state → next agent the human should invoke
$nextAgentMap = @{
    'planning'              = 'planner'
    'awaiting_human:hitl-1' = '(human: HITL #1)'
    'implementing'          = 'implementer'
    'verifying'             = '(script: agent-run.ps1 verify)'
    'reviewing'             = 'code-reviewer'
    'awaiting_human:hitl-2' = '(human: HITL #2)'
    'committing'            = 'pr-commit'
    'done'                  = 'DONE'
    'blocked'               = 'BLOCKED'
    'blocked:review_loop'   = 'BLOCKED:review_loop'
    'scope_violation'       = 'BLOCKED:scope_violation'
    'aborted'               = 'ABORTED'
}

function Write-BlockedReport($state, [string]$reason, [string]$detail) {
    $report = @"
# Blocked — $($state.run_id)

**Reason:** $reason
**Phase at block:** $($state.phase)
**iterations.verifier:** $($state.iterations.verifier) / $verifierCap
**iterations.review:**   $($state.iterations.review) / $reviewCap
**Last verifier status:** $($state.last_verifier_status)
**Last review verdict:**  $($state.last_review_verdict)

## Detail

$detail

## History

$(($state.history | ForEach-Object { "- $($_.ts) $($_.event)" }) -join "`n")

---
A human must investigate this run. Do not retry without addressing the root cause.
"@
    [System.IO.File]::WriteAllText((Join-Path $runDir 'blocked.md'), $report, [System.Text.UTF8Encoding]::new($false))
}

# ──────────────────────────────────────────────────────────────────────
# init
# ──────────────────────────────────────────────────────────────────────
if ($Command -eq 'init') {
    if (Test-Path $statePath) {
        Write-Host "[agent-run] state.json already exists for $RunId; refusing to overwrite."
        exit 3
    }
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    if ($PromptPath) {
        if (-not (Test-Path $PromptPath)) {
            Write-Host "[agent-run] prompt not found: $PromptPath"; exit 3
        }
        Copy-Item $PromptPath (Join-Path $runDir 'prompt.md') -Force
    }
    $state = [ordered]@{
        run_id                = $RunId
        phase                 = 'planning'
        iterations            = [ordered]@{ verifier = 0; review = 0 }
        last_verifier_status  = $null
        last_review_verdict   = $null
        blocked_reason        = $null
        started_at            = (Get-Date).ToString('o')
        history               = @(@{ ts = (Get-Date).ToString('o'); event = 'init' })
    }
    Write-State $state
    Write-Host "[agent-run] initialized $RunId at $runDir"
    Write-Host "[agent-run] next agent: planner"
    exit 0
}

# ──────────────────────────────────────────────────────────────────────
# status / next-agent
# ──────────────────────────────────────────────────────────────────────
$state = Read-State

if ($Command -eq 'status') {
    Write-Host "[agent-run] run-id: $($state.run_id)"
    Write-Host "[agent-run] phase: $($state.phase)"
    Write-Host "[agent-run] iterations.verifier: $($state.iterations.verifier) / $verifierCap"
    Write-Host "[agent-run] iterations.review:   $($state.iterations.review) / $reviewCap"
    Write-Host "[agent-run] last verifier: $($state.last_verifier_status)"
    Write-Host "[agent-run] last review:   $($state.last_review_verdict)"
    if ($state.blocked_reason) { Write-Host "[agent-run] blocked reason: $($state.blocked_reason)" }
    $next = $nextAgentMap[$state.phase]
    Write-Host "[agent-run] next: $next"
    exit 0
}

if ($Command -eq 'next-agent') {
    Write-Host $nextAgentMap[$state.phase]
    exit 0
}

# ──────────────────────────────────────────────────────────────────────
# verify (wraps scripts/verify.ps1 + advances state)
# ──────────────────────────────────────────────────────────────────────
if ($Command -eq 'verify') {
    if ($state.phase -ne 'verifying' -and $state.phase -ne 'implementing') {
        Write-Host "[agent-run] cannot run verify in phase '$($state.phase)'"; exit 3
    }
    Set-Phase $state 'verifying'
    Write-State $state

    & pwsh -NoProfile (Join-Path $repoRoot 'scripts/verify.ps1') -RunId $RunId -Iteration ($state.iterations.verifier + 1)
    $vec = $LASTEXITCODE

    $state = Read-State  # verify.ps1 may have updated it
    switch ($vec) {
        0 {
            $state.last_verifier_status = 'PASS'
            Set-Phase $state 'reviewing'
            Append-History $state "verifier PASS → reviewing"
            Write-State $state
            Write-Host "[agent-run] verifier PASS → next: code-reviewer (run review-prep first)"
            exit 0
        }
        1 {
            $state.last_verifier_status = 'FAIL'
            $state.iterations.verifier += 1
            if ($state.iterations.verifier -ge $verifierCap) {
                Set-Phase $state 'blocked' 'verifier_cap_reached'
                Write-State $state
                Write-BlockedReport $state 'verifier_cap_reached' "Verifier failed $verifierCap times. See verify-report.md for the latest failure."
                Write-Host "[agent-run] BLOCKED: verifier cap ($verifierCap) reached"
                exit 1
            }
            Set-Phase $state 'implementing'
            Append-History $state "verifier FAIL (iter $($state.iterations.verifier)/$verifierCap) → implementing"
            Write-State $state
            Write-Host "[agent-run] verifier FAIL ($($state.iterations.verifier)/$verifierCap) → next: implementer"
            exit 0
        }
        2 {
            $state.last_verifier_status = 'SCOPE_VIOLATION'
            Set-Phase $state 'scope_violation' 'scope_violation'
            Write-State $state
            Write-BlockedReport $state 'scope_violation' "Implementer wrote outside scope-allow.write. See verify-report.md row 'Pre: scope leak'."
            Write-Host "[agent-run] SCOPE_VIOLATION — escalate"
            exit 2
        }
        default {
            Write-Host "[agent-run] verify.ps1 returned unexpected exit code $vec"
            Set-Phase $state 'blocked' "verifier_tooling_exit_$vec"
            Write-State $state
            exit 1
        }
    }
}

# ──────────────────────────────────────────────────────────────────────
# review-prep (wraps scripts/review-precompute.ps1)
# ──────────────────────────────────────────────────────────────────────
if ($Command -eq 'review-prep') {
    if ($state.phase -ne 'reviewing') {
        Write-Host "[agent-run] cannot run review-prep in phase '$($state.phase)'"; exit 3
    }
    & pwsh -NoProfile (Join-Path $repoRoot 'scripts/review-precompute.ps1') -RunId $RunId
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[agent-run] review-precompute failed (exit $LASTEXITCODE)"; exit 3
    }
    Append-History $state "review-precompute OK"
    Write-State $state
    Write-Host "[agent-run] review-input.md ready → next: code-reviewer"
    exit 0
}

# ──────────────────────────────────────────────────────────────────────
# hitl-wait
# ──────────────────────────────────────────────────────────────────────
if ($Command -eq 'hitl-wait') {
    if (-not $Gate) { Write-Host "[agent-run] -Gate is required"; exit 3 }
    $artifact = if ($Gate -eq 'hitl-1') { Join-Path $runDir 'plan.approved.md' } else { Join-Path $runDir 'review.approved.md' }
    $expectedPhase = if ($Gate -eq 'hitl-1') { 'awaiting_human:hitl-1' } else { 'awaiting_human:hitl-2' }
    if ($state.phase -ne $expectedPhase) {
        Set-Phase $state $expectedPhase
        Write-State $state
    }
    Write-Host "[agent-run] AWAITING_HUMAN: $Gate — waiting for $artifact (poll ${PollSec}s, timeout ${TimeoutMin}m)"

    $deadline = if ($TimeoutMin -gt 0) { (Get-Date).AddMinutes($TimeoutMin) } else { [datetime]::MaxValue }
    while (-not (Test-Path $artifact)) {
        if ((Get-Date) -gt $deadline) {
            Set-Phase $state 'aborted' 'human_timeout'
            Write-State $state
            Write-BlockedReport $state 'human_timeout' "Waited > $TimeoutMin minutes for $artifact."
            Write-Host "[agent-run] HITL timeout exceeded"
            exit 4
        }
        Start-Sleep -Seconds $PollSec
    }
    $body = Get-Content $artifact -Raw

    if ($Gate -eq 'hitl-1') {
        if ($body -match '(?im)^\s*REJECT\b') {
            Set-Phase $state 'aborted' 'human_rejected_plan'
            Write-State $state
            Write-Host "[agent-run] HITL #1 = REJECT → aborted"
            exit 0
        }
        # Anything else (APPROVE / APPROVE-WITH-EDITS / edited plan) is acceptance
        Set-Phase $state 'implementing'
        Append-History $state "HITL #1 approved"
        Write-State $state
        Write-Host "[agent-run] HITL #1 approved → next: implementer"
        exit 0
    } else {
        if ($body -match '(?im)^\s*ABORT\b') {
            Set-Phase $state 'aborted' 'human_aborted_review'
            Write-State $state
            Write-Host "[agent-run] HITL #2 = ABORT → aborted"
            exit 0
        }
        if ($body -match '(?im)^\s*REQUEST_CHANGES\b') {
            $state.last_review_verdict = 'REQUEST_CHANGES'
            $state.iterations.review += 1
            if ($state.iterations.review -ge $reviewCap) {
                Set-Phase $state 'blocked:review_loop' 'review_cap_reached'
                Write-State $state
                Write-BlockedReport $state 'review_cap_reached' "HITL #2 REQUEST_CHANGES rounds reached cap ($reviewCap)."
                Write-Host "[agent-run] BLOCKED: review cap reached"
                exit 1
            }
            Set-Phase $state 'implementing'
            Append-History $state "HITL #2 REQUEST_CHANGES (round $($state.iterations.review)/$reviewCap) → implementing"
            Write-State $state
            Write-Host "[agent-run] HITL #2 REQUEST_CHANGES ($($state.iterations.review)/$reviewCap) → next: implementer"
            exit 0
        }
        # Default: APPROVE (or APPROVE WITH NITS or edited file)
        $state.last_review_verdict = 'APPROVE'
        Set-Phase $state 'committing'
        Append-History $state "HITL #2 approved → committing"
        Write-State $state
        Write-Host "[agent-run] HITL #2 approved → next: pr-commit"
        exit 0
    }
}

# ──────────────────────────────────────────────────────────────────────
# advance (manual)
# ──────────────────────────────────────────────────────────────────────
if ($Command -eq 'advance') {
    if (-not $To) { Write-Host "[agent-run] -To is required"; exit 3 }
    Set-Phase $state $To
    Write-State $state
    Write-Host "[agent-run] forced phase → $To"
    exit 0
}

# ──────────────────────────────────────────────────────────────────────
# record (called by an agent host after invoking an LLM agent)
# ──────────────────────────────────────────────────────────────────────
if ($Command -eq 'record') {
    if (-not $Phase) { Write-Host "[agent-run] -Phase is required"; exit 3 }
    Append-History $state "$Phase exit=$ExitCode"
    switch ($Phase) {
        'planner' {
            if ($ExitCode -eq 0) { Set-Phase $state 'awaiting_human:hitl-1' }
            else { Set-Phase $state 'blocked' "planner_exit_$ExitCode" }
        }
        'implementer' {
            if ($ExitCode -eq 0) { Set-Phase $state 'verifying' }
            else { Set-Phase $state 'blocked' "implementer_exit_$ExitCode" }
        }
        'reviewer' {
            if ($ExitCode -eq 0) { Set-Phase $state 'awaiting_human:hitl-2' }
            else { Set-Phase $state 'blocked' "reviewer_exit_$ExitCode" }
        }
        'pr-commit' {
            if ($ExitCode -eq 0) { Set-Phase $state 'done' }
            else { Set-Phase $state 'blocked' "pr_commit_exit_$ExitCode" }
        }
    }
    Write-State $state
    Write-Host "[agent-run] recorded $Phase exit=$ExitCode → phase=$($state.phase)"
    if ($state.phase -like 'blocked*') { exit 1 }
    exit 0
}

Write-Host "[agent-run] unknown command: $Command"
exit 3
