# TwinCatAutomationKit Agent Guide / Agent 指南

English summary: This is the short map for agents that modify the repository. Agents that only call the tool should read `docs/cli/json-plan.md` and `docs/reference/step-catalog.json` first.

这个文件是“修改仓库的 agent”的入口地图，不是给最终调用方的完整使用手册。调用方主要看 JSON plan 和 step catalog；改库的 agent 才需要读这里的层边界、变更路线和安全规则。

## 阅读顺序

这是改库 agent 的唯一权威阅读顺序。不要同时执行 [README.md](README.md)、[docs/index.md](docs/index.md) 和本文件里的多套清单；如果已经从 [README.md](README.md) 跳到这里，不需要回头重读 README。

### 改库最小必读

1. [README.md](README.md)：只在还没读过时读，用来确认项目目标和快速命令。
2. [AGENTS.md](AGENTS.md)：当前文件，确认层边界、生成物规则和安全约束。
3. [docs/agent-change-playbook.md](docs/agent-change-playbook.md)：按需求类型选择 primary files、tests 和 evidence。
4. [ARCHITECTURE.md](ARCHITECTURE.md)：确认层职责、执行路径和 `.tsproj` mutation 策略。
5. [docs/documentation-style.md](docs/documentation-style.md)：只要要改文档或 generated docs writer，就必须读。

### 按任务追加阅读

- 改 public step spec、CLI step 或 JSON plan 能力：读 [docs/reference/step-catalog.json](docs/reference/step-catalog.json)、[docs/reference/step-catalog.md](docs/reference/step-catalog.md)、[docs/reference/recommended-execution-order.md](docs/reference/recommended-execution-order.md) 和 [docs/cli/json-plan.md](docs/cli/json-plan.md)。
- 改创建工程、模板、guided build 或项目生成流程：读 [docs/roadmap/template-based-json-builds.md](docs/roadmap/template-based-json-builds.md)。
- 触及 XAE、build、activation、signing、ADS 或真实机器结论：读 [docs/evidence/index.md](docs/evidence/index.md)，必要时补 evidence。
- 只是查文档目录：读 [docs/index.md](docs/index.md)；它是索引，不是额外阅读顺序。

## 文档语言规则

- 说明文字中文优先，方便人工审核。
- 文件名、目录名、命令、API 名、DTO 名、step kind、JSON 字段保持英文。
- 每个重要手写文档开头保留一句 `English summary`。
- generated step spec text 可以保留英文，因为它对应代码里的 API 文本。
- raw evidence logs 不强制改写，但必须有中文 summary 说明结论。

## 核心规则

- `TwinCatStepCatalog.cs` 是 public step specs 的可编辑源。
- `docs/reference/*` 是 generated output。不要手改；改 source 后运行 `generate-docs`。
- `docs/reference/step-catalog.json` 和 `docs/reference/step-catalog.md` 是同一份 step spec 的机器/人类 companion 输出；改 step spec 或文档生成器时必须同一次 regenerate，不要只改其中一个。
- 一个 public step 应对应一个 TwinCAT 人类操作、一个 service method、一个 catalog entry、一个 CLI path 和一个 verification story。
- 使用 TwinCAT/XAE 官方模板是正确方向；不要追求绕开模板手写整个工程文件。
- `guided-build` 这类 macro command 只能是 recipe，不能掩盖 step-level 能力缺口。
- Beckhoff 提供稳定 COM path 时，优先用 direct TwinCAT service method。
- `.tsproj` 修改默认必须走我们已经写好的专用 API。大多数情况应是 dedicated primitive，例如 task、instance、binding、parameter、pointer、mapping、PLC metadata。
- 如果发现没有可用 API，不要临时拼 XML。收集信息并反馈：人类操作目标、变更前后 XML、应修改的节点路径、字段含义、验证方式、建议 API 名称。
- `tsproj.upsert-*` 只适合小范围、已理解的 XML gap；调用者必须知道每个字段为什么存在。
- `MergeNamedElementFragment` / `tsproj.merge-fragment` 是高风险 escape hatch。只有片段来自 known-good `.tsproj`、知识库说明清楚、插入层级明确、参数含义明确、并有测试或真实 evidence 时才允许使用；请求必须填写 `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence`。
- 重要工程 mutation 必须留下 durable evidence：XML snapshot、summary JSON、build log、activation archive 或 ADS readback。
- ObjectId math/encoding 使用 `TwinCatTsprojMutationService.DeriveNextObjectId` 和 `ConvertObjectIdToInitSymbolData`。

## Layer Boundaries

- `Abstractions`：只放 step specs、DTOs、run state 和 evidence model。不放 TwinCAT COM、ADS、DTE、XML mutation implementation 或 process execution。
- `Core`：放 orchestration 和 documentation tooling。不放 Beckhoff-specific automation。
- `TwinCat`：放所有 Beckhoff/DTE/`.tsproj`/signing/ADS 实现。
- `Cli`：把 user input 和 JSON plan 转成 public service calls。不实现第二套 TwinCAT 行为。
- `tests/TwinCatAutomationKit.IntegrationTests`：真实 TwinCAT/VS 行为验证。缺 config、activation、ADS 或 signing 前置条件时必须 fail，不能 clean skip。

## 安全扩展路径

1. 只有 public shape 变化时，才改 `TwinCatAutomationKit.Abstractions` DTO。
2. 在 `TwinCatAutomationKit.TwinCat` 实现行为。
3. 在 `TwinCatStepCatalog` 注册 step spec。
4. 在 `StepInvokeCommand` 和 `StepInvocationCatalog` 接 direct CLI。
5. pipeline/JSON 需要时再加 `TwinCatAtomicSteps` wrapper。
6. 增加或扩展 tests；真实 TwinCAT/VS 行为优先补 integration test。
7. 重新生成 `docs/reference`。
8. 触及 XAE、build、activation、signing 或 ADS 时，在 `docs/evidence` 记录真实机器 evidence。

## Change Routing

- Step spec shape：改 `TwinCatStepCatalog.cs`，再 regenerate `docs/reference`。
- DTO shape：改 `TwinCatRequests.cs`，再更新 services、CLI、tests。
- TwinCAT engineering behavior：改 `TwinCatEngineeringService.cs`。
- `.tsproj` file mutation：改 `TwinCatTsprojMutationService.cs`。
- Signing behavior：改 `TwinCatSigningService.cs`。
- ADS readback：改 `AdsValidationService.cs`。
- Pipeline wrapper/state：改 `TwinCatAtomicSteps.cs`。
- CLI behavior：改 `src/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli/Commands/*`。
- Orchestration/doc writer：改 `src/TwinCatAutomationKit.Core/*`。

## Generated Docs

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- generate-docs
```

## Validation Gate

真实 TwinCAT/VS 检查：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

完成前确认：

- public step specs 与 implementation/CLI 行为一致。
- generated docs 已更新。
- 触及真实工程行为时，integration test 或 evidence 已说明结果。
- evidence/log additions 已在 `docs/evidence` 建索引。
- 不包含 `bin/`、`obj/`、`.claude/`、`.artifacts/` 或 root `*.log`。
