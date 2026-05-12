param(
    [string]$Root = "",
    [int]$PollMilliseconds = 500,
    [switch]$Once
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

$completedRequestCleanupFailures = @{}

function ConvertTo-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($Value.Length -eq 0) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Add-ProcessArgument {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.ProcessStartInfo]$StartInfo,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($null -ne $StartInfo.ArgumentList) {
        [void]$StartInfo.ArgumentList.Add($Value)
        return
    }

    $quoted = ConvertTo-ProcessArgument -Value $Value
    if ([string]::IsNullOrWhiteSpace($StartInfo.Arguments)) {
        $StartInfo.Arguments = $quoted
    }
    else {
        $StartInfo.Arguments = $StartInfo.Arguments + " " + $quoted
    }
}

function ConvertFrom-PowerShellEncodedText {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ($null -eq $Value) {
        return ""
    }

    return [regex]::Replace(
        $Value,
        "_x([0-9A-Fa-f]{4})_",
        {
            param($Match)
            [string][char][Convert]::ToInt32($Match.Groups[1].Value, 16)
        })
}

function ConvertFrom-PowerShellClixmlLog {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $trimmed = $Value.TrimStart()
    if (-not $trimmed.StartsWith("#< CLIXML", [StringComparison]::OrdinalIgnoreCase)) {
        return $Value
    }

    $xmlText = $trimmed.Substring("#< CLIXML".Length).TrimStart()
    try {
        [xml]$document = $xmlText
        $namespaceManager = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
        $namespaceManager.AddNamespace("ps", "http://schemas.microsoft.com/powershell/2004/04")
        $lines = New-Object System.Collections.Generic.List[string]
        foreach ($node in $document.SelectNodes("//ps:S|//ps:AV|//ps:ToString", $namespaceManager)) {
            $line = ConvertFrom-PowerShellEncodedText -Value $node.InnerText
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                $lines.Add($line.TrimEnd()) | Out-Null
            }
        }

        if ($lines.Count -gt 0) {
            return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
        }
    }
    catch {
    }

    return $Value
}

function Clear-CompletedRequests {
    $removedCount = 0
    $failedCount = 0
    $newFailedCount = 0
    Get-ChildItem -LiteralPath $requestDir -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Where-Object {
            $baseName = $_.BaseName -replace '\.processing$', ''
            Test-Path -LiteralPath (Join-Path $responseDir "$baseName.response.json")
        } |
        ForEach-Object {
            $completedRequestPath = $_.FullName
            try {
                Remove-Item -LiteralPath $completedRequestPath -Force -ErrorAction Stop
                $removedCount++
            }
            catch {
                $failedCount++
                if (-not $completedRequestCleanupFailures.ContainsKey($completedRequestPath)) {
                    $completedRequestCleanupFailures[$completedRequestPath] = $true
                    $newFailedCount++
                    Write-Warning "Could not remove completed request '$completedRequestPath': $($_.Exception.Message). It will be ignored because a response already exists."
                }
            }
        }

    if ($removedCount -gt 0) {
        Write-Host "Removed $removedCount completed request file(s)."
    }

    if ($newFailedCount -gt 0) {
        Write-Host "Ignored $failedCount completed request file(s) that could not be removed."
    }
}

function Restore-AbandonedProcessingRequests {
    Get-ChildItem -LiteralPath $requestDir -Filter "*.processing.json" -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $baseName = $_.BaseName -replace '\.processing$', ''
            $responsePath = Join-Path $responseDir "$baseName.response.json"
            if (Test-Path -LiteralPath $responsePath) {
                Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
                return
            }

            $requestPath = Join-Path $requestDir "$baseName.json"
            if (Test-Path -LiteralPath $requestPath) {
                Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
                return
            }

            Move-Item -LiteralPath $_.FullName -Destination $requestPath -Force
        }
}

Write-Host "Interactive runner ready."
Write-Host "Root: $Root"
Write-Host "Requests: $requestDir"
Write-Host "Create '$stopFile' to stop."

