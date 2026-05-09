param(
    [string]$OutputRoot = "D:\t\tcak_cli_all_$(Get-Date -Format yyyyMMdd_HHmmss)",
    [switch]$Execute,
    [switch]$Visible,
    [int]$StartupDelayMs = 8000,
    [switch]$IncludeActivation,
    [switch]$IncludeAdsRead,
    [switch]$IncludeSigning,
    [switch]$IncludeMergeFragment,
    [switch]$GrantCertificate,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$CertificatePasswordFile,
    [string]$CertificatePasswordEnvVar,
    [string]$AdsNetId = "127.0.0.1.1.1",
    [int]$AdsPort = 851,
    [string]$AdsSymbol = "MAIN.nValue",
    [ValidateSet("Boolean", "Int32", "UInt32", "Int64", "UInt64", "Double", "String")]
    [string]$AdsType = "Int32"
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

$repoRoot = Find-RepoRoot
$cliProject = Join-Path $repoRoot "src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj"
$solutionName = "Demo"
$projectName = "Demo"
$cppProjectName = "DemoCpp"
$plcProjectName = "PlcA"
$plcInstanceName = "PlcInst01"
$solutionPath = Join-Path $OutputRoot "$solutionName.sln"
$projectPath = Join-Path $OutputRoot "$projectName\$projectName.tsproj"
$cppTmcPath = Join-Path $OutputRoot "$projectName\$cppProjectName\$cppProjectName.tmc"
$evidenceDir = Join-Path $OutputRoot "_cli_verify_evidence"
$payloadDir = Join-Path $OutputRoot "_cli_verify_payloads"
$logPath = Join-Path $OutputRoot "_cli_verify.log"
$task1AmsPort = 360
$taskCliAmsPort = 361
$targetTmxPath = Join-Path $OutputRoot "$projectName\$cppProjectName\_products\TwinCAT OS (x64)\Release\$cppProjectName\$cppProjectName.tmx"

$allKinds = @(
    "engineering.launch-visual-studio",
    "engineering.create-xae-solution",
    "engineering.open-xae-solution",
    "engineering.create-cpp-project",
    "engineering.create-plc-project",
    "engineering.create-module",
    "engineering.add-module-instance",
    "engineering.ensure-task",
    "engineering.export-tree-item-xml",
    "engineering.save-all",
    "engineering.close-visual-studio",
    "engineering.build-solution",
    "signing.grant-certificate",
    "signing.set-license",
    "signing.sign-twincat-binary",
    "signing.verify-twincat-binary",
    "engineering.activate-configuration",
    "tsproj.ensure-task",
    "tsproj.clear-task-layout",
    "tsproj.ensure-task-vars-group",
    "tsproj.ensure-task-image",
    "tsproj.ensure-cpp-instance",
    "tsproj.ensure-plc-instance",
    "tsproj.bind-instance-context",
    "tsproj.bind-instance-task",
    "tsproj.bind-plc-instance-task",
    "tsproj.set-task-affinity",
    "tsproj.set-plc-project-properties",
    "tsproj.set-plc-instance-metadata",
    "tsproj.set-cpp-instance-metadata",
    "tsproj.clear-plc-instance-vars",
    "tsproj.ensure-plc-instance-vars-group",
    "tsproj.clear-plc-init-symbols",
    "tsproj.clear-plc-task-pou-oids",
    "tsproj.clear-mappings",
    "tsproj.replace-mappings-section",
    "tsproj.replace-project-io-section",
    "tsproj.ensure-io-section",
    "tsproj.ensure-io-device",
    "tsproj.ensure-ethercat-box",
    "tsproj.ensure-io-pdo",
    "tsproj.ensure-io-box-image",
    "tsproj.ensure-mapping-info",
    "tsproj.ensure-io-mapping-link",
    "tsproj.apply-io-topology-plan",
    "tsproj.replace-data-types-section",
    "tsproj.replace-system-settings-section",
    "tsproj.ensure-system-settings",
    "tsproj.clear-instance-parameter-values",
    "tsproj.clear-instance-data-pointer-values",
    "tsproj.apply-instance-parameter-plan",
    "tsproj.apply-instance-interface-pointer-plan",
    "tsproj.apply-instance-data-pointer-plan",
    "tsproj.ensure-io-task-image",
    "tsproj.ensure-task-pou-oid",
    "tsproj.ensure-init-symbol",
    "tsproj.ensure-parameter",
    "tsproj.ensure-interface-pointer",
    "tsproj.ensure-data-pointer",
    "tsproj.ensure-mapping-link",
    "tsproj.clear-unrestored-var-links",
    "tsproj.upsert-element",
    "tsproj.upsert-fragment",
    "tsproj.apply-mutation-plan",
    "tsproj.merge-fragment",
    "validation.ads-scan",
    "validation.ads-read",
    "validation.ads-read-symbols"
)

$script:stepIndex = 0
$script:ranKinds = New-Object System.Collections.Generic.List[string]
$script:skippedKinds = New-Object System.Collections.Generic.List[string]
$script:failedKinds = New-Object System.Collections.Generic.List[string]
$script:cppInstanceName = "Demo_Obj1 (DemoCpp)"
$script:taskObjectId = "#x02010020"

function Format-CommandLine {
    param([string[]]$Arguments)
    ($Arguments | ForEach-Object {
        if ($_ -match '[\s#()^]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join " "
}

function Invoke-DotnetCli {
    param(
        [string]$Kind,
        [string[]]$Arguments,
        [switch]$PassThru
    )

    $script:stepIndex++
    $script:ranKinds.Add($Kind) | Out-Null

    $dotnetArgs = @("run", "--project", $cliProject, "--", "invoke-step", "--kind=$Kind") + $Arguments
    $commandLine = "dotnet " + (Format-CommandLine $dotnetArgs)

    Write-Host ""
    Write-Host ("[{0:00}] {1}" -f $script:stepIndex, $Kind)
    Write-Host $commandLine

    if (-not $Execute) {
        if ($PassThru) {
            return ""
        }

        return
    }

    $output = & dotnet @dotnetArgs 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join [Environment]::NewLine
    $text | Tee-Object -FilePath $logPath -Append | Out-Host

    if ($exitCode -ne 0) {
        $script:failedKinds.Add($Kind) | Out-Null
        throw "Command failed for $Kind with exit code $exitCode. See $logPath"
    }

    if ($PassThru) {
        return $text
    }
}

function Skip-Kind {
    param(
        [string]$Kind,
        [string]$Reason
    )

    $script:skippedKinds.Add($Kind) | Out-Null
    Write-Host ""
    Write-Host ("[SKIP] {0}" -f $Kind)
    Write-Host $Reason
}

function Get-JsonStringValue {
    param(
        [string]$Text,
        [string]$Name,
        [string]$Fallback
    )

    $pattern = '"' + [regex]::Escape($Name) + '"\s*:\s*"([^"]*)"'
    if ($Text -match $pattern) {
        return $Matches[1]
    }

    return $Fallback
}

function Get-CertificatePasswordArgs {
    $sources = @(
        [string]::IsNullOrWhiteSpace($CertificatePassword),
        [string]::IsNullOrWhiteSpace($CertificatePasswordFile),
        [string]::IsNullOrWhiteSpace($CertificatePasswordEnvVar)
    ) | Where-Object { -not $_ }

    if ($sources.Count -gt 1) {
        throw "Use only one certificate password source: -CertificatePassword, -CertificatePasswordFile, or -CertificatePasswordEnvVar."
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePasswordFile)) {
        return @("--password-file=$CertificatePasswordFile")
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePasswordEnvVar)) {
        return @("--password-env-var=$CertificatePasswordEnvVar")
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        return @("--password=$CertificatePassword")
    }

    return @()
}

function Write-PayloadFiles {
    if (-not $Execute) {
        return
    }

    New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

    @"
<Mappings>
  <OwnerA Name="TIXC^$cppProjectName^FileMutationCpp01">
    <OwnerB Name="TIPC^$plcProjectName^$plcInstanceName">
      <Link VarA="Outputs^Var 1" VarB="MAIN.nValue" />
    </OwnerB>
  </OwnerA>
</Mappings>
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "mappings.xml") -Encoding UTF8

    @"
<Io>
  <Device Id="30" Disabled="true" DevType="111" DevFlags="#x0003">
    <Name>CliVerifyIo</Name>
    <Comment>created by verify-all-invoke-steps.ps1</Comment>
  </Device>
</Io>
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "io.xml") -Encoding UTF8

    @"
{
  "devices": [
    {
      "deviceId": 32,
      "name": "Cli Device 32 (EtherCAT)",
      "devType": 111,
      "disabled": true,
      "devFlags": "#x0003",
      "amsPort": 28632,
      "amsNetId": "127.0.0.1.32.1",
      "addressInfo": {
        "tcComObjectId": "#x03010032"
      }
    }
  ],
  "boxes": [
    {
      "deviceId": 32,
      "boxId": 3201,
      "name": "Cli Box 3201 (EK1100)",
      "boxType": 9099,
      "imageId": 1002,
      "etherCatAttributes": [
        { "name": "SlaveType", "value": "1" },
        { "name": "Desc", "value": "EK1100" }
      ]
    }
  ],
  "pdos": [
    {
      "deviceId": 32,
      "boxId": 3201,
      "name": "PLC_Inputs",
      "index": "#x1a00",
      "flags": "#x0000",
      "syncMan": 3,
      "entries": [
        {
          "name": "Input",
          "index": "#x6000",
          "sub": "#x01",
          "type": "BIT"
        }
      ]
    }
  ],
  "mappingInfos": [
    {
      "identifier": "{00000000-0020-0201-4000-010132000101}",
      "id": "#x02030040"
    }
  ],
  "links": [
    {
      "ownerAName": "TIID^Cli Device 32 (EtherCAT)",
      "ownerBName": "TIPC^$plcProjectName^$plcInstanceName",
      "varA": "Cli Box 3201 (EK1100)^PLC_Inputs^Input",
      "varB": "PlcTask Inputs^MAIN.nValue",
      "linkAttributes": [
        { "name": "Size", "value": "1" }
      ]
    }
  ]
}
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "io-topology-plan.json") -Encoding UTF8

    @"
