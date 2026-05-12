# 2026-05-10 OptCNC Unattended Runner And XAE Crash / OptCNC 无人值守运行与 XAE 崩溃证据

English summary: OptCNC generation reached structure guards plus build/sign success, but activation did not reach TwinCAT RUN because Visual Studio/XAE crashed while opening the TwinCAT project; this round hardened unattended timeouts and runner exit-code handling, and added typed IO process-image guards without using sample XML copy or merge-fragment paths.

本文记录 2026-05-10 这轮 OptCNC JSON plan 真实执行、无人值守防卡死修正、以及当前 activation 失败根因。结论必须按失败处理：`D:\3rd_year\auto_sln\OptcncTwinCAT.sln` 已存在，结构 guard、build、sign 都有证据，但本轮没有得到 activation 后绿色 RUN，也没有得到 `10000/200/300 = Run` 的 ADS proof。

## 结论

- public step surface 已能表达当前生成物的 solution/tsproj、C++ item/source、TMC/TmcDesc guard、PLC/task/instance、MotionControl 参数、interface pointer、data pointer、data pointer `<Mappings>` link、Scope、sign/build/activate/ADS/event-log 验收链路。
- 当前 public step surface 仍不能合法完整复现样例的全部 IO metadata：root `ImageDatas` bitmap、box `ImageId`、107 个 PDO、382 个 PDO entry、SyncMan/FMMU、drive-manager XML 等仍缺专用接口或 XAE/ESI 生成 proof。
- `engineering.activate-configuration` 失败不是确认弹窗无人点击；Windows Application log 显示 `devenv.exe` 在 TwinCAT project type load/open 过程中崩溃。
- 本轮已把高风险 XAE/VS reused-session steps 保持 `command-timeout-ms` 保护，并在超时时放弃复用 session，避免以后测试因为隐藏弹窗或 COM hang 无限等待。
- 本轮没有复制样例 `.sln`、`.tsproj`、`.vcxproj`、`.filters`、`.tmc` 或 TwinCAT metadata XML，也没有使用 `merge-fragment`、bulk XML insert 或大块 XML 粘贴绕过 step 缺口。

## 生成物与结构证明

目标生成物：

```text
D:\3rd_year\auto_sln\OptcncTwinCAT.sln
D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj
D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\_products\TwinCAT OS (x64)\Release\MotionControl.tmx
```

r7 read-only structure guard：

```text
response: .artifacts\interactive-runner\responses\optcnc-current-shape-readonly-20260510-r7.response.json
stdout:   .artifacts\interactive-runner\logs\optcnc-current-shape-readonly-20260510-r7.stdout.log
exit:     0
```

结果：

```text
tsproj.assert-data-pointer-shape:
  AxesGroup0 matched 6 data record(s), 8 data pointer mapping link(s), 24 root mapping links.

engineering.verify-tmc-data-areas:
  MotionControl TMC matched 5/5 expected modules.

tsproj.assert-io-topology-shape:
  matched 5 devices, 28 boxes, 0 PDO, 24 root links.
```

注意：r7 当时的 IO guard 还没有断言 device process-image `Image` 数量，所以不能证明完整 IO parity。

## Build 与 signing

r8 sign/build/activate summary：

```text
summary:  .artifacts\optcnc-sign-build-activate-20260510-r8-summary.json
response: .artifacts\interactive-runner\responses\optcnc-sign-build-activate-20260510-r8.response.json
stdout:   .artifacts\interactive-runner\logs\optcnc-sign-build-activate-20260510-r8.stdout.log
```

结果：

```text
signing.set-license:
  succeeded, licenseName=optcnc, passwordWritten=true

validation.mark-event-log-window:
  succeeded, markedAt=2026-05-10T17:30:20.3095986+08:00

engineering.build-solution:
  succeeded, buildEngine=msbuild-projects, exitCode=0
  log: D:\3rd_year\auto_sln\_json_plan_evidence\msbuild-projects-sign-activate.log

signing.sign-twincat-binary:
  succeeded, TcSignTool exitCode=0
  target: D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\_products\TwinCAT OS (x64)\Release\MotionControl.tmx

signing.verify-twincat-binary:
  succeeded with accepted test-mode warning, TcSignTool exitCode=2
```

Signing password 值不写入 evidence；summary 只保留 `passwordWritten=true` 和 certificate/tool 结果。

## Activation 失败

r8 activation step：

```text
step: engineering.activate-configuration
started:  2026-05-10T17:30:21.9211814+08:00
finished: 2026-05-10T17:32:20.7996601+08:00
result: failed
message: RPC 服务器不可用。 (0x800706BA)
options:
  activation-timeout-ms=180000
  command-timeout-ms=300000
  suppress-ui=true
  enable-dialog-auto-dismiss=true
  allow-dte-command-fallback=false
```

同一时间窗口 Windows Application log：

```text
2026-05-10 17:30:43 .NET Runtime 1026
  process: devenv.exe
  exception: System.AccessViolationException
  stack includes:
    Microsoft.VisualStudio.Shell.Interop.IVsAggregatableProject.InitializeForOuter
    Microsoft.VisualStudio.ProjectSystem.ProjectTypeManager.OpenProject
    Microsoft.VisualStudio.PlatformUI.Solution.Open

2026-05-10 17:30:44 Application Error 1000
  process: devenv.exe
  fault module: TwinCAT System Manager.x64.dll
  module version: 3.1.0.4823
  exception: 0xc0000005
  fault offset: 0x0000000000ba53b1

2026-05-10 17:30:54 WER APPCRASH
  confirms devenv.exe / TwinCAT System Manager.x64.dll crash

2026-05-10 17:31:00 Application Error 1000
  process: devenv.exe
  module: KERNELBASE.dll
  exception: 0xe0434352
```

解释：当前失败点是 XAE/Visual Studio 在加载 TwinCAT project type 或打开 solution/project 过程中崩溃，导致后续 COM/RPC 断开。它不同于无人值守确认弹窗等待；也不同于之前 `AxesGroup0` OP 失败的 data pointer mapping link 缺失问题。

