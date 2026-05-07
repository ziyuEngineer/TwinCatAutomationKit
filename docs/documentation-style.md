# Documentation Style / 文档风格

English summary: Documentation is Chinese-first with stable English anchors for code symbols, CLI commands, step kinds, DTOs, paths, and generated step specs.

本文档定义本仓库文档怎么写。目标是同时满足两件事：你能用中文快速审核，agent 能用稳定的英文符号准确定位代码和 step 接口规范。

## 结论

采用“中文优先 + English anchors”，不是逐句双语，也不是纯英文。

- 说明、规则、背景、故障结论、审核判断：用中文。
- 文件名、目录名、命令、API、DTO、step kind、JSON 字段、日志路径：保持英文。
- 每个重要手写文档开头放一句 `English summary`，让非中文 agent 快速知道文档用途。
- generated step docs 可以保留英文 API text，因为它对应代码里的稳定描述；周围标题和说明用中文补充。

## 为什么这样写

纯英文不利于人工审核。逐句双语会让文档膨胀，消耗 agent 上下文，也更容易腐烂。中文优先能保证审核效率，English anchors 能保证 agent 可以稳定搜索 `StepContract`、`TwinCatTsprojMutationService`、`tsproj.apply-mutation-plan` 这类真实代码符号。

这个做法参考了 agent-first 工程里的几个原则：仓库本身是记录系统、`AGENTS.md` 是地图而不是百科全书、文档要按渐进式披露组织、重要事实要能从仓库本地发现。

## 手写文档规则

- 标题可以中英并列，例如 `Documentation Index / 文档索引`。
- 第一段优先写中文，然后保留一行简短 `English summary`。
- 不翻译代码符号。比如写 `TwinCatStepCatalog`，不要写成“孪生猫步骤目录类”。
- 命令必须能直接复制运行；不要为了中文化改动参数名。
- 对 agent 的操作约束要短、明确、可检查。
- 长日志不要直接塞进主文档；把结论写成 summary，原始日志放 `docs/evidence/raw/`。

## Generated Docs 规则

- `docs/reference/*` 由 `generate-docs` 生成，不手改。
- `docs/reference/step-catalog.json` 和 `docs/reference/step-catalog.md` 是同一份 step spec 的机器/人类 companion 输出；改 step spec 或生成器时必须同一次 regenerate 两个文件。
- `step-catalog.json` 必须保留 agent 可读的使用协议、同步规则、字段含义和唯一 `Steps` 列表；`step-catalog.md` 必须保留人类可读的读法、分类简表和完整 step 详情。
- 如果 `step-catalog.json` 与 `step-catalog.md` 内容冲突，视为 generated docs stale；不要手工调和，先回到 `TwinCatStepCatalog.cs` 或 `DocumentationSuiteWriter.cs` 修源头，再运行 `generate-docs`。
- 生成器可以输出中文标题、中文说明和双语标签。
- `StepContract.Summary`、input/output description、verification notes 保持 catalog 中的原文，避免翻译后和代码里的 step spec 不一致。
- 如果 public step spec 变化，先改 source，再 regenerate。

## Evidence 规则

- `docs/evidence/*.md` 用中文总结“证明了什么”和“以后怎么判断”。
- `docs/evidence/raw/*` 保留原始机器输出，可以是英文或混合语言，不做风格重写。
- 任何 raw log 被保留，都要在 index 或 dated evidence file 中解释原因。

## 审核清单

新文档或修改后的文档应满足：

- 人能在 1 分钟内知道这个文件解决什么问题。
- Agent 能从文档里的路径、类名、step kind 找到对应代码。
- 规则能转化为测试、命令、routing matrix 或 evidence。
- 没有把临时本机路径伪装成通用说明。
- 没有把 generated artifact 当成手写 source 修改。
