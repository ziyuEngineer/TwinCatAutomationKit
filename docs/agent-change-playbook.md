# Agent Change Playbook / 改库 Agent 变更手册

English summary: Use this playbook before editing the repository. It maps requested behavior to the correct layer, files, generated docs, tests, and evidence.

这个文件给“修改仓库的 agent”读。它不是调用说明；调用方应优先读 `docs/cli/json-plan.md` 和 `docs/reference/step-catalog.json`。

## 必读路径

1. `README.md`
2. `AGENTS.md`
3. `docs/documentation-style.md`
4. `ARCHITECTURE.md`
5. `docs/reference/step-catalog.json`
6. `docs/cli/json-plan.md`
7. `docs/roadmap/template-based-json-builds.md`
8. `docs/evidence/index.md`
9. 本文件

## Change Routing Matrix

| 需求类型 | Primary files | Also update | 不要做 |
|---|---|---|---|
| Request/response DTO shape | `src/TwinCatAutomationKit.Abstractions/.../TwinCatRequests.cs` | affected services, CLI, tests | 不要把 TwinCAT implementation 放进 `Abstractions` |
| Step spec fields、inputs、outputs、verification | `src/TwinCatAutomationKit.TwinCat/.../TwinCatStepCatalog.cs` | regenerate `docs/reference/*`; tests | 不要手改 generated reference docs |
| Live XAE/DTE behavior | `TwinCatEngineeringService.cs` | catalog/CLI/tests/evidence if public | 不要把 COM logic 移到 `Core` |
| `.tsproj` file mutation behavior | `TwinCatTsprojMutationService.cs` | catalog/CLI/integration tests/docs | 不要把 XML edit 分散到 CLI 或 tests |
| Missing `.tsproj` API | issue note/evidence first | propose dedicated API name, XML before/after, parent path | 不要临时复制未知 XML fragment |
| Generic XML mutation API | `UpsertElement`, `UpsertFragment`, `ApplyMutationPlan` | DTOs, catalog, CLI, `tsproj-xml-mutation-api.md`, tests | 有 dedicated primitive 时不要用 generic upsert |
| Fragment-based mutation | known-good fragment knowledge base | `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence` | 不要在不理解片段含义时使用 `merge-fragment` |
| Signing behavior | `TwinCatSigningService.cs` | catalog/CLI/tests/evidence | 不要打印密码或把密码写进 evidence |
| ADS runtime validation | `AdsValidationService.cs` | catalog/CLI/tests/evidence | 不要把 build success 当成 runtime validation |
| Pipeline wrapper/state behavior | `TwinCatAtomicSteps.cs` | catalog alignment and tests | 不要在 wrappers 里复制 service logic |
| CLI behavior | `src/TwinCatAutomationKit.Cli/.../Commands/*` | help text, CLI tests, JSON docs | 不要绕过 public service methods |
| JSON plan behavior | `JsonPlanCommand.cs` and `docs/cli/json-plan.md` | examples and tests | 不要引入 hidden global state |
| Generated reference docs | source spec files | run `generate-docs` | 不要 patch `docs/reference/*` |
| Real-machine investigation | `docs/evidence/*` | summary index and raw log placement | 不要把 root `*.log` 放进 repo |

## 安全扩展流程

1. 把需求映射成一个 TwinCAT 人类操作。
2. 按 matrix 选择层和文件。
3. 只有 public shape 变化时才加 DTO。
4. 在 service layer 实现行为。
5. 注册或调整 `TwinCatStepCatalog`。
6. 如果 step 公开给 CLI，接入 `invoke-step`。
7. 只有 pipeline/JSON 需要时才加 `TwinCatAtomicSteps`。
8. 增加或扩展 tests；真实 TwinCAT 行为优先补 integration test。
9. 重新生成 reference docs。
10. 触及 XAE、build、activation、signing 或 ADS 时，补充 evidence notes。

## `.tsproj` API 缺口处理

默认假设 `.tsproj` 修改应该有专用 API。发现没有可用 API 时，不要让 agent 自己拼 XML 完成任务。应该记录：

- 人类在 XAE 里做的操作是什么。
- 变更前后的 `.tsproj` XML 片段。
- 目标节点父路径和插入层级。
- 每个关键字段、属性、ObjectId、GUID、port 的含义。
- 可以怎样用 XAE reopen、build、activation 或 ADS readback 验证。
- 建议新增的 step kind 和 DTO 字段。

这些信息进入 issue note、evidence 或 roadmap，后续由开发者补 dedicated primitive。

## Hard Guardrails

- Public step name 使用 `<category>.<verb>-<target>`。
- `docs/reference/step-catalog.json` 必须和 `TwinCatStepCatalog` 同步。
- `docs/reference/step-catalog.json` 和 `docs/reference/step-catalog.md` 必须作为 companion generated docs 同步更新；不要只 patch 其中一个。
- `StepInvocationCatalog` 必须覆盖每个 public direct CLI step。
- 优先 dedicated `.tsproj` primitives，再考虑 generic upserts。
- `MergeNamedElementFragment` 是高风险 escape hatch，不是默认方案；请求缺少 `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence` 时应失败。
- ObjectId 使用 `TwinCatTsprojMutationService` 的 helper。
- 公开接口必须进入真实 TwinCAT/VS integration test；缺 config 或 runtime 前置条件时应 fail，不要 skip。
- Machine-specific config 不进 source control。

## Done Checklist

完成前检查：

1. Changed files 符合 routing matrix。
2. Public step specs、implementation、CLI 行为一致。
3. Step spec 变化后已 regenerate docs。
4. 行为有测试覆盖；没有覆盖时说明 residual risk。
5. Integration tests 通过：

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

6. Real-machine evidence 已在 `docs/evidence` 建索引。
7. 没有引入 `bin/`、`obj/`、`.claude/`、`.artifacts/` 或 root `*.log`。

## 常见失败模式

这些条目不是一次性警告。每次真实问题被定位后，应把“以后先看哪里、不要误判什么”写回本手册、相关调用文档或 `docs/evidence` summary，让下一个 agent 不需要重新翻 raw log。

- 直接 patch generated docs。
- 加了 DTO，但没有接 CLI 或 catalog。
- CLI command 自己编辑 XML，而不是调用 `TwinCatTsprojMutationService`。
- 加 public step，没有测试和真实验证计划。
- activation 失败时先怀疑 signing，却没有检查 `.tsproj` task、instance、pointer binding。
- build 或 activation 成功后就宣布 runtime proof，却没有 ADS readback 或等价运行时证据。
- 同一个 `.tsproj` 重复使用 task AMS port。
- 把 `D:\t\some_user_run` 这类一次性本机路径写进 reusable example。
- 从未知工程复制 XML fragment，却说不清字段含义和插入层级。
- 把 raw log 堆进 `docs/evidence/raw/`，但没有 dated summary entry、结论和后续排查入口。