## 无人值守修正

本轮把“可能弹窗或 COM hang 的 XAE/VS step 不能无限等待”写进项目：

- `run-plan --reuse-engineering-session` 下，高风险 reused engineering steps 仍保留 per-step `command-timeout-ms`，包括 `engineering.launch-visual-studio`、`engineering.open-xae-solution`、`engineering.export-tree-item-xml`、`engineering.build-solution`、`engineering.activate-configuration`。
- 高风险 step timeout 时会 abandon 当前 reused engineering session，并报告 abandoned/cleanup 计数；低风险 reused step 才关闭外层 worker timeout。
- `interactive-command-runner.ps1` 新增 `-Once`，用 `System.Diagnostics.ProcessStartInfo` 和 stdout/stderr redirect 获取可靠 exit code，避免 stale request 或 `Start-Process` 行为把失败误报成功。
- runner exit-code probe：

```text
response: .artifacts\interactive-runner\responses\runner-exitcode-probe-20260510-r4.response.json
command:  exit 7
result:   exitCode=7
```

这部分解决的是测试自动化可靠性：未来遇到确认框、COM hang 或 crash 时应按 timeout/exit code 失败退出，而不是等人手工点击。

本轮随后补了一个专门面向 XAE/VS crash 的 public guard：

```text
step: validation.assert-process-crash-window
method: AdsValidationService.AssertProcessCrashWindow
default providers: Application Error, .NET Runtime, Windows Error Reporting
default processes: devenv.exe, TcXaeShell.exe
default modules: TwinCAT System Manager.x64.dll, TwinCAT System Manager.dll
```

这个 step 复用 `validation.mark-event-log-window` 写出的 marker 文件，按 marker 后的 Application log 全局 index 过滤事件。这样 `engineering.activate-configuration` 或 `engineering.open-xae-solution` 失败后，不会只留下 `RPC 服务器不可用`，而是可以把同一窗口内的 `devenv.exe` / TwinCAT 插件 crash 明确变成 validation failure。

同时 `run-plan` 增加 `steps[].runAfterFailure`。当 `--stop-on-failure=true` 且前序 step 失败时，普通 step 会 skipped；只有显式标记 `runAfterFailure=true` 的 read-only diagnostics 会继续运行。OptCNC plan 已把这两条 activation 诊断 step 标记为失败后继续执行：

```text
assertActivationEventLogWindow: validation.assert-event-log-window, runAfterFailure=true
assertActivationProcessCrashWindow: validation.assert-process-crash-window, runAfterFailure=true
```

## IO image guard

此前生成物和 IO skeleton 只断言 Device/Box/link 数量，容易漏掉样例中的四个 device process-image `Image` 节点。本轮补了 typed guard 和 plan payload：

- `ExpectedIoDeviceShape.InfoImageId`
- `ExpectedIoDeviceShape.ImageCount`
- `ExpectedIoBoxShape.ImageId`
- `AssertIoTopologyShapeRequest.ExpectedImageCount`
- `IoDeviceShape.InfoImageId`
- `IoDeviceShape.ImageCount`
- `IoBoxShape.ImageId`
- `AssertIoTopologyShapeResult.ImageCount`

OptCNC IO skeleton payload 现在通过 typed `IoImageDefinition` 写入四个 device process-image nodes：

```text
Device 3 (EtherCAT): Image Id=2,  InfoImageId=3
Device 4 (EtherCAT): Image Id=4,  InfoImageId=5
Device 5 (EtherCAT): Image Id=6,  InfoImageId=7
Device 6 (RT-Ethernet): Image Id=8, InfoImageId=9
```

repo 内 file-only probe：

```text
artifact: .artifacts\optcnc-io-skeleton-image-probe-20260510.json
project:  t\optcnc-io-skeleton-probe\OptcncTwinCAT.tsproj
steps:
  tsproj.apply-io-topology-plan
  tsproj.assert-io-topology-shape
```

结果：

```text
Applied IO topology plan.
IO topology shape matched: 5 device(s), 28 box(es), 4 image(s), 0 PDO(s), 14 link(s).
```

该 probe 证明 public typed IO step 可以写入并断言四个 device process-image `Image` 节点。它仍不代表完整 IO parity，因为样例还包含 root `ImageDatas`、box `ImageId` 指向、PDO、SyncMan/FMMU 和 drive metadata。

本轮继续补了第二层只读 guard：

```text
step: tsproj.assert-io-image-references
method: TwinCatTsprojMutationService.AssertIoImageReferences
```

该 step 不写 `.tsproj`，只读取并断言：

- root `/TcSmProject/ImageDatas/ImageData` count 和 ids。
- `/Project/Io/Device/Image` direct process-image count。
- 带 `InfoImageId` 的 Device 是否同时有 direct `Image`。
- `Project/Io` 下所有 `ImageId` 引用是否能被 root `ImageData`、direct Device `Image`、Device `InfoImageId` 或显式 `AllowedUnbackedImageIds` 解释。

样例 read-only stats：

```text
reference root ImageData count: 9, ids 1000..1008
reference direct Device/Image count: 4, ids 2,4,6,8
reference Device InfoImageId count: 4, ids 3,5,7,9
reference ImageId reference count: 39, distinct 1000..1008,118,3,7
```

当前 `D:\3rd_year\auto_sln` 旧生成物在本轮读到：

```text
root ImageData count: 0
direct Device/Image count: 0
Device InfoImageId count: 4
ImageId reference count: 0
```

这说明旧生成物仍是半截 IO shape：Device 带 `InfoImageId`，但没有 direct device `Image`。由于当前 sandbox 不能写 `D:\3rd_year\auto_sln`，最新 skeleton payload 尚未真实落到目标工程；但 OptCNC JSON plan 已把新 guard 接到 typed skeleton 后：

