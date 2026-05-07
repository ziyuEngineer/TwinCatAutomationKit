# Tests / 测试

English summary: The repository test strategy is real TwinCAT/VS integration-only; missing machine/runtime prerequisites are failures, not skips.

本仓库不再保留离线测试项目。所有公开接口的验证都集中在 `TwinCatAutomationKit.IntegrationTests`，并且必须跑在真实 TwinCAT/VS 环境上。

## Project

| Project | When to run | What it proves |
|---|---|---|
| `TwinCatAutomationKit.IntegrationTests` | 每次改动完成前运行 | Real behavior: DTE/XAE automation、template creation、`.tsproj` file mutation、reopen、build、signing metadata、activation、ADS scan/read |

## Command

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

缺少 `Config/integration-test-config.json`、TwinCAT/VS、activation 或 ADS 前置条件时，runner 返回失败。不要把缺环境当成通过。`signing.grant-certificate`、`signing.sign-twincat-binary`、`signing.verify-twincat-binary` 默认明确排除，因为它们需要真实 TwinCAT OEM signing certificate/private key。

## Real Integration Strategy

集成测试在同一个短路径工程里按依赖顺序推进。前一步的输出既验证前一步，也作为后一步的输入基座。

链路：

1. 用 XAE/TwinCAT 模板创建 solution/project。
2. 添加 C++ project、PLC project、module、instance、task。
3. 用专用 `.tsproj` API 修改 binding、parameter、pointer、mapping。
4. reopen XAE，确认 file mutation 能被读回。
5. build。
6. signing metadata 写入；3 个 OEM certificate signing 操作作为明确排除项记录。
7. activation。
8. ADS scan、batch symbol read、single symbol read。
9. 最后断言实际覆盖集合包含每个 public step kind 和每个开放 service method。

## Integration Config

Machine-local config 被忽略，不进源码：

```text
tests/TwinCatAutomationKit.IntegrationTests/TwinCatAutomationKit.IntegrationTests/Config/integration-test-config.json
```

从示例复制：

```text
tests/TwinCatAutomationKit.IntegrationTests/TwinCatAutomationKit.IntegrationTests/Config/integration-test-config.example.json
```

工作目录保持短路径，通常用 `D:\t`，因为 TwinCAT generated paths 容易变长。

## Gate

合并 code 或 step spec change 前运行 integration tests。无法跑时要在 final/evidence 中明确说明没有完成真实机器验证，以及缺少的具体前置条件。
