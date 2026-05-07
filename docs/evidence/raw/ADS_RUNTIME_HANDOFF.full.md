# ADS Runtime Handoff

Working directory:

`[legacy-workspace-redacted]`

## Goal

Close the final runtime loop after JSON project generation:

1. Generate TwinCAT project from `examples\json-plans\complex-full-project.json`.
2. Build successfully.
3. Activate successfully.
4. Read a PLC symbol through ADS.

Steps 1-3 are currently verified. Step 4 is still failing with ADS port not open.

## Verified JSON / Build / Activation State

Final generated root:

`D:\t\tcak_json_user5`

The final full plan command completed successfully:

```powershell
dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- run-plan --file=examples\json-plans\complex-full-project.json --summary=D:\t\tcak_json_user5\run-plan-summary.json
```

Observed summary:

- `Succeeded: true`
- `Steps: 29`
- `Failed: 0`
- `Skipped: 2`
- `buildBaseRuntime.lastBuildInfo: 0`

Activation command completed successfully:

```powershell
dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- invoke-step --kind=engineering.activate-configuration --solution-path=D:\t\tcak_json_user5\Demo.sln --project-path=D:\t\tcak_json_user5\Demo\Demo.tsproj --save-configuration-archive=true --configuration-archive-path=D:\t\tcak_json_user5\_json_plan_evidence\activated.tszip --visible=false --startup-delay-ms=8000
```

Observed activation output:

- `Status: Succeeded`
- `activationCommand: ITcSysManager.ActivateConfiguration`
- `attemptedCommands: ITcSysManager.ActivateConfiguration | ITcSysManager.StartRestartTwinCAT`
- Evidence: `D:\t\tcak_json_user5\Demo\_Boot\TwinCAT OS (x64)\CurrentConfig.xml`

`CurrentConfig.xml` contains:

- `Create Object Demo_Obj1 (DemoCpp)`
- `Set Object Demo_Obj1 (DemoCpp) to SAFEOP`
- `Set Object Demo_Obj1 (DemoCpp) to OP`

It did not match:

- `1795`
- `invalid indexOffset`
- `AdsError`
- `OfflineCpp01`
- `Inputs.Value`

## Code Changes Already Made

1. `src\TwinCatAutomationKit.TwinCat\TwinCatAutomationKit.TwinCat\TwinCatEngineeringService.cs`
   - `ActivateConfiguration` now tries `ITcSysManager.ActivateConfiguration()` first.
   - DTE/XAE shell commands are fallback only.
   - This fixed the hidden XAE activation command hang.

2. `src\TwinCatAutomationKit.TwinCat\TwinCatAutomationKit.TwinCat\TwinCatStepCatalog.cs`
   - Activation summary updated to match the direct System Manager activation path.
   - `docs/reference` regenerated with `generate-docs`.

3. `examples\json-plans\complex-full-project.json`
   - Current root is `D:\t\tcak_json_user5`.
   - Build timeout is `1800000` ms because first-time TwinCAT build/Create Init Commands can exceed 20 minutes.
   - The disabled `adsRead` step now uses `"net-id": "local"` instead of hard-coding `127.0.0.1.1.1`; this is more portable because this machine's runtime NetId is `10.136.124.106.1.1`.

4. `docs\cli\validation-log.md`
   - Added `CLI-015` documenting hidden DTE activation command blocking.

5. `src\TwinCatAutomationKit.TwinCat\TwinCatAutomationKit.TwinCat\TwinCatAutomationKit.TwinCat.csproj`
   - Added `<UseWindowsForms>true</UseWindowsForms>`.
   - This fixed ADS validation failing before connect with:
     `Could not load file or assembly 'System.Windows.Forms, Version=4.0.0.0'`.

## Current ADS Problem

After adding `UseWindowsForms`, ADS validation reaches the real ADS layer but fails:

```powershell
dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- invoke-step --kind=validation.ads-read --net-id=local --port=851 --symbol=MAIN.nValue --type=Int32 --auto-reconnect=true
```

Result:

`ADS read failed: Port is not open. (AdsErrorCode: 1864, 0x748)`

Also tried:

```powershell
--net-id=127.0.0.1.1.1 --port=851
--net-id=local --port=800
```

Both failed with the same `0x748`.

Additional ADS diagnostics on 2026-04-27:

- Runtime `TcRegistry.xml` reports the runtime AMS NetId as `10.136.124.106.1.1`, not `127.0.0.1.1.1`.
- Retrying with `--net-id=10.136.124.106.1.1` still failed for both ports `851` and `800` with `0x748`.
- Direct PowerShell ADS scans using the repo's `TwinCAT.Ads.dll` failed to connect to every tested local port:
  `1, 10, 11, 12, 100, 200, 300, 350, 360, 800, 851, 852, 10000`.
- `TcSysSrv` is running as PID `3360` and listens on TCP `48898`, UDP `48899`, and TCP `8016`.
- Only the `TcSysSrv` service is installed/running. There is no separate visible `TcAmsSrv` service.
- `C:\ProgramData\Beckhoff\TwinCAT\3.1\Ams\tcsyssrv.ams.sock` exists, owned by `NT SERVICE\TcSysSrv`.
- Local group `TcAmsUsers` exists but currently has no members.
- Current user is `zy_engineer\david`; the shell is not elevated (`Administrators` is deny-only in the token).
- Attempting `Restart-Service -Name TcSysSrv -Force` from this shell failed with:
  `Cannot open 'TcSysSrv' service on computer '.'`

Important conclusion: this is no longer just a PLC symbol problem. ADS cannot register/open any local client connection to the AMS/router path.

Also tested Beckhoff's newer Support-Info-Report ADS v6.3 assemblies:

- `C:\Program Files (x86)\Beckhoff\TwinCAT\Functions\Support-Info-Report\TwinCAT.Ads.dll`
- Forcing `TransportProtocols.TcpIp` still failed before reaching the PLC:
  `Cannot register communication port 'Dynamic' for 'Ads' channel. (Error: ClientPortNotOpen, ConnectPortFailed).`

This points at local AMS access/router/client-port registration state or permissions, not `MAIN.nValue`.

After the user restarted `TcSysSrv` from an elevated PowerShell:

```powershell
Restart-Service -Name TcSysSrv -Force
Start-Sleep -Seconds 8
sc.exe queryex TcSysSrv
```

The service came back as PID `35568`. Running ADS read from the user's elevated shell changed the failure from `0x748 Port is not open` to:

`Target port could not be found. (AdsErrorCode: 6, 0x6)`

Interpretation: the AMS/router/client-port layer is now reachable from that elevated session, but target port `851` is not currently open in the runtime after the service restart. The next step is to re-run `engineering.activate-configuration` from the same elevated shell, then retry ADS.

Attempting that activation from this agent's non-elevated tool shell timed out/finally failed with:

`Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.`

That attempt did not create `activated-after-service-restart.tszip`, and `CurrentConfig.xml` timestamp stayed at `2026-04-27 14:35:54`.

## Important Findings

`D:\t\tcak_json_user5\Demo\Demo.tsproj` has two PLC instances:

- `PlcA Instance`
  - `AmsPort="851"`
  - Context bound to `PlcTask`
- `PlcInst01`
  - `AmsPort="800"`
  - mappings target this instance

Boot files exist under:

`D:\t\tcak_json_user5\Demo\_Boot\TwinCAT OS (x64)\Plc`

including:

- `Port_851.app`
- `Port_851.autostart`
- `Port_851_boot.tizip`
- `Port_851.json`

`CurrentConfig.xml` contains PLC object creation and symbol download entries, including ports `800` and `851`, but ADS read still reports the port is not open.

Current suspicion:

- `ITcSysManager.StartRestartTwinCAT()` returns and `IsTwinCATStarted()` is true, but the local TwinCAT target may not be fully in RUN mode or the PLC ADS server is not open.
- `ITcSmCommands` exposes useful methods:
  - `ReSetToRunMode()`
  - `ReSetToConfigMode()`
  - `GetTargetRealTimeStatus()`
  - `ActivateConfiguration2()`
- Need verify whether explicitly switching target to RUN opens PLC ADS port 851.

## Interrupted Command / Process Note

The user interrupted an attempted PowerShell command that instantiated `TCatSysManagerLib.TcSmCommandsClass` and called `ReSetToRunMode()`.

After interruption, process listing showed several `pwsh` processes around `2026-04-27 14:46:10`. Before killing anything, inspect command lines and only stop processes that are clearly from this diagnostic command.

Suggested inspection:

```powershell
Get-CimInstance Win32_Process -Filter "Name='pwsh.exe' or Name='powershell.exe'" |
  Select-Object ProcessId,ParentProcessId,CreationDate,CommandLine
```

## Next Steps

1. From an elevated PowerShell, check whether restarting `TcSysSrv` refreshes the AMS socket/router:
   `Restart-Service -Name TcSysSrv -Force; Start-Sleep -Seconds 8; sc.exe queryex TcSysSrv`
2. After service restart, re-run activation from the same elevated PowerShell:
   `dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- invoke-step --kind=engineering.activate-configuration --solution-path=D:\t\tcak_json_user5\Demo.sln --project-path=D:\t\tcak_json_user5\Demo\Demo.tsproj --save-configuration-archive=true --configuration-archive-path=D:\t\tcak_json_user5\_json_plan_evidence\activated-after-service-restart.tszip --visible=false --startup-delay-ms=8000`
3. After activation succeeds, retry:
   `dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- invoke-step --kind=validation.ads-read --net-id=10.136.124.106.1.1 --port=851 --symbol=MAIN.nValue --type=Int32 --auto-reconnect=true`
4. If ADS returns to `0x748`, add `zy_engineer\david` to local group `TcAmsUsers`, sign out/in or restart the shell/session, then retry ADS. Command needs elevation:
   `net localgroup TcAmsUsers zy_engineer\david /add`
5. Query `ITcSmCommands.GetTargetRealTimeStatus()` only after ADS client-port registration works. The previous direct `TcSmCommandsClass` creation failed/not reliable.
6. Once ADS client-port registration works, if only PLC 851 still fails, investigate whether the JSON plan should:
   - use only the wizard-generated `PlcA Instance` on port 851 instead of creating `PlcInst01`, or
   - set `PlcInst01` to a normal PLC port and ensure it gets real boot/autostart state, or
   - add a dedicated CLI step to force target RUN mode and verify PLC port availability.

## Test Status

Last self-hosted test run:

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests.csproj
```

Result:

`All 180 tests passed.`

There are NU1900 warnings because the sandbox cannot reach `https://api.nuget.org/v3/index.json`; they did not block build/test.

## 2026-04-27 15:44 +08 ADS Retest From Agent Shell

After the user's elevated reactivation succeeded for:

- `D:\t\tcak_json_user5\Demo.sln`
- `D:\t\tcak_json_user5\Demo\Demo.tsproj`

the agent shell retried the requested ADS read:

```powershell
dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- invoke-step --kind=validation.ads-read --net-id=local --port=851 --symbol=MAIN.nValue --type=Int32 --auto-reconnect=true
```

Result from the agent shell:

```text
Status: Failed
Result: ADS read failed: Port is not open. (AdsErrorCode: 1864, 0x748)
```

This differs from the user's elevated PowerShell result after `TcSysSrv` restart, which had reached:

```text
Target port could not be found. (AdsErrorCode: 6, 0x6)
```

Permission checks from the same agent shell:

- `whoami /user`: `zy_engineer\david`
- `whoami /groups`: token is `Mandatory Label\Medium Mandatory Level`
- `BUILTIN\Administrators`: `Group used for deny only`
- `TcAmsUsers`: group exists but still has no members

The agent attempted the recommended admin mutation:

```powershell
net localgroup TcAmsUsers zy_engineer\david /add
```

but Windows returned:

```text
System error 5 has occurred.
Access is denied.
```

Conclusion: the current agent tool shell cannot prove the post-activation PLC port state because it cannot register/open the local ADS client port. The `0x748` observed here is consistent with this non-elevated session and empty `TcAmsUsers`, not necessarily with the current runtime state. Continue the runtime diagnosis from an elevated PowerShell, or first add `zy_engineer\david` to `TcAmsUsers` from an elevated shell and start a fresh shell/session before retrying ADS.

## 2026-04-27 16:01 +08 Beckhoff / RTCommToolkit ADS Port Check

The Beckhoff ADS low-level C/C++ API opens a local ADS/router client port explicitly with `AdsPortOpen()` / `AdsPortOpenEx()`, then sends requests with that local port handle and a target `AmsAddr`.

`D:\2nd_year\rtcomm0105_sync\RTCommToolkit` follows that model:

- `ads_middleware\include\ads_middleware\AdsBase.hh`
  - calls `AdsPortOpen()` when `USE_TWINCAT_ROUTER` is defined
  - otherwise calls `AdsPortOpenEx()`
  - throws if `local_port <= 0`
  - closes with `AdsPortClose()` / `AdsPortCloseEx(local_port)`