```text
assertOptCncIoImageSkeletonFromXae: tsproj.assert-io-image-references, enabled by includeEngineeringIoTreePlan
assertOptCncIoImageSkeleton:        tsproj.assert-io-image-references, enabled by includeIoSkeleton
payload: optcnc-io-image-skeleton-shape.json
expectedRootImageDataCount=0
expectedDeviceImageCount=4
expectedImageReferenceCount=0
requireDeviceImageForInfoImageId=true
```

## 2026-05-11 strict IO topology guard

本轮把 `tsproj.assert-io-topology-shape` 从数量级 guard 扩展成更接近 OptCNC 样例结构的 typed guard。新增检查包括：

- 全局 `ExpectedPdoEntryCount`。
- Device 直接子 `Box`、直接 `Image` 数量，以及直接子元素类型计数。
- Box 的 `ParentBoxId`、`BoxFlags`、`PdoEntryCount`、直接/递归子 Box 数量，以及直接子元素和 `EtherCAT` 子元素类型计数。
- 输出 summary 增加 `pdoEntryCount`，便于 evidence 中直接看 107 个 PDO 和 382 个 PDO entry 是否保留。

OptCNC JSON plan 同步拆成两个明确 shape：

```text
t\optcnc-io-topology-skeleton-shape.json
  default route, includeIoSkeleton=true
  expected: 5 devices, 28 boxes, 4 direct device images, 0 PDO, 0 PDO entries, 24 root mapping links

t\optcnc-io-topology-sample-shape.json
  XAE/ESI route, includeEngineeringIoTreePlan=true
  expected: 5 devices, 28 boxes, 4 direct device images, 107 PDO, 382 PDO entries, 24 root mapping links
```

`sample-shape` 只用于 read-only assertion 或 XAE/ESI route 验收；它没有把样例 `Project/Io` XML 复制进生成物，也没有通过 `merge-fragment` 或 bulk XML insert 写入目标工程。

验证结果：

```text
dotnet build tests ... -o .\.artifacts\tests-unlocked
  result: succeeded, 0 errors
  note: MSB3101 cache write warnings from locked obj files, build exit code remained 0

dotnet build CLI --no-restore --no-dependencies -o .\.artifacts\tests-unlocked
  result: succeeded, 0 errors

probe-run --kind=tsproj.assert-io-topology-shape
  result: succeeded
  runDir: t\probes\20260511-095639\01-ts-assert-io-topology-shape-bf1d36

invoke-step tsproj.assert-io-topology-shape against reference sample
  project: D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj
  result: succeeded
  matched: 5 devices, 28 boxes, 4 images, 107 PDO, 382 PDO entries, 24 root links

run-plan --dry-run t\optcnc-auto-sln-from-kit.json
  summary: .artifacts\optcnc-dry-run-post-catalog-20260511-summary.json
  result: 479 steps, 25 skipped, 0 failed
```

这说明当前 public step surface 至少能把 OptCNC IO skeleton 与 reference sample 的关键 shape 差异表达成 typed validation。它仍不是完整生成 proof：完整 IO parity 还需要 XAE/ESI 成功生成 root `ImageDatas`、Box `ImageId`、PDO、SyncMan/FMMU、drive metadata，并且要通过 reopen/build/activate/ADS 验证。

对当前磁盘上的 `D:\3rd_year\auto_sln` 用 repo 内最新 skeleton guard 做只读复查，结果仍失败：

```text
project: D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj

tsproj.describe-io-topology:
  5 devices, 28 boxes, 0 direct device images, 0 PDO, 0 PDO entries, 2 MappingInfo, 5 OwnerA, 24 root mapping links

tsproj.assert-io-topology-shape with latest optcnc-current-topology-skeleton-shape-20260511.json:
  failed with 9 errors
  Project/Io has 0 Image records, expected 4
  Device 3/4/5/6 each has 0 Image records and 0 direct Image records, expected 1

tsproj.assert-io-image-references with latest optcnc-current-image-skeleton-shape-20260511.json:
  failed with 5 errors
  Project/Io has 0 direct device Image records, expected 4
  Devices 3/4/5/6 have InfoImageId but no direct Image node
```

这不是新增接口的失败，而是目标目录里的旧 `_json_plan_payloads` 还缺今天新增的 `optcnc-io-image-skeleton-shape.json`，且当前 sandbox 无法写 `D:\3rd_year\auto_sln` 去真实重跑最新 plan。结论仍按未完成处理。

## 2026-05-11 attached DTE dialog watcher

用户明确要求无人值守运行不能卡在 Visual Studio 确认框。本轮继续把弹窗处理范围从“本轮新启动的 VS/XAE 进程”扩展到“attached/reused DTE session”：

- `TwinCatEngineeringSession` 现在会从 `DTE.MainWindow.HWnd` 解析宿主进程 id。
- `EnableDialogAutoDismiss=true` 且能解析到目标 pid 时，即使 session 是 attach/reuse 进来的，也会启动 dialog auto-dismiss watcher。
- watcher 仍只监控 selected host process ids，避免扫全局桌面窗口。
- watcher 选择按钮时现在读取 dialog title 和 child window text；Visual Studio 标题只有 `Microsoft Visual Studio`、正文里才出现 save/activate/restart 提示时，也能按正文识别是否应选择 `Yes`。
- `engineering.launch-visual-studio` step catalog 文案已更新，明确 `targetProcessIds` 和 `autoDismissedDialogs` 是验证点。

验证结果：

