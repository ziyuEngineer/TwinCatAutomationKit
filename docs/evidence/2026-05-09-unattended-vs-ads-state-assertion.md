# 2026-05-09 Unattended VS and ADS State Assertion / 无人值守 VS 与 ADS 状态断言

English summary: This evidence records the 2026-05-09 hardening that prevents Visual Studio/TwinCAT integration runs from waiting forever on unattended UI/COM hangs and adds exact ADS Run-state plus TcSysSrv event-window checks.

本轮针对 OptCNC 生成/激活任务补了一组验收必需能力：

- Visual Studio/XAE 自动化默认进入无人值守模式，减少弹窗或 DTE command fallback 卡住测试的风险。
- 新增 DTE host modal dialog watcher：只监控本轮新启动的 `devenv.exe` / `TcXaeShell.exe`，自动处理常见 `OK`、TwinCAT 激活/重启类 `Yes`、未知确认框的 `Cancel/No`，并把处理记录暴露为 `autoDismissedDialogs`。
- 新增 `validation.assert-ads-state`，把 `10000/200/300 = Run` 这类 runtime 条件变成 step 硬失败，而不是只看 `engineering.activate-configuration` 或 `validation.ads-scan` 的成功状态。
- 新增 `validation.mark-event-log-window` 和 `validation.assert-event-log-window`，把 activation 同一时间窗内的 `TcSysSrv` Error/Critical、`AdsState: >15<` Config 回退、`AdsError: 1792`、`FPU invalid operation` 变成可失败的 public guard。
- 新增 `tsproj.assert-data-pointer-shape`，把 OptCNC `AxesGroup0` 的 data pointer 记录数和 root `<Mappings>` link 保留情况变成 JSON plan 里的 typed guard。
- 新增 `tsproj.describe-io-topology`，把复杂 IO topology 读成 normalized JSON 证据，便于比较样例和生成物而不复制 raw TwinCAT metadata XML。
- 新增 `tsproj.compare-io-topology`，把样例/生成物 IO topology 差异变成 read-only guard，避免 IO 缺失时误报等价。
- 新增 `run-plan` / `invoke-step` 的 `--command-timeout-ms` 外层 wall-clock timeout。这个 timeout 不依赖单个 service 自己正确返回；如果某个 VS/TwinCAT COM 调用因为确认弹窗或 UI prompt 卡住，CLI 会按时失败退出。
- `command-timeout-ms` 超时后会清理本次 step 超时窗口中新启动的 `devenv.exe` / `TcXaeShell.exe` host process；运行前已经存在的 IDE process 不会被清理，避免后续无人值守测试把未确认弹窗留成后台残留。
- CLI parser 现在同时支持 `--key=value`、`--key value` 和裸 boolean flag，例如 `--dry-run`；避免无人值守脚本因为参数写法差异把 `true` 错当 plan path 或 timeout value。
- CLI project 默认 `UseAppHost=false`，后续 build 不再依赖可被残留 `TwinCatAutomationKit.Cli.exe` apphost 锁住的输出文件；`dotnet run` / `dotnet TwinCatAutomationKit.Cli.dll` 仍可执行。
- 新增 Scope file-only typed steps：`scope.ensure-configuration` 和 `scope.assert-configuration-shape`，用于生成/校验 OptCNC Scope `.tcscopex` 的 ADS acquisition 和 chart channel shape。
- 新增工程级 IO typed steps：`engineering.create-io-device` 和 `engineering.create-ethercat-box`，通过 XAE `ITcSmTreeItem.CreateChild` 表达人工添加 IO Device / EtherCAT terminal 的操作。该路径让后续 OptCNC IO 拓扑能走 Beckhoff Automation Interface，而不是把 sample `.tsproj` 的 `Project/Io` metadata 复制成 JSON。
- 新增 `engineering.generate-io-mappings`，优先走 `ITcSmCommands.GenerateMappings` / `ITcSmCommands2.GenerateMappings`，默认禁止 DTE menu fallback；需要 fallback 时也受 dialog watcher、operation timeout 和 outer `--command-timeout-ms` 保护。
- 新增 `engineering.search-io-devices` 和 `engineering.reload-io-devices`，分别走 `ITcSmCommands.SearchDevices` / `ReloadDevices`；不提供 DTE menu fallback，避免无人值守测试因为菜单确认框卡住。
- 新增 `engineering.apply-io-tree-plan`，批量编排 IO Device / EtherCAT Box `CreateChild` 请求；OptCNC plan 中这条 XAE/ESI 创建路线默认关闭，打开后仍受 outer timeout、dialog watcher 和 topology guard 约束。
- `verify-all-invoke-steps.ps1` 现在显式传 `--enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500`，不再只依赖 CLI 默认值。

