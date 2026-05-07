# 2026-04-30 ADS Port 0x748 Runtime State / ADS 端口 0x748 运行时状态

English summary: This evidence records the real-machine ADS/runtime investigation and the later C++/PLC process-image mapping fix for the Demo project.

本文记录一次真实机器 ADS 排查，以及后续把 C++/PLC mapping 链打通的修正。目标是让后续 agent 不要继续误判为 PLC symbol path、单个 `851` 端口问题或 Visual Studio 问题。

## 2026-04-30 14:33 最终验证结论

- `1795 invalid indexOffset` 已确认来自 fallback TMC/TmcDesc 中 unsupported `TraceLevelMax / PTCID #x03002103`，已移除。
- `1803 invalid parameter value(s)` 已确认来自 fallback C++ OP 阶段强制初始化未绑定 smart data pointers，已改为不依赖 `m_spDataIn.Init()` / `m_spDataOut.Init()`。
- `MAIN.nStage1 = 0` 的后续问题不是 ADS/runtime 未通，而是 C++ fallback TMC 把 `DataIn/DataOut` 放在 `AreaType=Standard` 的 `Data` 区，mapping 虽被 XAE 保存但不参与 process image。
- 最终修正是把 C++ fallback module 改成 process-image data areas：`Input`/`DataIn` 使用 `AreaType=InputDst`，`Output`/`DataOut` 使用 `AreaType=OutputSrc`，JSON mapping 改为 `Input^DataIn` 和 `Output^DataOut`。
- 真实机器 activation 后，XAE 在 `.tsproj` 中生成了 root `<Mappings>` 的 `MappingInfo`，`UnrestoredVarLinks=0`。
- ADS scan 结果：`100`、`200`、`300`、`800`、`851`、`10000` 均为 `Run`，`852` 为预期的 `0x6 Target port could not be found`。
- ADS readback：

```text
MAIN.nSeed          UInt32  -> 5
MAIN.nStage1        UInt32  -> 128
MAIN.bPipelineOk    Boolean -> True
MAIN.nMismatchCount UInt32  -> 0
```

这证明当前 runtime、ADS、C++ 输入、C++ CycleUpdate、C++ 输出到 PLC 输入的链路已打通。

## 结论

- 当前失败不是 `MAIN.nSeed`、`MAIN.nValue` 等 symbol path 问题。
- 当前失败也不是单独 PLC runtime `851` 问题；本地 ADS 扫描显示 `100`、`200`、`300`、`800`、`851`、`852`、`10000` 全部返回 `Port is not open (AdsErrorCode: 1864, 0x748)`。
- `TcSysSrv` 服务处于 `Running`，但当前普通 token 无法重启服务，也无法结束残留 `devenv` 进程。
- 当前登录 token 的 `whoami /groups` 没有显示 `TcAmsUsers`，虽然 `Get-LocalGroupMember -Group TcAmsUsers` 显示 `ZY_ENGINEER\david` 已经在该组。后续优先怀疑 token 未刷新或 ADS 权限上下文不一致。
- `C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_Default\3.1\Boot\CurrentConfig.xml` 是本轮实际 TwinCAT boot 配置，比工程目录下的 `_Boot` snapshot 更能代表当前 runtime 尝试加载的内容。

## 当前工程状态

调查对象：

```text
D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo.sln
D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\Demo.tsproj
```

`Demo.tsproj` 已确认不再是早期的 `TraceLevelMax` assigned value 残留问题。2026-04-30 后续修正还确认：默认 fallback C++ module 代码没有处理 `PID 0x03002103`，所以 `TraceLevelMax` 也不应继续出现在 fallback TMC/instance TmcDesc 的 `Parameters` 定义区。

当前 C++ instance `Demo_Obj1 (DemoCpp)` 下有：

```text
ParameterValues:
  Parameter.data1 = 123

InterfacePointerValues:
  CyclicCaller -> #x02010040

DataPointerValues:
  empty container only
```

当前 `DemoCpp.tmc` 和 `Demo.tsproj` 中均不应再出现：

```text
TraceLevelMax
#x03002103
```

原因是 fallback 生成的 C++ 源码 `SETOBJPARA_MAP` 只支持 `PID_DemoCppParameter = 0x00000001`、data pointers 和 `CyclicCaller`，不支持 `TraceLevelMax` 对应的 `PID 0x03002103`。如果 TMC 仍声明该参数，TwinCAT activation 会按 TMC 尝试 `TcSetObjPara(PID=0x03002103)`，对象返回 `AdsError 1795 (invalid indexOffset)`。

## 2026-04-30 1795 后续修正

继续排查 `Create Object Demo_Obj1 (DemoCpp) >> AdsError: 1795 (invalid indexOffset)` 后发现：

