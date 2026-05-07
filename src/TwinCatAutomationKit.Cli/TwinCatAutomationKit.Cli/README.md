# CLI Project / CLI 项目

English summary: This project hosts the repository-facing command-line surface for TwinCatAutomationKit.

这个项目是 TwinCatAutomationKit 的命令行入口。CLI 只做 routing、parsing、workspace resolution、help 和 request preview；真实 TwinCAT 行为必须在 library layer 中实现。

## Rules

- `Program.cs` 只保持 top-level command routing。
- User-facing command entry points 放在 `Commands/`。
- Parsing、workspace resolution、JSON formatting、repository-root lookup 放在 shared helpers，避免每个 command 复制。
- 不要把 TwinCAT engineering behavior 移进 CLI layer。
- `guided-build` 是 macro recipe，不能用它跳过 public step kind 的 direct CLI parity。
- 大 XML/JSON payload 优先走 `--xml-file` 和 `--json-file`。

## Current Layout

- `Program.cs`
  Top-level command dispatch。
- `Commands/StepInvokeCommand.cs`
  `invoke-step` dispatch、request preview、common helper logic。
- `Commands/StepInvokeCommand.Engineering.cs`
  `engineering.*` step kinds 的 direct CLI adapters。
- `Commands/StepInvokeCommand.Tsproj.cs`
  `tsproj.*` step kinds 的 direct CLI adapters。
- `Commands/GuidedBuildCommand.cs`
  Macro recipe runner、option parsing、reporting、runtime state。
- `Commands/GuidedBuildCommand.Definitions.cs`
  guided-build step recipe definitions；不要和 step-level parity work 混在一起。
- `Commands/Help`, `Commands/Reference`, `Commands/Validation`
  Non-step command entry points 和 help text。
- `Features/StepInvocation`
  Step invocation catalog 和 support types。
- `Shared/`
  Parser、workspace resolution、repository-root helpers。

## Adding A New CLI Step

1. 先在正确 library layer 复用或新增行为。
2. 在 `TwinCatStepCatalog` 中确认 public step contract。
3. 为对应 step kind 增加一个 direct CLI path。
4. 更新 help text 和 request preview。
5. 增加或扩展 tests。
6. 在 `docs/cli/validation-log.md` 或 step record 中记录验证结论。