## 代码变更

无人值守路径：

- `LaunchVisualStudioRequest` 新增 `SuppressUi=true`，`TwinCatEngineeringService.LaunchVisualStudio` 默认设置 `DTE.SuppressUI=true`。
- `LaunchVisualStudioRequest` 新增 `EnableDialogAutoDismiss=true`、`DialogPollIntervalMs=500`。`TwinCatEngineeringSession` 记录是否附着现有 IDE、监控的 host process id，以及 watcher 命中的 dialog title/action/count。
- `SaveAll`、`BuildCurrentSolution`、`ActivateConfiguration` 在执行前 best-effort 设置 `SuppressUI=true`。
- `ActivateConfigurationRequest` 新增 `SuppressUi=true`、`AllowDteCommandFallback=false` 和 `ActivationTimeoutMs=120000`。默认不再自动执行 `TwinCAT.ActivateConfiguration`、`Build.ActivateConfiguration` 这类 DTE menu command fallback，因为这些路径可能弹确认框。
- `TwinCatEngineeringService.ActivateConfiguration` 现在把 `ITcSysManager.ActivateConfiguration` 和 `StartRestartTwinCAT` 放在 bounded STA worker 中执行；如果 COM 调用卡住或弹窗无人确认，step 会按 `ActivationTimeoutMs` 失败，而不是无限等待。
- DTE launch 失败时会清理本轮新启动但未成功绑定的 `devenv.exe`。
- DTE launch 失败清理同时覆盖 `devenv.exe` 和 `TcXaeShell.exe`，只清理本轮新启动且仍无窗口标题的 host，避免误杀用户已有 IDE。
- dialog watcher 同样只对本轮新启动 host process id 生效；如果 `LaunchVisualStudio` 附着到用户已有 IDE，则不会自动点任何窗口，避免误操作人工 session。
- `LaunchVisualStudioRequest.LaunchTimeoutMs` 默认 `60000` ms；direct CLI 可传 `--launch-timeout-ms=...`，integration 可用 `TAK_INTEGRATION_DTE_LAUNCH_TIMEOUT_MS` 覆盖，避免 `Activator.CreateInstance` 长时间挂住。
- integration runner 默认父进程 watchdog：父进程启动 `--run-tests-child` 子进程；如果子进程超过 `TAK_INTEGRATION_RUNNER_TIMEOUT_MS`，父进程杀掉子进程树并返回失败，避免无人值守机器无限等待 VS 弹窗或 COM hang。
- integration step probe 现在也走父进程 watchdog。入口支持 `probe-run --kind=<step-kind>` 和 `--probe=<step-kind>`；未知参数直接失败，不再静默落回整套 7 个 integration tests。
- `tsproj.assert-data-pointer-shape` probe 改为 file-only 最小 `.tsproj` fixture，不需要为纯 file-mutation/assertion step 启动 DTE。
- integration `WorkRootBase` 会验证“可创建子目录并写文件”，不可写时 fallback 到 repo 内短路径 `t`，避免只检查根目录存在导致第一步创建工程才失败。
- `StepInvokeCommand.ExecutePlanStep` 支持 `command-timeout-ms`，`JsonPlanCommand` 支持全局 `--command-timeout-ms` 和 step-level `options.command-timeout-ms`。timeout 总是停止 plan，即使 `--stop-on-failure=false`。timeout 前会记录已有 `devenv` / `TcXaeShell` process id，timeout 后只清理本次窗口中新出现的 host。
- `CliOptionParser.Parse` 支持 `--key value` 和裸 flag；`JsonPlanCommand` 可用 `--file .\t\optcnc-auto-sln-from-kit.json --dry-run --stop-on-failure true --command-timeout-ms 60000` 这种常见写法。
- `TwinCatAutomationKit.Cli.csproj` 设置 `UseAppHost=false`。本轮出现两个旧 `TwinCatAutomationKit.Cli` apphost 进程无法被当前权限结束，导致普通 build 无法复制 `.exe`；切到 DLL apphost-free 输出后完整 solution build 通过，只剩旧 `.exe` 删除 warning，不再失败。
- `t\optcnc-auto-sln-from-kit.json` 在 activation 前新增 `markActivationEventLogWindow`，activation 后新增 `assertActivationEventLogWindow`，并给 activation、ADS assertion 设置 step-level `command-timeout-ms`。