- 早期已知问题是 `ParameterValues` 下残留 `TraceLevelMax = tlAlways`；当前 `.artifacts` 已不再有这个 assigned value。
- 更底层的确定问题是 `DemoCpp.tmc` 和 `Demo.tsproj` 的 `Parameters` 定义仍声明 `TraceLevelMax` / `PTCID #x03002103`，但 fallback C++ module 代码没有对应 `SETOBJPARA`/`GETOBJPARA` 映射。
- 另一轮风险来自 `DataPointerValues`：JSON plan 曾把 `DataIn` 和 `DataOut` 指向 `Demo_Obj1` 自己的 `AreaNo 2`。
- 对照 Beckhoff `S10-Mod2ModDataPointer` 样例，`DataPointerValues` 通常指向另一个 provider module 的数据区，例如另一个 object 的 `AreaNo 3`，不是默认指向当前 object 自己。

因此本轮修正：

```text
src/TwinCatAutomationKit.TwinCat/TwinCatEngineeringService.cs
  fallback module TMC generation no longer writes TraceLevelMax / PTCID #x03002103
  generated TMC Parameters keep only the supported Parameter struct by default

examples/json-plans/complex-full-project.json
  includeDataPointerValues = false
  新增 clearDataPointerValues
  applyDataPointerPlan 仅在 includeDataPointerValues=true 时启用

新增 public step:
  tsproj.clear-instance-data-pointer-values
```

当前 `.artifacts` 也已同步清掉：

```text
D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\DemoCpp\DemoCpp.tmc
D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\Demo.tsproj
  no TraceLevelMax
  no #x03002103
```

当前 `.artifacts` 已执行：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.clear-instance-data-pointer-values --project-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\Demo.tsproj" --instance-name="Demo_Obj1 (DemoCpp)" --remove-container-when-empty=false
```

后续不要在默认 activation demo 里重新启用 `DataPointerValues`，除非有 known-good provider object、`AreaNo`、`ByteOffs`、`ByteSize` 对照 evidence。

随后尝试用 CLI 自动 activation：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.activate-configuration --solution-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo.sln" --project-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\Demo.tsproj" --visible=false --startup-delay-ms=8000 --save-configuration-archive=true --configuration-archive-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\_json_plan_evidence\activated-after-data-pointer-clear.tszip"
```

结果没有进入 TwinCAT runtime activation 层，而是在 DTE 层失败：

```text
Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.
```

因此本条 evidence 不能声明 `1795` 已由真实 activation 消除；它证明本轮已移除两个默认 activation demo 中不应存在的写入来源：unsupported `TraceLevelMax / #x03002103` 参数定义，以及高风险自引用 `DataPointerValues`。

## 2026-04-30 Mapping 后续修正

同一轮检查还发现当前 `.artifacts` 中 root `<Mappings>` 一度为空，而 PLC instance 下残留 `UnrestoredVarLinks`，并带有 `RestoreInfo="ANotFound"`。这表示链接没有真正恢复成 root mapping，激活时可能继续带入 unresolved link 状态。

本轮修正：

```text
examples/json-plans/complex-full-project.json
  新增 clearMappings
  新增 clearUnrestoredVarLinks
  再用 tsproj.ensure-mapping-link 重建双向 mapping

新增 public step:
  tsproj.clear-unrestored-var-links
```

当前 `.artifacts` 检查结果：

```text
ROOT_MAPPINGS=1
UNRESTORED=0
DATA_POINTER_VALUE_COUNT=0
TraceLevelMax=0
#x03002103=0
```

## 2026-04-30 activation/ADS verified after fixes

继续真实 activation 后确认了两个独立 runtime 错误：

1. `1795 invalid indexOffset` 来自 unsupported `TraceLevelMax / PTCID #x03002103`。
2. 去掉 data pointer values 后又出现 `1803 invalid parameter value(s)`，触发点是 fallback C++ module 进 OP 时强制 `m_spDataIn.Init()` / `m_spDataOut.Init()`，但默认 demo 没有 known-good data pointer binding。

最终源码修正：

```text
src/TwinCatAutomationKit.TwinCat/TwinCatEngineeringService.cs
  fallback TMC no longer declares TraceLevelMax / #x03002103
  fallback C++ CycleUpdate uses mapped data area:
    m_Counter = m_Parameter.data1 + m_Data.dataIn
    m_Data.dataOut = m_Counter
  fallback C++ no longer requires smart data pointer Init() to enter OP

examples/json-plans/complex-full-project.json
  plcInstanceName = "PlcA Instance"
  includeDataPointerValues = false
```

当前 `.artifacts` 手动同步了同样修正，并重建：

