# 2026-05-09 OptCNC TMC Shape Guard / OptCNC TMC 形状防线

English summary: The existing `D:\3rd_year\auto_sln` output still had fallback C++ TMC DataAreas (`Input/DataIn`, `Output/DataOut`). This note records the diagnosis, the public structured TMC/TmcDesc repair steps, and the runner evidence that the OptCNC MotionControl module shape and array DataPointerValues now match the sample.

本文记录对 `D:\3rd_year\auto_sln` 现有输出的复盘。结论是：源码 payload 已写入，但 `MotionControl.tmc` 仍是 fallback skeleton，因此 C++ module 的 input/output 名称、类型和数量与样例不一致。修复方向不是复制样例 `.tmc` 或手写 XML，而是让 JSON plan 先执行 TwinCAT C++ TMC code generator / publish，再用 public validation step 断言生成结果。

## Diagnosis

目标样例：

```text
D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\MotionControl\MotionControl.tmc
```

现有输出：

```text
D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\MotionControl.tmc
```

关键差异：

- 样例 `.tmc` 大小约 `95621` bytes；现有输出 `.tmc` 大小约 `20367` bytes。
- 样例 `.tmc` 的 modules 是源码注解生成的 `ModuleSimDriver`、`ModuleAxesGroup`、`ModuleBeckhoffDriver`、`ModuleAxis`、`ModuleCommandsExecuter`。
- 现有输出多了 `MotionControl` skeleton module。
- 现有输出每个 module 都只有 `Input/DataIn:UDINT` 和 `Output/DataOut:UDINT`，不是目标样例的 `Outputs/Inputs/Data/Debug` 与具体 symbol 类型。

目标 shape 抽样：

```text
ModuleSimDriver:
  Outputs: DriverOutputs:DriverRxType, ExternalBrakeReleased:BOOL
  Inputs: DriverInputs:DriverTxType, ExternalBrakeRelease:BOOL
ModuleAxesGroup:
  Outputs: []
  Inputs: []
  Debug: Data:INT
  Data: StateData:AxesGroupStateDataType, InterpolationData:AxesGroupInterpolationDataType, CycleCounter:UDINT, ProcessData:AxesGroupProcessDataType
ModuleBeckhoffDriver:
  Outputs: DriverOutputs:DriverRxType, DriverIOOut:DriverIOOutType, Power:BIT
  Inputs: DriverInputs:DriverTxType, DriverIOIn:DriverIOInType
```

现有输出 shape：

```text
MotionControl:
  Input: DataIn:UDINT
  Output: DataOut:UDINT
ModuleSimDriver:
  Input: DataIn:UDINT
  Output: DataOut:UDINT
...
```

## Code and JSON changes

新增 public step：

```text
engineering.verify-tmc-data-areas
```

接口：

```text
TwinCatEngineeringService.VerifyTmcDataAreas
```

用途：

- 只读 `.tmc`。
- 验证 module、DataArea、AreaType、symbol name/type。
- 在 `engineering.add-module-instance` 之前失败，避免 fallback skeleton 继续污染实例、mapping、parameter 后续步骤。

OptCNC JSON plan 更新：

- `t\optcnc-auto-sln-from-kit.json`
- 新增 payload：`${payloadDir}\expected-motion-tmc-data-areas.json`
- 新增 step：`verifyMotionTmcDataAreas`
- 后续改为结构化 TMC 路径：`engineering.apply-tmc-module-model` -> `engineering.verify-tmc-data-areas` -> `tsproj.refresh-cpp-instance-tmc-desc` -> `tsproj.apply-instance-data-pointer-plan`。
- 旧的 `engineering.start-tmc-code-generator` 和 `engineering.publish-modules` 在主 OptCNC plan 中显式 disabled，避免再次生成 fallback skeleton 覆盖源码片段。
- `tsproj.apply-instance-data-pointer-plan` 扩展了可选 `ArrayIndex`，用于写目标样例里的 `<Data ArrayIndex="...">` 数组 data pointer 值。

当前 plan shape：

- steps: `439`
- files: `113`
- `applyMotionTmcModuleModel` 先于 `verifyMotionTmcDataAreas`
- `refreshMotionInstanceTmcDesc` 在 MotionControl C++ instance 创建后刷新 8 个实例的 `TmcDesc`
- `applyMotionDataPointerValues` 写入 `AxesGroup0` / `CommandsExecuter` 的 8 条数组 data pointer 值
- `disableBeckhoffDriver1` / `disableAxis0` 同步目标样例的 C++ instance `Disabled=true`
- `engineering.create-module` 的 `allow-offline-fallback=false`
- 没有 `merge-fragment`、`tsproj.upsert-*`、复制/clone project file 的 step。