ADS 状态断言：

- 新 DTO：`ExpectedAdsPortState`、`AssertAdsStateRequest`、`AdsPortStateAssertion`、`AssertAdsStateResult`。
- 新 service：`AdsValidationService.AssertStates`。
- 新 public step：`validation.assert-ads-state`。
- direct CLI 支持：

```powershell
dotnet run --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.assert-ads-state "--expected=10000=Run;200=Run;300=Run"
```

PowerShell 中 `;` 是语句分隔符，所以 `--expected=...` 必须加引号。

TcSysSrv 事件窗口断言：

- 新 DTO：`MarkEventLogWindowRequest`、`EventLogWindowMarker`、`AssertEventLogWindowRequest`、`AssertEventLogWindowResult`。
- 新 service：`AdsValidationService.MarkEventLogWindow` 和 `AdsValidationService.AssertEventLogWindow`。
- 新 public steps：`validation.mark-event-log-window`、`validation.assert-event-log-window`。
- direct CLI 支持：

```powershell
dotnet run --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.mark-event-log-window --marker-file=D:\3rd_year\TwinCatAutomationKit_push\t\event-window-marker-smoke.json --command-timeout-ms=60000
dotnet run --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.assert-event-log-window --marker-file=D:\3rd_year\TwinCatAutomationKit_push\t\event-window-marker-smoke.json --max-events=5 --command-timeout-ms=60000
```

Data pointer 形状断言：

- 新 DTO：`ExpectedDataPointerValueShape`、`ExpectedMappingLinkShape`、`AssertDataPointerShapeRequest`、`DataPointerValueShape`、`MappingLinkShape`、`AssertDataPointerShapeResult`。
- 新 public step：`tsproj.assert-data-pointer-shape`。
- `t\optcnc-auto-sln-from-kit.json` 在 data pointer mapping 写入后和 activation 后都加入该 guard，要求 `AxesGroup0` 保留 6 个 data pointer record 和 8 条关键 root mapping link。

Scope 配置断言：

- 新 DTO：`ScopeAdsChannelDefinition`、`ScopeChartChannelDefinition`、`EnsureScopeConfigurationRequest`、`ScopeConfigurationChannelShape`、`AssertScopeConfigurationShapeRequest`。
- 新 service：`TwinCatScopeConfigurationService.EnsureConfiguration` 和 `AssertConfigurationShape`。
- 新 public steps：`scope.ensure-configuration`、`scope.assert-configuration-shape`。
- `t\optcnc-auto-sln-from-kit.json` 在 `engineering.create-scope-project` 后生成 `scope-configuration.json` 和 `scope-shape.json`，覆盖 sample 的 `BufferWriteAvailable` / `BufferReadAvailable` 两个 ADS channels 和 YT chart channels。

工程级 IO CreateChild steps：