```text
dotnet build tests ... -o .\.artifacts\tests-unlocked
  result: succeeded, 0 errors

dotnet build CLI --no-restore --no-dependencies -o .\.artifacts\tests-unlocked
  result: succeeded, 0 errors

help step --kind=engineering.launch-visual-studio
  result: succeeded
  observed: EnableDialogAutoDismiss description mentions attached DTE host process when pid can be resolved

generate-docs
  result: succeeded

post text-scan watcher hardening build:
  dotnet build tests ... -o .\.artifacts\tests-unlocked
  result: succeeded, 0 errors
  dotnet build CLI --no-restore --no-dependencies -o .\.artifacts\tests-unlocked
  result: succeeded, 0 errors

run-plan stop-on-failure hardening:
  before fix: a failed createSolution was followed by unresolved ${steps.ensureServoLoop.outputs.objectId}, masking the original DTE launch failure
  after fix: skipped steps no longer resolve options/enabled/runAfterFailure after stop-on-failure, and deferred payload files do not throw secondary unresolved-token errors
  summary: .artifacts\optcnc-real-run-20260511b-summary.json
  result: 479 steps, 1 failed createSolution, 478 skipped, failure preserved as DTE launch issue
  regression: tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-run-plan-stop-on-failure.ps1
  regression result: .artifacts\run-plan-stop-on-failure-20260511-r5, 4 steps, 1 failed, 3 skipped, no unresolved-token secondary error

DTE launch diagnostic hardening:
  invoke-step engineering.launch-visual-studio --launch-timeout-ms=10000
  result: failed as expected on this machine
  message now includes last exception and DTE host snapshot
  observed snapshot: total=7 devenv hosts, 6 headless; samples include pre-existing headless devenv ids and new headless/visible hosts
  2026-05-11 r7: internal DTE COM activation probe now uses almost the full launch-timeout window instead of a fixed 15 s cap
  r7 probe: engineering.launch-visual-studio --launch-timeout-ms=60000 --command-timeout-ms=75000
  r7 result: failed after 55000 ms COM activation wait, not 15000 ms; new host ids were cleaned after failure and only 5 pre-existing headless devenv remained

cleanup probe:
  invoke-step engineering.cleanup-dte-host-processes --dry-run=false --include-windowed=false
  matched: 5 headless devenv candidates
  killed: 0
  reason: Access denied
```

这解决的是“将来测试遇到确认框必须失败或自动关闭，不能无限等人”的工程问题。它不改变 r8 的结论：r8 activation 失败的直接证据仍是 `devenv.exe` / `TwinCAT System Manager.x64.dll` crash，不是一个已观察到的等待确认框。

## 2026-05-11 ESI guard复查

对 OptCNC XAE/ESI CreateChild route 的 product revision payload 重新执行 file-only guard：

```text
step: ethercat.assert-product-revisions
payload: .artifacts\optcnc-product-revisions-20260511.json
search: C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Config\Io\EtherCAT
scannedFileCount: 134
requestedCount: 19
matchedCount: 15
missingCount: 4
```

缺失项：

```text
00F5060-F000
iA-GAI04A0_2xMII_REV_00020020
iA-MPA64B0 2xMII_REV00030022
iA-MPS32A0 2xMII 00020020
```

因此在当前机器上不能默认开启 `includeEngineeringIoTreePlan=true` 并期待完整 reference IO metadata 由 XAE 自动生成；缺第三方 KEB/iA ESI 时，XAE route 应先在 file-only guard 失败，而不是启动 Visual Studio 后卡在设备选择或确认窗口。

## 当前未完成验收

按用户验收口径，本轮仍未完成：

```text
activate 后保持绿色 RUN: no
ADS port 10000 = Run: no proof
ADS port 200/300 = Run: no proof
同一 activation window 无 TcSysSrv error: no final assert, because activation crashed before post checks
激活后 .tsproj 关键结构仍保留: no post-activation proof in this round
```

r9 曾用当前 sandbox `-Once` runner 复测 exit-code path：

```text
response: .artifacts\interactive-runner\responses\optcnc-sign-build-activate-20260510-r9.response.json
exitCode: 3
failure: Access to the path D:\3rd_year\auto_sln\OptcncTwinCAT\MotionControl\MotionControl.vcxproj is denied.
```

r9 只证明 runner 会正确返回失败 exit code；它不能用于 activation 结论。当前 sandbox 无法写 `D:\3rd_year\auto_sln`，因此本轮最新 IO image payload 尚未在目标路径重新生成/激活。

## 本轮新增验证

由于旧 `TwinCatAutomationKit.Cli.exe` 进程锁住默认 `bin\Debug` 输出且当前权限无法 `taskkill`，验证使用隔离输出目录：

```powershell
dotnet build .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -o .\.artifacts\cli-unlocked\ /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
```

结果：

```text
build: succeeded, 0 errors
warnings: MSB3101 obj cache write denied warnings only
```

新增 public step 验证：

```powershell
dotnet .\.artifacts\cli-unlocked\TwinCatAutomationKit.Cli.dll help step --kind=validation.assert-process-crash-window
dotnet run --no-build --no-restore --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj -- probe-run --kind=validation.assert-process-crash-window
```

结果：

```text
help: method AdsValidationService.AssertProcessCrashWindow, CLI supported
probe: Succeeded validation.assert-process-crash-window
runDir: t\probes\20260511-091009\01-val-assert-process-crash-win-77fe39
```

Catalog/docs/CLI coverage：

```powershell
dotnet .\.artifacts\cli-unlocked\TwinCatAutomationKit.Cli.dll generate-docs
powershell -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-all-invoke-steps.ps1 -OutputRoot .\.artifacts\verify-all-crash-guard-20260510
```

结果：

```text
generate-docs: succeeded, output docs\reference
verify-all dry-run: 75 invocations, 74 unique executed kinds, 13 skipped kinds, missing 0, failed 0
```

OptCNC dry-run：

```powershell
dotnet .\.artifacts\cli-unlocked\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --dry-run --stop-on-failure true --command-timeout-ms 60000 --reuse-engineering-session true --summary .\.artifacts\optcnc-dry-run-crash-guard-r4-20260510-summary.json
```

结果：

```text
steps: 477
failed: 0
skipped: 24
assertActivationEventLogWindow: _runAfterFailure=true
assertActivationProcessCrashWindow: _runAfterFailure=true
```

继续补 IO image reference guard 后的验证：

