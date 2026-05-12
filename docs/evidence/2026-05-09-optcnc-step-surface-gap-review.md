# 2026-05-09 OptCNC Step Surface Gap Review / OptCNC Step 接口缺口复核

English summary: This evidence records the 2026-05-09 handoff review of whether the current public step surface can fully reproduce the reference OptCNC TwinCAT project; Scope and runtime guard steps were added, but the IO topology workflow still must not be forced with XML bulk copy or sample metadata cloning.

本文记录接手 `OptcncTwinCAT` 复杂工程生成任务后的第一阶段判断。结论：当前 public step surface 已覆盖 C++ source/item、TMC module model、TcCOM instance、task binding、参数、interface pointer、data pointer mapping、Scope project/config、可解释 IO skeleton、sign/build/activate/ADS 验证主链路，但仍不足以合法完整复现目标样例 `D:\2nd_year\twincat0926\OptcncTwinCAT` 的完整 IO process image/PDO/SyncMan/FMMU/drive metadata。当前机器也不能给出真实 activation/ADS RUN proof。因此本轮没有把 `D:\3rd_year\auto_sln` 硬凑成“完整等价”并重新激活，避免用样例 `.tsproj` 的 TwinCAT metadata 变相复制。

## 快照对比

对比对象：

```text
sample: D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj
current: D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj
```

结构摘要：

| 项目 | sample | current |
|---|---:|---:|
| `.tsproj` size | `289048` bytes | `98382` bytes |
| `Project/Io` | yes | no |
| IO `Device` | `5` | `0` |
| IO `Box` | `28` | `0` |
| IO process `Image` | `4` | `0` |
| IO `Pdo` | `107` | `0` |
| `SyncMan` / `Fmmu` / `MBoxUserCmdData` | `83` / `68` / `75` | `0` / `0` / `0` |
| `Slot` / drive-manager `Xml` | `11` / `2` | `0` / `0` |
| `MappingInfo` / `OwnerA` / `Link` | `2` / `5` / `24` | `0` / `1` / `8` |

System/task 差异：

```xml
sample Settings:
<Settings MaxCpus="64" NonWinCpus="63"><Cpu /><Cpu CpuId="1" /><IoIdleTask Priority="6" /></Settings>

current Settings:
<Settings><Cpu CpuId="1" /><IoIdleTask Priority="6" /></Settings>
```

```text
sample tasks:  ServoLoop Id=2, PlcTask Id=4
current tasks: ServoLoop Id=2, PlcTask Id=3
```

## 本轮已补接口

为避免为了 OptCNC 继续使用 `replace-system-settings-section` 或 `replace-project-io-section`，本轮补了这些 typed public fields：