<DataTypes>
  <DataType>
    <Name>CliVerifyType</Name>
  </DataType>
</DataTypes>
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "datatypes.xml") -Encoding UTF8

    @"
<Settings>
  <Option Name="CliVerify">true</Option>
</Settings>
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "settings.xml") -Encoding UTF8

    @"
<DataType>
  <Name>CliVerifyMergedType</Name>
</DataType>
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "merge-datatype.xml") -Encoding UTF8

    @"
[
  {
    "instanceName": "FileMutationCpp01",
    "parameterName": "Parameter.data1",
    "valueText": "321"
  },
  {
    "instanceName": "$($script:cppInstanceName)",
    "parameterName": "Parameter.data1",
    "valueText": "123"
  }
]
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "parameter-plan.json") -Encoding UTF8

    @"
[
  {
    "instanceName": "FileMutationCpp01",
    "pointerName": "CyclicCaller",
    "objectId": "$($script:taskObjectId)"
  },
  {
    "instanceName": "$($script:cppInstanceName)",
    "pointerName": "CyclicCaller",
    "objectId": "$($script:taskObjectId)"
  }
]
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "interface-pointer-plan.json") -Encoding UTF8

    @"
[
  {
    "instanceName": "FileMutationCpp01",
    "pointerName": "Inputs.Value",
    "objectId": "$($script:taskObjectId)",
    "areaNo": 0,
    "byteOffset": 8,
    "byteSize": 4
  },
  {
    "instanceName": "$($script:cppInstanceName)",
    "pointerName": "Inputs.Value",
    "objectId": "$($script:taskObjectId)",
    "areaNo": 0,
    "byteOffset": 16,
    "byteSize": 4
  },
  {
    "instanceName": "$($script:cppInstanceName)",
    "pointerName": "IndexedData",
    "objectId": "$($script:taskObjectId)",
    "areaNo": 3,
    "byteOffset": 24,
    "byteSize": 8,
    "arrayIndex": 1
  }
]
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "data-pointer-plan.json") -Encoding UTF8

    @"
{
  "parentPath": [
    { "elementName": "Cpp" },
    { "elementName": "Project", "nameValue": "$cppProjectName" },
    { "elementName": "Instance", "nameValue": "FileMutationCpp01" },
    { "elementName": "TmcDesc" },
    { "elementName": "ParameterValues" }
  ],
  "elementName": "Value",
  "matchNameValue": "CliGenericElementParam",
  "childValues": [
    { "elementName": "ValueText", "value": "456" }
  ]
}
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "generic-element-upsert.json") -Encoding UTF8

    @"
{
  "parentPath": [
    { "elementName": "DataTypes" }
  ],
  "fragmentXml": "<DataType><Name>CliGenericFragmentType</Name></DataType>",
  "matchElementName": "DataType",
  "matchNameValue": "CliGenericFragmentType"
}
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "generic-fragment-upsert.json") -Encoding UTF8

    @"
{
  "elementUpserts": [
    {
      "parentPath": [
        { "elementName": "Cpp" },
        { "elementName": "Project", "nameValue": "$cppProjectName" },
        { "elementName": "Instance", "nameValue": "FileMutationCpp01" },
        { "elementName": "TmcDesc" },
        { "elementName": "ParameterValues" }
      ],
      "elementName": "Value",
      "matchNameValue": "CliGenericPlanParam",
      "childValues": [
        { "elementName": "ValueText", "value": "789" }
      ]
    }
  ],
  "fragmentUpserts": [
    {
      "parentPath": [
        { "elementName": "DataTypes" }
      ],
      "fragmentXml": "<DataType><Name>CliGenericPlanType</Name></DataType>",
      "matchElementName": "DataType",
      "matchNameValue": "CliGenericPlanType"
    }
  ]
}
"@ | Set-Content -LiteralPath (Join-Path $payloadDir "generic-mutation-plan.json") -Encoding UTF8
}

