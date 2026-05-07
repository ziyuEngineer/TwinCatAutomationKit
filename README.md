# TwinCatAutomationKit / TwinCAT 自动化工具包

English summary: TwinCatAutomationKit is a CLI/JSON-first TwinCAT automation kit. It uses TwinCAT/XAE templates like a human operator would, then applies fine-grained steps to modify, build, activate, and validate the generated project.

TwinCatAutomationKit 的目标不是绕开 TwinCAT 模板从零手写工程文件，而是模拟人在 XAE 里的真实操作：先用 Beckhoff/TwinCAT 自带模板创建默认工程，再通过极细粒度 API 和 CLI step 删除、修改、补齐、构建、激活和验证。

最终使用方式是：外部 agent 或用户写一个 JSON plan，在命令行执行 `run-plan`，就能按顺序创建和调整 TwinCAT 工程。

这里的文档采用中文优先，不做纯英文说明。文件名、命令、API、DTO、step kind、JSON 字段保持英文；解释、审核规则和操作判断用中文。这样你能快速审核，agent 也能靠稳定的英文符号定位代码。

## 设计目标

- 一个 TwinCAT 人类操作 = 一个 service API = 一个 step 接口规范 = 一个 `invoke-step` CLI 路径。
- JSON plan 只组合公开 step，不依赖隐藏的本机工程文件夹或旧工程复制。
- 工程创建优先使用 TwinCAT/XAE 官方模板；后续修改通过我们明确实现的细粒度 API 完成。
- 每个有工程意义的变更都留下 evidence，例如 XML snapshot、build log、activation archive、summary JSON 或 ADS readback。
- 文档是记录系统：新的 agent 只读 docs、step specs、tests 和 evidence，也能继续写出同质量的代码。
- `guided-build` 这类 macro command 只能是 recipe，不能掩盖 step-level 能力缺口。

## 当前状态

当前仓库应自洽运行，不依赖隐藏 seed folder、外部旧工程目录或一次性本机输出。

已具备能力：

- `engineering.*`：Visual Studio/XAE/DTE 操作、工程创建、C++/PLC project、module、instance、task、build、activate。
- `tsproj.*`：针对真实 XAE 生成工程的 `.tsproj` file mutation，包括 task、instance、binding、mapping、parameter、pointer、generic XML upsert。
- `signing.*`：TwinCAT C++ signing 设置、签名、验证和授权。
- `validation.ads-read`：ADS runtime readback。
- `run-plan`：从 JSON plan 执行多步骤自动化。
- real integration tests：在真实 TwinCAT/VS 环境里覆盖每个 public step，并验证 XAE、build、signing、activation 和 ADS 相关行为。

重要定位：

- 使用 TwinCAT/XAE 官方模板是设计的一部分，不是临时限制。
- 禁止依赖隐藏 seed folder、旧工程复制或未记录的本机模板目录。
- 如果某个 `.tsproj` 修改没有专用 API，应记录缺口并反馈，后续开发专用 API；不要随意复制未知 XML fragment。

## 仓库结构

- `src/TwinCatAutomationKit.Abstractions`
  DTO、step specs、run state、evidence model。这里不能放 TwinCAT COM、ADS、DTE 或 XML mutation implementation。
- `src/TwinCatAutomationKit.Core`
  pipeline runner 和 generated docs writer。这里不放 Beckhoff-specific 逻辑。
- `src/TwinCatAutomationKit.TwinCat`
  DTE/System Manager automation、`.tsproj` mutation、signing、ADS validation、atomic steps。
- `src/TwinCatAutomationKit.Cli`
  `invoke-step`、`run-plan`、`generate-docs`、help 和 reference commands。调用方主要使用这一层。
- `tests/TwinCatAutomationKit.IntegrationTests`
  真实 TwinCAT/VS 集成测试；缺少本机配置或 runtime 前置条件时按失败处理。
- `docs/reference`
  generated step interface docs。不要手改，改 source 后 regenerate。
- `docs/evidence`
  真实机器运行证据和调查摘要。
- `examples/json-plans`
  JSON plan 示例。

## 快速开始

```powershell
dotnet build .\TwinCatAutomationKit.sln

dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- list-steps

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json --dry-run=true
```

Step 接口变化后重新生成参考文档：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- generate-docs
```

## JSON Plan 调用

`run-plan` 是面向外部 agent 和用户的主要入口。调用方写 JSON plan，CLI 按顺序执行公开 step：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json --summary=.artifacts\complex-plan-summary.json
```

写 plan 的阅读顺序：

- [docs/cli/json-plan.md](docs/cli/json-plan.md)
- [docs/reference/step-catalog.json](docs/reference/step-catalog.json)
- [docs/reference/step-catalog.md](docs/reference/step-catalog.md)
- [examples/json-plans](examples/json-plans)

`step-catalog.md` 是人看的 step 接口索引；`step-catalog.json` 是同一份 step 接口规范的机器可读版本，方便调用 agent 自动查参数。它不是完整工程示例，完整示例看 `examples/json-plans/*.json`。

## 按角色继续阅读

如果只是调用这个工具，上面的 JSON plan 阅读顺序已经够用；不需要读架构和改库手册。

如果要修改这个仓库，转到 [AGENTS.md](AGENTS.md)。`AGENTS.md` 是改库 agent 的唯一阅读顺序入口。