- `tsproj.ensure-task`: 新增 `TaskId`，可把 `PlcTask` 明确写成样例的 `Id="4"`，并检查 task id 冲突。
- `tsproj.ensure-system-settings`: 新增 `MaxCpus`、`NonWinCpus`、`CpuEntries`、`ReplaceCpuEntries`，可表达样例的空 `<Cpu />` 和 `<Cpu CpuId="1" />`。
- `tsproj.ensure-io-device`: 新增 device-level `EtherCatAttributes`、`EtherCatElements`、`EthernetAttributes`、`EthernetElements`，可表达 `Device/EtherCAT` 和 RT-Ethernet `Device/Ethernet/Esl`。
- `tsproj.ensure-ethercat-box`: 新增 `EtherCatElements`，可表达重复 `SyncMan`、`Fmmu`、`DcMode`、`BootStrapData`、`MBoxUserCmdData`、`CoeProfile`、`DcData`、`Slot` 等 structured elements。
- `IoImageDefinition`: 新增 `ImageFlags`，可表达样例 Device 6 process image 的 `ImageFlags="#x00000030"`。
- `run-plan` dry-run request projection: 补 `tsproj.ensure-task` 和 `tsproj.ensure-system-settings` 新字段展示，避免 summary 丢关键输入。
- `engineering.create-scope-project`: 生成新的 Scope `.tcmproj` 和可选空 `.tcscopex`，并把 Scope project entry 写入 solution；不复制样例 `.sln` 或 Scope project 文件。
- `scope.ensure-configuration`: 通过 typed DTO 写入 Scope `.tcscopex` 的 ADS acquisitions 和 YT chart channels。
- `scope.assert-configuration-shape`: 解析 Scope `.tcscopex` 并断言 Scope 名、chart 名、ADS/channel 数量、symbol 和 acquisition link。
- `invoke-step/run-plan --command-timeout-ms`: 给每个 public step 增加外层 wall-clock timeout，避免无人值守 VS/TwinCAT 弹窗导致测试无限等待；timeout 后只清理本次 step 窗口中新启动的 `devenv.exe` / `TcXaeShell.exe` host，不碰运行前已经存在的 IDE。
- `tsproj.describe-io-topology`: 只读解析 `.tsproj`，输出 normalized Device/Box/PDO/MappingInfo/OwnerA/Link 摘要和可选属性摘要；不返回 raw XML，也不修改工程，用于复杂 IO 差异定位和后续 step 设计证据。
- `tsproj.compare-io-topology`: 只读比较两个 `.tsproj` 的 normalized IO topology facts，报告 count、Device/Box/Image/PDO/PDO Entry、MappingInfo、OwnerA 和 mapping link 差异；不导入 reference metadata，也不输出 raw XML。
- `validation.mark-event-log-window` / `validation.assert-event-log-window`: 在 activation 前后标记并断言 `TcSysSrv` event window，防止 `engineering.activate-configuration` 返回 succeeded 但同一窗口出现 Error/Critical、`AdsState: >15<` Config 回退、`AdsError: 1792` 或 `FPU invalid operation`。
- `t\optcnc-auto-sln-from-kit.json` 新增 `optcnc-io-topology-skeleton.json`、`applyOptCncIoTopologySkeleton` 和 `assertOptCncIoTopologySkeleton`。该 skeleton 只使用 typed IO DTO 表达可解释字段：5 个 disabled Device、28 个 Box 层级、2 个 `MappingInfo` 和 6 条 `TIID^设备 3 (EtherCAT)` 到 `BeckhoffDriver1` 的关键 IO mapping link；不复制 sample raw IO XML，不包含 PDO/SyncMan/FMMU/drive-manager metadata。
- `engineering.create-io-device` / `engineering.create-ethercat-box`: 新增工程级 public step，通过 XAE Automation Interface `ITcSmTreeItem.CreateChild` 创建 IO Device 和 EtherCAT box/terminal。该路径对应人工在 XAE IO tree 添加 EtherCAT master、EK/EL/AX 等节点的操作；不写 sample `.tsproj` metadata，也不 bulk insert XML。Beckhoff 官方说明：EtherCAT master 使用 subtype `111`，E-Bus box/terminal 使用 subtype `9099` 并通过 `vInfo` / product revision 指定具体产品。
- `engineering.generate-io-mappings`: 新增工程级 public step，优先调用当前 `ITcSysManager` COM object 上的 `ITcSmCommands.GenerateMappings` / `ITcSmCommands2.GenerateMappings`；DTE menu command fallback 默认关闭，只有显式 `AllowDteCommandFallback=true` 才尝试 `TwinCAT.生成映射` 等命令。该 step 只执行 XAE 的 GenerateMappings 人类操作，不声明 IO parity，后续仍必须用 `tsproj.describe/compare/assert-io-topology` 验证。
- `engineering.search-io-devices` / `engineering.reload-io-devices`: 新增工程级 public step，分别调用 `ITcSmCommands.SearchDevices` 和 `ITcSmCommands.ReloadDevices` / `ITcSmCommands2` 同名方法。两者不提供 DTE menu fallback，默认 `SuppressUi=true`，并受 step 内 `TimeoutMs`、outer `--command-timeout-ms` 和 dialog watcher 保护，避免无人值守 VS 确认框导致测试无限等待。
- `engineering.apply-io-tree-plan`: 新增工程级 batch public step，只编排 `engineering.create-io-device` 和 `engineering.create-ethercat-box` 的 `CreateChild` 请求；payload 是人类操作参数，不包含 `.tsproj` raw metadata。OptCNC plan 已加入 `optcnc-io-tree-plan.json`、`applyOptCncIoTreeFromXae` 和后续 topology guard，但默认 `includeEngineeringIoTreePlan=false`，避免在缺少本机 XAE/ESI proof 时强行执行未验证的硬件树创建。
- `ethercat.assert-product-revisions`: 新增 file-only public guard，用于在 XAE `CreateChild` 前检查本机 ESI/device-description XML 是否包含 plan 中的 `productRevision/vInfo`。OptCNC plan 已加入 `optcnc-io-tree-product-revisions.json` 和 `assertOptCncIoTreeProductRevisions`，默认跟 `includeEngineeringIoTreePlan` 一起关闭；打开工程级 IO tree 路线时，它会先失败在缺 ESI 的可解释位置，而不是进入 VS/XAE 弹窗或设备选择等待。