if (-not (Test-Path -LiteralPath $cliProject)) {
    throw "CLI project not found: $cliProject"
}

if ($Execute) {
    if (Test-Path -LiteralPath $OutputRoot) {
        throw "OutputRoot already exists. Pick a new path or delete it yourself: $OutputRoot"
    }

    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
    "verify-all-invoke-steps started $(Get-Date -Format o)" | Set-Content -LiteralPath $logPath -Encoding UTF8
} else {
    Write-Host "DRY RUN ONLY. Add -Execute to run commands against real TwinCAT/XAE."
}

Write-Host "RepoRoot:   $repoRoot"
Write-Host "OutputRoot: $OutputRoot"
Write-Host "CLI:        $cliProject"

$visibleText = if ($Visible) { "true" } else { "false" }
$commonEngineeringArgs = @("--solution-path=$solutionPath", "--project-path=$projectPath", "--visible=$visibleText", "--startup-delay-ms=$StartupDelayMs")

Invoke-DotnetCli "engineering.launch-visual-studio" @("--visible=$visibleText", "--startup-delay-ms=$StartupDelayMs")
Invoke-DotnetCli "engineering.create-xae-solution" @("--solution-directory=$OutputRoot", "--solution-name=$solutionName", "--project-name=$projectName", "--visible=$visibleText", "--startup-delay-ms=$StartupDelayMs")
Invoke-DotnetCli "engineering.open-xae-solution" $commonEngineeringArgs
Invoke-DotnetCli "engineering.create-cpp-project" ($commonEngineeringArgs + @("--cpp-project-name=$cppProjectName"))
Invoke-DotnetCli "engineering.create-plc-project" ($commonEngineeringArgs + @("--plc-project-name=$plcProjectName"))
Invoke-DotnetCli "engineering.create-module" ($commonEngineeringArgs + @("--cpp-project-name=$cppProjectName", "--module-name=AuxModule", "--allow-offline-fallback=true"))

