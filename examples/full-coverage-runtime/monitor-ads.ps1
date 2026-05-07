param(
    [string]$NetId = "local",
    [int]$Port = 851,
    [int]$IntervalMs = 1000,
    [int]$Count = 0,
    [switch]$VerboseStepOutput,
    [string]$Symbols = "MAIN.nCycle:UInt32;MAIN.nSeed:UInt32;MAIN.nStage1:UInt32;MAIN.nStage1ChangeCount:UInt32;MAIN.nStage2Seed:UInt32;MAIN.nStage2:UInt32;MAIN.nStage2ChangeCount:UInt32;MAIN.bHeartbeat:Boolean;MAIN.bPipelineOk:Boolean;MAIN.nMismatchCount:UInt32"
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$cliProject = Join-Path $repoRoot "src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj"

$iteration = 0
while ($true) {
    $iteration++
    $stamp = Get-Date -Format o

    $output = & dotnet run --project $cliProject -- invoke-step --kind=validation.ads-read-symbols --net-id=$NetId --port=$Port "--symbols=$Symbols" --auto-reconnect=true --continue-on-error=true 2>&1
    $exitCode = $LASTEXITCODE

    if ($VerboseStepOutput) {
        Write-Host "[$stamp] sample $iteration"
        $output | ForEach-Object { Write-Host $_ }
    } else {
        $resultLine = $output | Where-Object { $_ -like "Result: ADS read symbols completed:*" } | Select-Object -First 1
        $statusLine = $output | Where-Object { $_ -like "Status:*" } | Select-Object -First 1
        if ($exitCode -eq 0 -and $resultLine) {
            $values = $resultLine -replace '^Result: ADS read symbols completed:\s*', ''
            $map = @{}
            foreach ($entry in ($values -split ';')) {
                $parts = $entry.Trim() -split '=', 2
                if ($parts.Count -eq 2) {
                    $map[$parts[0].Trim()] = $parts[1].Trim()
                }
            }

            $cycle = $map["MAIN.nCycle"]
            $heartbeat = $map["MAIN.bHeartbeat"]
            $pipelineOk = $map["MAIN.bPipelineOk"]
            $mismatch = $map["MAIN.nMismatchCount"]
            $failedEntries = $map.GetEnumerator() | Where-Object { $_.Value -like "<failed:*" }

            if ($failedEntries.Count -gt 0) {
                Write-Host "[$stamp] sample $iteration ADS returned failed symbol reads"
                foreach ($entry in $failedEntries) {
                    Write-Host "  $($entry.Key): $($entry.Value)"
                }
                Write-Host "  Hint: activate the full coverage plan first, then read ADS port $Port."
            } else {
                $seed = [int]$map["MAIN.nSeed"]
                $stage1 = [int]$map["MAIN.nStage1"]
                $stage1Expected = $seed + 123
                $stage1Status = if ($stage1 -eq $stage1Expected) { "OK" } else { "CHECK expected=$stage1Expected" }
                $stage2Seed = [int]$map["MAIN.nStage2Seed"]
                $stage2 = [int]$map["MAIN.nStage2"]
                $stage2Expected = $stage2Seed + 17
                $stage2Status = if ($stage2 -eq $stage2Expected) { "OK" } else { "CHECK expected=$stage2Expected" }

                Write-Host "[$stamp] sample $iteration cycle=$cycle heartbeat=$heartbeat pipelineOk=$pipelineOk mismatch=$mismatch"
                Write-Host "  primary: MAIN.nSeed=$seed + 123 => MAIN.nStage1=$stage1 [$stage1Status]; changes=$($map["MAIN.nStage1ChangeCount"])"
                Write-Host "  aux:     MAIN.nStage2Seed=$stage2Seed + 17 => MAIN.nStage2=$stage2 [$stage2Status]; changes=$($map["MAIN.nStage2ChangeCount"])"
            }
        } else {
            Write-Host "[$stamp] sample $iteration ADS read failed"
            if ($statusLine) {
                Write-Host "  $statusLine"
            }
            $output | ForEach-Object { Write-Host "  $_" }
        }
    }

    if ($Count -gt 0 -and $iteration -ge $Count) {
        break
    }

    Start-Sleep -Milliseconds $IntervalMs
}