```powershell
dotnet build .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -o .\.artifacts\build-cli-full-20260511-r5 /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet build .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj --no-restore -o .\.artifacts\build-tests-20260511-r4 /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet .\.artifacts\cli-unlocked\TwinCatAutomationKit.Cli.dll help step --kind=tsproj.assert-io-image-references
dotnet .\.artifacts\tests-unlocked\TwinCatAutomationKit.IntegrationTests.dll probe-run --kind=tsproj.assert-io-image-references
dotnet .\.artifacts\build-cli-full-20260511-r5\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --dry-run --stop-on-failure true --command-timeout-ms 60000 --reuse-engineering-session true --summary .\.artifacts\optcnc-dry-run-20260511-r5-summary.json
powershell -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-all-invoke-steps.ps1 -OutputRoot .\.artifacts\verify-all-20260511-r4
powershell -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-run-plan-stop-on-failure.ps1 -OutputRoot .\.artifacts\run-plan-stop-on-failure-20260511-r5
dotnet .\.artifacts\build-cli-full-20260511-r5\TwinCatAutomationKit.Cli.dll generate-docs
```

结果：

```text
CLI isolated full build: succeeded, 0 errors, MSB3101 obj cache write warnings only
IntegrationTests isolated build: succeeded, 0 errors, MSB3101 obj cache write warnings only
help: tsproj.assert-io-image-references is in catalog and CLI supported
probe: Succeeded tsproj.assert-io-image-references
probe runDir: t\probes\20260511-093240\01-ts-assert-io-image-reference-d0eab3
OptCNC dry-run: 479 steps, 25 skipped, 0 failed
verify-all dry-run: 76 invocations, 75 unique executed kinds, 13 skipped kinds, missing 0, failed 0
run-plan stop-on-failure regression: 4 steps, 1 failed, 3 skipped, exit code 3 expected, no unresolved-token secondary error
generate-docs: succeeded, output docs\reference
```

额外 DTE launch timeout 分配验证：

```powershell
dotnet build .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -o .\.artifacts\build-cli-full-20260511-r7 /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet .\.artifacts\build-cli-full-20260511-r7\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --visible=false --startup-delay-ms=2000 --suppress-ui=true --enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500 --launch-timeout-ms=60000 --attach-to-existing=false --command-timeout-ms=75000 --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r7\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.cleanup-dte-host-processes --dry-run=true --include-windowed=true --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r7\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --dry-run --stop-on-failure true --command-timeout-ms 60000 --reuse-engineering-session true --summary .\.artifacts\optcnc-dry-run-20260511-r7-summary.json
dotnet .\.artifacts\build-cli-full-20260511-r7\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --var:root=D:\3rd_year\TwinCatAutomationKit_push\.artifacts\optcnc-real-root-20260511-r7 --stop-on-failure true --command-timeout-ms 90000 --reuse-engineering-session true --summary .\.artifacts\optcnc-real-run-20260511-r7-summary.json
```

结果：

```text
CLI isolated full build: succeeded, 0 errors, MSB3101 obj cache write warnings only
launch probe: failed on current machine after 55000 ms DTE COM activation wait
previous behavior: same 60000 ms launch-timeout only waited 15000 ms inside COM activation before fallback
cleanup dry-run after failure: 5 candidate devenv processes, all pre-existing headless ids; r7 new host ids were not left running
OptCNC dry-run after launch timeout allocation change: 479 steps, 25 skipped, 0 failed
OptCNC real run with repo-writable root: failed at createSolution after 55000 ms DTE COM activation wait; summary recorded 479 steps, 1 failed, 478 skipped
OptCNC real run failure did not leave unresolved-token secondary errors; skipped steps kept empty options after stop-on-failure
OptCNC real run did not create a solution or tsproj; only deferred payload files were emitted under .artifacts\optcnc-real-root-20260511-r7
IntegrationTests build note: default obj output is locked by an existing process; isolated CLI build still compiled the modified TwinCat layer
```

继续补无人值守 DTE launch 阶段弹窗保护后验证：

```powershell
dotnet build .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -o .\.artifacts\build-cli-full-20260511-r9 /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet build .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj --no-restore --no-dependencies -o .\.artifacts\build-tests-20260511-r9 /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet .\.artifacts\build-cli-full-20260511-r9\TwinCatAutomationKit.Cli.dll generate-docs
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-run-plan-stop-on-failure.ps1 -OutputRoot .\.artifacts\run-plan-stop-on-failure-20260511-r9
dotnet .\.artifacts\build-cli-full-20260511-r8\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --visible=false --startup-delay-ms=2000 --suppress-ui=true --enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500 --launch-timeout-ms=60000 --attach-to-existing=false --command-timeout-ms=75000 --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r8\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.cleanup-dte-host-processes --dry-run=true --include-windowed=true --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r10\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --visible=false --startup-delay-ms=2000 --suppress-ui=true --enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500 --launch-timeout-ms=60000 --attach-to-existing=false --command-timeout-ms=75000 --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r10\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --var:root=D:\3rd_year\TwinCatAutomationKit_push\.artifacts\optcnc-real-root-20260511-r10 --stop-on-failure true --command-timeout-ms 90000 --reuse-engineering-session true --summary .\.artifacts\optcnc-real-run-20260511-r10-summary.json
```

结果：

```text
CLI isolated full build: succeeded, 0 errors, MSB3101 obj cache write warnings only
IntegrationTests no-dependencies build: succeeded, 0 errors, MSB3101 obj cache write warnings only
generate-docs: succeeded, output docs\reference
run-plan stop-on-failure regression: passed
DTE launch probe on current machine: still failed after 55000 ms COM activation wait
cleanup dry-run after failed launch: 5 candidate devenv processes, all pre-existing headless ids; no r8 launch host was left running
r10 DTE launch probe on current machine: still failed after 55000 ms COM activation wait; launch-time watcher path executed and failure was bounded by timeout
r10 OptCNC real run with repo-writable root: failed at createSolution after 55000 ms DTE COM activation wait; summary recorded 479 steps, 1 failed, 478 skipped
```

本轮修复点：

