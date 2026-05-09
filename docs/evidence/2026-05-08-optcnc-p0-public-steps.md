# 2026-05-08 Optcnc P0 Public Steps / Optcnc P0 Public Step 验证记录

English summary: This evidence records validation for the Optcnc P0 public-step gap implementation: build, generated docs, JSON dry-run, and real XAE/VS integration passed when run through the interactive STA command runner.

本文记录 `docs/roadmap/optcnc-public-step-gap-requirements.md` 的 P0 public steps 实现后的验证结果。结论是：代码构建、`docs/reference` 生成、`t/optcnc-auto-sln-from-kit.json` dry-run 和真实 XAE/VS integration 都已通过。直接从当前非交互 shell 启动 DTE 仍会失败；真实 integration 通过用户交互桌面里的 `interactive-command-runner.ps1` 运行，runner 进程是 STA，并能创建 `VisualStudio.DTE.17.0`。

## 覆盖的新增 public steps

本轮 P0 覆盖以下 step kind：

- `engineering.create-vs-cpp-project`
- `engineering.ensure-solution-project-dependency`
- `engineering.publish-modules`
- `cpp.create-project-item`
- `cpp.write-project-item-content`
- `cpp.remove-project-item`
- `cpp.set-project-property`
- `cpp.set-item-definition-property`
- `cpp.set-project-item-metadata`

这些 step 的实现路径保持层边界：DTO 在 `TwinCatAutomationKit.Abstractions`，TwinCAT/VS/MSBuild 行为在 `TwinCatEngineeringService`，CLI 只解析参数并调用 service，step spec 来源是 `TwinCatStepCatalog`，`docs/reference/*` 由 `generate-docs` 生成。

## 构建和文档生成

执行命令：

```powershell
dotnet build .\TwinCatAutomationKit.sln /nr:false /p:UseSharedCompilation=false /m:1 -v:minimal
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- generate-docs
```

结果：

- solution build 成功，`0` errors。
- `generate-docs` 成功，输出目录为 `D:\3rd_year\TwinCatAutomationKit\docs\reference`。
- 两个命令都出现 `MSB3101` warning：`AssemblyReference.cache` 写入被拒绝。该 warning 没有阻止 build 或 docs 生成。
- PowerShell profile 中的 `micromamba.exe` hook 也会打印错误，但发生在命令完成后，不影响本轮验证结论。

## JSON plan dry-run

执行命令：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=t\optcnc-auto-sln-from-kit.json --dry-run=true --summary=.artifacts\optcnc-dry-run-summary.json
```

结果：

- Plan: `optcnc-auto-sln-from-kit`
- Mode: `dry-run`
- Steps: `104`
- Failed: `0`
- Skipped: `2`
- 被跳过的 step 是 plan 中按配置关闭的 `engineering.build-solution` 和 `engineering.activate-configuration`。

这证明 `t/optcnc-auto-sln-from-kit.json` 已能通过 public steps 显式表达 P0 迁移路径：创建 `AdsClient`，设置 solution dependency，创建 C++/resource project item，写入 payload source，设置 project/tool/item metadata，调用 `engineering.publish-modules`，再进入实例、task、parameter、pointer 和 mapping steps。

## 真实 XAE/VS integration

直接从当前非交互 shell 执行 integration 时，仍然在 `VisualStudio.DTE.17.0` COM launch 阶段失败：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

配置摘要：

- `VS ProgId`: `VisualStudio.DTE.17.0`
- `Work root`: `D:\3rd_year\TwinCatAutomationKit\t`
- `EnableActivation=true`
- `EnableAdsRead=true`
- signing certificate 仍按默认排除。

失败结果：

- `7 of 7 integration tests FAILED`
- 第一个 scenario 在 `stage: launch VS` 失败。
- 后续 scenario 复用 shared setup failure，因此没有重复创建工程。
- 失败诊断目录：`D:\3rd_year\TwinCatAutomationKit\t\391475bd`

关键错误：

```text
Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.
Inner: Retrieving the COM class factory for component with CLSID {33ABD590-0400-4FEF-AF98-5F5A8A99CFC3} failed due to the following error: 80080005 服务器运行失败 (0x80080005 (CO_E_SERVER_EXEC_FAILURE)).
```

随后在用户交互桌面启动 runner：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\interactive-command-runner.ps1
```

DTE probe request 结果：

- request id: `dte-probe-6436ec61aae24aba81f7390aef46b1ca`
- stdout: `ApartmentState=STA`
- stdout: `AttachedToExisting=false`
- stdout: `Version=17.0`
- stdout: `Name=Microsoft Visual Studio`
- stdout: `CreatedInstanceQuit=true`

最终 integration request：

- request id: `integration-e5bad6e6cd084a6ba27f1b8698440366`
- stdout: `ApartmentState=STA`
- stdout: `All 7 integration tests passed.`
- stdout: `DotNetExitCode=0`
- preserved integration artifacts: `D:\3rd_year\TwinCatAutomationKit\t\c1a20235`
- runner logs:
  - `.artifacts\interactive-runner\logs\integration-e5bad6e6cd084a6ba27f1b8698440366.stdout.log`
  - `.artifacts\interactive-runner\logs\integration-e5bad6e6cd084a6ba27f1b8698440366.stderr.log`

## 证明了什么

- 新增 P0 step 的 public surface、CLI path、JSON plan path 和 generated docs 可以构建并被 dry-run 解析。
- `t/optcnc-auto-sln-from-kit.json` 不再需要复制样例 `.sln/.tsproj/.vcxproj/.filters/.tmc`，也不需要文件系统脚本复制源码目录或 generic `.tsproj` XML escape hatch。
- `engineering.create-vs-cpp-project` 在真实 DTE session 中创建了普通 VS C++ `AdsClient` project，并写入 solution dependency。
- `cpp.create-project-item` / `cpp.write-project-item-content` / `cpp.remove-project-item` / `cpp.set-project-property` / `cpp.set-item-definition-property` / `cpp.set-project-item-metadata` 在真实 TwinCAT C++ project 和普通 VS C++ project 中被执行，并通过 `.vcxproj`、`.filters`、物理文件、atomic wrapper summary 和后续 build 验证。
- `engineering.publish-modules` 在真实 TwinCAT C++ project 上执行，`.tmc` 保持可读并包含 `AuxModule` metadata；`updated` output 单独记录本次 timestamp/hash 是否变化。
- build、activation 和 ADS readback 在 integration 中通过，证明新增 P0 step 没破坏既有 runtime proof。

## 没证明什么

- 没有证明直接从当前非交互 Codex shell 可以稳定启动 DTE；该路径仍失败为 `CO_E_SERVER_EXEC_FAILURE`。
- 没有覆盖真实 OEM signing certificate 的 binary sign/verify，仍按测试矩阵默认排除。
- 没有证明 full OptCNC build/activation；本轮 dry-run 只验证 `t/optcnc-auto-sln-from-kit.json` 的 public step 展开，真实 integration 使用仓库 integration scenario。

## 后续排查入口

1. 后续需要真实 XAE/VS proof 时，优先让用户先启动 `interactive-command-runner.ps1`，再由 agent 投递 request JSON。
2. 如果要验证完整 `t/optcnc-auto-sln-from-kit.json` 非 dry-run，应另开 dated evidence，记录真实 OptCNC 目标目录、build/activation/ADS 结果。
3. 直接 shell 的 DTE launch failure 仍可作为独立环境问题排查，但不阻塞 runner 路径下的真实 integration proof。
