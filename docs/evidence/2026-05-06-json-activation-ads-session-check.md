# 2026-05-06 JSON Activation ADS Session Check / JSON 激活与 ADS 会话检查

English summary: This evidence records a follow-up validation attempt for the previous JSON-created TwinCAT activation issue; it did not reproduce AdsError 1795, but the current Codex session could not reach VS DTE or ADS runtime.

本文记录一次针对“JSON + CLI 创建 TwinCAT 工程后 activation 报 `AdsError 1795`”的复查。结论要分开看：仓库已有 2026-04-30 真实机器 evidence 证明 `1795` 根因已修复；本轮 2026-05-06 没有复现 `1795`，但也没有成功重新跑到 activation 层，因为当前 Codex 会话里的 Visual Studio DTE 和 ADS runtime 都不可用。

## 本轮结论

- 没有看到 `AdsError 1795` 回归。
- 当前 `examples/json-plans/complex-full-project.json` 的默认 runtime-safe 设置仍在：
  - `includeDataPointerValues=false`
  - `includeActivation=false`
  - `includeAdsRead=false`
  - `applyDataPointerPlan` 只有在 `includeDataPointerValues=true` 时执行
  - mapping 使用 `Input^DataIn` 和 `Output^DataOut`
- 当前 fallback C++ module 仍使用 process-image data areas：
  - `Input` / `DataIn` 使用 `AreaType=InputDst`
  - `Output` / `DataOut` 使用 `AreaType=OutputSrc`
  - `InitDataPointer()` 不再要求未绑定 smart data pointers 进入 OP
- 离线测试通过，说明 step catalog、CLI surface、JSON plan resolution 和 `.tsproj` mutation 没有明显回退。
- 本轮没有完成新的真实 activation/ADS readback 证明；阻塞点是当前会话环境，不是 activation 返回 `1795`。

## 执行过的验证

离线回归：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests\TwinCatAutomationKit.Tests.csproj
```

结果：

```text
All 198 tests passed.
```

过程中有一个非阻塞 warning：

```text
MSB3101: failed to write ... TwinCatAutomationKit.Tests.csproj.AssemblyReference.cache
Access to the path ... is denied.
```

JSON plan dry-run：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json --dry-run=true --summary=.artifacts\verify-complex-dryrun-summary.json
```

结果：

```text
Summary
  Steps:     35
  Succeeded: 0
  Skipped:   5
  Failed:    0
```

被 skip 的 5 个 step 是按 plan flag 显式关闭的 runtime/machine-state step：signing settings、data pointer plan、activation、ADS scan、ADS read symbols。

## 集成测试阻塞

第一次直接运行：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

结果：22 个集成测试都在创建 `D:\t\...` work directory 时失败：

```text
Access to the path 'D:\t\...' is denied.
```

随后临时写入本机 `integration-test-config.json`，把 `WorkRootBase` 指到 repo 内 `.artifacts\it`，再运行集成测试。该临时配置已在本轮结束前删除，没有保留为 source 文件。

第二次结果：22 个集成测试都在启动 `VisualStudio.DTE.17.0` 时失败：

```text
Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.
Inner: Retrieving the COM class factory for component with CLSID {33ABD590-0400-4FEF-AF98-5F5A8A99CFC3} failed due to the following error: 80080005 CO_E_SERVER_EXEC_FAILURE.
```

这表示本轮没有进入 XAE project create/build/activation 行为层。

## 单独 activation 和 ADS 验证

尝试对已有 `.artifacts` 中的 `complex-full-project` 执行 activation：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.activate-configuration --solution-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo.sln" --project-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\Demo.tsproj" --visible=false --startup-delay-ms=8000 --save-configuration-archive=true --configuration-archive-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\_json_plan_evidence\verify-activated.tszip"
```

结果仍是 DTE 启动失败：

```text
invoke-step failed for engineering.activate-configuration
Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.
```

ADS scan：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.ads-scan --net-id=local --ports=100,200,300,800,851,852,10000
```

结果：

```text
Status: Failed
Result: ADS scan found no reachable ports for local.
openPortCount=0
failedPortCount=7
100/200/300/800/851/852/10000 -> Port is not open. (AdsErrorCode: 1864, 0x748)
```