```powershell
& 'D:\large_software2\2022\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' "D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\DemoCpp\DemoCpp.vcxproj" /p:Configuration=Release /p:Platform="TwinCAT OS (x64)" /v:minimal
```

build 输出显示：

```text
DemoCpp.vcxproj -> ...\Release\DemoCpp.tmx
Signing output with TwinCAT certificate 'optcnc'
```

随后以提升权限执行 activation：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.activate-configuration --solution-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo.sln" --project-path="D:\3rd_year\TwinCatAutomationKit\.artifacts\json-plans\complex-full-project\Demo\Demo.tsproj" --visible=false --startup-delay-ms=8000
```

结果：

```text
Status: Succeeded
activationCommand: ITcSysManager.ActivateConfiguration
attemptedCommands: ITcSysManager.ActivateConfiguration | ITcSysManager.StartRestartTwinCAT
```

最新 ADS scan：

```text
100   Run  TCatSysSrv
200   Run  RTime(Um)
300   Run  I/O Server
800   Run  Plc30 App
851   Run  Plc30 App
852   0x6 Target port could not be found
10000 Run  TwinCAT System
openPortCount=6
```

同一轮在普通 PowerShell token 下复测，结果也一致：

```text
validation.ads-scan -> openPortCount=6
ads-read --port=851 --symbol=MAIN.nSeed --type=UInt32 -> 5
```

ADS readback：

```text
port 851 MAIN.nSeed   UInt32  -> 5
port 851 MAIN.nStage1 UInt32  -> 0
port 800 MAIN.nSeed   UInt32  -> 5
port 800 MAIN.nStage1 UInt32  -> 0
```

最新 Windows Event Log 中，`2026-04-30 13:56:50` activation 后只有：

```text
Activate configuration performed ...
TwinCAT system start completed. AdsState: >5<
```

没有新的 `1795` 或 `1803`。旧的 `1803` 是 `2026-04-30 12:23:40`，旧的 `1795` 是 `2026-04-29`。

因此当前结论必须分开：

- ADS/runtime activation error 已修到真实机器验证通过：系统 Run，851 可读，没有新的 1795/1803。
- C++/PLC mapping 功能链也已验证通过：`MAIN.nStage1` 为 128，`MAIN.bPipelineOk` 为 true。不要再把 `MAIN.nStage1` 的历史 0 值描述成 ADS 端口或 activation 1795 问题；根因是 C++ fallback TMC 过去使用了 Standard data area，而不是 process-image `InputDst`/`OutputSrc` data areas。

后续复现这组 readback 时，优先用 public batch step，让终端和 summary 同时保留多变量值：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.ads-read-symbols --net-id=local --port=851 "--symbols=MAIN.nSeed:UInt32;MAIN.nStage1:UInt32;MAIN.bPipelineOk:Boolean;MAIN.nMismatchCount:UInt32" --auto-reconnect=true
```

这条命令本身不是新增 real-machine evidence；它是对本文件已验证变量集合的可复现入口。

## 2026-04-30 14:33 process-image mapping 修正

源码修正：

```text
src/TwinCatAutomationKit.TwinCat/TwinCatEngineeringService.cs
  fallback TMC:
    Input  AreaNo=1 AreaType=InputDst  Symbol=DataIn
    Output AreaNo=2 AreaType=OutputSrc Symbol=DataOut
    DataPointers no longer declare DataIn/DataOut by default
  fallback C++:
    OBJDATAAREA_VALUE(1, m_DataIn)
    OBJDATAAREA_VALUE(2, m_DataOut)
    m_Counter = m_Parameter.data1 + m_DataIn
    m_DataOut = m_Counter

examples/json-plans/complex-full-project.json
  PlcTask Outputs^MAIN.nSeed -> Input^DataIn
  Output^DataOut -> PlcTask Inputs^MAIN.nStage1
```

当前 `.artifacts` 同步修正并重建 C++：

```text
DemoCpp.vcxproj -> ...\Release\DemoCpp.tmx
Signing output with TwinCAT certificate 'optcnc'
```

真实 activation：

```text
Status: Succeeded
activationCommand: ITcSysManager.ActivateConfiguration
attemptedCommands: ITcSysManager.ActivateConfiguration | ITcSysManager.StartRestartTwinCAT
```

注意：本次 `--save-configuration-archive=true` 的目标 `activated-after-process-image-dataareas.tszip` 未实际落盘，CLI output 中 `configurationArchivePath` 为 null；有效 evidence 是工程 `_Boot/.../CurrentConfig.xml`、Windows Event Log 和 ADS readback。

activation 后 `.tsproj` 关键片段：

