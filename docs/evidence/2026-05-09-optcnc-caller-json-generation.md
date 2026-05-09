# 2026-05-09 OptCNC Caller JSON Generation / OptCNC 调用者 JSON 生成证据

English summary: This evidence records the caller-level OptCNC reproduction run: `t/optcnc-auto-sln-from-kit.json` generated `D:\3rd_year\auto_sln` through public kit steps, without copying sample project files or using generic `.tsproj` XML insertion, and strict integration passed after service hardening.

本文记录以调用者身份使用 kit 和 JSON 生成 `D:\3rd_year\auto_sln` 的结果。样例来源是 `D:\2nd_year\twincat0926\OptcncTwinCAT`。本轮没有复制样例 `.sln/.tsproj/.vcxproj/.filters/.tmc`，没有写文件系统复制源码目录的脚本，也没有使用 generic `.tsproj` XML escape hatch。

## JSON plan shape

计划文件：

```powershell
t\optcnc-auto-sln-from-kit.json
```

当前 plan 形态：

- `432` 个 step。
- `112` 个 `files[]` payload，其中源码/资源内容作为 JSON payload 写入。
- `111` 个 `cpp.write-project-item-content` 目标文件全部等于 JSON payload。
- `0` 个 `merge-fragment`、`upsert-xml`、`generic`、`xml-fragment`、`bulk` 类 step kind。
- `0` 个 `copy` / `clone` 类 step kind。
- `0` 个 `.sln/.tsproj/.vcxproj/.filters/.tmc` project-file payload。

关键 public steps：

- `engineering.create-xae-solution`
- `engineering.create-cpp-project`
- `engineering.create-vs-cpp-project`
- `engineering.ensure-solution-project-dependency`
- `cpp.create-project-item`
- `cpp.write-project-item-content`
- `cpp.remove-project-item`
- `cpp.set-project-property`
- `cpp.set-item-definition-property`
- `cpp.set-project-item-metadata`
- `engineering.create-module`
- `engineering.publish-modules`
- `engineering.add-module-instance`
- dedicated `tsproj.*` primitives for task binding, parameters, interface pointers, and mappings

## Dry-run evidence

执行命令：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=t\optcnc-auto-sln-from-kit.json --dry-run=true --summary=.artifacts\optcnc-real-payload-dry-run-after-fixes-summary.json
```

结果：

- Plan: `optcnc-auto-sln-from-kit`
- Mode: `dry-run`
- Steps: `432`
- Failed: `0`
- Skipped: `2`
- skipped steps: `engineering.build-solution` and `engineering.activate-configuration`，由 plan variables `includeBuild=false`、`includeActivation=false` 显式关闭。

说明：dry-run 不执行真实 step，所以 summary 中 `Succeeded` 计数不是实际成功执行数；判断重点是所有 option interpolation 和 public step contract 校验通过，且 failed 为 `0`。

## Real caller run

真实 XAE/VS caller run 通过用户交互桌面中的 `interactive-command-runner.ps1` 执行，避免非交互 shell 的 DTE launch 问题。

runner request:

- id: `optcnc-real-caller-9b53debbdca14a09ad3869ec6debee12`
- started: `2026-05-08T23:16:06.4946331+08:00`
- finished: `2026-05-09T01:01:07.6265982+08:00`
- exitCode: `0`
- timedOut: `false`
- response: `.artifacts\interactive-runner\responses\optcnc-real-caller-9b53debbdca14a09ad3869ec6debee12.response.json`
- stdout: `.artifacts\interactive-runner\logs\optcnc-real-caller-9b53debbdca14a09ad3869ec6debee12.stdout.log`
- summary: `.artifacts\optcnc-real-payload-run-summary.json`

real summary:

- Steps: `432`
- Succeeded: `430`
- Skipped: `2`
- Failed: `0`
- Generated solution: `D:\3rd_year\auto_sln\OptcncTwinCAT.sln`
- TwinCAT project: `D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj`

关键输出：

- `engineering.publish-modules` succeeded，`updatedTmcPath=D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\MotionControl.tmc`。
- module instances persisted with caller names: `SimDriver1`, `SimDriver2`, `AxesGroup0`, `BeckhoffDriver1`, `Axis1`, `Axis2`, `Axis0`, `CommandsExecuter`。
- task object ids included `ServoLoop=#x02010020` and `PlcTask=#x02010030`。
- evidence XML exported:
  - `D:\3rd_year\auto_sln\_json_plan_evidence\motion-control.xml`
  - `D:\3rd_year\auto_sln\_json_plan_evidence\tasks.xml`

## Generated project inspection

Post-run inspection showed the generated project item counts match the sample `.vcxproj` item counts:

| Project | ClCompile | ClInclude | ResourceCompile | None |
| --- | ---: | ---: | ---: | ---: |
| `Ruckig` | 12 | 3 | 0 | 1 |
| `Tinyxml2` | 2 | 4 | 0 | 1 |
| `MotionControl` | 22 | 42 | 1 | 1 |
| `AdsClient` | 2 | 3 | 0 | 0 |

Encoding-aware text comparison showed all `111` written source/resource targets match the sample source text. Generated `.sln/.tsproj/.vcxproj/.filters/.tmc` files are not byte-identical to the sample project files, which is expected because they were created by XAE/VS/service steps rather than copied from the sample.

## Strict integration after hardening

The caller run exposed two real service issues that were fixed and covered by strict integration:

- XAE may persist C++ module instances as `Name (ModuleClass)` / `Name (ProjectName)`. `AddModuleInstance` and `SaveAll` now normalize only XAE-generated suffixes whose suffix matches `TmcDesc/Name`, so downstream public `tsproj.*` steps can address instances by caller-provided names.
- `engineering.create-vs-cpp-project` with `AllowTemplateFallback=true` now avoids hidden DTE template lookup when no explicit candidate template is provided. It creates a minimal service-generated VS C++ project and adds it to the loaded solution instead of blocking in an invisible VS template path.

Final strict integration request:

- id: `integration-after-fallback-no-dte-query-12e63bd3458f4a259ea624004010fa99`
- started: `2026-05-09T01:48:15.4428428+08:00`
- finished: `2026-05-09T01:49:54.6301056+08:00`
- exitCode: `0`
- timedOut: `false`
- stdout: `.artifacts\interactive-runner\logs\integration-after-fallback-no-dte-query-12e63bd3458f4a259ea624004010fa99.stdout.log`
- preserved artifacts: `D:\3rd_year\TwinCatAutomationKit\t\e157bc56`

stdout conclusion:

```text
All 7 integration tests passed.
```

The preserved `.tsproj` at `t\e157bc56\ItProject\ItProject.tsproj` contains naked instance names:

- `<Name>ItPrimary</Name>`
- `<Name>ItAux</Name>`

This directly covers the stricter regression asserted by `OrderedTwinCatScenarioTests`.

## Not covered

- The OptCNC plan keeps `engineering.build-solution` and `engineering.activate-configuration` disabled by variables, so this evidence does not claim full OptCNC build, activation, or ADS runtime readback.
- The sample `Scope` project is not reproduced because there is no dedicated public Scope project step yet.
- Hardware-backed EtherCAT topology is not reproduced; the current plan covers C++/resource/source payloads, module publish, instances, tasks, parameters, interface pointers, and TIXC mappings.
- Real signing certificate grant/sign/verify remains excluded by the integration test matrix unless a signing config is provided.
