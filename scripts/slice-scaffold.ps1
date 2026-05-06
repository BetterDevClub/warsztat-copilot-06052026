#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Scaffold deterministic stubs for a VSA slice based on plan.approved.md §3.

.DESCRIPTION
  Reads the markdown table in §3 ("Files to create / modify") of an approved
  plan and creates skeleton files for entries marked `create` that match
  recognized slice templates:

    - src/BookSlot.Features/Features/<Area>/<Op>/<Op>.cs
        → single-file slice (Command / Response / Validator / Handler /
          Endpoint nested classes, matches existing repo convention).
    - tests/BookSlot.UnitTests/<Area>/<Op>HandlerTests.cs
        → xUnit test class with a single placeholder Fact.
    - tests/BookSlot.IntegrationTests/<Area>/<Op>EndpointTests.cs
        → xUnit test class with a single placeholder Fact.

  Idempotent: existing files are never overwritten.

  Files in §3 that don't match a known template (e.g. Domain entities,
  EF configurations, migrations) are left for the implementer to create
  by hand — scaffolding them mechanically risks getting the domain model
  wrong.

.PARAMETER PlanPath
  Path to plan.approved.md.

.EXAMPLE
  pwsh ./scripts/slice-scaffold.ps1 -PlanPath ./.agent-run/abc/plan.approved.md
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $PlanPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not (Test-Path $PlanPath)) {
    Write-Host "[slice-scaffold] plan not found: $PlanPath"
    exit 3
}

# ── Parse §3 markdown table ──────────────────────────────────────────
$lines = Get-Content $PlanPath
$inSec3 = $false
$tableRows = New-Object System.Collections.Generic.List[string]
foreach ($ln in $lines) {
    if ($ln -match '^##\s+3\.') { $inSec3 = $true; continue }
    if ($ln -match '^##\s+\d+\.' -and $inSec3) { break }
    if ($inSec3 -and $ln -match '^\s*\|') { $tableRows.Add($ln) | Out-Null }
}

if ($tableRows.Count -lt 3) {
    Write-Host "[slice-scaffold] §3 table not found or empty in $PlanPath"
    exit 3
}

# Skip header (row 0) and divider (row 1)
$entries = @()
for ($i = 2; $i -lt $tableRows.Count; $i++) {
    $cols = ($tableRows[$i] -split '\|') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    if ($cols.Count -lt 3) { continue }
    $entries += [pscustomobject]@{
        Path      = ($cols[0] -replace '^`|`$', '').Trim()  # strip surrounding backticks
        Action    = $cols[1].ToLowerInvariant()
        Rationale = $cols[2]
    }
}

# ── Helpers ───────────────────────────────────────────────────────────
$created = @(); $skipped = @(); $unsupported = @()

function Ensure-File([string]$relative, [string]$content) {
    $abs = Join-Path $repoRoot $relative
    if (Test-Path $abs) {
        $script:skipped += $relative
        return
    }
    $dir = Split-Path $abs -Parent
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    [System.IO.File]::WriteAllText($abs, $content, [System.Text.UTF8Encoding]::new($false))
    $script:created += $relative
}

function Build-SliceFile([string]$area, [string]$folder, [string]$typeName) {
    @"
using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BookSlot.Features.$area.$folder;

/// <summary>TODO: one-sentence description of $typeName.</summary>
public static class $typeName
{
    /// <summary>Request body. TODO: replace fields.</summary>
    public sealed record Command(/* TODO */);

    /// <summary>Response. TODO: replace fields.</summary>
    public sealed record Response(/* TODO */);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            // TODO: rules from plan.approved.md §4 / §5
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant)
        {
            _db = db;
            _tenant = tenant;
        }

        /// <summary>Handles the command.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (_tenant.TenantId is null)
            {
                return Result.Failure<Response>(Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));
            }

            // TODO: implement per plan.approved.md §5
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(new Response(/* TODO */));
        }
    }

    /// <summary>Endpoint registration.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.TenantScoped;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            // TODO: replace verb + route per plan.approved.md §4
            app.MapPost("/TODO", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.IsSuccess ? result.ToOkResult() : result.ToHttpResult();
                })
                .WithName("$area.$typeName")
                .WithTags("$area")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner") // TODO: align with plan §4
                .Produces<Response>(StatusCodes.Status200OK);
        }
    }
}
"@
}

function Build-UnitTestFile([string]$area, [string]$op) {
    @"
using BookSlot.Domain.Primitives;

namespace BookSlot.UnitTests.$area;

public class ${op}HandlerTests
{
    [Fact(Skip = "scaffolded — implement per plan.approved.md §6")]
    public void TODO_replace_with_real_test()
    {
        // Arrange
        // Act
        // Assert
        true.Should().BeTrue();
    }
}
"@
}

function Build-IntegrationTestFile([string]$area, [string]$op) {
    @"
namespace BookSlot.IntegrationTests.$area;

public class ${op}EndpointTests
{
    [Fact(Skip = "scaffolded — implement per plan.approved.md §6")]
    public void TODO_replace_with_real_test()
    {
        // Arrange
        // Act
        // Assert
        true.Should().BeTrue();
    }
}
"@
}

# ── Process entries ───────────────────────────────────────────────────
foreach ($e in $entries) {
    if ($e.Action -ne 'create') { continue }
    $p = $e.Path -replace '\\', '/'

    # Slice file: src/BookSlot.Features/Features/<Area>/<Folder>/<Type>.cs
    # Skip multi-file split suffixes (Handler / Validator / Endpoints / Errors) —
    # the repo convention is single-file slices with nested classes.
    if ($p -match '^src/BookSlot\.Features/Features/([^/]+)/([^/]+)/([^/]+)\.cs$') {
        $area = $Matches[1]; $folder = $Matches[2]; $typeName = $Matches[3]
        if ($typeName -match '(Handler|Validator|Endpoints|Errors)$') {
            $unsupported += "$p (legacy multi-file split — slice scaffold uses single-file convention)"
            continue
        }
        Ensure-File $p (Build-SliceFile $area $folder $typeName)
        continue
    }

    # Unit test: tests/BookSlot.UnitTests/<Area>/<Op>HandlerTests.cs
    if ($p -match '^tests/BookSlot\.UnitTests/([^/]+)/([^/]+)HandlerTests\.cs$') {
        $area = $Matches[1]; $op = $Matches[2]
        Ensure-File $p (Build-UnitTestFile $area $op)
        continue
    }

    # Integration test: tests/BookSlot.IntegrationTests/<Area>/<Op>EndpointTests.cs
    if ($p -match '^tests/BookSlot\.IntegrationTests/([^/]+)/([^/]+)EndpointTests\.cs$') {
        $area = $Matches[1]; $op = $Matches[2]
        Ensure-File $p (Build-IntegrationTestFile $area $op)
        continue
    }

    $unsupported += $p
}

Write-Host "[slice-scaffold] plan: $PlanPath"
Write-Host "[slice-scaffold] created:    $($created.Count)"
foreach ($x in $created)     { Write-Host "  + $x" }
Write-Host "[slice-scaffold] skipped:    $($skipped.Count) (already exist)"
foreach ($x in $skipped)     { Write-Host "  = $x" }
Write-Host "[slice-scaffold] unsupported: $($unsupported.Count) (implementer must hand-craft these)"
foreach ($x in $unsupported) { Write-Host "  ? $x" }
exit 0