- `EnableDialogAutoDismiss=true` 现在覆盖 DTE COM activation、fallback `/Embedding` launch、startup delay 和 active session，不再只在 `TwinCatEngineeringSession` 构造后才启动。
- 启动期 watcher 会持续发现本轮新启动的 `devenv.exe` / `TcXaeShell.exe` 进程并纳入监控；失败清理仍只清理本轮新 host。
- DTE COM activation 和 startup 期间自动处理过的弹窗会合并进 `autoDismissedDialogs` 输出，方便后续 evidence 判断是否真的出现过 VS/TwinCAT modal dialog。

继续定位 VS DTE launch failure 后的证据：

```powershell
dotnet .\.artifacts\build-cli-full-20260511-r11\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --visible=false --startup-delay-ms=2000 --suppress-ui=true --enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500 --launch-timeout-ms=30000 --attach-to-existing=false --command-timeout-ms=45000 --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r12\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --visible=false --startup-delay-ms=2000 --suppress-ui=true --enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500 --launch-timeout-ms=60000 --attach-to-existing=false --command-timeout-ms=75000 --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r14\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --visible=false --startup-delay-ms=2000 --suppress-ui=true --enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500 --launch-timeout-ms=60000 --attach-to-existing=false --command-timeout-ms=75000 --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r15\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --visible=false --startup-delay-ms=3000 --suppress-ui=true --enable-dialog-auto-dismiss=true --dialog-poll-interval-ms=500 --launch-timeout-ms=90000 --attach-to-existing=false --root-suffix=TAKTemp --command-timeout-ms=110000 --raw-output=true
```

结果：

```text
r11 diagnostics: new ownerless #32770 window found in devenv process; title "Microsoft Visual Studio"; button "确定"
r12 auto-dismiss: ownerless #32770 VS dialog is now dismissed; no new host left after failure
r13/r14 dialog text: VS reports "未知错误", pointing to ActivityLog; auto-dismiss records the message text
r14 fallback /Log: ActivityLog written under .artifacts\build-cli-full-20260511-r14\dte-launch-logs
r14 ActivityLog errors: Failed to initialize Registry Root Hive; Failure calling InitializeAppID
r15 root-suffix TAKTemp: same ActivityLog root hive errors; detailed log shows 0x80070005 E_ACCESSDENIED
```

本轮新增 public surface：

- `LaunchVisualStudioRequest.RootSuffix`
- CLI option `--root-suffix`
- `engineering.launch-visual-studio` catalog parameter `RootSuffix`
- fallback DTE launch now runs `/Embedding /NoSplash /Log <workspace log>` and appends ActivityLog error summaries to the step failure message

当前机器结论：无人值守弹窗等待问题已由项目处理；剩余 DTE failure 是 Visual Studio registry root hive 权限问题，ActivityLog 明确返回 `0x80070005 E_ACCESSDENIED`。在当前无提权 sandbox 下，不能修复用户 hive/registry ACL，也不能 kill 既有 headless VS 进程；因此真实 OptCNC build/sign/activate 仍被第一步 DTE launch 前置条件阻断。

继续补 `TcXaeShell` 和多 VS host 诊断后验证：

```powershell
dotnet build .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -o .\.artifacts\build-cli-full-20260511-r18 /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet .\.artifacts\build-cli-full-20260511-r18\TwinCatAutomationKit.Cli.dll generate-docs
.\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-run-plan-stop-on-failure.ps1 -CliDll .\.artifacts\build-cli-full-20260511-r18\TwinCatAutomationKit.Cli.dll -OutputRoot .\.artifacts\run-plan-stop-on-failure-20260511-r18
dotnet .\.artifacts\build-cli-full-20260511-r18\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --prog-id=TcXaeShell.DTE.15.0 --timeout-ms=45000 --enable-dialog-auto-dismiss=true
dotnet .\.artifacts\build-cli-full-20260511-r18\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --var:root=D:\3rd_year\TwinCatAutomationKit_push\.artifacts\optcnc-real-root-20260511-r18 --var:dteProgId=TcXaeShell.DTE.15.0 --stop-on-failure true --command-timeout-ms 120000 --reuse-engineering-session true --summary .\.artifacts\optcnc-real-run-20260511-r18-summary.json
dotnet .\.artifacts\build-cli-full-20260511-r19\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --prog-id=VisualStudio.DTE.16.0 --timeout-ms=45000 --enable-dialog-auto-dismiss=true
dotnet .\.artifacts\build-cli-full-20260511-r19\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --prog-id=VisualStudio.DTE.16.0 --dte-host-path="D:\large software\Visual Studio\1\Common7\IDE\devenv.exe" --timeout-ms=45000 --enable-dialog-auto-dismiss=true
dotnet .\.artifacts\build-cli-full-20260511-r22\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --prog-id=VisualStudio.DTE.16.0 --dte-host-path="D:\large software\Visual Studio\1\Common7\IDE\devenv.exe" --prefer-dte-host-launch=true --launch-timeout-ms=45000 --enable-dialog-auto-dismiss=true
dotnet .\.artifacts\build-cli-full-20260511-r22\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --prog-id=TcXaeShell.DTE.15.0 --prefer-dte-host-launch=true --launch-timeout-ms=45000 --enable-dialog-auto-dismiss=true
.\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-run-plan-stop-on-failure.ps1 -CliDll .\.artifacts\build-cli-full-20260511-r22\TwinCatAutomationKit.Cli.dll -OutputRoot .\.artifacts\run-plan-stop-on-failure-20260511-r22
dotnet .\.artifacts\build-cli-full-20260511-r22\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --dry-run true --summary .\.artifacts\optcnc-dry-run-20260511-r22-summary.json
dotnet .\.artifacts\build-cli-full-20260511-r22\TwinCatAutomationKit.Cli.dll run-plan --file .\t\optcnc-auto-sln-from-kit.json --var:root=D:\3rd_year\TwinCatAutomationKit_push\.artifacts\optcnc-real-root-20260511-r22 --var:dteProgId=VisualStudio.DTE.16.0 --var:dteHostPath="D:\large software\Visual Studio\1\Common7\IDE\devenv.exe" --var:preferDteHostLaunch=true --stop-on-failure true --command-timeout-ms 120000 --reuse-engineering-session true --summary .\.artifacts\optcnc-real-run-20260511-r22-summary.json
```