$addInstanceOutput = Invoke-DotnetCli "engineering.add-module-instance" ($commonEngineeringArgs + @("--cpp-project-name=$cppProjectName", "--instance-base-name=Demo_Obj1", "--module-class-name=DemoCpp")) -PassThru
$treeItemPath = Get-JsonStringValue $addInstanceOutput "treeItemPath" "TIXC^$cppProjectName^$($script:cppInstanceName)"
if ($treeItemPath -match '\^([^\^]+)$') {
    $script:cppInstanceName = $Matches[1]
}

# CreatePlcProject commonly leaves a wizard-created PlcTask on AmsPort 350.
# TwinCAT requires Task AmsPort values to be unique, so every script-created
# task gets its own port.
$ensureTaskOutput = Invoke-DotnetCli "engineering.ensure-task" ($commonEngineeringArgs + @("--task-name=Task1", "--task-subtype=0", "--priority=15", "--cycle-time-us=10000", "--ams-port=$task1AmsPort")) -PassThru
$script:taskObjectId = Get-JsonStringValue $ensureTaskOutput "objectId" $script:taskObjectId

Invoke-DotnetCli "engineering.export-tree-item-xml" ($commonEngineeringArgs + @("--tree-item-path=TIXC^$cppProjectName", "--destination-path=$(Join-Path $evidenceDir 'cpp.project.xml')", "--recursive=true"))
Invoke-DotnetCli "engineering.save-all" $commonEngineeringArgs
Invoke-DotnetCli "engineering.close-visual-studio" ($commonEngineeringArgs + @("--save-before-close=true"))