## Verification

代码构建：

```powershell
dotnet build .\TwinCatAutomationKit.sln --no-restore /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
```

结果：成功，`0` errors。当前非交互 shell 对若干 `obj\...\*.AssemblyReference.cache` 写入报 `MSB3101` warning；这是本 shell 的文件写入/cache 限制，不是编译错误。

生成文档：

```powershell
dotnet .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\bin\Debug\net8.0-windows\TwinCatAutomationKit.Cli.dll generate-docs
```

结果：`docs\reference` regenerated。

dry-run：

```powershell
dotnet .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\bin\Debug\net8.0-windows\TwinCatAutomationKit.Cli.dll run-plan --file=t\optcnc-auto-sln-from-kit.json --dry-run=true --summary=.artifacts\optcnc-tmc-shape-guard-dry-run-summary.json
```

结果：

- Steps: `434`
- Failed: `0`
- Skipped: `2`
- Summary: `.artifacts\optcnc-tmc-shape-guard-dry-run-summary.json`

同一份期望 payload 对目标样例通过：

```powershell
dotnet .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\bin\Debug\net8.0-windows\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.verify-tmc-data-areas --project-tmc-path=D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\MotionControl\MotionControl.tmc --json-file=.\.artifacts\expected-motion-tmc-data-areas.json --raw-output=true
```

结果：

```text
Status: Succeeded
Result: TMC data areas matched 5/5 expected module(s).
```

同一份期望 payload 对现有坏输出失败：

```powershell
dotnet .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\bin\Debug\net8.0-windows\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.verify-tmc-data-areas --project-tmc-path=D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\MotionControl.tmc --json-file=.\.artifacts\expected-motion-tmc-data-areas.json --raw-output=true
```

结果：

```text
Status: Failed
Result: TMC data area verification failed with 15 error(s).
```

核心错误包括：

- `ModuleSimDriver` missing `Outputs` / `Inputs`
- `ModuleAxesGroup` missing `Outputs` / `Inputs` / `Debug` / `Data`
- `ModuleBeckhoffDriver` missing `Outputs` / `Inputs`
- `ModuleAxis` missing `Outputs` / `Inputs` / `Data`
- `ModuleCommandsExecuter` missing `Inputs` / `Outputs` / `Data`
- unexpected module `MotionControl`

## Real XAE limitation in this shell

尝试直接对现有 `D:\3rd_year\auto_sln` 执行：

```powershell
dotnet .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\bin\Debug\net8.0-windows\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.start-tmc-code-generator --solution-path=D:\3rd_year\auto_sln\OptcncTwinCAT.sln --project-path=D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj --cpp-project-name=MotionControl --post-start-delay-ms=5000 --wait-for-updated-tmc-timeout-ms=60000
```

结果：

```text
Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.
```

因此本轮没有声称已在当前 shell 中修复现有 `D:\3rd_year\auto_sln`。真实重跑/修复仍需要在交互桌面 runner 中执行 updated JSON plan，或由人工打开 XAE 后触发同等 public steps。当前改动的可验证结论是：坏 `.tmc` 现在会被 public step 明确拦截，目标样例 `.tmc` 会通过同一断言。

## Structured repair evidence

后续在 `interactive-runner` 中执行结构化修复，没有复制样例 `.tmc/.tsproj/.sln/.vcxproj/.filters`，也没有使用 XML fragment/bulk insert。允许复制的是 C++ 源码文件内容，且通过 `cpp.write-project-item-content` 作为项目 item 内容写入。

修复新增/使用的 public step：

- `engineering.apply-tmc-module-model`：用 JSON module model 写 `MotionControl.tmc` 的 module、DataArea、parameter、interface pointer、data pointer 和 event class。
- `tsproj.refresh-cpp-instance-tmc-desc`：从项目 `.tmc` 刷新现有 8 个 C++ instance 的 `TmcDesc`，保留 task context 和 value sections。
- `tsproj.apply-instance-data-pointer-plan`：通过 `ArrayIndex` 写入数组 `DataPointerValues`。
- `tsproj.set-cpp-instance-metadata`：写 C++ instance-level metadata，本轮用于同步目标样例的 `BeckhoffDriver1` / `Axis0` `Disabled=true`。

runner 修复源码和 TMC：

- request id: `optcnc-incremental-repair-20260509-125058`
- exitCode: `0`
- stdout: `.artifacts\interactive-runner\logs\optcnc-incremental-repair-20260509-125058.stdout.log`
- summary: `.artifacts\optcnc-incremental-repair-20260509-125058-summary.json`
- result: `17` steps succeeded, `0` failed

