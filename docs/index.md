# Documentation Index / 文档索引

English summary: This directory separates caller-facing docs from repository-maintainer docs, then links generated step specs, operational guides, roadmap items, and evidence.

这个目录按角色组织。调用这个工具的 agent 主要需要 CLI、JSON plan 和 step catalog；修改这个仓库的 agent 需要读架构、变更路线、测试和 evidence。

## 调用这个工具的 agent 先读

- [JSON Plan Runner](cli/json-plan.md)：`run-plan` 文件结构、变量、payload 和 runtime 规则。
- [CLI Index](cli/index.md)：CLI 入口和常用命令。
- [Step Catalog JSON](reference/step-catalog.json)：机器可读 step 接口规范，不是完整 plan 示例。
- [Step Catalog](reference/step-catalog.md)：人类可读 step 接口索引。
- [Complex Full Project JSON](../examples/json-plans/complex-full-project.json)：完整 JSON plan 示例。

## 修改这个仓库的 agent 先读

- [README.md](../README.md)：项目目标、当前状态、快速命令。
- [AGENTS.md](../AGENTS.md)：改库前必须读的短地图。
- [Architecture](../ARCHITECTURE.md)：层边界、执行路径、测试策略。
- [Agent Change Playbook](agent-change-playbook.md)：routing matrix 和 done checklist。
- [Documentation Style](documentation-style.md)：中文优先、English anchors、generated docs 的写法。

## Step Specs / Step 接口规范

- [Step Catalog](reference/step-catalog.md)
- [Step Catalog JSON](reference/step-catalog.json)
- [Recommended Execution Order](reference/recommended-execution-order.md)
- [Legacy Capability Map](reference/program-real-samples-capability-map.md)
- [Tsproj XML Mutation API](reference/tsproj-xml-mutation-api.md)
- [Interface Extension Workflow](reference/interface-extension-workflow.md)

`docs/reference/*` 是 generated artifacts。不要手改。改 `TwinCatStepCatalog`、DTO 或 documentation writer 后运行：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- generate-docs
```

## CLI 与 JSON Plan

- [CLI Index](cli/index.md)
- [JSON Plan Runner](cli/json-plan.md)
- [Direct CLI Real XAE Guide](cli/direct-cli-real-xae-guide.md)
- [CLI Validation Log](cli/validation-log.md)
- [Step Record Template](cli/step-record-template.md)

## Roadmap / 路线

- [Template-Based JSON Builds](roadmap/template-based-json-builds.md)：使用 TwinCAT/XAE 官方模板，再用细粒度 step 修改模板生成内容。

## Evidence / 证据

- [Evidence Index](evidence/index.md)：真实机器结论的入口。
- `docs/evidence/raw/`：只存保留价值明确的原始日志。raw log 不要求改成中文，但必须有中文 summary 指向它。

## 新 Agent 推荐读法

调用方：

1. 读 `docs/cli/json-plan.md`。
2. 读 `docs/reference/step-catalog.json` 查可用 step 和参数。
3. 参考 `examples/json-plans/*.json` 写自己的 plan。
4. 先用 `--dry-run=true` 验证变量和参数。

改库方：

1. 以 `AGENTS.md` 的阅读顺序为唯一权威入口；不要把本索引当成第二套清单重复执行。
2. 按 `docs/agent-change-playbook.md` 的 routing matrix 修改。
3. Step 接口变化后 regenerate reference docs。
4. 跑真实 TwinCAT/VS integration tests；触及 activation、signing 或 ADS 时补 durable evidence。