验证：

```powershell
dotnet build .\TwinCatAutomationKit.sln --no-restore /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -- generate-docs
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -- run-plan --file=t\optcnc-auto-sln-from-kit.json --dry-run=true --summary=.artifacts\optcnc-step-surface-gap-dry-run-summary.json
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj --no-restore
```

结果：

```text
build: succeeded, 0 errors; MSB3101 obj cache write warnings only
generate-docs: succeeded
dry-run: plan optcnc-auto-sln-from-kit, 458 steps, failed 0, skipped 4
summary: .artifacts\optcnc-step-surface-gap-dry-run-summary.json
integration initial run: failed before XAE/DTE because configured WorkRootBase D:\3rd_year\TwinCatAutomationKit\t was not writable in this session
integration after unattended hardening: WorkRootBase fell back to D:\3rd_year\TwinCatAutomationKit_push\t; child process watchdog terminated a VS/DTE launch hang after 45000 ms
after data-pointer guard update: dry-run plan has 461 steps, failed 0, skipped 6; direct guard against D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj succeeded for AxesGroup0 with 6 data pointer records and 8 root mapping links
after Scope + outer timeout update: build succeeded; generate-docs succeeded; verify-all invoke dry-run had 65 invocations, 64 unique executed kinds, missing 0, failed 0; OptCNC dry-run has 464 steps, failed 0, skipped 6
after IO compare update: verify-all invoke dry-run had 67 invocations, 66 unique executed kinds, missing 0, failed 0
after event-log activation guard: verify-all invoke dry-run had 67 invocations, 66 unique executed kinds, skipped 12, missing 0, failed 0; OptCNC dry-run has 466 steps, failed 0, skipped 8
after IO skeleton plan update: OptCNC dry-run has 468 steps, failed 0, skipped 8; uniqueKinds=43, ioSkeletonSteps=2
after engineering IO CreateChild step update: build succeeded; generate-docs succeeded; verify-all invoke dry-run had 69 invocations, 68 unique executed kinds, skipped 12, missing 0, failed 0
after engineering GenerateMappings step update: build succeeded; generate-docs succeeded; verify-all invoke dry-run had 70 invocations, 69 unique executed kinds, skipped 12, missing 0, failed 0
after engineering SearchDevices/ReloadDevices step update: build succeeded; generate-docs succeeded; verify-all invoke dry-run had 72 invocations, 71 unique executed kinds, skipped 12, missing 0, failed 0; verify-all commands now explicitly include --enable-dialog-auto-dismiss=true and --dialog-poll-interval-ms=500
after engineering ApplyIoTreePlan wrapper update: build succeeded; generate-docs succeeded; verify-all invoke dry-run had 73 invocations, 72 unique executed kinds, skipped 12, missing 0, failed 0
plan scan after optional IO tree route: totalSteps=473, uniqueKinds=47, ioTreeSteps=1, ioSkeletonSteps=2, includeEngineeringIoTreePlan=false, includeIoSkeleton=true, badKinds=""
after EtherCAT ESI guard update: OptCNC dry-run has 474 steps, failed 0, skipped 14; `assertOptCncIoTreeProductRevisions` is step 458, directly before `applyOptCncIoTreeFromXae`
full OptCNC IO tree ESI scan: 19 unique productRevision values, 15 matched, 4 missing (`00F5060-F000`, `iA-GAI04A0_2xMII_REV_00020020`, `iA-MPA64B0 2xMII_REV00030022`, `iA-MPS32A0 2xMII 00020020`)
Beckhoff subset ESI scan: CU2508, EK1100, EL1889/1904/2889/2904/6910/9011/9410, AX5118/5125/5140/5160, AX5805/5806 all matched installed Beckhoff ESI XML
verify-all invoke dry-run after EtherCAT ESI guard: 74 invocations, 73 unique executed kinds, skipped 12, missing 0, failed 0
ethercat.assert-product-revisions probe: succeeded as file-only probe
ADS state guard: ports 10000/200/300 all failed with 0x748 Port is not open, so this machine is not in acceptable RUN state
command-timeout-ms probe: engineering.launch-visual-studio with --command-timeout-ms=1 failed immediately with a timeout instead of waiting for the DTE launch timeout
tsproj.describe-io-topology probe: succeeded as file-only step without launching DTE
tsproj.compare-io-topology probe: succeeded as file-only step without launching DTE
validation.mark-event-log-window probe: succeeded as file-only validation probe
validation.assert-event-log-window probe: succeeded as file-only validation probe
sample describe: 5 Device, 28 Box, 4 Image, 107 PDO, 382 PDO Entry, 2 MappingInfo, 5 OwnerA, 24 Link
current describe: 0 Device, 0 Box, 0 Image, 0 PDO, 0 PDO Entry, 0 MappingInfo, 1 OwnerA, 8 Link
sample/current compare: failed as expected; IO topology comparison found 25 difference(s) before truncation
event-log smoke: marker succeeded at 2026-05-09T20:59:21.3175384+08:00 with lastEntryIndex 44086; immediate assert observed 0 TcSysSrv events and no forbidden events
IO skeleton direct target run: blocked by sandbox with "Access to the path 'D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj' is denied."
IO skeleton probe copy: apply succeeded on t\optcnc-io-skeleton-probe\OptcncTwinCAT.tsproj; assert matched 5 Device, 28 Box, 2 MappingInfo, 14 Link
IO skeleton probe describe: 5 Device, 28 Box, 0 Image, 0 PDO, 0 PDO Entry, 2 MappingInfo, 2 OwnerA, 14 Link
IO skeleton probe compare vs sample: DeviceCount and BoxCount now match; ImageCount 4 vs 0, PdoCount 107 vs 0, PdoEntryCount 382 vs 0, OwnerACount 5 vs 2, RootMappingLinkCount 24 vs 14; first remaining diffs are Box ImageId plus Pdo/Fmmu/SyncMan child counts
```