# Activation must not run against a half-wired TcCOM instance. Bind the real
# instance to the real task before the build/sign/activate chain.
Invoke-DotnetCli "tsproj.bind-instance-task" @("--project-path=$projectPath", "--instance-name=$($script:cppInstanceName)", "--task-object-id=$($script:taskObjectId)", "--priority=15", "--cycle-time-ns=10000000", "--include-cyclic-caller=true")
Invoke-DotnetCli "tsproj.ensure-parameter" @("--project-path=$projectPath", "--instance-name=$($script:cppInstanceName)", "--parameter-name=Parameter.data1", "--value-text=123")

if ($IncludeSigning -and -not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $licenseName = [System.IO.Path]::GetFileNameWithoutExtension($CertificatePath)
    Invoke-DotnetCli "signing.set-license" (@("--project-path=$projectPath", "--cpp-project-name=$cppProjectName", "--license-name=$licenseName") + (Get-CertificatePasswordArgs))
} else {
    Skip-Kind "signing.set-license" "Skipped because it writes build-time signing settings into the C++ project. Re-run with -IncludeSigning -CertificatePath=... to include it."
}

Invoke-DotnetCli "engineering.build-solution" ($commonEngineeringArgs + @("--timeout-ms=300000"))

if ($IncludeSigning) {
    if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        throw "-IncludeSigning requires -CertificatePath."
    }

    $passwordArgs = @(Get-CertificatePasswordArgs)
    if ($GrantCertificate) {
        Invoke-DotnetCli "signing.grant-certificate" (@("--certificate-path=$CertificatePath") + $passwordArgs)
    } else {
        Skip-Kind "signing.grant-certificate" "Skipped because certificate grants modify local TcSignTool authorization. Re-run with -IncludeSigning -GrantCertificate to include it."
    }

    Invoke-DotnetCli "signing.sign-twincat-binary" (@("--certificate-path=$CertificatePath", "--target-paths=$targetTmxPath") + $passwordArgs)
    Invoke-DotnetCli "signing.verify-twincat-binary" @("--target-paths=$targetTmxPath")
} else {
    Skip-Kind "signing.grant-certificate" "Skipped because signing requires a real TwinCAT certificate. Re-run with -IncludeSigning -CertificatePath=..."
    Skip-Kind "signing.sign-twincat-binary" "Skipped because signing requires a real TwinCAT certificate. Re-run with -IncludeSigning -CertificatePath=..."
    Skip-Kind "signing.verify-twincat-binary" "Skipped because unsigned local smoke binaries are not expected to verify."
}

if ($IncludeActivation) {
    Invoke-DotnetCli "engineering.activate-configuration" ($commonEngineeringArgs + @("--save-configuration-archive=true", "--configuration-archive-path=$(Join-Path $evidenceDir 'activated.tszip')"))
} else {
    Skip-Kind "engineering.activate-configuration" "Skipped by default because it activates the local TwinCAT configuration. Re-run with -IncludeActivation to include it."
}

Write-PayloadFiles