runner 刷新 TMC/TmcDesc：

- request id: `optcnc-refresh-tmcdesc-20260509-125700`
- exitCode: `0`
- stdout: `.artifacts\interactive-runner\logs\optcnc-refresh-tmcdesc-20260509-125700.stdout.log`
- summary: `.artifacts\optcnc-refresh-tmcdesc-20260509-125700-summary.json`
- result: `4` steps succeeded, `0` failed
- `engineering.verify-tmc-data-areas`: `TMC data areas matched 5/5 expected module(s).`
- `tsproj.refresh-cpp-instance-tmc-desc`: `Refreshed 8 C++ instance TmcDesc item(s).`

runner 写数组 DataPointerValues 和 disabled metadata：

- request id: `optcnc-refresh-tmcdesc-datapointers-disabled-20260509-132644`
- exitCode: `0`
- stdout: `.artifacts\interactive-runner\logs\optcnc-refresh-tmcdesc-datapointers-disabled-20260509-132644.stdout.log`
- summary: `.artifacts\optcnc-refresh-tmcdesc-datapointers-disabled-20260509-132644-summary.json`
- result: `7` steps succeeded, `0` failed
- `tsproj.apply-instance-data-pointer-plan`: `Instance data pointer plan applied with 8 item(s).`
- `tsproj.set-cpp-instance-metadata`: `BeckhoffDriver1` and `Axis0` updated.

runner 修复 `MotionControl` include paths 并验证 build：

- request id: `optcnc-fix-motioncontrol-include-paths-20260509-include-fix`
- exitCode: `0`
- stdout: `.artifacts\interactive-runner\logs\optcnc-fix-motioncontrol-include-paths-20260509-include-fix.stdout.log`
- summary: `.artifacts\optcnc-fix-motioncontrol-include-paths-20260509-include-fix-summary.json`
- result: `21` steps succeeded, `0` failed
- change: only `cpp.set-item-definition-property` and `engineering.save-all`; no source payloads and no `.tsproj/.tmc` mutation in this plan.
- verified: every `MotionControl.vcxproj` `ItemDefinitionGroup` now includes `$(ProjectDir)AxesGroup`, `$(ProjectDir)Utilities`, and `$(ProjectDir)CommandsExecuter`.

runner build:

- request id: `optcnc-build-generated-solution-20260509-after-include-fix`
- exitCode: `0`
- stdout: `.artifacts\interactive-runner\logs\optcnc-build-generated-solution-20260509-after-include-fix.stdout.log`
- summary: `.artifacts\optcnc-build-generated-solution-20260509-after-include-fix-summary.json`
- result: `engineering.build-solution` succeeded, `lastBuildInfo=0`.

runner signing/build/activation/ADS scan:

- request id: `optcnc-sign-build-activate-20260509`
- exitCode: `0`
- response: `.artifacts\interactive-runner\responses\optcnc-sign-build-activate-20260509.response.json`
- stdout: `.artifacts\interactive-runner\logs\optcnc-sign-build-activate-20260509.stdout.log`
- summary: `.artifacts\optcnc-sign-build-activate-20260509-summary.json`
- result: `6` steps succeeded, `0` failed.
- `signing.set-license`: wrote `TcSignTwinCat=true`, `TcSignTwinCatCertName=optcnc`, and `TcSignTwinCatCertPW=123` to `D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\MotionControl.vcxproj`.
- `engineering.build-solution`: succeeded, `lastBuildInfo=0`.
- `signing.sign-twincat-binary`: signed `D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\_products\TwinCAT OS (x64)\Release\MotionControl.tmx` with `C:\ProgramData\Beckhoff\TwinCAT\3.1\CustomConfig\Certificates\optcnc.tccert`, exitCode `0`.
- `signing.verify-twincat-binary`: verification returned exitCode `2` and was accepted as the known TcSignTool test-mode certificate warning (`allow-test-mode-warning=true`).
- `engineering.activate-configuration`: succeeded through `ITcSysManager.ActivateConfiguration`; evidence written to `D:\3rd_year\auto_sln\_json_plan_evidence\activated-after-signing.tszip` and `D:\3rd_year\auto_sln\OptcncTwinCAT\_Boot\TwinCAT OS (x64)\CurrentConfig.xml`.
- `validation.ads-scan`: ports `100`, `200`, `300`, and `10000` were reachable; ports `800`, `851`, and `852` returned ADS `0x6` target-port-not-found.
- ADS state after scan: port `10000` reported `Config`, so this is activation evidence plus port reachability, not proof that the local runtime is in `Run` or that a PLC runtime exists.