```text
<Mappings>
  <MappingInfo .../>
  <OwnerA Name="TIPC^PlcA^PlcA Instance">
    <OwnerB Name="TIXC^DemoCpp^Demo_Obj1 (DemoCpp)">
      <Link VarA="PlcTask Inputs^MAIN.nStage1" VarB="Output^DataOut"/>
      <Link VarA="PlcTask Outputs^MAIN.nSeed" VarB="Input^DataIn"/>
    </OwnerB>
  </OwnerA>
</Mappings>
```

日志检查：`2026-04-30 14:33` 只有 activate/start、C++ repository driver loaded successfully、license valid、TwinCAT system start completed；没有新的 `1795` 或 `1803`。

## 实际 Boot 配置

系统实际 boot 文件：

```text
C:\ProgramData\Beckhoff\TwinCAT\3.1\Runtimes\UmRT_Default\3.1\Boot\CurrentConfig.xml
```

该文件中已经能看到：

```text
Create Object Demo_Obj1 (DemoCpp)
Set Object Demo_Obj1 (DemoCpp) to SAFEOP
Set Object Demo_Obj1 (DemoCpp) to OP
Download Symbols, port 851
```

这说明 XAE/TwinCAT 已经生成过包含 `Demo_Obj1` 和 PLC symbol download 的 boot config。后续不要只看工程目录 `_Boot` 下的旧 snapshot 判断当前 runtime 状态。

## 历史 ADS 扫描结果

早期排查阶段仓库新增了正式诊断入口：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=validation.ads-scan --net-id=local
```

早期运行结果：

```text
Status: Failed
Result: ADS scan found no reachable ports for local.
openPortCount: 0
failedPortCount: 7
```

该命令和早前临时 `TwinCAT.Ads.TcAdsClient.Connect(port)` 扫描结论一致：

```text
100   Port is not open. (AdsErrorCode: 1864, 0x748)
200   Port is not open. (AdsErrorCode: 1864, 0x748)
300   Port is not open. (AdsErrorCode: 1864, 0x748)
800   Port is not open. (AdsErrorCode: 1864, 0x748)
851   Port is not open. (AdsErrorCode: 1864, 0x748)
852   Port is not open. (AdsErrorCode: 1864, 0x748)
10000 Port is not open. (AdsErrorCode: 1864, 0x748)
```

因此当所有端口都返回 `0x748` 时，`validation.ads-read --port=851 --symbol=...` 的失败是下游现象。只有当 `100` 或 `10000` 至少可读时，才继续讨论 PLC symbol path。最终修正后的 14:33 验证结果见本文顶部。

## 权限和进程状态

本轮命令观察：

```text
whoami -> zy_engineer\david
Get-LocalGroupMember -Group TcAmsUsers -> ZY_ENGINEER\david
whoami /groups -> 未显示 TcAmsUsers
BUILTIN\Administrators -> Group used for deny only
```

这表示用户在本地组里，但当前进程 token 没拿到 `TcAmsUsers`。如果该组 membership 是近期添加的，需要重新登录或重启后再测 ADS。

同时存在残留 `devenv`：

```text
PID 44672
PID 78164
```

当前权限下：

```text
Stop-Process ... -> Access is denied
taskkill /F ...  -> Access is denied
```

这会阻挡自动 DTE activation，但不是 ADS `0x748` 的完整解释；`0x748` 已经在直接 ADS port scan 中复现。

## 后续判断入口

后续 agent 先按这个顺序排查：

1. 刷新 token：重新登录或重启机器后确认 `whoami /groups` 中出现 `TcAmsUsers`。
2. 用 `validation.ads-scan` 先测 `100` 和 `10000`。如果这两个仍是 `0x748`，不要继续调 symbol path。
3. 确认 `TcSysSrv` 可由当前权限重启；如果不能，先解决 Windows/UAC 权限上下文。
4. 再运行 `engineering.activate-configuration` 或人工 XAE activation。
5. 只有 ADS `100/10000` 可读且 `851` 存在后，再用 `validation.ads-read` 读取 `MAIN.nSeed`、`MAIN.nStage1`。

## 残留注意事项

- 普通 token 偶尔仍会在并发 ADS 读时遇到单次 `0x748`；提升权限执行 `TcSysUI.exe /Start` 后，scan/read 能恢复。后续排查应先跑 `validation.ads-scan`，不要直接把单次 symbol read 失败归因到 mapping。
- `activated-after-process-image-dataareas.tszip` 没有落盘；不要把该路径当作 evidence。以 `_Boot/.../CurrentConfig.xml`、Windows Event Log 和 ADS readback 为准。
- `complex-full-project` 在本机已通过真实 activation 和 ADS readback 证明 runtime/mapping 链路成功。