- 新 DTO：`CreateIoDeviceRequest`、`CreateEthercatBoxRequest`。
- 新 service methods：`TwinCatEngineeringService.CreateIoDevice` 和 `CreateEthercatBox`。
- 新 public steps：`engineering.create-io-device`、`engineering.create-ethercat-box`。
- direct CLI 支持普通参数和 `--json-file`：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.create-io-device --solution-path=D:\t\Demo.sln --project-path=D:\t\Demo\Demo.tsproj "--name=Device 3 (EtherCAT)" --subtype=111 --parent-tree-item-path=TIID --disabled=true --command-timeout-ms=120000
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.create-ethercat-box --solution-path=D:\t\Demo.sln --project-path=D:\t\Demo\Demo.tsproj "--parent-tree-item-path=TIID^Device 3 (EtherCAT)" "--name=Term 2 (EK1100)" --product-revision=EK1100-0000-0017 --disabled=true --command-timeout-ms=120000
```

Beckhoff 官方 Automation Interface 文档给出的稳定含义是：`ITcSmTreeItem.CreateChild` 用 subtype 和 `vInfo` 创建树节点；EtherCAT master subtype 是 `111`；E-Bus terminal/box subtype 是 `9099`，`vInfo` 传 product revision。当前实现不复制 IO XML，只把这些 COM 参数做成 public step。

工程级 IO GenerateMappings step：

- 新 DTO：`GenerateIoMappingsRequest`。
- 新 service method：`TwinCatEngineeringService.GenerateIoMappings`。
- 新 public step：`engineering.generate-io-mappings`。
- direct CLI 支持：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.generate-io-mappings --solution-path=D:\t\Demo.sln --project-path=D:\t\Demo\Demo.tsproj --allow-dte-command-fallback=false --timeout-ms=120000 --command-timeout-ms=180000
```

这个 step 的成功只证明 XAE GenerateMappings 动作被执行；OptCNC 仍必须用 `tsproj.assert-io-topology-shape`、`tsproj.describe-io-topology` 或 `tsproj.compare-io-topology` 证明 root `<Mappings>` 和 IO process image/PDO 结构没有丢。

工程级 IO scan/reload steps：

- 新 DTO：`SearchIoDevicesRequest`、`ReloadIoDevicesRequest`。
- 新 service methods：`TwinCatEngineeringService.SearchIoDevices` 和 `ReloadIoDevices`。
- 新 public steps：`engineering.search-io-devices`、`engineering.reload-io-devices`。
- 两个 step 都只走 `ITcSmCommands` / `ITcSmCommands2`，不尝试 DTE menu command；如果当前 COM object 不暴露接口就失败，让调用方补环境或 step，而不是在无人值守机器上等确认框。
- direct CLI 支持：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.search-io-devices --solution-path=D:\t\Demo.sln --project-path=D:\t\Demo\Demo.tsproj --timeout-ms=120000 --command-timeout-ms=180000
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.reload-io-devices --solution-path=D:\t\Demo.sln --project-path=D:\t\Demo\Demo.tsproj --timeout-ms=120000 --command-timeout-ms=180000
```

工程级 IO tree batch step：

- 新 DTO：`ApplyIoTreePlanRequest`、`ApplyIoTreePlanResult`。
- 新 service method：`TwinCatEngineeringService.ApplyIoTreePlan`。
- 新 public step：`engineering.apply-io-tree-plan`。
- 该 step 不写 `.tsproj` XML；它按 payload 顺序调用 `CreateIoDevice` / `CreateEthercatBox`，再 `SaveAll`。无人值守运行必须配合 `--command-timeout-ms`，并在后续用 `tsproj.assert-io-topology-shape` 或 `tsproj.compare-io-topology` 验证 XAE 生成结果。
- direct CLI 支持 `--json-file`：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.apply-io-tree-plan --solution-path=D:\t\Demo.sln --project-path=D:\t\Demo\Demo.tsproj --json-file=D:\t\payloads\io-tree-plan.json --command-timeout-ms=600000
```

## 验证命令