ADS multi-read：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.ads-read-symbols --net-id=local --port=851 "--symbols=MAIN.nSeed:UInt32;MAIN.nStage1:UInt32;MAIN.bPipelineOk:Boolean;MAIN.nMismatchCount:UInt32" --auto-reconnect=true
```

结果：

```text
Status: Failed
MAIN.nSeed / MAIN.nStage1 / MAIN.bPipelineOk / MAIN.nMismatchCount all failed:
Port is not open. (AdsErrorCode: 1864, 0x748)
```

这说明当前会话无法看到运行中的 ADS endpoint；它没有证明生成工程错误，也没有证明 `1795` 回归。

## 与 2026-04-30 evidence 的关系

[2026-04-30 ADS Port 0x748 Runtime State](2026-04-30-ads-port-0748-runtime-state.md) 仍是当前关于 `1795` 是否修复的主要真实机器 evidence：

- `1795 invalid indexOffset` 来自 fallback TMC/TmcDesc 中 unsupported `TraceLevelMax / PTCID #x03002103`。
- 后续 `1803` 来自 fallback C++ OP 阶段强制初始化未绑定 smart data pointers。
- 最终修正后真实 activation 成功，系统 Run，851 可读，没有新的 `1795/1803`。
- C++/PLC mapping 链路用 `MAIN.nStage1=128`、`MAIN.bPipelineOk=True`、`MAIN.nMismatchCount=0` 证明通过。

本轮只补充了“2026-05-06 当前 Codex 会话不可作为新的 activation proof”的事实。

## 后续排查入口

如果用户手动 Visual Studio/XAE 可用，但 Codex 会话不可用，优先排查这些环境差异：

1. 当前 Codex 进程是否运行在非交互 session、不同完整性级别或不同用户 token。
2. 是否有残留 `devenv` / `dotnet` automation 进程占用 DTE 或卡在启动。
3. `VisualStudio.DTE.17.0` COM registration 是否指向可启动的 VS executable。
4. ADS router / TwinCAT services 是否在当前用户 token 下可见，当前 token 是否包含 `TcAmsUsers`。
5. TwinCAT runtime 是否处于 Run；只有 `validation.ads-scan` 至少能读到 `100` 或 `10000` 后，再判断 PLC port `851` 和 symbol path。

后续如果要证明“JSON 生成工程后 ADS 全链路已解决”，应重新跑一轮真实机器流程：`run-plan` with activation/read flags enabled，随后保留 `run-summary.json`、`activated.tszip`、`validation.ads-scan` 和 `validation.ads-read-symbols` 输出。

## 2026-05-06 后续环境诊断

继续排查后确认了两类独立问题。

### 当前 ADS/runtime 状态

当前用户 token：

```text
user: ZY_ENGINEER\david
integrity: Medium Mandatory Level
TcAmsUsers: present and enabled
Administrators: deny-only
```

`TcSysSrv` 服务存在且处于 `Running`，`TcSystemServiceUm` 监听了 ADS 相关端口：

```text
TCP 0.0.0.0:48898 LISTENING
UDP 0.0.0.0:48899
process: TcSystemServiceUm
```

但 Windows System event log 显示本机 TwinCAT 在 `2026-05-06 08:49:44` 启动完成时的 state 是：

```text
TwinCAT system start completed. AdsState: >15<
```

`15` 对应 Config state，不是 Run state。因此本轮 `validation.ads-scan` 返回全部 `0x748 Port is not open` 的直接原因是 runtime 端口没有处于可读运行态。当前 boot 配置仍指向：

```text
C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_Default\3.1\Boot\CurrentConfig.xml
D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\Demo.tsproj
```

`CurrentConfig.xml` 中仍有 `Demo_Obj1 (DemoCpp)` 和 `PlcA Instance` 的 object creation 记录。这说明当前失败不是 boot config 被清空，也不是 JSON 工程路径丢失；更准确地说，是当前 TwinCAT 没有在 Run state 暴露 ADS ports。

### 当前 DTE/COM 状态

`VisualStudio.DTE.17.0` 注册存在：

```text
CLSID={33ABD590-0400-4FEF-AF98-5F5A8A99CFC3}
LocalServer32="D:\large_software2\2022\Microsoft Visual Studio\2022\Community\common7\ide\devenv.exe"
```

VS installation 也由 `vswhere` 报告为 complete/launchable：

```text
Visual Studio Community 2022
installationVersion=17.14.37012.4
isComplete=true
isLaunchable=true
```

