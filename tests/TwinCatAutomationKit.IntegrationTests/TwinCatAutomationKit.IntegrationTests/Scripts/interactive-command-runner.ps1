param(
    [string]$Root = "",
    [int]$PollMilliseconds = 500
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
}

$runnerRoot = Join-Path $Root ".artifacts\interactive-runner"
$requestDir = Join-Path $runnerRoot "requests"
$responseDir = Join-Path $runnerRoot "responses"
$logDir = Join-Path $runnerRoot "logs"
$stopFile = Join-Path $runnerRoot "stop"

New-Item -ItemType Directory -Force -Path $requestDir, $responseDir, $logDir | Out-Null

Write-Host "Interactive runner ready."
Write-Host "Root: $Root"
Write-Host "Requests: $requestDir"
Write-Host "Create '$stopFile' to stop."

while ($true) {
    if (Test-Path -LiteralPath $stopFile) {
        Write-Host "Stop file detected. Exiting."
        break
    }

    $requestFile = Get-ChildItem -LiteralPath $requestDir -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike "*.processing.json" } |
        Sort-Object LastWriteTimeUtc |
        Select-Object -First 1

    if ($null -eq $requestFile) {
        Start-Sleep -Milliseconds $PollMilliseconds
        continue
    }

    $request = Get-Content -LiteralPath $requestFile.FullName -Raw | ConvertFrom-Json
    $id = if ([string]::IsNullOrWhiteSpace($request.id)) { [Guid]::NewGuid().ToString("N") } else { [string]$request.id }
    $command = [string]$request.command
    $workingDirectory = if ([string]::IsNullOrWhiteSpace($request.workingDirectory)) { $Root } else { [string]$request.workingDirectory }
    $timeoutSeconds = if ($null -eq $request.timeoutSeconds) { 1800 } else { [int]$request.timeoutSeconds }

    $processingFile = Join-Path $requestDir "$id.processing.json"
    Move-Item -LiteralPath $requestFile.FullName -Destination $processingFile -Force

    $stdoutPath = Join-Path $logDir "$id.stdout.log"
    $stderrPath = Join-Path $logDir "$id.stderr.log"
    $startedAt = [DateTimeOffset]::Now
    Write-Host "RUN ${id}: $command"

    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList @("-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-EncodedCommand", $encodedCommand) `
        -WorkingDirectory $workingDirectory `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru
    $timedOut = -not $process.WaitForExit($timeoutSeconds * 1000)
    if ($timedOut) {
        try {
            $process.Kill($true)
        }
        catch {
        }
    }

    $process.Refresh()
    if (-not (Test-Path -LiteralPath $stdoutPath)) {
        New-Item -ItemType File -Path $stdoutPath -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $stderrPath)) {
        New-Item -ItemType File -Path $stderrPath -Force | Out-Null
    }

    $finishedAt = [DateTimeOffset]::Now
    $exitCode = if ($timedOut) { -1 } elseif ($null -eq $process.ExitCode) { 0 } else { $process.ExitCode }
    $response = [ordered]@{
        id = $id
        command = $command
        workingDirectory = $workingDirectory
        exitCode = $exitCode
        timedOut = $timedOut
        startedAt = $startedAt.ToString("O")
        finishedAt = $finishedAt.ToString("O")
        stdoutPath = $stdoutPath
        stderrPath = $stderrPath
    }

    $responsePath = Join-Path $responseDir "$id.response.json"
    $response | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $responsePath -Encoding UTF8
    Remove-Item -LiteralPath $processingFile -Force -ErrorAction SilentlyContinue
    Write-Host "DONE $id exit=$exitCode timedOut=$timedOut"
}
