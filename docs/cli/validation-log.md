# CLI Validation Log / CLI 验证日志

English summary: This log records step-design issues, real-machine failures, fixes, and evidence so future agents can avoid repeating the same TwinCAT/XAE debugging work.

这个文件记录的是“已经踩过的坑”和“真实机器证明过的结论”。它不是 changelog，也不是完整 console transcript。原始日志如果仍有诊断价值，应放在 `docs/evidence/raw/`，这里写清楚问题、结论、修复和 evidence。

## 记录规则

- 以 step 或 command 为单位记录，不写“CLI 坏了”这种模糊描述。
- 必须写 expected vs actual。
- 必须写 evidence path、command line 或 request payload。
- 修复后保留条目，加 `Resolution`，不要删除历史。
- 新条目沿用 `CLI-XXX` 编号。

## Verified Baseline / 已验证基线

- Date: 2026-04-24
- Machine: local TwinCAT 4026 + VS2022, `VisualStudio.DTE.17.0`
- Direct CLI coverage: `TwinCatStepCatalog` 中全部 public step kind 已接入 `invoke-step`。
- Real direct-CLI smoke: `D:\t\tcak_manual_pipeline2`
- Real integration result: `All 22 integration tests passed.`
- Historical broad direct-CLI smoke after fixes:
  - Output root: `D:\t\tcak_cli_all_20260424_161856`
  - `Invocations: 45`
  - `Executed: 44 unique kind(s)`
  - `Skipped: 6 unique kind(s)`（历史结果；当前 integration test 策略不再把 skip 当作通过）
  - `Missing: 0`
  - `Failed: 0`
  - 当时默认 skip：`signing.grant-certificate`、`signing.set-license`、`signing.sign-twincat-binary`、`signing.verify-twincat-binary`、`engineering.activate-configuration`、`validation.ads-read`
  - Current note: 当前验证门槛以真实 TwinCAT integration test 为准，缺 signing、activation 或 ADS 前置条件时应失败。

## Open / 未关闭问题

### CLI-002: Real validation notes are not yet systematic

- Status: open
- Scope: engineering commands 和 high-impact `.tsproj` steps。
- Expected: 每个 step 都有简短真实 XAE/TwinCAT 验证记录。
- Actual: 部分知识仍分散在 tests、examples 和旧调查笔记。
- Evidence: 本文件已开始收敛，但不是每个 step 都有单独 step record。
- Usage-guide note: 后续新增真实验证时，用 [step-record-template.md](step-record-template.md) 补记录。

### CLI-003: Macro flows still hide step-level gaps

- Status: open
- Scope: `guided-build` 和 example runners。
- Expected: macro flow 只能组合 step，不能掩盖 direct CLI parity 缺口。
- Actual: 某些旧 recipe 仍比单步 CLI 更容易跑通。
- Evidence: `src/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli/Commands/GuidedBuildCommand.cs`
- Usage-guide note: 新能力必须先有 step-level spec，再考虑 macro recipe。

### CLI-005: Payload commands depend on file-backed UX discipline

- Status: open
- Scope: XML fragment 和 JSON batch-plan commands。
- Expected: 用户应优先使用 `--xml-file` 和 `--json-file`，并快速理解 payload shape。
- Actual: file-backed support 已有，但示例和验证笔记还可继续补强。
- Evidence: `StepInvokeCommand` 支持 file-backed payload。
- Usage-guide note: 大 payload 不应强塞到命令行字符串。

## Fixed / 已修复问题

### CLI-018: JSON plan runtime proof should print multiple ADS values

- Status: fixed
- Step kind: `validation.ads-read-symbols`
- Human TwinCAT action: activation 后一次读取多个 PLC/TcCOM runtime symbol，直接在终端看见值。
- Expected: `run-plan` 的 ADS 验收步骤能读取 `MAIN.nSeed`、`MAIN.nStage1`、`MAIN.bPipelineOk`、`MAIN.nMismatchCount` 等多个变量，并在终端 `Result` 中打印 `symbol=value`。
- Actual before fix: 只有单变量 `validation.ads-read`，`complex-full-project.json` 默认只演示 `MAIN.nSeed`，不能一眼证明 PLC/C++ mapping 链路。
- Resolution: 增加 `AdsValidationService.ReadSymbols`、public step `validation.ads-read-symbols`、direct CLI/JSON wiring、`complex-full-project.json` batch readback、reference docs 和真实 integration coverage。
- Evidence: 验证应跑 `dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj`；真实 runtime 值仍以 dated evidence 为准，例如 `docs/evidence/2026-04-30-ads-port-0748-runtime-state.md`。
- Usage-guide note: activation 后先跑 `validation.ads-scan`；端口可达后用 `validation.ads-read-symbols` 留下多变量 readback。不要把 build 或 activation 成功单独当成 runtime proof。