- `ads_middleware\test\TestAdsVar.cc`
  - calls `AdsPortOpen()`
  - fills `AmsAddr.netId`
  - sets `pAddr->port`
  - calls `AdsSyncReadWriteReq(...)`

The current repository uses Beckhoff's .NET ADS wrapper:

```csharp
using TcAdsClient client = new();
client.Connect(request.Port);
```

or:

```csharp
client.Connect(AmsNetId.Parse(request.NetId), request.Port);
```

That .NET `Connect(...)` is the wrapper-level equivalent of opening/registering the local ADS client port and binding it to the target ADS port. There is no separate public `AdsPortOpen()` call in the current .NET path.

One .NET-console-specific fix was applied:

```csharp
client.Synchronize = false;
```

in `src\TwinCatAutomationKit.TwinCat\TwinCatAutomationKit.TwinCat\AdsValidationService.cs` before `Connect(...)`. This aligns the console client with Beckhoff's .NET sample pattern and also avoids the Windows Forms synchronization path that previously caused a `System.Windows.Forms` assembly dependency.

Verification after this change:

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests.csproj
```

Result:

```text
All 180 tests passed.
```

Retried ADS read:

```powershell
dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- invoke-step --kind=validation.ads-read --net-id=local --port=851 --symbol=MAIN.nValue --type=Int32 --auto-reconnect=true
```

Result still:

```text
ADS read failed: Port is not open. (AdsErrorCode: 1864, 0x748)
```

Conclusion from the API comparison: the CLI was already calling the .NET API that opens/registers the ADS client port (`TcAdsClient.Connect`). Adding the console synchronization setting did not change the failure. The remaining `0x748` in this agent shell is therefore not explained by a missing application-level `AdsPortOpen` call; it is still consistent with the current process/session being unable to open/register an ADS client port with the local TwinCAT router.

## 2026-04-27 17:27 +08 Runtime Root Cause Found

The agent received permission to run elevated diagnostics and moved the failure past the AMS/router layer.

Elevated ADS repair:

- `TcAmsUsers` already contained `david`.
- Elevated token was `High Mandatory Level`.
- `TcSysSrv` restarted successfully.
- Elevated ADS read changed from `0x748` to:

```text
ADS read failed: Target port could not be found. (AdsErrorCode: 6, 0x6)
```

Elevated reactivation of the current target project succeeded:

```text
Status: Succeeded
activationCommand: ITcSysManager.ActivateConfiguration
attemptedCommands: ITcSysManager.ActivateConfiguration | ITcSysManager.StartRestartTwinCAT
CurrentConfig timestamp: 2026-04-27 16:09:23
```

But elevated ADS scans showed:

- ADS/router works.
- `100`: `TCatSysSrv`
- `200`: `RTime(Um)`
- `300`: `I/O Server`
- `10000`: `TwinCAT System`
- `800`, `851`, `852`: `Target port could not be found (0x6)`

The agent added `tools\AdsProbe` as a local diagnostic helper to call Beckhoff .NET ADS APIs directly:

- `ReadState`
- `ReadDeviceInfo`
- `TryWriteControl(AdsState.Run, deviceState)`

`TryWriteControl` could set:

- port `100` to Run
- port `200` to Run
- port `300` to Run

but PLC ports `800`, `851`, and `852` still did not exist. Port `10000` remained `AdsState=Config, deviceState=1`, and `TryWriteControl` on `10000` returned `DeviceServiceNotSupported`.

The missing PLC ports are now explained by the Windows Application event log from `TcSysSrv`. Each attempted TwinCAT system start reports:

```text
License Violation: License 'TC3 C++' not found,
LicenseId = {304D006A-8299-4560-AB79-438534B50288},
Comment 'DemoCpp!CDemoCpp'
```

and then:

```text
Sending ams command >> Init10\IO: Create TComObj PREOP:
Create Object Demo_Obj1 (DemoCpp) >>
AdsError: 1795 (0x703, ADS ERROR: invalid indexOffset) << failed!
```

The same start sequence ends with:

```text
TwinCAT system start completed. AdsState: >15<
```

This means the active JSON project is not reaching a healthy RUN state because the generated C++ module instance requires the TC3 C++ runtime license. PLC ADS port `851` is missing as a downstream effect of TwinCAT aborting/rolling back the runtime start during C++ object creation.

Additional checks:

- `TcSystemSrv.TcSystemServer.StartSystem()` and `.RestartSystem()` were called successfully from elevated PowerShell, but `SystemState` stayed `15` and PLC ports remained absent because the same C++ license failure occurs during startup.
- DTE command enumeration found the localized TwinCAT menu command IDs:
  - `4357`: activate configuration
  - `4358`: restart TwinCAT system
  - `4359`: restart TwinCAT config mode
- Raising command `4358` by GUID/ID returned but did not open PLC ports.
- Raising command `4357` reproduced the known hidden XAE activation hang and its residual process was stopped.

Current conclusion:

The remaining runtime verification blocker is no longer ADS client setup, not `AdsPortOpen`, and not the PLC symbol path. The blocker is that the current JSON runtime project includes `DemoCpp`, and this runtime does not have a valid `TC3 C++` license. Keeping the C++ module and C++ mappings is still the right target for the JSON runtime showcase; ADS readback should not be the required runtime proof for this scenario.

Revised validation target:

1. Keep the C++ project, module instance, pointer plan, and C++/PLC mappings.
2. Do not use ADS as the runtime validation mechanism.
3. Generate a PLC `MAIN` with directly observable variables:
   - `MAIN.nSeed`
   - `MAIN.nStage1`
   - `MAIN.nExpectedStage1`
   - `MAIN.bPipelineOk`
   - `MAIN.nMismatchCount`
   - `MAIN.bHeartbeat`
4. Map `MAIN.nSeed -> Demo_Obj1.Data^DataIn`.
5. Map `Demo_Obj1.Data^DataOut -> MAIN.nStage1`.
6. With `Demo_Obj1.Parameter.data1 = 123`, online verification is:
   - change `MAIN.nSeed` in XAE Online view
   - expect `MAIN.nStage1 = MAIN.nSeed + 123`
   - expect `MAIN.nExpectedStage1` to match `MAIN.nStage1`
   - expect `MAIN.bPipelineOk = TRUE`
   - expect `MAIN.bHeartbeat` to toggle

This still requires a valid `TC3 C++` runtime license to actually reach RUN, because the mapped C++ object must be created by the TwinCAT runtime. Without that license, activation/start continues to stop before the observable mapping chain can run.

## 2026-04-27 18:35 +08 JSON Plan Revised For Non-ADS Manual Runtime Check

`examples\json-plans\complex-full-project.json` was updated so the complex JSON plan remains a C++/PLC/mapping runtime showcase but no longer depends on ADS readback for proof.

Changes:

- `includeAdsRead` remains `false`.
- The plan now writes `Demo\PlcA\POUs\MAIN.TcPOU` after `engineering.create-plc-project` by using a delayed file payload with a step-output token in the file content.
- PLC variables were changed to `UDINT` to match the generated C++ module's `DataIn` / `DataOut` symbols.
- Mapping is now:
  - `PlcTask Outputs^MAIN.nSeed -> Data^DataIn`
  - `Data^DataOut -> Inputs^MAIN.nStage1`
- Manual online check is now `nStage1 = nSeed + 123`.

Dry-run validation:

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=.\examples\json-plans\complex-full-project.json --dry-run=true
```