integration failure excerpt:

```text
Access to the path 'D:\3rd_year\TwinCatAutomationKit\t\ddae498e' is denied.
7 of 7 integration tests FAILED.
```

后续修正见 [2026-05-09 Unattended VS and ADS State Assertion](2026-05-09-unattended-vs-ads-state-assertion.md)：integration runner 现在会验证 work root 子目录写入能力，不可写时 fallback 到 repo 内短路径 `t`；默认父进程 watchdog 也会在 VS/DTE hang 时杀掉子进程树。当前机器真实 XAE proof 仍被 `VisualStudio.DTE.17.0` 启动/附着 hang 阻塞，不再是 work root 权限问题。

本轮还用临时 `.artifacts\optcnc-step-surface-probe` 验证新增字段能写出：

```text
Settings MaxCpus/NonWinCpus
empty Cpu plus CpuId=1
PlcTask Id=4
Device/Ethernet/Esl
repeated Box/EtherCAT/DcMode
```

当前工具安全策略拦截了删除该临时探针目录的 `Remove-Item` 命令；该目录不是源码，不应提交。

本轮新增 `tsproj.describe-io-topology` 后，使用 public read-only step 重新量化了 IO 差异：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.describe-io-topology --project-path=D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj --max-items-per-collection=5 --command-timeout-ms=60000
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.describe-io-topology --project-path=D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj --max-items-per-collection=5 --command-timeout-ms=60000
```

结果：

```text
sample:  IO topology described: 5 device(s), 28 box(es), 4 image(s), 107 PDO(s), 24 mapping link(s).
         imageCount=4, pdoEntryCount=382, mappingInfoCount=2, ownerACount=5
current: IO topology described: 0 device(s), 0 box(es), 0 image(s), 0 PDO(s), 8 mapping link(s).
         imageCount=0, pdoEntryCount=0, mappingInfoCount=0, ownerACount=1