ADS error note:

- Local Beckhoff headers define `1792 = 0x700 = ADSERR_DEVICE_ERROR`: `C:\Program Files (x86)\Beckhoff\TwinCAT\AdsApi\TcAdsDll\Include\TcAdsDef.h` and `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\SDK\Include\AdsErrorCodes.h`.
- The later SAFEOP `1792` investigation found the actionable cause in task layout, not the ADS code text itself. See [2026-05-09 OptCNC SAFEOP 1792 Task Layout](2026-05-09-optcnc-safeop-1792-task-layout.md). The reusable rule is: generated OptCNC ADT tasks must not keep task process-image `Vars` / `Image` nodes.

最终核查：

```text
MotionControl.tmc:
  engineering.verify-tmc-data-areas => TMC data areas matched 5/5 expected module(s).

OptcncTwinCAT.tsproj:
  IAxisInterface correct GUID count: 6
  IAxisFSMInterface correct GUID count: 3
  IAxesGroupFSMInterface correct GUID count: 3
  ICommandsExecuterInterface correct GUID count: 1
  known wrong GUID/interface combinations: 0
  DataPointerValues containers: 2
  Disabled attributes: match target rows

MotionControl.vcxproj:
  AxesWrapper.h / AxesGroupWrapper.h / RingBuffer.h / tinyfsm.hpp files exist
  each file is registered in .vcxproj
  all 10 ItemDefinitionGroup entries contain AxesGroup/Utilities/CommandsExecuter include paths

Build:
  engineering.build-solution => succeeded, lastBuildInfo=0

Signing:
  MotionControl.vcxproj TcSignTwinCat=true
  TcSignTwinCatCertName=optcnc
  TcSignTwinCatCertPW=123
  MotionControl.tmx signed and verified with accepted test-mode certificate warning

Activation / ADS:
  engineering.activate-configuration => succeeded after the task layout fix
  activation archive => D:\3rd_year\auto_sln\_json_plan_evidence\activated-after-signing.tszip
  ADS scan reachable ports => 100, 200, 300, 10000
  ADS scan missing ports => 800, 851, 852
  detailed SAFEOP 1792 evidence => docs/evidence/2026-05-09-optcnc-safeop-1792-task-layout.md
```

数组 `DataPointerValues` 与目标样例逐项比较结果：无差异。匹配的 8 行是：

```text
AxesGroup0 / AxisProcessData[0] -> #x01010070 area 3 offset 0 size 120
AxesGroup0 / AxisProcessData[1] -> #x01010050 area 3 offset 0 size 120
AxesGroup0 / AxisProcessData[2] -> #x01010060 area 3 offset 0 size 120
AxesGroup0 / AxisStateData[0]   -> #x01010070 area 3 offset 120 size 8
AxesGroup0 / AxisStateData[1]   -> #x01010050 area 3 offset 120 size 8
AxesGroup0 / AxisStateData[2]   -> #x01010060 area 3 offset 120 size 8
CommandsExecuter / AxesGroupProcessData[0] -> #x01010030 area 6 offset 524 size 720
CommandsExecuter / AxesGroupStateData[0]   -> #x01010030 area 6 offset 0 size 32
```

主 OptCNC plan dry-run：

```powershell
dotnet .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\bin\Debug\net8.0-windows\TwinCatAutomationKit.Cli.dll run-plan --file=t\optcnc-auto-sln-from-kit.json --dry-run=true --summary=.artifacts\optcnc-auto-sln-from-kit-after-task-safeop-fix-dryrun-summary.json
```

结果：

- Steps: `457`
- Skipped: `4` (`generateMotionTmc`、`publishMotionModules`、`buildSolution`、`activate`)
- Failed: `0`
- Step `455`: `signing.set-license`, `MotionControl`, `license-name=optcnc`, password redacted in CLI output.

代码构建：

```powershell
dotnet build .\TwinCatAutomationKit.sln --no-restore /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
```

结果：`0` errors。当前 shell 仍有 `MSB3101` warning，原因是 `obj\...\AssemblyReference.cache` 写入被拒绝，不影响编译输出。

## Remaining gaps

- 已验证当前生成工程在 task layout 修复后 `engineering.build-solution`、签名、`engineering.activate-configuration` 和 ADS port scan 通过。ADS scan 不是 PLC/runtime symbol readback；端口 `851/852` 不存在，且 `10000` 返回 `Stop`。
- 样例 `Scope` project 和真实 EtherCAT topology 仍未复现。
- PLC 不是本轮缺口：目标样例本身没有 PLC project/source；当前工作重点是 MotionControl C++ module/TMC/TmcDesc/mapping parity。