Result:

```text
Plan: complex-full-project
Mode: dry-run
Steps: 31
Skipped: 2
Failed: 0
```

Only NU1900 warnings were emitted because NuGet vulnerability metadata could not be fetched from `https://api.nuget.org/v3/index.json`.

## 2026-04-27 19:05 +08 Build-Time TwinCAT Signing License Interface Added

The JSON runtime plan now has an explicit interface step for the TwinCAT C++ signing settings requested for the C++ runtime path.

New public step:

```text
signing.set-license
TwinCatSigningService.SetLicense
```

Behavior:

- Resolves the C++ `.vcxproj` from `project-path` + `cpp-project-name`, or accepts an explicit `cpp-project-file-path` / `vcxproj-path`.
- Writes a single `<PropertyGroup Label="TcSign">` into the C++ project.
- Removes duplicate old signing properties from other property groups.
- Writes:
  - `TcSignTwinCat=true`
  - `TcSignTwinCatCertName=<license-name>`
  - `TcSignTwinCatCertPW=<password>`
- `run-plan` now redacts password/secret/token option values in console and summary output while still passing the real value to step execution.

The JSON plan now passes:

```json
"signingLicenseName": "optcnc",
"signingLicensePassword": "123"
```

and runs this step before `engineering.build-solution`:

```json
{
  "id": "setTwinCatSigningLicense",
  "kind": "signing.set-license",
  "options": {
    "cpp-project-name": "${cppProjectName}",
    "license-name": "${signingLicenseName}",
    "password": "${signingLicensePassword}",
    "enable-signing": true
  }
}
```

This is the same setting pattern that produces TwinCAT build output like:

```text
TcSignTool.exe sign ... /f "C:\ProgramData\Beckhoff\TwinCAT\3.1\CustomConfig\Certificates\optcnc.tccert" /p "123" /q
```

Validation:

```powershell
dotnet build .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -v minimal
dotnet run --project .\tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests.csproj
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- generate-docs
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=.\examples\json-plans\complex-full-project.json --dry-run=true
```

Results:

```text
Build succeeded.
All 182 tests passed.
run-plan dry-run: Steps 31, Skipped 2, Failed 0
```

Only NU1900 warnings were emitted because NuGet vulnerability metadata could not be fetched.

The interface was also applied to the current generated target:

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=signing.set-license --project-path=D:\t\tcak_json_user5\Demo\Demo.tsproj --cpp-project-name=DemoCpp --license-name=optcnc --password-env-var=TCAK_SIGNING_PASSWORD --enable-signing=true
```

Result:

```text
Status: Succeeded
projectFilePath: D:\t\tcak_json_user5\Demo\DemoCpp\DemoCpp.vcxproj
licenseName: optcnc
passwordWritten: true
```

Confirmed in `D:\t\tcak_json_user5\Demo\DemoCpp\DemoCpp.vcxproj`:

```xml
<PropertyGroup Label="TcSign">
  <TcSignTwinCat>true</TcSignTwinCat>
  <TcSignTwinCatCertName>optcnc</TcSignTwinCatCertName>
  <TcSignTwinCatCertPW>123</TcSignTwinCatCertPW>
</PropertyGroup>
```

Direct signature verification was run after rebuilding the current target:

```powershell
& 'C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\SDK\Bin\TcSignTool.exe' verify 'D:\t\tcak_json_user5\Demo\DemoCpp\_products\TwinCAT OS (x64)\Release\DemoCpp.tmx'
```

Result:

```text
File 'D:\t\tcak_json_user5\Demo\DemoCpp\_products\TwinCAT OS (x64)\Release\DemoCpp.tmx' has signature.
   issuer optcnc (optcnc@abc.com), certificate expires on 04/16/2028
Warning: Signature found, but OEM certificate was not signed by Beckhoff. Driver can only be used in test mode.
```

The command exits with code 1 because the certificate is not Beckhoff-signed. The important distinction is:

- The new `signing.set-license` interface correctly writes the build-time TwinCAT C++ signing settings and the rebuilt `.tmx` is signed with `optcnc`.
- That does not prove a production C++ runtime license is installed. If activation still reports a TC3 C++ license violation, the next runtime step is to run in TwinCAT test mode or install/activate the Beckhoff-signed runtime license.

## 2026-04-27 19:40 +08 Manual XAR Activation ADS 1795 Diagnosis

Manual / CLI activation of `D:\t\tcak_json_user5\Demo.sln` still produces the visible ADS 1795 activation error, but the Windows Application log shows the real failing layer:

```text
TwinCAT System: Source: License Server
License Violation: License 'TC3 C++' not found,
LicenseId = {304D006A-8299-4560-AB79-438534B50288},
Comment 'DemoCpp!CDemoCpp'

Sending ams command >> Init10\IO: Create TComObj PREOP:
Create Object Demo_Obj1 (DemoCpp) >> AdsError: 1795
(0x703, ADS ERROR: invalid indexOffset) << failed!

