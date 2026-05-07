# Direct CLI Real XAE Guide / 真实 XAE 直接 CLI 指南

English summary: This guide shows the shortest real-machine `invoke-step` chain that creates, mutates, builds, and prepares a TwinCAT project for activation evidence.

这个文件是给新开发者和 agent 的真实机器交接说明。规则很简单：每个 public step kind 都应能通过一个 `invoke-step` 命令到达；最终证明必须来自真实 TwinCAT/XAE evidence。

## Baseline / 当前基线

- Step coverage：`TwinCatStepCatalog` 中每个 public step kind 都已接入 `invoke-step`。
- Real integration baseline：2026-04-24，`All 22 integration tests passed.`
- Verified direct-CLI smoke directory：`D:\t\tcak_manual_pipeline2`
- `engineering.create-cpp-project` 对不完整 wizard output 有 deterministic fallback；不要要求用户先手动打开 XAE 来生成 C++ module class。

## Discover Commands / 查看命令

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- list-steps
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- show-order
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help invoke-step
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=tsproj.bind-instance-task
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=signing.set-license
```

## Verified Minimal Chain / 已验证最小链路

真实 TwinCAT 项目路径要短，推荐 `D:\t\...`。下面命令来自已验证 smoke；运行自己的工程时替换 workspace 名即可。

创建 XAE solution：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.create-xae-solution --solution-directory=D:\t\tcak_manual_pipeline2 --solution-name=Demo --project-name=Demo --visible=false --startup-delay-ms=8000
```

创建 C++ project：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.create-cpp-project --solution-path=D:\t\tcak_manual_pipeline2\Demo.sln --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj --cpp-project-name=DemoCpp --visible=false --startup-delay-ms=8000
```

增加 module instance：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.add-module-instance --solution-path=D:\t\tcak_manual_pipeline2\Demo.sln --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj --cpp-project-name=DemoCpp --instance-base-name=Demo_Obj1 --visible=false --startup-delay-ms=8000
```

创建实时 task：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.ensure-task --solution-path=D:\t\tcak_manual_pipeline2\Demo.sln --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj --task-name=Task1 --task-subtype=0 --priority=15 --cycle-time-us=10000 --ams-port=360 --visible=false --startup-delay-ms=8000
```

下一步要使用 `engineering.ensure-task` 返回的真实 `objectId`。已验证运行中是 `#x02010020`，`engineering.add-module-instance` 返回的 instance name 是 `Demo_Obj1 (DemoCpp)`。

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.ensure-task --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj --task-name=Task1 --priority=15 --cycle-time-ns=10000000 --ams-port=360

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.bind-instance-task --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj "--instance-name=Demo_Obj1 (DemoCpp)" "--task-object-id=#x02010020" --priority=15 --cycle-time-ns=10000000

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.ensure-parameter --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj "--instance-name=Demo_Obj1 (DemoCpp)" --parameter-name=Parameter.data1 --value-text=123

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=engineering.build-solution --solution-path=D:\t\tcak_manual_pipeline2\Demo.sln --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj --timeout-ms=300000 --visible=false --startup-delay-ms=8000
```

期望结果：`Status: Succeeded`，并且 `lastBuildInfo: 0`。

## Runtime 注意事项

- Task AMS port 在同一个 project 内必须唯一。PLC wizard 可能已经创建 port `350` 的 `PlcTask`，脚本新 task 建议从 `360`、`361` 递增。
- Activation 必须在真实 module instance 绑定到真实 task 之后执行。build success 不能替代 runtime graph validation。
- `Demo_Obj1 (DemoCpp)` 需要 `tsproj.bind-instance-task` 和必要的 pointer/parameter writeback 后再 activate。
- 如果 activation 报 `AdsError 1795 (invalid indexOffset)`，优先检查 `.tsproj` 中 task、instance、`CyclicCaller`、`DataIn`、`DataOut` 是否和 TMC 匹配。

## Signing Built C++ Modules

TwinCAT C++ binary 应在 `engineering.build-solution` 后、activation 前签名。共享脚本里优先用 `--password-file` 或 `--password-env-var`；inline `--password` 只适合本机一次性验证。

如果希望 TwinCAT/MSBuild 在 build 时签名，先写 C++ project signing settings：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=signing.set-license --project-path=D:\t\tcak_manual_pipeline2\Demo\Demo.tsproj --cpp-project-name=DemoCpp --license-name=optcnc --password-file=D:\certs\MyModuleCert.password.txt --enable-signing=true
```

这会把 `TcSignTwinCat`、`TcSignTwinCatCertName` 和 `TcSignTwinCatCertPW` 写入 C++ `.vcxproj`。

手动签名和验证 built `.tmx`：

```powershell
$tmx = "D:\t\tcak_manual_pipeline2\Demo\DemoCpp\_products\TwinCAT OS (x64)\Release\DemoCpp\DemoCpp.tmx"

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=signing.sign-twincat-binary --certificate-path=D:\certs\MyModuleCert.tccert --target-paths=$tmx --password-file=D:\certs\MyModuleCert.password.txt

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=signing.verify-twincat-binary --target-paths=$tmx
```

`signing.grant-certificate` 会改变本机 signing authorization，属于 machine state，不是普通工程 mutation。

## Evidence To Check / 应检查的证据

- `.tsproj`：instance node、task binding、`CyclicCaller`、parameter value。
- `.tmc`：C++ project 至少有一个带 GUID 的 `<Module>`。
- Build log：`_products\TwinCAT OS (x64)\Release\...\*.log`。
- JSON plan summary：`--summary=...` 输出。
- Activation archive 或 `_Boot\TwinCAT OS (x64)\CurrentConfig.xml`。
- ADS readback：activation 后优先用 `validation.ads-scan` 确认端口，再用 `validation.ads-read-symbols` 一次读取多个 runtime symbol；终端 `Result` 中的 `symbol=value` 列表是最快的人工验收入口。

如果 build 失败，先看 build log，再决定是否改 `.tsproj` mutation code。

## Full Real Validation / 完整真实验证

广覆盖 direct-CLI smoke：

```powershell
$verifyScript = ".\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-all-invoke-steps.ps1"
& $verifyScript
& $verifyScript -Execute
```

第一个命令是 dry-run。第二个命令执行真实 XAE/file workflow。这个脚本放在 integration tests 目录下，因为它是开发者/agent 的真实机器验证 harness，不是最终用户的 JSON plan 入口。默认跳过 activation、signing、ADS read 和 `tsproj.merge-fragment`。前三类需要 machine/runtime consent、真实证书或正在运行的目标 symbol；`tsproj.merge-fragment` 是高风险 escape hatch，只有 fragment 来源、目标父路径、字段含义和 evidence 都写清楚时才用 `-IncludeMergeFragment` 显式开启。

包含机器状态步骤时使用：

```powershell
$verifyScript = ".\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-all-invoke-steps.ps1"
& $verifyScript -Execute -IncludeActivation -IncludeSigning -CertificatePath=D:\certs\MyModuleCert.tccert -CertificatePasswordFile=D:\certs\MyModuleCert.password.txt -IncludeAdsRead -AdsNetId=127.0.0.1.1.1 -AdsPort=851 -AdsSymbol=MAIN.nValue -AdsType=Int32
```

真实 integration tests：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```
integration test 出现缺前置条件失败时，不应把真实验证视为完成。