```powershell
dotnet build .\TwinCatAutomationKit.sln --no-restore /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet run --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- generate-docs
dotnet run --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.assert-ads-state "--expected=10000=Run;200=Run;300=Run"
$env:TAK_INTEGRATION_RUNNER_TIMEOUT_MS='45000'; dotnet run --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
$env:TAK_INTEGRATION_RUNNER_TIMEOUT_MS='90000'; $env:TAK_INTEGRATION_DTE_LAUNCH_TIMEOUT_MS='20000'; dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- --probe=tsproj.assert-data-pointer-shape
dotnet run --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.assert-data-pointer-shape --project-path=D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj --json-file=t\optcnc-axesgroup-shape-assert.json --raw-output=true
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.launch-visual-studio --launch-timeout-ms=60000 --command-timeout-ms=1 --visible=false --suppress-ui=true
dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- --probe=scope.ensure-configuration
dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- --probe=scope.assert-configuration-shape
dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- --probe=tsproj.describe-io-topology --file-only=true
dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- --probe=tsproj.compare-io-topology --file-only=true
dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- --probe=validation.mark-event-log-window --file-only=true
dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- --probe=validation.assert-event-log-window --file-only=true
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.describe-io-topology --project-path=D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj --max-items-per-collection=5 --command-timeout-ms=60000
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.compare-io-topology --project-path=D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj --reference-project-path=D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj --max-differences=25 --command-timeout-ms=60000
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.mark-event-log-window --marker-file=D:\3rd_year\TwinCatAutomationKit_push\t\event-window-marker-smoke.json --command-timeout-ms=60000
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.assert-event-log-window --marker-file=D:\3rd_year\TwinCatAutomationKit_push\t\event-window-marker-smoke.json --max-events=5 --command-timeout-ms=60000
powershell -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-all-invoke-steps.ps1
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=engineering.create-io-device
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=engineering.create-ethercat-box
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=engineering.generate-io-mappings
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=engineering.search-io-devices
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=engineering.reload-io-devices
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=engineering.apply-io-tree-plan
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=.\t\optcnc-auto-sln-from-kit.json --dry-run=true --command-timeout-ms=300000
dotnet .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\bin\Debug\net8.0-windows\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --dry-run --stop-on-failure true --command-timeout-ms 60000
dotnet build .\TwinCatAutomationKit.sln --no-restore /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=engineering.launch-visual-studio
dotnet build .\TwinCatAutomationKit.sln --no-restore /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
```

结果：