Invoke-DotnetCli "tsproj.ensure-task" @("--project-path=$projectPath", "--task-name=TaskCli", "--priority=18", "--cycle-time-ns=20000000", "--ams-port=$taskCliAmsPort", "--io-at-begin=true")
Invoke-DotnetCli "tsproj.ensure-task-vars-group" @("--project-path=$projectPath", "--task-name=TaskCli", "--group-name=Inputs", "--var-grp-type=1", "--insert-type=1", "--type-name=REAL", "--count=2", "--bit-stride=32", "--external-address-stride=4")
Invoke-DotnetCli "tsproj.ensure-task-image" @("--project-path=$projectPath", "--task-name=TaskCli", "--image-id=3", "--size-in=16", "--size-out=8", "--image-name=Image", "--io-at-begin=true")
Invoke-DotnetCli "tsproj.clear-task-layout" @("--project-path=$projectPath", "--task-name=TaskCli", "--remove-vars=true", "--remove-image=true")
Invoke-DotnetCli "tsproj.ensure-cpp-instance" @("--project-path=$projectPath", "--cpp-project-name=$cppProjectName", "--instance-name=FileMutationCpp01", "--object-id=#x02020010", "--context-id=1", "--context-name=CliCtx", "--priority=15", "--cycle-time-ns=10000000")
Invoke-DotnetCli "tsproj.ensure-plc-instance" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName")
Invoke-DotnetCli "tsproj.bind-instance-context" @("--project-path=$projectPath", "--instance-name=FileMutationCpp01", "--task-object-id=$($script:taskObjectId)", "--priority=15", "--cycle-time-ns=10000000", "--context-id=1", "--context-name=CliCtx", "--include-cyclic-caller=true")
Invoke-DotnetCli "tsproj.bind-plc-instance-task" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--plc-task-name=Task1", "--task-object-id=$($script:taskObjectId)", "--priority=15", "--cycle-time-ns=10000000", "--context-id=0")
Invoke-DotnetCli "tsproj.set-task-affinity" @("--project-path=$projectPath", "--task-name=Task1", "--affinity=#x1", "--enable-adt-tasks=true")
Invoke-DotnetCli "tsproj.set-plc-project-properties" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--ams-port=851", "--reload-tmc=true")
Invoke-DotnetCli "tsproj.set-plc-instance-metadata" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--tc-sm-class=PlcTask", "--keep-unrestored-links=2", "--class-factory=$plcProjectName")
Invoke-DotnetCli "tsproj.set-cpp-instance-metadata" @("--project-path=$projectPath", "--instance-name=$($script:cppInstanceName)", "--disabled=true", "--keep-unrestored-links=2")
Invoke-DotnetCli "tsproj.ensure-plc-instance-vars-group" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--group-name=PlcTask Outputs", "--var-grp-type=2", "--area-no=1", "--variables=MAIN.nValue:DINT:0:0;MAIN.rValue:REAL:32:4")
Invoke-DotnetCli "tsproj.clear-plc-instance-vars" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName")
Invoke-DotnetCli "tsproj.ensure-plc-instance-vars-group" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--group-name=PlcTask Outputs", "--var-grp-type=2", "--area-no=1", "--variables=MAIN.nValue:DINT:0:0;MAIN.rValue:REAL:32:4")
Invoke-DotnetCli "tsproj.clear-plc-init-symbols" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--remove-container-when-empty=true")
Invoke-DotnetCli "tsproj.clear-plc-task-pou-oids" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--remove-container-when-empty=false")
Invoke-DotnetCli "tsproj.clear-mappings" @("--project-path=$projectPath")
Invoke-DotnetCli "tsproj.replace-mappings-section" @("--project-path=$projectPath", "--xml-file=$(Join-Path $payloadDir 'mappings.xml')")
Invoke-DotnetCli "tsproj.replace-project-io-section" @("--project-path=$projectPath", "--xml-file=$(Join-Path $payloadDir 'io.xml')")
Invoke-DotnetCli "tsproj.ensure-io-section" @("--project-path=$projectPath")
Invoke-DotnetCli "tsproj.ensure-io-device" @("--project-path=$projectPath", "--device-id=31", "--name=Cli Device 31 (EtherCAT)", "--dev-type=111", "--disabled=true", "--dev-flags=#x0003", "--ams-port=28631", "--ams-net-id=127.0.0.1.31.1", "--remote-name=Cli Device 31 (EtherCAT)")
Invoke-DotnetCli "tsproj.ensure-ethercat-box" @("--project-path=$projectPath", "--device-id=31", "--box-id=3101", "--name=Cli Box 3101 (EK1100)", "--box-type=9099", "--image-id=1000")
Invoke-DotnetCli "tsproj.ensure-io-box-image" @("--project-path=$projectPath", "--device-id=31", "--box-id=3101", "--image-id=1001")
Invoke-DotnetCli "tsproj.ensure-io-pdo" @("--project-path=$projectPath", "--device-id=31", "--box-id=3101", "--name=Channel 1", "--index=#x1600", "--in-out=1", "--flags=#x0011", "--sync-man=0", "--entries=Output:#x7000:#x01:BIT")
Invoke-DotnetCli "tsproj.ensure-mapping-info" @("--project-path=$projectPath", "--identifier={00000000-0020-0201-3000-010131000101}", "--id=#x02030030")
Invoke-DotnetCli "tsproj.ensure-io-mapping-link" @("--project-path=$projectPath", "--owner-a-name=TIID^Cli Device 31 (EtherCAT)", "--owner-b-name=TIXC^$cppProjectName^FileMutationCpp01", "--var-a=Cli Box 3101 (EK1100)^Channel 1^Output", "--var-b=Outputs^Power")
Invoke-DotnetCli "tsproj.apply-io-topology-plan" @("--project-path=$projectPath", "--json-file=$(Join-Path $payloadDir 'io-topology-plan.json')")
Invoke-DotnetCli "tsproj.replace-data-types-section" @("--project-path=$projectPath", "--xml-file=$(Join-Path $payloadDir 'datatypes.xml')", "--insert-before-project=true")
Invoke-DotnetCli "tsproj.replace-system-settings-section" @("--project-path=$projectPath", "--xml-file=$(Join-Path $payloadDir 'settings.xml')", "--insert-before-tasks=true")
Invoke-DotnetCli "tsproj.ensure-system-settings" @("--project-path=$projectPath", "--cpu-id=1", "--io-idle-task-priority=6", "--insert-before-tasks=true")
Invoke-DotnetCli "tsproj.clear-instance-parameter-values" @("--project-path=$projectPath", "--instance-name=FileMutationCpp01")
Invoke-DotnetCli "tsproj.apply-instance-parameter-plan" @("--project-path=$projectPath", "--json-file=$(Join-Path $payloadDir 'parameter-plan.json')")
Invoke-DotnetCli "tsproj.apply-instance-interface-pointer-plan" @("--project-path=$projectPath", "--json-file=$(Join-Path $payloadDir 'interface-pointer-plan.json')")
Invoke-DotnetCli "tsproj.clear-instance-data-pointer-values" @("--project-path=$projectPath", "--instance-name=FileMutationCpp01", "--remove-container-when-empty=false")
Invoke-DotnetCli "tsproj.apply-instance-data-pointer-plan" @("--project-path=$projectPath", "--json-file=$(Join-Path $payloadDir 'data-pointer-plan.json')")
Invoke-DotnetCli "tsproj.ensure-io-task-image" @("--project-path=$projectPath", "--task-name=Task1", "--instance-name=FileMutationCpp01", "--image-id=1", "--size-in=40", "--size-out=10", "--pointer-name=IoTaskImage", "--ensure-default-task-variables=true", "--input-real-count=2", "--output-byte-count=2", "--io-at-begin=true")
Invoke-DotnetCli "tsproj.ensure-task-pou-oid" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--priority=15", "--object-id=$($script:taskObjectId)")
Invoke-DotnetCli "tsproj.ensure-init-symbol" @("--project-path=$projectPath", "--plc-project-name=$plcProjectName", "--plc-instance-name=$plcInstanceName", "--symbol-name=MAIN.__TaskOid", "--object-id=$($script:taskObjectId)")
Invoke-DotnetCli "tsproj.ensure-interface-pointer" @("--project-path=$projectPath", "--instance-name=FileMutationCpp01", "--pointer-name=CyclicCaller", "--object-id=$($script:taskObjectId)")
Invoke-DotnetCli "tsproj.ensure-data-pointer" @("--project-path=$projectPath", "--instance-name=FileMutationCpp01", "--pointer-name=Inputs.Value", "--object-id=$($script:taskObjectId)", "--area-no=0", "--byte-offset=8", "--byte-size=4", "--array-index=0")
Invoke-DotnetCli "tsproj.clear-unrestored-var-links" @("--project-path=$projectPath")
Invoke-DotnetCli "tsproj.ensure-mapping-link" @("--project-path=$projectPath", "--owner-a-name=TIXC^$cppProjectName^FileMutationCpp01", "--owner-b-name=TIPC^$plcProjectName^$plcInstanceName", "--var-a=Outputs^Var 1", "--var-b=MAIN.nValue")
Invoke-DotnetCli "tsproj.upsert-element" @("--project-path=$projectPath", "--json-file=$(Join-Path $payloadDir 'generic-element-upsert.json')")
Invoke-DotnetCli "tsproj.upsert-fragment" @("--project-path=$projectPath", "--json-file=$(Join-Path $payloadDir 'generic-fragment-upsert.json')")
Invoke-DotnetCli "tsproj.apply-mutation-plan" @("--project-path=$projectPath", "--json-file=$(Join-Path $payloadDir 'generic-mutation-plan.json')")
if ($IncludeMergeFragment) {
    Write-Host ""
    Write-Host "[WARN] tsproj.merge-fragment is running only for direct CLI smoke coverage. Do not copy this DataTypes smoke fragment into reusable JSON plans when a dedicated primitive exists."
    Invoke-DotnetCli "tsproj.merge-fragment" @(
        "--project-path=$projectPath",
        "--parent-element-name=DataTypes",
        "--xml-file=$(Join-Path $payloadDir 'merge-datatype.xml')",
        "--match-element-name=DataType",
        "--match-name-value=CliVerifyMergedType",
        "--replace-existing=true",
        "--fragment-source=tests integration smoke generated known-good fragment",
        "--target-parent-path=TcSmProject/DataTypes",
        "--field-meaning=DataType Name only; smoke coverage for merge-fragment CLI wiring, not a reusable engineering recipe",
        "--verification-evidence=integration smoke requires XAE reopen or generated evidence before reuse")
} else {
    Skip-Kind "tsproj.merge-fragment" "Skipped by default because merge-fragment is a high-risk escape hatch. Re-run with -IncludeMergeFragment only when the fragment source, parent path, field meanings, and evidence are documented."
}

