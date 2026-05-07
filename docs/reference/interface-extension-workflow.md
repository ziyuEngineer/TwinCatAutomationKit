# Interface Extension Workflow / 接口扩展流程

本文件定义新增公开 TwinCatAutomationKit step 接口时的流程。

## Extension Chain / 扩展链路

1. 在 `TwinCatAutomationKit.Abstractions` 定义或调整 request/response DTO。
2. 在 `TwinCatAutomationKit.TwinCat` service method 中实现确定性行为。
3. 在 `TwinCatStepCatalog` 注册或调整 step 规范，写清 preconditions 和 verification notes。
4. 如果 pipeline/JSON 运行需要使用该能力，添加 `TwinCatAtomicSteps` wrapper。
5. 用 CLI `generate-docs` 重新生成文档。
6. 在 `tests/TwinCatAutomationKit.IntegrationTests` 新增或扩展真实 TwinCAT/VS integration tests。
7. 用 `dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj` 验证。

## Quality Gate / 质量门槛

- Step kind 必须使用 `<category>.<verb>-<target>` 命名。
- 行为应尽量幂等；如果不能幂等，必须显式说明。
- Verification guidance 必须描述可观察 evidence，例如 XML readback、ADS read 或 summary artifact。
- 合并前 generated docs 必须反映新 step 规范。

## Anti-Patterns / 反模式

- 把 ad-hoc XML edit 分散在多个文件，而不是收敛到 `TwinCatTsprojMutationService`。
- 有 public capability，但没有 `TwinCatStepCatalog` step 规范。
- 改了 step 规范但没有 regenerated docs，也没有真实 TwinCAT integration coverage。