但是 COM automation 路径失败。System event log 连续出现：

```text
Microsoft-Windows-DistributedCOM 10010
服务器 {33ABD590-0400-4FEF-AF98-5F5A8A99CFC3} 没有在要求的超时时间内向 DCOM 注册。
```

本轮曾发现一个无窗口 `devenv.exe` 残留：

```text
PID=30564
MainWindowHandle=0
MainWindowTitle=<empty>
GetActiveObject("VisualStudio.DTE.17.0") -> no active DTE
```

该无窗口残留进程已结束；结束后重新创建 `VisualStudio.DTE.17.0` 仍失败为 `80080005 CO_E_SERVER_EXEC_FAILURE`。因此不是单个残留 `devenv` 进程造成的占用。

机器上也注册了 `TcXaeShell.DTE.17.0`：

```text
CLSID={47f09213-63bf-4966-9a9f-cf03a1e94334}
LocalServer32=C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe
```

但 `TcXaeShell.DTE.17.0` COM 创建同样失败为 `80080005 CO_E_SERVER_EXEC_FAILURE`。这说明问题不是单纯用错 `VisualStudio.DTE.17.0`；当前会话里 VS/XAE local COM server 都无法按 DTE automation 模式完成 DCOM registration。

### 当前判断

本机手动打开 XAE/VS 可以工作，并不等价于 DTE automation 可工作。当前证据指向：

1. TwinCAT runtime 当前在 Config state，所以 ADS ports 对 `validation.ads-*` 不可读。
2. Codex/CLI 无法通过 DTE automation 把工程 activate/start 到 Run，因为 VS/XAE COM server 没有注册成功。
3. `TcAmsUsers` 不是本轮首要问题；当前 token 里已经包含该组。
4. `1795` 没有在本轮出现；当前阻塞点在 activation 前。

下一步应先恢复 DTE automation 或提供非 DTE activation/start path，再重新跑 JSON plan 的 activation + ADS readback。

## 2026-05-06 用户手动 activation 后 ADS 验证通过

用户随后按手动流程打开 JSON/CLI 生成的 solution 并确认 activation 可以正常完成。activation 后在同一仓库根目录执行 public ADS validation steps，结果证明当前 runtime、PLC port `851`、PLC symbols、C++/PLC process-image mapping 链路均正常。

ADS scan command：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.ads-scan --net-id=local --ports=100,200,300,800,851,852,10000
```

结果摘要：

```text
Status: Succeeded
openPortCount=5
failedPortCount=2

100   reachable, adsState=Invalid, deviceName=TCatSysSrv
200   reachable, adsState=Run,     deviceName=RTime(Um)
300   reachable, adsState=Run,     deviceName=I/O Server
800   failed,    AdsErrorCode=6 Target port could not be found
851   reachable, adsState=Run,     deviceName=Plc30 App
852   failed,    AdsErrorCode=6 Target port could not be found
10000 reachable, adsState=Run,     deviceName=TwinCAT System
```

`800` 和 `852` 不存在不是本轮失败信号；关键是 `10000`、runtime、I/O 和 PLC `851` 都可达，且 `851` 是 `Run`。

ADS readback command：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.ads-read-symbols --net-id=local --port=851 "--symbols=MAIN.nSeed:UInt32;MAIN.nStage1:UInt32;MAIN.bPipelineOk:Boolean;MAIN.nMismatchCount:UInt32" --auto-reconnect=true
```

结果：

```text
Status: Succeeded
succeededCount=4
failedCount=0

MAIN.nSeed=5
MAIN.nStage1=128
MAIN.bPipelineOk=True
MAIN.nMismatchCount=0
```

本轮补充结论：

- JSON/CLI 生成的当前 `complex-full-project` 可以被手动 XAE activation 正常激活。
- activation 后没有报告 `AdsError 1795`。
- ADS runtime proof 通过：`851` 端口 Run，四个关键 symbol 全部读到期望值。
- 这与 2026-04-30 evidence 的最终结论一致：`1795` 根因已修复，C++/PLC mapping 链路也已打通。
- 仍未覆盖的是 Codex/CLI 当前自动 DTE activation 通道；该通道的阻塞点仍是 DTE COM server `80080005`，不是 generated TwinCAT project 的 runtime/ADS 行为。