if ($IncludeAdsRead) {
    Invoke-DotnetCli "validation.ads-scan" @("--net-id=$AdsNetId", "--ports=100,200,300,800,$AdsPort,10000")
    Invoke-DotnetCli "validation.ads-read" @("--net-id=$AdsNetId", "--port=$AdsPort", "--symbol=$AdsSymbol", "--type=$AdsType", "--auto-reconnect=true")
    Invoke-DotnetCli "validation.ads-read-symbols" @("--net-id=$AdsNetId", "--port=$AdsPort", "--symbols=$($AdsSymbol):$($AdsType)", "--auto-reconnect=true")
} else {
    Skip-Kind "validation.ads-scan" "Skipped by default because it needs a running ADS router/runtime. Re-run with -IncludeAdsRead and pass -AdsNetId/-AdsPort."
    Skip-Kind "validation.ads-read" "Skipped by default because it needs a running target and a valid ADS symbol. Re-run with -IncludeAdsRead and pass -AdsNetId/-AdsPort/-AdsSymbol/-AdsType."
    Skip-Kind "validation.ads-read-symbols" "Skipped by default because it needs a running target and valid ADS symbols. Re-run with -IncludeAdsRead and pass -AdsNetId/-AdsPort/-AdsSymbol/-AdsType."
}