### CLI-017: Broad smoke should not encourage default `merge-fragment`

- Status: fixed
- Step kind: `tsproj.merge-fragment`, `tests/TwinCatAutomationKit.IntegrationTests/TwinCatAutomationKit.IntegrationTests/Scripts/verify-all-invoke-steps.ps1`
- Expected: `tsproj.merge-fragment` 只作为 documented known-good fragment 的 escape hatch，不能在主示例或 broad smoke 默认路径里暗示它是普通 DataTypes/Io/System 修改方式。
- Actual before fix: complex JSON plan 用 `merge-fragment` 添加 datatype；broad smoke 默认执行 `merge-fragment`，但没有逐项记录 fragment source、parent path、field meanings 和 evidence。
- Resolution: complex JSON plan 改用 `tsproj.replace-data-types-section` 一次写完整 `DataTypes`；broad smoke 默认 skip `tsproj.merge-fragment`，需要 `-IncludeMergeFragment` 显式开启；`MergeNamedElementFragmentRequest` 缺少 `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence` 时会失败。
- Usage-guide note: 有 dedicated primitive 时优先 dedicated；`merge-fragment` 只在补 API 前的高风险缺口调查中使用。

### CLI-016: Generic `.tsproj` mutation APIs need direct CLI parity

- Status: fixed
- Step kind: `tsproj.upsert-element`, `tsproj.upsert-fragment`, `tsproj.apply-mutation-plan`
- Human TwinCAT action: 没有 dedicated primitive 时，从 JSON 对 `.tsproj` 做低层 XML edit。
- Expected: `docs/reference/tsproj-xml-mutation-api.md` 里的 generic mutation methods 能被 `run-plan` 使用。
- Actual before fix: C# 方法存在并有测试，但不是 public step specs，也没有 `invoke-step` dispatch。
- Resolution: 增加 catalog entries、direct CLI wiring、JSON-file payload support、broad smoke script coverage 和真实 integration coverage。
- Usage-guide note: 优先 dedicated primitive；generic mutation plan 只用于低层 gap。

### CLI-015: Hidden DTE activation command can block without returning to CLI

- Status: fixed
- Step kind: `engineering.activate-configuration`
- Human TwinCAT action: 通过 stateless CLI activate generated configuration。
- Environment: `D:\t\tcak_json_user3`
- Expected: activation 成功或返回可诊断错误。
- Actual before fix: hidden XAE shell command path 阻塞超过 20 分钟，留下 `dotnet`/`devenv` automation processes。
- Evidence: `D:\t\tcak_json_user3\Demo\_Boot\TwinCAT OS (x64)\CurrentConfig.xml` 已存在且包含 `Demo_Obj1 (DemoCpp)` OP entries，但 CLI 无输出、无 `activated.tszip`。
- Resolution: `TwinCatEngineeringService.ActivateConfiguration` 先尝试 `ITcSysManager.ActivateConfiguration()`，localized/global DTE commands 只作为 fallback。
- Usage-guide note: hidden/stateless CLI 优先 direct System Manager activation path。

### CLI-014: DTE build timeout was bypassed by synchronous `SolutionBuild.Build`

- Status: fixed
- Step kind: `engineering.build-solution`
- Human TwinCAT action: 通过 XAE/Visual Studio automation build solution。
- Expected: `--timeout-ms` 能限制等待时间。
- Actual before fix: `SolutionBuild.Build(true)` 会在 synchronous COM call 内阻塞，仓库 timeout loop 没机会运行。
- Resolution: 改为 `Build(false)`，再轮询 `BuildState`；超时时抛出清晰 timeout exception。
- Verified result: slow activation test 返回 `Solution build did not finish within 300000 ms`，而不是无限挂起。
- Usage-guide note: 第一次 XAE build 可能很慢；timeout 是 XAE/VS build-state 问题，不代表 XML mutation 成功。

