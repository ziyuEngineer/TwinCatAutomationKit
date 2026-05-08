# 2026-05-08 Strict Integration DTE Launch Failure / 严格集成测试 DTE 启动失败记录

English summary: This evidence records a strict integration-test run that failed before project creation because the local Visual Studio/XAE DTE COM server could not launch.

本文记录一次加强测试断言后的真实 TwinCAT 集成测试运行。结论是：本轮没有跑到 `.tsproj` mutation、build、activation 或 ADS 断言；失败发生在第一步 `VisualStudio.DTE.17.0` COM launch/attach 阶段。

## 执行命令

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

配置摘要：

- `VS ProgId`: `VisualStudio.DTE.17.0`
- `Work root`: `D:\3rd_year\TwinCatAutomationKit\t`
- `EnableActivation=true`
- `EnableAdsRead=true`
- signing certificate 三项仍按默认排除。

## 结果

第一个 scenario 在 `stage: launch VS` 失败，后续 6 个 scenario 复用 shared setup failure，因此没有重新创建工程。

关键错误：

```text
Unable to launch or attach to Visual Studio DTE 'VisualStudio.DTE.17.0'.
Inner: Retrieving the COM class factory for component with CLSID {33ABD590-0400-4FEF-AF98-5F5A8A99CFC3} failed due to the following error: 80080005 服务器运行失败 (0x80080005 (CO_E_SERVER_EXEC_FAILURE)).
```

失败诊断目录：

```text
D:\3rd_year\TwinCatAutomationKit\t\6af870ed
```

## 证明了什么

- 当前机器/会话不能启动或附加 `VisualStudio.DTE.17.0` local COM server。
- 本轮测试没有形成新的 `7/7 PASS`。
- 本轮失败不是新增 strict `.tsproj` / ADS 断言的业务语义失败；测试没有执行到这些阶段。

## 没证明什么

- 没有证明 `InitSymbols` runtime-only ADS proof 是否通过。
- 没有证明 `DataPointer`、generic batch failure、signing metadata stale cleanup 等新增断言是否通过真实工程。
- 没有证明 activation/ADS 链路回归或失败。

## 后续排查入口

1. 先处理本机 DTE/XAE COM 启动问题，重点看 `VisualStudio.DTE.17.0` local server、DCOM registration 和残留 `devenv` 状态。
2. DTE 能启动后重新运行完整 integration tests。
3. 新一轮如果能跑到 export 阶段，正常 `ExportTreeItemXml` 仍必须是非 fallback `TreeItem` XML。
4. 新一轮如果能跑到 ADS 阶段，必须额外确认 `MAIN.RuntimeOnlyInitSymbolProbe` 读回目标 ObjectId，证明 `.tsproj` `InitSymbol` runtime 注入真的生效。