结果：

```text
r18 CLI isolated full build: succeeded, 0 errors, MSB3101 obj cache write warnings only
r18 generate-docs: succeeded, output docs\reference
r18 run-plan stop-on-failure regression: passed
r18 TcXaeShell.DTE.15.0 fallback host: C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe
r18 TcXaeShell ActivityLog: CPkgDefCacheNonVolatileBase cache creation failed with 0x80070005 E_ACCESSDENIED; detection_keys_reg.pkgdef parse errors; no modal wait remained
r18 OptCNC repo-root real run with TcXaeShell.DTE.15.0: failed at createSolution; 479 steps, 1 failed, 478 skipped; payload expansion succeeded but no solution/tsproj created
r19 VisualStudio.DTE.16.0 fallback host: D:\large software\Visual Studio\1\Common7\IDE\devenv.exe
r19 explicit dte-host-path to VS2019: same host and same ActivityLog root hive E_ACCESSDENIED
r22 prefer-dte-host-launch=true with VS2019: failed in about 1.6s, skipped Activator.CreateInstance, kept host pid/ActivityLog diagnostics
r22 prefer-dte-host-launch=true with TcXaeShell: failed in about 1.7s, skipped Activator.CreateInstance, kept host pid/ActivityLog diagnostics
r22 stop-on-failure regression: passed; unresolved skipped-step output references no longer mask the first failure
r22 OptCNC dry-run: 479 steps, 25 skipped, 0 failed
r22 OptCNC repo-root real run with VS2019 explicit host and prefer launch: failed at createSolution in about 2s; 479 steps, 1 failed, 478 skipped; no solution/tsproj created
```

本轮新增 public surface：

- `LaunchVisualStudioRequest.DteHostPath`
- `LaunchVisualStudioRequest.PreferDteHostLaunch`
- CLI option `--dte-host-path`
- CLI option `--prefer-dte-host-launch`
- JSON plan/default option `dte-host-path`
- JSON plan/default option `prefer-dte-host-launch`
- `engineering.launch-visual-studio` catalog parameter `DteHostPath`
- `engineering.launch-visual-studio` catalog parameter `PreferDteHostLaunch`
- Visual Studio fallback host discovery now filters `vswhere` by DTE major version (`VisualStudio.DTE.16.0` -> VS2019, `17.0` -> VS2022) before falling back to default install paths.
- fallback launch records the host process id and, when the host exits after writing ActivityLog errors, fails early instead of waiting the full launch timeout.

该修正解决的是多 IDE/TcXaeShell 机器上的无人值守确定性：失败信息会显示实际 fallback host、arguments、ActivityLog path 和摘要；不会因为错误 host、隐藏 VS “未知错误”弹窗或 ownerless `#32770` 对话框无限等待。当前机器仍因 VS/TcXaeShell registry/pkgdef cache 权限失败而不能进入真实 XAE session。

2026-05-11 用户指出 `interactive-command-runner.ps1` 可能能绕过当前 DTE 问题。本轮复测和修复结论如下：

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\interactive-command-runner.ps1 -Root D:\3rd_year\TwinCatAutomationKit_push -Once
dotnet .\.artifacts\build-cli-full-20260511-r22\TwinCatAutomationKit.Cli.dll invoke-step --kind=engineering.launch-visual-studio --prog-id=VisualStudio.DTE.17.0 --attach-to-existing=true --launch-timeout-ms=45000 --enable-dialog-auto-dismiss=true
```

结果：

```text
runner compatibility bug: fixed; ProcessStartInfo.ArgumentList can be null in the Windows PowerShell host used here, so runner now falls back to quoted Arguments.
runner stale queue bug: fixed; startup now removes requests that already have responses and restores/removes abandoned .processing.json files.
runner no-response bug: fixed; request parsing/process-start failures now write response exitCode=-2 with runnerError instead of exiting without a response.
runner logging: fixed; child commands run with -NoProfile -NonInteractive -OutputFormat Text -STA, progress is silenced, and CLIXML stdout/stderr is converted to readable text.
runner success probe: .artifacts\interactive-runner\responses\runner-success-probe-20260511-134914.response.json, exitCode=0, stdout=runner-ok.
runner failure probe: .artifacts\interactive-runner\responses\runner-failure-probe-20260511-135111.response.json, exitCode=7, stderr contains runner-fail as readable text.
DTE via runner -Once: .artifacts\interactive-runner\responses\dte-probe-runner-fixed-20260511-134727.response.json, exitCode=1, no hang.
DTE failure remains: VisualStudio.DTE.17.0 ActivityLog still reports Failed to initialize Registry Root Hive and Failure calling InitializeAppID, both 0x80070005 E_ACCESSDENIED.
attach-to-existing=true: no usable ROT DTE was found from the five pre-existing headless devenv.exe processes; fallback launch still hits the same registry hive E_ACCESSDENIED.
```

这证明 `interactive-command-runner.ps1` 这条路径现在适合无人值守投递和收集真实 exit code，不会因为弹窗、旧 request 或 runner 自身异常让 agent 一直等待。但从当前 sandbox/session 启动 runner 时，它没有改变 VS/TcXaeShell registry/profile 权限上下文；DTE 仍在进入 XAE 之前失败。要让 runner 真正帮助完整 OptCNC run，仍需要用户在可用的交互桌面/权限上下文中常驻启动 runner，或先修复 VS/TcXaeShell user hive/pkgdef cache ACL。

2026-05-11 接手 `019e0bf6-81eb-72e2-9470-32db8c921e63` 后继续复查 runner 队列清理。旧 `.artifacts\interactive-runner\requests` 下有多个 request 和 `.processing.json`，对应 response 已存在；当前 sandbox 对这些旧文件执行 `Remove-Item` 返回 `Access denied`。runner 已改成：

```text
-Once with no pending request: exits after cleanup scan instead of polling forever.
Completed request delete failure: warns once per path, then ignores the file because response exists.
Completed request filtering: old request/processing files with response no longer block new requests.
```

追加验证：

```text
runner-once-new-request-20260511-1645:
  command: Write-Output runner-new-ok; exit 0
  response: .artifacts\interactive-runner\responses\runner-once-new-request-20260511-1645.response.json
  result: exitCode=0, stdout=runner-new-ok