### CLI-013: JSON complex plan mixed activation-safe runtime state with demonstration-only showcase

- Status: fixed
- Step kind: `run-plan`, `engineering.activate-configuration`, `tsproj.apply-instance-data-pointer-plan`
- Human TwinCAT action: 从 JSON 生成 complex project 并 activate。
- Expected: activation 时只包含 runtime-valid C++ instance bindings。
- Actual before fix: 示例混入 `OfflineCpp01`、`Inputs.Value` 等 demonstration-only 结构，可能让 real `Demo_Obj1 (DemoCpp)` 缺少有效 `DataIn`/`DataOut` pointer。
- Evidence: `D:\t\tcak_json_complex_user1\Demo\Demo.tsproj`；manual activation 报 `AdsError: 1795 (invalid indexOffset)`。
- Resolution: complex JSON example 只指向 real `Demo_Obj1 (DemoCpp)`，使用 fallback module TMC 的真实名字：`Parameter.data1`、`CyclicCaller`、`DataIn`、`DataOut`。
- Verified result: `D:\t\tcak_json_runtime_safe2` 完成 JSON plan，`Failed: 0`，`buildBaseRuntime.lastBuildInfo=0`，direct activation 成功。

### CLI-012: Broad smoke script printed invoke-step results twice

- Status: fixed
- Step kind: `tests/TwinCatAutomationKit.IntegrationTests/TwinCatAutomationKit.IntegrationTests/Scripts/verify-all-invoke-steps.ps1`
- Expected: 每个命令结果在 console 和 `_cli_verify.log` 各出现一次。
- Actual before fix: PowerShell helper 同时打印并返回文本，未捕获的返回值再次进入 pipeline。
- Resolution: `Invoke-DotnetCli` 只在 `-PassThru` 时返回 command text。
- Usage-guide note: PowerShell helper 的 return value 也是 pipeline output，打印和返回要分清。

### CLI-011: Batch JSON plan CLI must accept array payloads

- Status: fixed
- Step kind: `tsproj.apply-instance-parameter-plan`, `tsproj.apply-instance-interface-pointer-plan`, `tsproj.apply-instance-data-pointer-plan`
- Expected: file-backed JSON batch plan 支持 `{ "items": [...] }` wrapper 和直接 `[...]` array。
- Actual before fix: CLI 先按 wrapper request deserialize，遇到 array 时失败。
- Resolution: `ReadPlanRequest<TItem,TRequest>` 先检测 array payload，再转成 item-list request。
- Verified result: broad smoke `D:\t\tcak_cli_all_20260424_161856`，`Missing: 0`，`Failed: 0`。

### CLI-010B: TwinCAT build-time signing settings need a direct interface

- Status: fixed
- Step kind: `signing.set-license`
- Human TwinCAT action: build 前设置 C++ project TwinCAT signing certificate/license name 和 password。
- Expected: JSON plan 不需要手改 `.vcxproj` 就能配置 build-time signing。
- Actual before fix: signing interface 只能在 build 后调用 `TcSignTool`，没有写 build-time TcSign properties 的 public step。
- Resolution: 增加 `TwinCatSigningService.SetLicense`、public step、direct CLI wiring、JSON-plan usage、reference docs 和真实 integration test。
- Usage-guide note: `signing.set-license` 要在 `engineering.build-solution` 前执行；共享脚本优先 password file/env var。

### CLI-010: TwinCAT C++ signing belongs in the direct CLI surface

- Status: fixed
- Step kind: `signing.grant-certificate`, `signing.sign-twincat-binary`, `signing.verify-twincat-binary`
- Human TwinCAT action: activation 前签名和验证 built C++ module binary。
- Evidence: `TcSignTool.exe` help output on 2026-04-24，生成 step specs 见 `docs/reference/step-catalog.md`。
- Resolution: 增加 `TwinCatSigningService`、三个 signing step specs、direct `invoke-step` wiring 和 script integration。
- Usage-guide note: sign after build，verify immediately，再 activate；`signing.grant-certificate` 是 machine state。

### CLI-009: Activation must run after real instance binding, not after build alone