TwinCAT System: Source: TCOM Server
TcSetObjPara failed ('Demo_Obj1 (DemoCpp)' OID 0x01010010)
hr = 0x98110703, PID = 0x03002103, LEN = 4
```

Conclusion:

- ADS/AMS is always present during TwinCAT activation because the runtime uses it internally to create/configure TcCOM objects.
- The current `1795` is a symptom emitted while creating `Demo_Obj1 (DemoCpp)`.
- The preceding License Server entry is the root cause: the machine/runtime does not currently have an accepted `TC3 C++` runtime license for `DemoCpp!CDemoCpp`.
- PLC boot artifacts are present, including `Port_851.app` and `Port_851.autostart`; this is not the old “PLC 851 port not open because PLC never loaded” diagnosis.
- The JSON plan/build/signing path is working. Runtime activation is blocked by TwinCAT C++ license/test-mode state.

## 2026-04-27 19:52 +08 TC1300 Trial Present, Remaining ADS 1795 Is TraceLevelMax Parameter

After the TC1300 / `TC3 C++` trial license was visible in XAE, the latest activation logs changed. The earlier explicit license violation disappeared; the remaining failure is:

```text
TwinCAT System: Source: TCOM Server
TcSetObjPara failed ('Demo_Obj1 (DemoCpp)' OID 0x01010010)
hr = 0x98110703, PID = 0x03002103, LEN = 4

Sending ams command >> Init10\IO: Create TComObj PREOP:
Create Object Demo_Obj1 (DemoCpp) >> AdsError: 1795
(0x703, ADS ERROR: invalid indexOffset) << failed!
```

`PID = 0x03002103` maps directly to this TMC parameter:

```xml
<Parameter HideParameter="true">
  <Name>TraceLevelMax</Name>
  <PTCID>#x03002103</PTCID>
</Parameter>
```

The JSON plan had been writing `TraceLevelMax = tlAlways` as a parameter value. That value is not needed for runtime validation and the target runtime rejects it during C++ object creation. The JSON plan was updated to stop writing `TraceLevelMax`; it now only writes the user parameter `Parameter.data1`.

Next validation command:

```powershell
dotnet run --project ".\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj" -- run-plan --file=".\examples\json-plans\complex-full-project.json" --summary="D:\t\tcak_json_user5\_json_plan_evidence\manual-run-summary.json"
```

Then activate the regenerated project. Do not just reactivate the existing `Demo.tsproj` if it still contains:

```xml
<Name>TraceLevelMax</Name>
```

under `ParameterValues`; regenerate or remove that stale parameter first.

## 2026-04-27 20:18 +08 Existing Solution Was Stale/Empty, ParameterValues Fixed In Current Tsproj

The latest `1795` still reported the same PID:

```text
TcSetObjPara failed ('Demo_Obj1 (DemoCpp)' OID 0x01010010)
hr = 0x98110703, PID = 0x03002103, LEN = 4
```

Inspection showed why it persisted: although the JSON payload no longer contained `TraceLevelMax`, the already-existing `D:\t\tcak_json_user5\Demo\Demo.tsproj` still had the old value under `ParameterValues`. `ApplyInstanceParameterPlan` is intentionally an upsert operation, so it did not delete stale values from previous runs.

Fix added:

- New public step: `tsproj.clear-instance-parameter-values`
- Method: `TwinCatTsprojMutationService.ClearInstanceParameterValues`
- JSON plan now runs `clearCppParameterValues` before `applyParameterPlan`.
- Tests pass: `All 184 tests passed.`

Current target repair applied directly:

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.clear-instance-parameter-values --project-path="D:\t\tcak_json_user5\Demo\Demo.tsproj" --instance-name="Demo_Obj1 (DemoCpp)"

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.apply-instance-parameter-plan --project-path="D:\t\tcak_json_user5\Demo\Demo.tsproj" --json-file="D:\t\tcak_json_user5\_json_plan_payloads\parameter-plan.json"
```

Current `ParameterValues` state is now correct:

```xml
<ParameterValues>
  <Value>
    <Name>Parameter.data1</Name>
    <Value>123</Value>
  </Value>
</ParameterValues>
```

The remaining `<Name>TraceLevelMax</Name>` in the file is only the TMC parameter definition, not an assigned value. That is expected and should not be removed from the module description.

Important operational note: `D:\t\tcak_json_user5\Demo.sln` was later found to be an empty Visual Studio solution with no `Demo\Demo.tsproj` project reference. This can happen after manually opening/loading the TwinCAT project and saving the solution incorrectly. If XAE is already open, it may also keep the stale project in memory. Close XAE and reopen the fixed `D:\t\tcak_json_user5\Demo\Demo.tsproj` directly, or regenerate into a clean new root before activating again.