```

该 step 的输出是 normalized facts，不包含 sample 的 `SyncMan`、`Fmmu`、`MBoxUserCmdData`、drive-manager `<Xml>` 等 raw metadata 字符串，因此可作为 gap 证据和后续 DTO 设计输入，但不能被当成“从样例导出后再导入”的工程生成路径。

随后新增 `tsproj.compare-io-topology`，把样例与生成物的 normalized 差异变成 public guard：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.compare-io-topology --project-path=D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj --reference-project-path=D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj --max-differences=25 --command-timeout-ms=60000
```

结果：

```text
IO topology comparison found 25 difference(s) before truncation.
```

这条 step 的作用是验收和缺口定位：它能防止 plan 在 IO 仍缺失时误报等价，也能指出 process image / PDO entry 层级缺失，但不能被用作“把 reference topology 克隆到 candidate”的导入接口。

本轮进一步把可解释 IO skeleton 接进 OptCNC JSON plan：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.apply-io-topology-plan --project-path=D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj --json-file=t\optcnc-io-topology-skeleton.json --command-timeout-ms=60000
```

对真实目标路径执行时被当前 sandbox 拦截：

```text
Access to the path 'D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj' is denied.
```

对 repo 内 probe copy 执行同一 public step 后通过：

```text
Applied IO topology plan: 5 device(s), 28 box(es), 0 PDO(s), 2 MappingInfo item(s), 6 link(s).
IO topology shape matched: 5 device(s), 28 box(es), 0 PDO(s), 14 link(s).
IO topology described: 5 device(s), 28 box(es), 0 PDO(s), 14 mapping link(s).
```

其中 14 条 link 是当前 auto_sln 副本已有的 8 条 `AxesGroup0` data pointer link 加上新写入的 6 条 IO-to-`BeckhoffDriver1` link。完整 plan 顺序会先重建 Axis/Sim/Beckhoff internal links，再执行 IO skeleton，因此 `assertOptCncIoTopologySkeleton` 在主 plan 中要求 24 条 root link 和 5 个 `OwnerA`。

再用 `tsproj.compare-io-topology` 对 sample 和 skeleton probe copy 做 normalized diff：

```powershell
dotnet run --no-build --no-restore --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.compare-io-topology --reference-project-path=D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj --project-path=t\optcnc-io-skeleton-probe\OptcncTwinCAT.tsproj --include-mappings=true --include-attributes=false --max-differences=12 --raw-output=true --command-timeout-ms=60000
```

结果：

```text
DeviceCount: reference 5, candidate 5, matches true
BoxCount: reference 28, candidate 28, matches true
ImageCount: reference 4, candidate 0, matches false
PdoCount: reference 107, candidate 0, matches false
PdoEntryCount: reference 382, candidate 0, matches false
MappingInfoCount: reference 2, candidate 2, matches true
OwnerACount: reference 5, candidate 2, matches false
RootMappingLinkCount: reference 24, candidate 14, matches false
first diffs: Box ImageId values plus Box 3/3 and 3/4 PdoCount/PdoEntryCount/Fmmu/Pdo/SyncMan child counts
```

这证明 skeleton 没有伪装成完整 IO parity：它只把可解释拓扑和关键 IO link 放进 plan，剩余差异仍集中在 XAE/ESI/drive tool 生成的 process image、PDO、SyncMan/FMMU 和 root mapping owner/link 细节。

随后把工程级 IO tree 创建路线作为默认关闭通道接入同一个 OptCNC JSON plan：

```text
payload: optcnc-io-tree-plan.json
step: applyOptCncIoTreeFromXae -> engineering.apply-io-tree-plan
guard: assertOptCncIoTreeFromXae -> tsproj.assert-io-topology-shape
default: includeEngineeringIoTreePlan=false
```

该 payload 使用 sample 中可读的产品名 / revision 线索形成 `CreateChild` 参数，例如 `EK1100-0000-0018`、`EL1889-0000-0019`、`AX5125-0000-0214`、`iA-MPS32A0 2xMII 00020020`。这不是导入 sample `Box` XML；它不包含 `Pdo`、`SyncMan`、`Fmmu`、drive-manager `<Xml>` 或 `MBoxUserCmdData`。打开该路线后必须由 XAE 成功创建 tree item，并由后续 topology guard 证明结构保留；当前机器还没有这条真实 proof，所以默认仍使用 typed skeleton 路线。

随后补了 `tsproj.assert-data-pointer-shape`，用于防止 XAE activate/save 删除 data pointer 时 plan 仍误判成功。OptCNC plan 现在在 8 条 data pointer mapping link 写入后、以及 activation 后各执行一次 guard：

```text
AxesGroup0 data pointer records: 6
root Mappings critical links: 8
```

直接对当前生成物验证结果：

```text
Data pointer shape for AxesGroup0 matched: 6 data record(s), 8 mapping link(s).
```

## 仍然不能硬凑的 gap

### 1. IO topology 来源语义缺口

目标人类操作：在 XAE 中扫描/添加 EtherCAT master、CU2508、EK1100、EL/AX terminals、drive/safety terminal，并让 TwinCAT 生成对应 PDO、SyncManager、FMMU、mailbox command、slot 和 drive-manager metadata。

官方依据：

- Beckhoff `ITcSmTreeItem::CreateChild`: `CreateChild(name, subtype, before, vInfo)` 是 Automation Interface 创建 child tree item 的稳定 COM 操作；`vInfo` 由 subtype 决定。
- Beckhoff `Creating and handling EtherCAT devices`: EtherCAT master 用 subtype `111`，EtherCAT slave 用 subtype `130`；EtherCAT boxes 通过 parent EtherCAT master 上的 `CreateChild` 创建。
- Beckhoff `ITcSmTreeItem Item Sub Types: E-Bus`: E-Bus boxes/terminals/modules 共用 subtype `9099`，具体产品由 `vInfo` 的 product revision 决定，例如 `EK1100-0000-0017`。

样例差异：sample 有 5 个 device、28 个 box、4 个 process image、107 个 PDO、382 个 Entry，并包含大量 `SyncMan`、`Fmmu`、`MBoxUserCmdData`、`Slot` 和 drive-manager `<Xml>`；当前 plan 已能通过 typed skeleton 表达 5 Device、28 Box、2 MappingInfo 和 6 条关键 IO mapping link，但仍不能合法表达 XAE/ESI 生成的 process image/PDO/SyncMan/FMMU/drive metadata。

稳定节点：`/TcSmProject/Project/Io/Device/Box/EtherCAT` 和 `/TcSmProject/Mappings`。

字段含义：`Pdo/Entry` 可以解释为 process data；`SyncMan`、`Fmmu`、`MBoxUserCmdData`、`SlotData`、AX drive-manager `<Xml>` 多数是 XAE/ESI/drive tool 生成的硬件 metadata。没有真实扫描/import evidence 时，把 sample 这些十六进制串直接转成 JSON payload，本质仍是复制 TwinCAT metadata。

推荐 step：

```text
engineering.create-io-device              已实现：subtype/vInfo/Disabled/CreateChild
engineering.create-ethercat-box           已实现：subtype 9099 + ProductRevision/vInfo/CreateChild
engineering.scan-io-devices               未实现：真实硬件扫描
engineering.generate-io-mappings          已实现：优先 ITcSmCommands.GenerateMappings，显式允许时才 DTE fallback
engineering.export-io-topology-model      未实现：从 XAE/ESI 生成 topology model，不输出 raw sample XML
tsproj.apply-io-topology-model            未实现：只消费受控导出器 model，不消费 sample `.tsproj` XML
```

其中 `apply-io-topology-model` 只能消费由同版本 XAE/ESI 或受控导出器生成、带 source/evidence 的 model，不能让调用方随手粘 sample `.tsproj` XML。

验证方式：XAE reopen 后 IO tree 可见；导出 `TIID` tree XML；normalized topology diff 覆盖 5 Device、28 Box、4 Image、107 PDO、382 Entry、24 Link；disabled hardware 第一阶段可只要求 reopen + build，真实硬件才做 activation/process image readback。

### 2. Scope project public step 已补，仍需真实 XAE reopen proof

目标人类操作：在 solution 中创建/导入 TwinCAT Scope project，并配置样例中的 Scope 文件。

样例差异：sample 有 `Scope\Scope.tcmproj` 和 `Scope Project1.tcscopex`。本轮之前 caller plan 未复现该 project；当前 plan 已新增 `engineering.create-scope-project`、`scope.ensure-configuration`、`scope.assert-configuration-shape`。

稳定节点：VS solution project entries 和 Scope project 文件。不要复制 sample `.sln` 或 scope metadata。

已实现 step：

```text
engineering.create-scope-project
scope.ensure-configuration
scope.assert-configuration-shape
```

本轮 typed Scope config 覆盖样例中可解释的两条 ADS acquisition/channel：

```text
BufferWriteAvailable -> CommandsExecuter.Data.StateData.BufferWriteAvailable, target port 351, offset 2197815308
BufferReadAvailable  -> CommandsExecuter.Data.StateData.BufferReadAvailable,  target port 351, offset 2197815312
YT Chart channels: BufferReadAvailable, BufferWriteAvailable
```

验证结果：

```text
scope.ensure-configuration probe: succeeded
scope.assert-configuration-shape probe: succeeded
verify-all invoke dry-run: includes both scope steps, missing 0, failed 0
OptCNC plan dry-run: steps 7/8 are scope.ensure-configuration and scope.assert-configuration-shape
```

剩余 proof：本机 `VisualStudio.DTE.17.0` 仍无法稳定启动/附着，因此还没有真实 XAE reopen 后 Scope project 可见的 evidence。该缺口不能用复制 sample Scope XML 替代。

### 3. Activation RUN 和事件窗口验收

目标人类操作：激活后证明 `port 10000 = Run`，同时 `200/300 = Run`，并确认没有回到 Config。

差异：`validation.ads-scan` 输出 `portsJson`，但 step success 只表示至少一个 port reachable，不会按 plan 声明 expected ADS state；`engineering.activate-configuration` 返回 succeeded 也不能证明 TwinCAT 保持 RUN，必须检查 activation 同一窗口内的 `TcSysSrv` 日志。

本轮已补 steps：

```text
validation.mark-event-log-window
validation.assert-ads-state
validation.assert-event-log-window
```

核心 DTO：

```text
MarkEventLogWindowRequest: LogName, ProviderName, MarkerFilePath
AssertEventLogWindowRequest: MarkerFilePath, FailOnErrorOrCritical, FailOnConfigAdsState, FailMessageContains
AssertAdsStateRequest: NetId
ExpectedPorts: [{ Port, AdsState, DeviceState }]
```

验证方式：activation 前执行 event-log marker；activation 后执行 event-log assert 和 ADS state assert。失败时输出每个 port 的实际 state，并列出同一窗口内的 `TcSysSrv` forbidden events。本机验证中 `10000/200/300` 返回 `0x748 Port is not open`，step 按预期失败，防止把非 RUN 状态误认为通过；event-log marker/assert smoke 也已验证可无人值守执行且不启动 DTE。

## 本轮未执行完整生成/激活的原因

验收要求是完整生成/修复 `D:\3rd_year\auto_sln\OptcncTwinCAT.sln`、build、sign、activate、ADS scan 证明 `10000 = Run`，且无新 `TcSysSrv` error。当前 public steps 仍不能合法表达 sample 的完整 IO topology 人类操作；当前 sandbox 也禁止写 `D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj`，所以本轮只能把 IO skeleton 接进 plan 并在 repo 内 probe copy 验证。当前机器 ADS `10000/200/300` 也都是 `0x748 Port is not open`，不能给出 RUN proof。如果继续生成再激活，只能得到“缺完整 IO process image/PDO/drive metadata 的 MotionControl/Scope/IO skeleton 工程”或通过 sample metadata 变相复制。两者都不满足本任务“尽量等价于目标样例”与“禁止 TwinCAT metadata XML 复制/大块粘贴”的组合约束。

后续路线应先补 IO 的真实 XAE/ESI public workflow，或明确降低验收范围为“MotionControl + Scope config + IO skeleton parity without full process image/PDO/drive metadata”。降低范围也必须写入 evidence，不能把已有 MotionControl RUN 或 IO skeleton 当成完整 OptCNC sample parity。
