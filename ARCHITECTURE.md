# Architecture / 架构

English summary: TwinCatAutomationKit is a CLI/JSON-first control plane that uses TwinCAT/XAE templates and then applies composable steps to mutate, build, activate, and validate projects.

TwinCatAutomationKit 的架构目标不是绕开 XAE 模板，也不是复制旧脚本，而是把“人类在 XAE 中会做的具体操作”拆成可复用、可组合、可测试、可留下证据的 step-level control plane。

## 目标形态

目标用户写一个 JSON plan，执行一个 CLI command，然后得到 TwinCAT 工程和 evidence：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=my-project.json
```

工程创建应像人类使用 XAE 一样：先通过 TwinCAT/Beckhoff 官方模板得到默认工程结构，再用明确的细粒度 step 修改它。禁止依赖隐藏 seed folder、旧工程复制或未记录的本机模板目录。

## 核心原则

### One Action, One Interface

每个 TwinCAT 人类可见操作都应有：

- 一个 public request/response DTO shape。
- 一个 service method。
- 一个 `StepContract` step spec 代码对象。
- 一个 direct `invoke-step` CLI path。
- 一个 verification story。

`guided-build` 这样的 macro command 只能是 recipe，不能掩盖 step-level 能力缺口。只要 macro flow 需要一个动作，这个动作就应能被单独的 step 表达、调用和验证。

### Step Specs Before Orchestration

`TwinCatStepCatalog` 定义公开 step vocabulary，并生成：

- Markdown/JSON reference docs。
- recommended execution order。
- CLI help 的接口说明。
- catalog integrity tests 的基准。

调用方和 future agent 应先读 `docs/reference/step-catalog.json`。它是机器可读的 step 接口规范，不是 JSON plan 示例。完整 plan 示例在 `examples/json-plans/`。

### Engineering Success Is Not Runtime Proof

build 或 activation 成功只是必要条件，不是完整证明。可靠运行应留下分层 evidence，并且 evidence 要让后续 agent 能快速判断，不需要在 raw log 里乱找。

每次重要运行至少应尽量记录：

- plan 或 CLI command：输入是什么。
- run summary：哪些 step 成功、跳过、失败。
- generated files 和 `.tsproj` snapshot：工程结构变成了什么。
- exported TwinCAT tree XML：XAE/COM 看到的结构是什么。
- build log 和 signing output：编译与签名结果。
- activation archive 或 current config：运行时配置实际是什么。
- ADS readback：runtime symbol 是否能读到期望值。
- conclusion note：这一轮证明了什么，没证明什么，下一步该看哪里。

Evidence 放在 run output 或 `docs/evidence`。raw logs 只能作为附录，必须有 summary 指向它们。

### Evidence Feedback Loop / 经验沉淀闭环

evidence 的价值不是堆日志，而是把一次失败或成功变成后续 agent 能复用的判断规则。每次解决真实 TwinCAT 问题后，都应把结论沉淀到合适位置：

- 面向调用方的经验：写进 `docs/cli/json-plan.md`、`examples/json-plans/*` 或 step verification notes，例如哪些 `enabled` flag、short path、ADS port、symbol path 组合是必要的。
- 面向改库 agent 的经验：写进 `docs/agent-change-playbook.md`、`ARCHITECTURE.md` 或相关 evidence summary，例如缺什么 dedicated API、哪个 layer 不能放实现、哪种 failure signal 容易误判。
- 面向真实机器证明的材料：写进 `docs/evidence/YYYY-MM-DD-*.md`，raw log 只作为附录；summary 必须说明“证明了什么、没有证明什么、下次先看哪里”。

常见失败不是单独的 checklist，它们应该回流成开发和使用规则。典型例子：

- build 成功但 activation 失败：先看 task、instance、pointer binding 和 signing，不要把 build success 当 runtime proof。
- activation 成功但 ADS readback 失败：先区分 AMS 权限、runtime state、target port 和 exact symbol path，再考虑改 `.tsproj` mutation。
- generic XML 能跑一次：不代表可以作为 public recipe；如果字段含义说不清，应记录 API 缺口并补 dedicated primitive。
- raw log 很长：不代表 evidence 充分；没有中文 summary 和下一步判断入口时，后续 agent 仍然会重复踩坑。

## Layers

### Abstractions

包含：

- request/response records。
- step specs。
- run state。
- evidence records。
- automation step interface。

不包含：

- DTE。
- Beckhoff COM。
- ADS client calls。
- XML mutation implementation。
- CLI parsing。

### Core

包含：

- `AutomationPipelineRunner`。
- run summary JSON/CSV output。
- generated reference documentation writer。

`Core` 可以 orchestrate generic steps，但不知道 TwinCAT 内部细节。

### TwinCat

包含：

- `TwinCatEngineeringService`。
- `TwinCatTsprojMutationService`。
- `TwinCatSigningService`。
- `AdsValidationService`。
- `TwinCatAtomicSteps`。
- `TwinCatStepCatalog`。

这是唯一允许接触 TwinCAT-specific API、文件格式、signing tool 和 runtime ADS 的层。

### CLI

包含：

- `invoke-step`。
- `run-plan`。
- `list-steps`。
- `show-order`。
- `generate-docs`。
- help、workspace 和 parsing helpers。

CLI 是外部调用的主要入口。它的职责是把 arguments/JSON 转成 public service request，不应复制 service behavior。

## Execution Paths

### CLI Single-Step Path

人或 agent 只执行一个明确操作时使用：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=tsproj.ensure-mapping-link --project-path=Demo.tsproj --owner-a-name=... --owner-b-name=... --var-a=... --var-b=...
```

### JSON Plan Path

需要从很多显式操作搭一个工程时使用：

1. 定义 `variables`。
2. 定义 default workspace options。
3. 用 `files` 创建 XML/JSON payload files。
4. 按顺序执行 `steps`。
5. 用 `${steps.id.outputs.name}` 引用前面步骤的输出。

JSON runner 最终仍 dispatch 到同一个 `invoke-step` surface。

## `.tsproj` Mutation Strategy

TwinCAT 并不是所有写操作都有稳定 COM path，所以 `.tsproj` mutation 是一等能力。但默认策略不是让 agent 任意改 XML，而是把常见人类操作沉淀成专用 API。

覆盖范围：

- tasks 和 task layout。
- C++/PLC instance declarations。
- PLC metadata。
- task bindings。
- parameter values。
- interface/data pointers。
- mappings。
- root sections，例如 `DataTypes`、`Io`、`System/Settings`。

mutation 优先级：

1. dedicated stable primitive，例如 `tsproj.ensure-task`。
2. batch primitive，例如 `tsproj.apply-instance-parameter-plan`。
3. 小范围、已理解的 generic `tsproj.upsert-element`、`tsproj.upsert-fragment`、`tsproj.apply-mutation-plan`。
4. 发现没有合适 API 时，记录缺口并反馈给开发者补 API。
5. `tsproj.merge-fragment` 只作为高风险 escape hatch，必须有 known-good fragment 知识库、明确插入层级、字段解释、测试或真实 evidence；代码要求 `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence`，并拒绝不唯一的 parent element。

## Testing Strategy

测试策略改为真实 TwinCAT/VS integration-only。catalog、CLI、`.tsproj` mutation、pipeline wrapper、signing、activation 和 ADS 都必须挂到真实 XAE 生成的工程链路里验证；缺少本机 config 或 runtime 前置条件时测试应失败，而不是 skip。

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

集成测试应尽量在同一个短路径工程里顺序推进：前一个 step 的真实输出既是验证对象，也是后一个 step 的基座。这样可以同时证明“每一步真的生效”和“组合起来能继续往下走”。

理想集成测试链路：

1. 用 TwinCAT/XAE 模板创建 solution/project。
2. 添加 C++/PLC project、module、instance、task。
3. 通过专用 `.tsproj` API 修改 binding、parameter、pointer、mapping。
4. reopen XAE，确认 `.tsproj` file mutation 能被 XAE 读回。
5. build。
6. signing。
7. activation。
8. ADS readback。

测试中的 `.tsproj` mutation 不是孤立 fixture 检查：它必须作用在本轮由 XAE 模板生成的真实工程上，并通过 reopen、export XML、build、activation 或 ADS readback 继续证明。不能把 build success 当 runtime proof。