- Status: fixed
- Step kind: `engineering.activate-configuration`
- Expected: activation 看到 built binary 和有效 task-bound module instance。
- Actual before fix: activation 在 real `Demo_Obj1 (DemoCpp)` 绑定到 `Task1` 前执行，PREOP object creation 失败，`AdsError 1795`。
- Evidence: `D:\t\tcak_cli_all_20260424_152114\Demo\Demo.tsproj`
- Resolution: broad smoke 在 build 前写入 real `tsproj.bind-instance-task` 和 `tsproj.ensure-parameter`，再执行 signing/activation。
- Usage-guide note: build success 不是 activation proof；runtime graph 必须有效。

### CLI-008: Task AMS ports must be unique inside one TwinCAT project

- Status: fixed
- Step kind: `engineering.ensure-task`, `tsproj.ensure-task`
- Expected: script-created `Task1` 不应和 wizard-created `PlcTask` 争用 AMS port。
- Actual before fix: PLC wizard 可能留下 port `350`，脚本也用 `350`，XAE build 报 port 必须唯一。
- Evidence: `D:\t\tcak_cli_all_20260424_152114\_cli_verify.log`
- Resolution: smoke script 保留 `350` 给 PLC wizard，script task 使用 `360`，file-mutation task 使用 `361`。
- Usage-guide note: 新 task 需要显式分配唯一 port。

### CLI-007: Fallback module bootstrap duplicated `VID_<Project>` in C++ headers

- Status: fixed
- Scope: `engineering.create-cpp-project` 和 `engineering.create-module` 的 deterministic fallback。
- Expected: fallback artifacts 在 fresh TwinCAT C++ project 中可编译。
- Actual before fix: `DemoCppClassFactory.cpp` 包含 `DemoCppVersion.h`，fallback 又往 `DemoCppServices.h` 插入 `VID_DemoCpp`，导致 C++ redefinition。
- Evidence: `D:\t\tcak_manual_pipeline\Demo\DemoCpp\_products\TwinCAT OS (x64)\Release\DemoCpp\DemoCpp.log`
- Resolution: `UpsertVendorIdInServicesHeader(...)` 在 class factory 已包含 version header 时跳过插入。
- Verified result: `engineering.build-solution` 返回 `lastBuildInfo=0`。

### CLI-006: Real integration run showed module-class population gap

- Status: fixed
- Scope: 依赖 usable module class 的 engineering 和 pipeline steps。
- Actual before fix: real integration 只通过 `18/22`，4 个 skip 都因为 `DemoCpp.tmc` 没有 usable module class。
- Resolution: `CreateCppProject` 在 wizard output 不完整时 bootstrap deterministic module artifacts；`AddModuleInstance` 验证 `.tsproj` persistence，不持久时注入 file-mutation instance skeleton。
- Verified result: 2026-04-24 real integration `22/22` passed，`0` failures，`0` skips。
- Usage-guide note: 不要求用户手动打开 XAE 生成 module class；如果 `DemoCpp.tmc` 无 module class，视为 regression。

### CLI-004: Lifecycle steps use stateless compatibility adapters

- Status: accepted limitation
- Scope: `engineering.launch-visual-studio`, `engineering.open-xae-solution`, `engineering.save-all`, `engineering.close-visual-studio`, `engineering.activate-configuration`
- Expected: session-level action 理想情况下应属于 persistent CLI session model。
- Actual: 当前 CLI 用 stateless adapters：launch/attach、optionally open solution、perform action、close。
- Evidence: `StepInvokeCommand.cs`
- Usage-guide note: 这些命令是 one-shot automation adapters，不是长连接 interactive session protocol。

### CLI-001: Direct parity is complete but still needs hardening

- Status: fixed
- Scope: `invoke-step` coverage。
- Expected: 每个 public step kind 有 direct CLI path。
- Actual: direct parity 已完整，但某些 command 仍需要更强真实机器验证。
- Evidence: `docs/cli/index.md` 和 `StepInvocationCatalog.cs`
- Usage-guide note: parity 不等于 operational robustness。

## Entry Template / 新条目模板

```md
### CLI-XXX: Short problem title

- Status: open | fixed | accepted limitation
- Step kind:
- Human TwinCAT action:
- Environment:
- Command or request:
- Expected:
- Actual:
- Evidence:
- Suspected cause:
- Resolution:
- Usage-guide note:
```