$executedUnique = @($script:ranKinds | Select-Object -Unique)
$skippedUnique = @($script:skippedKinds | Select-Object -Unique)
$planned = @($executedUnique + $skippedUnique | Select-Object -Unique)
$missing = @($allKinds | Where-Object { $planned -notcontains $_ })

Write-Host ""
Write-Host "Summary"
Write-Host "  OutputRoot: $OutputRoot"
Write-Host "  Invocations: $($script:ranKinds.Count)"
Write-Host "  Executed:    $($executedUnique.Count) unique kind(s)"
Write-Host "  Skipped:     $($skippedUnique.Count) unique kind(s)"
Write-Host "  Missing:    $($missing.Count)"
Write-Host "  Failed:     $($script:failedKinds.Count)"

if ($skippedUnique.Count -gt 0) {
    Write-Host "  Skipped kinds:"
    $skippedUnique | ForEach-Object { Write-Host "    $_" }
}

if ($missing.Count -gt 0) {
    Write-Host "  Missing kinds:"
    $missing | ForEach-Object { Write-Host "    $_" }
    throw "The script did not cover every known invoke-step kind."
}

if ($skippedUnique.Count -gt 0) {
    throw "Full invoke-step verification skipped one or more kinds. Re-run with the required signing, activation, ADS, and merge-fragment switches."
}

if ($Execute) {
    Write-Host "  Log:        $logPath"
}

if (-not $Execute) {
    Write-Host ""
    Write-Host "Dry-run complete. Re-run with -Execute to run against real TwinCAT/XAE."
}