```text
build: succeeded, 0 errors; MSB3101 obj cache write warnings only
generate-docs: succeeded with --no-restore; normal restore path hit an obj tmp permission error in this sandbox
validation.assert-ads-state: failed as intended because local ports 10000/200/300 returned 0x748 Port is not open
integration watchdog: child exceeded 45000 ms at VS/DTE launch and parent terminated the child process tree
integration probe alias before file-only fix: ran only tsproj.assert-data-pointer-shape and failed in 21 s at DTE launch instead of running all 7 tests
integration probe alias after file-only fix: succeeded in 1.1 s and wrote before/after .tsproj snapshots under t\probes\20260509-185331\01-ts-assert-data-pointer-shape-d09771
OptCNC tsproj.assert-data-pointer-shape direct run: succeeded; AxesGroup0 matched 6 data record(s) and 8 mapping link(s)
OptCNC dry-run after activation timeout: 461 steps, failed 0, skipped 6; activate step includes activation-timeout-ms=180000 and allow-dte-command-fallback=false
command-timeout-ms probe: engineering.launch-visual-studio with --command-timeout-ms=1 failed immediately with timeout text and did not wait for the 60000 ms launch timeout; later hardening added timeout-window cleanup for newly started DTE host processes
scope.ensure-configuration probe: succeeded
scope.assert-configuration-shape probe: succeeded
verify-all invoke dry-run after Scope steps: 65 invocations, 64 unique executed kinds, missing 0, failed 0
verify-all invoke dry-run after IO compare step: 67 invocations, 66 unique executed kinds, missing 0, failed 0
OptCNC dry-run after Scope steps: 464 steps, failed 0, skipped 6
OptCNC plan scan after Scope steps: totalSteps=464, uniqueKinds=39, scopeSteps=2, forbidden merge/upsert/replace escape kinds=0
tsproj.describe-io-topology probe: succeeded as file-only step, no DTE launch
tsproj.describe-io-topology sample/current: sample has 5 Device, 28 Box, 4 Image, 107 PDO, 382 PDO Entry, 24 root mapping links; current has 0 Device, 0 Box, 0 Image, 0 PDO, 0 PDO Entry, 8 root mapping links
tsproj.compare-io-topology probe: succeeded as file-only step, no DTE launch
tsproj.compare-io-topology sample/current: failed as intended with 25 differences before truncation because generated auto_sln still lacks IO topology
validation.mark-event-log-window probe: succeeded as file-only validation probe
validation.assert-event-log-window probe: succeeded as file-only validation probe
validation.mark-event-log-window direct smoke: succeeded; marker time 2026-05-09T20:59:21.3175384+08:00, lastEntryIndex 44086
validation.assert-event-log-window direct smoke: succeeded; observed 0 TcSysSrv event(s), Error/Critical 0, Config AdsState 0
verify-all invoke dry-run after event-log guard: 67 invocations, 66 unique executed kinds, skipped 12, missing 0, failed 0
OptCNC dry-run after event-log activation guard: 466 steps, failed 0, skipped 8, uniqueKinds 41
OptCNC plan scan after event-log activation guard: totalSteps=466, uniqueKinds=41, scopeSteps=2, eventLogSteps=2, forbidden merge/upsert/replace escape kinds=0
OptCNC dry-run after IO skeleton plan update: 468 steps, failed 0, skipped 8, uniqueKinds 43
unattended dialog watcher build: succeeded; LaunchVisualStudioRequest/catalog/CLI now expose enable-dialog-auto-dismiss and dialog-poll-interval-ms
help step engineering.launch-visual-studio: lists EnableDialogAutoDismiss and DialogPollIntervalMs
verify-all invoke dry-run after dialog watcher: 67 invocations, 66 unique executed kinds, skipped 12, missing 0, failed 0
verify-all invoke dry-run after engineering IO CreateChild steps: 69 invocations, 68 unique executed kinds, skipped 12, missing 0, failed 0
help step engineering.create-io-device / engineering.create-ethercat-box: generated catalog entries include subtype, parent path, vInfo/product revision, Disabled, AllowExisting, PostCreateDelayMs
verify-all invoke dry-run after engineering GenerateMappings step: 70 invocations, 69 unique executed kinds, skipped 12, missing 0, failed 0
help step engineering.generate-io-mappings: generated catalog entry includes SuppressUi, AllowDteCommandFallback=false, TimeoutMs
verify-all invoke dry-run after engineering SearchDevices/ReloadDevices steps: 72 invocations, 71 unique executed kinds, skipped 12, missing 0, failed 0
verify-all unattended args: dry-run command lines explicitly include --enable-dialog-auto-dismiss=true and --dialog-poll-interval-ms=500 for Visual Studio/XAE steps
verify-all invoke dry-run after engineering ApplyIoTreePlan wrapper: 73 invocations, 72 unique executed kinds, skipped 12, missing 0, failed 0
OptCNC dry-run after optional IO tree route: 473 steps, failed 0, skipped 13
OptCNC plan scan after optional IO tree route: totalSteps=473, uniqueKinds=47, ioTreeSteps=1, ioSkeletonSteps=2, forbidden merge/upsert/replace escape kinds=0
CLI command-timeout cleanup build: succeeded, 0 errors; timeout probe failed fast with "New Visual Studio/XAE host processes cleaned up: 0."
OptCNC plan defaults now explicitly include enable-dialog-auto-dismiss=true and dialog-poll-interval-ms=500; dry-run shows both defaults on steps and reports 473 steps, failed 0, skipped 13
build after dialog watcher: succeeded, 0 errors; MSB3101 obj cache write warnings only
CLI parser flag/space form: `run-plan --file .\t\optcnc-auto-sln-from-kit.json --dry-run --stop-on-failure true --command-timeout-ms 60000` succeeded; summary reported 474 steps, failed 0
UseAppHost=false build: full solution build succeeded with 0 errors; old locked `TwinCatAutomationKit.Cli.exe` caused only MSB3061 delete warning, not build failure
verify-all invoke dry-run after ESI guard/parser hardening: 74 invocations, 73 unique executed kinds, skipped 12, missing 0, failed 0
ethercat.assert-product-revisions probe: succeeded as file-only probe after StepProbeRunner dispatch included `ethercat.*`
```

ADS failure excerpt:

```text
ADS state assertion failed:
10000=<expected Run, actual (unreachable): Port is not open. (AdsErrorCode: 1864, 0x748)>;
200=<expected Run, actual (unreachable): Port is not open. (AdsErrorCode: 1864, 0x748)>;
300=<expected Run, actual (unreachable): Port is not open. (AdsErrorCode: 1864, 0x748)>
```

integration watchdog excerpt:

```text
RUN  ordered-step-surface engineering + tsproj + reopen + build ...      stage: launch VS
Integration test child process exceeded 45000 ms and was terminated.
```

single probe success excerpt:

```text
Succeeded tsproj.assert-data-pointer-shape
  RunDir: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-185331\01-ts-assert-data-pointer-shape-d09771
  Sln:    D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-185331\01-ts-assert-data-pointer-shape-d09771\w\S\S.sln
  Tsproj: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-185331\01-ts-assert-data-pointer-shape-d09771\w\S\S.tsproj
  Note:   tsproj.assert-data-pointer-shape probe succeeded.
```

Scope probe success excerpts:

```text
Succeeded scope.ensure-configuration
  RunDir: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-201044\01-scope-ensure-configuration-257f3b
  Note:   scope.ensure-configuration probe succeeded.

Succeeded scope.assert-configuration-shape
  RunDir: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-201044\01-scope-assert-configuration-s-78d156
  Note:   scope.assert-configuration-shape probe succeeded.
```

IO describe probe excerpt:

```text
Succeeded tsproj.describe-io-topology
  RunDir: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-203017\01-ts-describe-io-topology-431c5d
  Note:   tsproj.describe-io-topology probe succeeded.
```

IO compare probe excerpt:

```text
Succeeded tsproj.compare-io-topology
  RunDir: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-203927\01-ts-compare-io-topology-5addfb
  Note:   tsproj.compare-io-topology probe succeeded.
```

Event log guard probe excerpts:

```text
Succeeded validation.mark-event-log-window
  RunDir: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-205944\01-val-mark-event-log-window-2a78eb
  Note:   validation.mark-event-log-window probe succeeded.

Succeeded validation.assert-event-log-window
  RunDir: D:\3rd_year\TwinCatAutomationKit_push\t\probes\20260509-205944\01-val-assert-event-log-window-580357
  Note:   validation.assert-event-log-window probe succeeded.
```

外层 timeout excerpt：

```text
invoke-step failed for engineering.launch-visual-studio
invoke-step 'engineering.launch-visual-studio' did not finish within 1 ms. The background step worker was abandoned so unattended runs do not wait forever on Visual Studio/TwinCAT UI prompts.
```

当前实现中的 timeout 文本还会追加 `New Visual Studio/XAE host processes cleaned up: N`，用于确认 CLI 已尝试清理本次超时窗口中新启动的 IDE host。本轮 probe 结果：

```text
invoke-step 'engineering.launch-visual-studio' did not finish within 1 ms. The background step worker was abandoned so unattended runs do not wait forever on Visual Studio/TwinCAT UI prompts. New Visual Studio/XAE host processes cleaned up: 0.
```

## 结论

`engineering.activate-configuration` 仍然只是执行激活动作，不是 RUN 证明。OptCNC 验收必须在 activation 后执行：

```text
validation.mark-event-log-window before activation
validation.assert-event-log-window after activation
validation.assert-ads-state: 10000=Run;200=Run;300=Run
```

并继续检查同一激活窗口内没有新的 `TcSysSrv` error。当前机器状态下 ADS 端口未打开，说明 runtime 没有处于可验收 RUN 状态；这不是 `validation.assert-ads-state` 的失败，而是它正确拦截了“假激活成功”。

无人值守方面，后续 integration run 或单步 probe 即使遇到 VS 弹窗、DTE COM hang 或无响应 `devenv.exe` / `TcXaeShell.exe`，默认也会先由 `EnableDialogAutoDismiss` 尝试处理本轮新启动 host 的 modal dialog，再由 watchdog 限时退出；activation step 本身也有 `ActivationTimeoutMs`，`run-plan` / `invoke-step` 还有 `--command-timeout-ms` 外层保险，并会清理 timeout 窗口中新启动的 IDE host，不应再无限等待人工确认或留下本轮新启动的挂起 IDE。纯 `.tsproj`、Scope config 和 event-log validation probe 不应启动 DTE；本轮 `tsproj.assert-data-pointer-shape`、`scope.ensure-configuration`、`scope.assert-configuration-shape`、`validation.mark-event-log-window`、`validation.assert-event-log-window` 已按这个策略落地。
