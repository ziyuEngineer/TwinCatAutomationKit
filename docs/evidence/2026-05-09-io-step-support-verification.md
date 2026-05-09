# IO Step Support Verification / IO Step 支持验证

English summary: This evidence records the verification status for the new dedicated IO topology steps added on 2026-05-09, including what passed locally and what blocked real XAE/runtime proof.

## Summary

本轮按 `docs/roadmap/io-step-support-requirements.md` 增加了 dedicated IO topology step surface，用于表达 `Project/Io`、EtherCAT Box、PDO、`MappingInfo` 和 IO mapping link，而不是把旧工程 `Project/Io` 整段 XML 当作长期方案复制。

已接入并验证可见的 public step kinds：

- `tsproj.ensure-io-section`
- `tsproj.ensure-io-device`
- `tsproj.ensure-ethercat-box`
- `tsproj.ensure-io-pdo`
- `tsproj.ensure-io-box-image`
- `tsproj.ensure-mapping-info`
- `tsproj.ensure-io-mapping-link`
- `tsproj.apply-io-topology-plan`

## Commands

生成 step reference docs：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -- generate-docs
```

结果：成功写入 `docs/reference`。过程中出现 `MSB3101`，原因是 `obj\...\*.AssemblyReference.cache` 写入被拒绝；该警告未阻止 docs 生成。

编译验证：

```powershell
dotnet build .\TwinCatAutomationKit.sln --no-restore -m:1 -v:n
dotnet build .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -v:m
dotnet build .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj --no-restore -v:m
```

结果：均成功，仍有同一类 `MSB3101` obj cache 写权限警告。并行 solution build 曾出现一次 `生成失败` 但无 error；单进程 solution build 成功，判断和 obj cache 写入权限/并发有关。

direct CLI surface 检查：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj --no-restore -- help invoke-step |
  Select-String -Pattern "ensure-io-section|ensure-io-device|ensure-ethercat-box|ensure-io-pdo|ensure-io-box-image|ensure-mapping-info|ensure-io-mapping-link|apply-io-topology-plan"
```

结果：8 个 IO step kind 均出现在 `invoke-step` help 输出中。

coverage/reference 静态检查：

```powershell
git diff --check
rg -n "ensure-io-section|ensure-io-device|ensure-ethercat-box|ensure-io-pdo|ensure-io-box-image|ensure-mapping-info|ensure-io-mapping-link|apply-io-topology-plan" docs\reference tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests
```

结果：`git diff --check` 无 whitespace error；8 个 IO step 出现在 generated catalog、recommended execution order、integration coverage matrix、integration README 和 verify script 中。

direct CLI verification script dry run：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Scripts\verify-all-invoke-steps.ps1
```

结果：dry run 输出 `Missing: 0`、`Failed: 0`，证明脚本已规划所有 `$allKinds`。脚本最终仍返回失败，因为默认跳过 signing、activation、ADS 和 `merge-fragment`，这是脚本设计的 full verification gate，不是 IO step 缺口。

## Real XAE/runtime blockers

仓库要求真实 TwinCAT/XAE 行为优先用 integration test 或 evidence 证明。本轮尝试运行：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj --no-restore
```

结果：7 个 integration tests 均失败在共享场景初始化，首个根因是：

```text
Access to the path 'D:\3rd_year\TwinCatAutomationKit\t\7677b80f' is denied.
```

当前配置 `tests/.../Config/integration-test-config.json` 的 `WorkRootBase` 指向 `D:\3rd_year\TwinCatAutomationKit\t`，不在当前可写 workspace 内；测试 loader 没有环境变量 override。本轮没有修改该本机配置文件来临时绕过。

随后尝试把 `StepProbeRunner` 输出重定向到仓库内：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj --no-restore -- probe-run --kind=tsproj.apply-io-topology-plan --out=.\obj\io-step-probes
```

结果：probe 仍未执行到 IO mutation，因为它先要创建 XAE solution，失败点是：

```text
Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.
```

因此本 evidence 不声明 XAE reopen、build、activation 或 ADS runtime proof 已通过。当前证明范围是：public surface、CLI wiring、generated docs、coverage registration、script planning 和编译通过；真实 XAE/runtime proof 仍需在可写短路径 work root 且 DTE 可启动的机器状态下补跑。

## Follow-up

后续补 runtime evidence 时，优先运行完整 integration runner。若只验证 IO step，可先运行 `probe-run --kind=tsproj.apply-io-topology-plan` 并保存生成的 `.tsproj` snapshot，再 reopen XAE 导出 IO tree XML。参考工程等价性还需要 normalized XML 对比，至少覆盖 5 个 Device、28 个 Box、2 个 `MappingInfo`、5 个 `OwnerA` 和关键 IO mapping links。