while ($true) {
    if (Test-Path -LiteralPath $stopFile) {
        Write-Host "Stop file detected. Exiting."
        break
    }

    Clear-CompletedRequests
    Restore-AbandonedProcessingRequests

    $requestFile = Get-ChildItem -LiteralPath $requestDir -Filter "*.json" -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -notlike "*.processing.json" -and
            -not (Test-Path -LiteralPath (Join-Path $responseDir "$($_.BaseName).response.json"))
        } |
        Sort-Object LastWriteTimeUtc |
        Select-Object -First 1

    if ($null -eq $requestFile) {
        if ($Once) {
            Write-Host "No pending request. Exiting."
            break
        }

        Start-Sleep -Milliseconds $PollMilliseconds
        continue
    }

    $id = $requestFile.BaseName
    $requestText = ""
    $command = ""
    $workingDirectory = $Root
    $timeoutSeconds = 1800
    $processingFile = Join-Path $requestDir "$id.processing.json"
    $responsePath = Join-Path $responseDir "$id.response.json"
    $originalRequestPath = $requestFile.FullName
    $stdoutPath = Join-Path $logDir "$id.stdout.log"
    $stderrPath = Join-Path $logDir "$id.stderr.log"
    $startedAt = [DateTimeOffset]::Now

    try {
        $requestText = Get-Content -LiteralPath $requestFile.FullName -Raw
        $request = $requestText | ConvertFrom-Json
        $id = if ([string]::IsNullOrWhiteSpace($request.id)) { [Guid]::NewGuid().ToString("N") } else { [string]$request.id }
        $command = [string]$request.command
        $workingDirectory = if ([string]::IsNullOrWhiteSpace($request.workingDirectory)) { $Root } else { [string]$request.workingDirectory }
        $timeoutSeconds = if ($null -eq $request.timeoutSeconds) { 1800 } else { [int]$request.timeoutSeconds }

        $processingFile = Join-Path $requestDir "$id.processing.json"
        $responsePath = Join-Path $responseDir "$id.response.json"
        $stdoutPath = Join-Path $logDir "$id.stdout.log"
        $stderrPath = Join-Path $logDir "$id.stderr.log"
        if (Test-Path -LiteralPath $responsePath) {
            Remove-Item -LiteralPath $originalRequestPath -Force -ErrorAction SilentlyContinue
            continue
        }

        Set-Content -LiteralPath $processingFile -Value $requestText -Encoding UTF8
        Remove-Item -LiteralPath $originalRequestPath -Force -ErrorAction SilentlyContinue

        Write-Host "RUN ${id}: $command"

        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
        $commandPrefix = @'
$ProgressPreference = "SilentlyContinue"
$InformationPreference = "Continue"
'@
        $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($commandPrefix + "`r`n" + $command))
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = "powershell.exe"
        foreach ($argument in @("-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-STA", "-ExecutionPolicy", "Bypass", "-EncodedCommand", $encodedCommand)) {
            Add-ProcessArgument -StartInfo $startInfo -Value $argument
        }
        $startInfo.WorkingDirectory = $workingDirectory
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true

        $process = [System.Diagnostics.Process]::Start($startInfo)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($timeoutSeconds * 1000)
        if ($timedOut) {
            try {
                $process.Kill($true)
            }
            catch {
            }
        }

        $process.Refresh()
        $stdoutTask.Wait(5000) | Out-Null
        $stderrTask.Wait(5000) | Out-Null
        Set-Content -LiteralPath $stdoutPath -Value (ConvertFrom-PowerShellClixmlLog -Value $stdoutTask.Result) -Encoding UTF8
        Set-Content -LiteralPath $stderrPath -Value (ConvertFrom-PowerShellClixmlLog -Value $stderrTask.Result) -Encoding UTF8

        $finishedAt = [DateTimeOffset]::Now
        $exitCode = if ($timedOut) { -1 } else { $process.ExitCode }
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

        $response | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $responsePath -Encoding UTF8
        Remove-Item -LiteralPath $originalRequestPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $processingFile -Force -ErrorAction SilentlyContinue
        Write-Host "DONE $id exit=$exitCode timedOut=$timedOut"
    }
    catch {
        $finishedAt = [DateTimeOffset]::Now
        $message = $_.Exception.ToString()
        Set-Content -LiteralPath $stdoutPath -Value "" -Encoding UTF8
        Set-Content -LiteralPath $stderrPath -Value $message -Encoding UTF8
        $response = [ordered]@{
            id = $id
            command = $command
            workingDirectory = $workingDirectory
            exitCode = -2
            timedOut = $false
            startedAt = $startedAt.ToString("O")
            finishedAt = $finishedAt.ToString("O")
            stdoutPath = $stdoutPath
            stderrPath = $stderrPath
            runnerError = $message
        }

        $response | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $responsePath -Encoding UTF8
        Remove-Item -LiteralPath $originalRequestPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $processingFile -Force -ErrorAction SilentlyContinue
        Write-Host "DONE $id exit=-2 runnerError=True"
    }

    if ($Once) {
        break
    }
}
