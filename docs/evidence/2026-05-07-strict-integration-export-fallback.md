# 2026-05-07 Strict Integration Export Fallback / 严格集成测试导出 fallback 记录

English summary: This evidence records a stricter integration-test run that correctly failed when normal `engineering.export-tree-item-xml` returned fallback content because the Visual Studio/XAE COM server became unavailable.

本文记录一次严格化后的真实 TwinCAT 集成测试运行。结论是：测试现在会正确拒绝正常路径里的 `ExportTreeItemXml` fallback；本轮失败暴露的是当前机器上 Visual Studio/XAE COM automation 已掉线，而不是把 fallback 注释文件误当作成功 XML。

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

两次完整运行都在第一个 scenario 的正常 export 阶段失败。失败目录：

- `D:\3rd_year\TwinCatAutomationKit\t\da42ace8`
- `D:\3rd_year\TwinCatAutomationKit\t\eb8a652f`

第二次失败信息已经被新断言改成明确原因：

```text
Tree export for TIXC^ItCpp returned fallback content.
See D:\3rd_year\TwinCatAutomationKit\t\eb8a652f\evidence\cpp.before-file-mutation.xml.
```

fallback artifact 内容：

```text
<!-- ExportTreeItemXml fallback for 'TIXC^ItCpp': COMException: RPC 服务器不可用。 (0x800706BA) -->
```

当前机器还有一个无窗口 `devenv` 残留进程，`Stop-Process -Force` 返回拒绝访问：

```text
Cannot stop process "devenv (41604)" because of the following error: 拒绝访问。
```

## 证明了什么

- 正常 `engineering.export-tree-item-xml` 路径不再允许 fallback 冒充成功。
- 如果 XAE/COM 掉线，集成测试会在 export proof 层失败，并指向具体 fallback artifact。
- 旧行为会继续尝试把 fallback 注释文件按 XML 解析，最终只报 `Root element is missing`；新行为的失败原因更准确。
- 本轮没有形成新的 7/7 PASS，因为当前机器 COM 状态阻塞真实 XAE export。不能把这轮作为 runtime proof。

## 没证明什么

- 没有证明 activation/ADS 链路回归或失败；本轮没有跑到 build/activation/ADS 阶段。
- 没有证明 C++/PLC mutation 语义失败；失败发生在 file mutation 之前的 XAE export proof。

## 后续排查入口

后续如果重新跑严格集成测试，应先处理残留 `devenv`：

1. 用同一用户的交互桌面关闭 Visual Studio，或重启机器清掉无法停止的 `devenv`。
2. 确认 `VisualStudio.DTE.17.0` 能创建新的 DTE session。
3. 重新运行 integration tests；正常路径的 `cpp.before-file-mutation.xml` 和 `tasks.before-file-mutation.xml` 必须是可解析 `TreeItem` XML，不是 fallback 注释。
4. 只有 missing-node boundary case 允许 fallback，并且必须验证 fallback 文本包含失败 tree path 和 lookup error。
