param(
    [string]$OutputRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-RepoRoot {
    $current = Get-Item -LiteralPath $PSScriptRoot
    while ($null -ne $current) {
        if (Test-Path -LiteralPath (Join-Path $current.FullName "TwinCatAutomationKit.sln")) {
            return $current.FullName
        }

        $parentPath = Split-Path -Parent $current.FullName
        if ([string]::IsNullOrWhiteSpace($parentPath) -or $parentPath -eq $current.FullName) {
            break
        }

        $current = Get-Item -LiteralPath $parentPath
    }

    throw "Repository root not found from $PSScriptRoot."
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$repoRoot = Find-RepoRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path (Join-Path $repoRoot ".artifacts") "run-plan-stop-on-failure-$(Get-Date -Format yyyyMMdd-HHmmss)"
}

$cliProject = Join-Path $repoRoot "src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj"
$planPath = Join-Path $OutputRoot "stop-on-failure-plan.json"
$summaryPath = Join-Path $OutputRoot "stop-on-failure-summary.json"
$logPath = Join-Path $OutputRoot "stop-on-failure-output.log"
$missingProjectPath = Join-Path $OutputRoot "missing-input.tsproj"
$deferredPayloadPath = Join-Path $OutputRoot "payload-from-unexecuted-output.txt"

if (-not (Test-Path -LiteralPath $cliProject)) {
    throw "CLI project not found: $cliProject"
}

if (Test-Path -LiteralPath $OutputRoot) {
    throw "OutputRoot already exists. Pick a new path or delete it yourself: $OutputRoot"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$plan = [ordered]@{
    schemaVersion = 1
    name = "run-plan stop-on-failure unresolved output regression"
    files = @(
        [ordered]@{
            path = $deferredPayloadPath
            content = '${steps.failFirst.outputs.projectPath}'
        }
    )
    steps = @(
        [ordered]@{
            id = "failFirst"
            kind = "tsproj.describe-io-topology"
            options = [ordered]@{
                "project-path" = $missingProjectPath
            }
        },
        [ordered]@{
            id = "skipUnresolvedOption"
            kind = "tsproj.ensure-task"
            options = [ordered]@{
                "project-path" = '${steps.failFirst.outputs.projectPath}'
                "task-name" = "ShouldNotResolve"
            }
        },
        [ordered]@{
            id = "skipUnresolvedEnabled"
            kind = "tsproj.ensure-system-settings"
            enabled = '${steps.failFirst.outputs.enabled}'
            options = [ordered]@{
                "project-path" = (Join-Path $OutputRoot "unused.tsproj")
            }
        },
        [ordered]@{
            id = "skipUnresolvedRunAfterFailure"
            kind = "tsproj.describe-io-topology"
            runAfterFailure = '${steps.failFirst.outputs.keepGoing}'
            options = [ordered]@{
                "project-path" = (Join-Path $OutputRoot "also-unused.tsproj")
            }
        }
    )
}

$plan | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $planPath -Encoding UTF8

$dotnetArgs = @(
    "run",
    "--no-restore",
    "--project",
    $cliProject,
    "--",
    "run-plan",
    "--file",
    $planPath,
    "--stop-on-failure",
    "true",
    "--summary",
    $summaryPath
)

Write-Host "RepoRoot:   $repoRoot"
Write-Host "OutputRoot: $OutputRoot"
Write-Host "Plan:       $planPath"
Write-Host "Summary:    $summaryPath"
Write-Host "Command:    dotnet $($dotnetArgs -join ' ')"

$previousErrorActionPreference = $ErrorActionPreference
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $previousNativeCommandPreference = $Global:PSNativeCommandUseErrorActionPreference
    $Global:PSNativeCommandUseErrorActionPreference = $false
} else {
    $previousNativeCommandPreference = $null
}

try {
    $ErrorActionPreference = "Continue"
    $output = & dotnet @dotnetArgs 2>&1
    $exitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $previousErrorActionPreference
    if ($null -ne $previousNativeCommandPreference) {
        $Global:PSNativeCommandUseErrorActionPreference = $previousNativeCommandPreference
    }
}

$text = $output -join [Environment]::NewLine
$text | Set-Content -LiteralPath $logPath -Encoding UTF8
$text | Out-Host

Assert-True ($exitCode -eq 3) "Expected run-plan exit code 3 for a handled failed step, got $exitCode."
Assert-True (Test-Path -LiteralPath $summaryPath) "Expected summary to be written: $summaryPath"
Assert-True (-not (Test-Path -LiteralPath $deferredPayloadPath)) "Deferred payload should not be written after stop-on-failure: $deferredPayloadPath"
Assert-True ($text.Contains("Deferred payload files left unresolved after stop-on-failure: 1")) "Expected deferred payload diagnostic in run-plan output."
Assert-True (-not $text.Contains("Unable to resolve token")) "Skipped steps must not raise unresolved-token errors after stop-on-failure."

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
Assert-True (-not $summary.Succeeded) "Summary should report Succeeded=false."
Assert-True ($summary.Steps.Count -eq 4) "Expected 4 step results, got $($summary.Steps.Count)."
Assert-True ($summary.Steps[0].Status -eq "Failed") "First step should preserve the original failure."
Assert-True ($summary.Steps[1].Status -eq "Skipped") "Second step should be skipped."
Assert-True ($summary.Steps[2].Status -eq "Skipped") "Third step should be skipped before resolving enabled."
Assert-True ($summary.Steps[3].Status -eq "Skipped") "Fourth step should be skipped before resolving runAfterFailure output references."
$secondOptionCount = @($summary.Steps[1].Options.PSObject.Properties).Count
$thirdOptionCount = @($summary.Steps[2].Options.PSObject.Properties).Count
$fourthOptionCount = @($summary.Steps[3].Options.PSObject.Properties).Count
Assert-True ($secondOptionCount -eq 0) "Skipped unresolved option step should not resolve or record options."
Assert-True ($thirdOptionCount -eq 0) "Skipped unresolved enabled step should not resolve or record options."
Assert-True ($fourthOptionCount -eq 0) "Skipped unresolved runAfterFailure step should not resolve or record options."

Write-Host ""
Write-Host "run-plan stop-on-failure regression passed."
