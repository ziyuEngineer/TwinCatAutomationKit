# CLI Program / CLI 程序

English summary: The CLI is the public command surface for agents and external automation. Every public step should be reachable through `invoke-step`, and multi-step automation should use `run-plan`.

CLI 是这个库给外部 agent 和人类脚本使用的稳定入口。它不应该实现第二套 TwinCAT 行为，只负责把命令行参数或 JSON plan 转成 library layer 的 public service calls。

## Commands

- `list-steps`
- `show-order`
- `generate-docs`
- `help`
- `help step --kind=<step-kind>`
- `invoke-step --kind=<step-kind> ...`
- `run-plan --file=<plan.json>`
- `guided-build` 和 `guided-build-plan`：旧的 macro recipe。
- `ads-read`：兼容命令；新代码优先用 `invoke-step --kind=validation.ads-read`，多变量 runtime 证明优先用 `invoke-step --kind=validation.ads-read-symbols`。

## 当前基线

- Public step kinds 来自 `TwinCatStepCatalog`；以 `docs/reference/step-catalog.json` 为准。
- Direct `invoke-step` coverage：当前每个 public step kind 都有 CLI path。
- JSON plan 入口：`run-plan`。
- 大 payload 输入：优先用 `--xml-file` 和 `--json-file`。

需要区分两件事：

- CLI parity 表示 step 有 direct command path。
- Operational proof 还需要 tests；涉及 engineering/runtime 的行为还需要真实 XAE/TwinCAT evidence。

## 推荐用法

列出可用操作：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- list-steps
```

查看一个操作：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- help step --kind=tsproj.apply-mutation-plan
```

执行一个操作：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.ensure-parameter --project-path=D:\t\demo\Demo\Demo.tsproj "--instance-name=Demo_Obj1 (DemoCpp)" --parameter-name=Parameter.data1 --value-text=123
```

执行 JSON plan：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json --dry-run=true
```

## Code Layout

- CLI entry: `src/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli/Program.cs`
- Commands: `src/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli/Commands/`
- Step support list: `src/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli/Features/StepInvocation/StepInvocationCatalog.cs`
- Shared helpers: `src/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli/Shared/`

## 改 CLI 时的规则

- 新 public step 先在 service 和 catalog 里成立，再接 CLI。
- `invoke-step` 的 request preview 要能帮助用户确认输入。
- JSON plan 行为变化要更新 [json-plan.md](json-plan.md) 和示例。
- 不要在 CLI command 中写 ad-hoc `.tsproj` XML edit。

## 继续阅读

- [JSON Plan Runner](json-plan.md)
- [Direct CLI Real XAE Guide](direct-cli-real-xae-guide.md)
- [Validation Log](validation-log.md)
- [Step Record Template](step-record-template.md)
- [Generated Step Catalog](../reference/step-catalog.md)