runner-once-failure-request-20260511-1646:
  command: Write-Error runner-new-fail; exit 7
  response: .artifacts\interactive-runner\responses\runner-once-failure-request-20260511-1646.response.json
  result: exitCode=7, stderr readable, no CLIXML block
```

结论：当前 runner 队列目录里仍可能留有删不掉的 completed request 文件，但它们不会被重复执行，也不会阻塞新 request。`-Once` 不会在空队列时继续等待。这修的是无人值守通道稳定性，不改变 DTE/XAE 仍因 registry/pkgdef cache 权限失败的主结论。

离线 IO 对比：

```powershell
dotnet .\.artifacts\build-cli-full-20260511-r10\TwinCatAutomationKit.Cli.dll invoke-step --kind=tsproj.describe-io-topology --project-path="D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj" --raw-output=true
dotnet .\.artifacts\build-cli-full-20260511-r10\TwinCatAutomationKit.Cli.dll invoke-step --kind=tsproj.describe-io-topology --project-path="D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj" --raw-output=true
```

结果：

```text
sample: 5 devices, 28 boxes, 4 images, 107 PDOs, 382 PDO entries, 2 MappingInfo, 5 OwnerA, 24 root links
current D:\3rd_year\auto_sln: 5 devices, 28 boxes, 0 images, 0 PDOs, 0 PDO entries, 2 MappingInfo, 5 OwnerA, 24 root links
```

结论：当前目标工程已经有 IO skeleton 和 24 条 root mapping links，但缺少 XAE/ESI 生成的 process image、PDO、SyncMan/FMMU/MBoxUserCmdData/drive-manager metadata。继续补等价性时不能复制 XML，只能通过 XAE/ESI 路线生成或继续补 typed IO model steps。

这轮还修了 integration test probe watchdog 的 dotnet-host 子进程启动问题。之前直接运行：

```powershell
dotnet .\.artifacts\tests-unlocked\TwinCatAutomationKit.IntegrationTests.dll probe-run --kind=...
```

父进程会用 `Environment.ProcessPath` 得到 `dotnet.exe`，再直接追加 `--run-probe-child`，导致 dotnet 把 `--run-probe-child` 当成 dotnet command。`Program.RunChildWithWatchdog` 现在在检测到 dotnet host 时会把当前 test assembly path 作为第一个子进程参数，从而保留 watchdog/timeout 行为并支持 isolated DLL 运行。这属于无人值守稳定性修复：验证工具本身不会因为启动方式不同而假失败或挂住等待人工干预。

## 剩余接口缺口

完整 IO parity 需要继续补 public step，而不是复制样例 XML：

| 缺口 | 目标人类操作 | 稳定节点 | 字段含义 | 推荐 step | 验证 |
|---|---|---|---|---|---|
| root `ImageDatas` bitmap | XAE 根据 ESI/设备生成 process image image-data | `Project/Io/ImageDatas` | process image buffer metadata, image id, byte layout | `engineering.export-io-topology-model` + `tsproj.apply-io-topology-model` 或纯 XAE `engineering.apply-io-tree-plan` 后 `engineering.generate-io-mappings` | `tsproj.assert-io-topology-shape` 断言 image count、box image refs、PDO/FMMU/SyncMan counts |
| box `ImageId` | XAE 创建 EtherCAT box/terminal 后绑定 process image | `Project/Io/Device/Box/@ImageId` | box/terminal process image reference | 同上，必须避免 dangling image refs | guard 检查所有 `Box/@ImageId` 有对应 `ImageData` 或可解释来源 |
| PDO/PDO entry | XAE 根据 ESI 和 device revision 生成 PDO layout | `Box/Pdo` and entries | process data object layout | `engineering.create-ethercat-box` + ESI product revision guard + generated topology export/apply | compare sample/current PDO count and selected PDO entry shape |
| SyncMan/FMMU/MBoxUserCmdData | XAE/ESI 根据 terminal/drive metadata 生成 mailbox/process data mapping | `Box/EtherCAT/*` | EtherCAT communication metadata | XAE/ESI workflow step，不应手写 raw metadata | `tsproj.compare-io-topology` plus activation/ADS RUN |
| drive-manager XML | AX drive setup tool metadata | drive-manager `Xml` nodes | drive-specific configuration payload | dedicated drive configuration step or XAE export/import model with field-level docs | activation and drive-related topology assertion |

## 后续排查入口

优先顺序：

1. 在能写 `D:\3rd_year\auto_sln` 的非 sandbox runner 中重新执行最新 `t\optcnc-auto-sln-from-kit.json`，确认四个 device process-image `Image` 节点是否进入目标 tsproj。
2. 用 `validation.mark-event-log-window` 包住 `engineering.open-xae-solution` 和 `engineering.activate-configuration`，把 XAE crash 分成“打开 solution 崩溃”还是“activation 崩溃”。
3. 如果仍在 `TwinCAT System Manager.x64.dll` 3.1.0.4823 崩溃，先用最小 tsproj 逐步开启 IO skeleton、MotionControl instances、data pointer mappings，定位触发 XAE project load crash 的结构。
4. 不要为了绕过 crash 复制样例 IO XML；如果 XAE 创建完整 IO 树可行，应优先把 `engineering.apply-io-tree-plan`、ESI guard、`engineering.generate-io-mappings` 和 typed topology assertions 串成默认关闭但可验证的路线。
