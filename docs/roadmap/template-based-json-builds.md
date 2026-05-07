# Template-Based JSON Builds / 基于 TwinCAT 模板的 JSON 构建路线

English summary: The goal is a JSON plan that drives TwinCAT/XAE templates like a human operator, then applies fine-grained steps to modify, build, activate, and validate the generated project.

最终目标是：用户只写一个 JSON plan，CLI 就能像人类操作 XAE 一样，先用 Beckhoff/TwinCAT 官方模板创建默认工程，再用明确的 step 删除、修改、补齐和验证工程内容。

这不是从零手写工程文件。TwinCAT 模板是正确基座，因为人类在 XAE 里也通常从模板创建工程，然后继续添加 project、module、task、instance、mapping 和参数。

## 目标边界

应该做：

- 使用已安装的 TwinCAT/XAE 官方模板创建 solution、`.tsproj`、C++ project、PLC project。
- 把模板生成后的具体修改拆成可组合 step。
- 对 `.tsproj` 修改优先开发 dedicated primitive。
- 每一步留下能复查的 evidence。
- 让 JSON plan 可以从空输出目录稳定生成工程，不依赖旧工程复制。

不应该做：

- 绕过 TwinCAT 模板，手写全部 `.sln`、`.tsproj`、`.vcxproj`、`.plcproj`。
- 依赖隐藏 seed folder。
- 从本机旧工程复制未知 XML fragment。
- 让 agent 在不了解字段含义时拼接 `.tsproj`。

## Current Baseline

当前已有：

- `engineering.create-xae-solution`：通过 XAE 模板创建 solution/project。
- `engineering.create-cpp-project`：通过 Beckhoff wizard 创建 C++ project，并在 wizard output 不完整时做 deterministic fallback。
- `engineering.create-plc-project`：通过可用 PLC template 创建 PLC project。
- `engineering.add-module-instance`、`engineering.ensure-task`：用 XAE/COM 执行人类可见操作。
- `tsproj.*`：对模板生成内容做细粒度、可测试的 `.tsproj` file mutation。
- `run-plan`：把上述 step 串成 JSON 自动化流程。

## Roadmap Items

### Template Discovery

需要让 CLI/JSON 更清楚地发现和记录本机可用模板：

- XAE solution template。
- C++ project wizard id。
- PLC template path。
- TwinCAT version 和 Visual Studio DTE version。

每次使用模板创建工程时，应在 summary/evidence 中记录实际用到的模板来源。

### Template Output Normalization

模板输出可能随 TwinCAT/VS 版本变化。需要把“模板生成后的规范化修改”沉淀成 step：

- 删除或重命名默认节点。
- 设置 project metadata。
- 创建或更新 task。
- 创建 module instance。
- 绑定 task/instance/pointer。
- 统一 AMS port、ObjectId、mapping 和 parameter。

### Dedicated `.tsproj` API Expansion

如果发现某个人类操作只能靠 generic XML 或 fragment 表达，应先记录缺口，再补 dedicated primitive。记录内容：

- 操作目标。
- 修改前后 XML。
- 插入父路径和层级。
- 字段含义。
- 可验证 evidence。
- 建议 step kind 和 DTO。

### Fragment Knowledge Base

`tsproj.merge-fragment` 只有在 fragment 知识库足够清楚时才允许使用。每个 fragment entry 至少要说明：

- `FragmentSource`：来源工程、TwinCAT 版本或 dated evidence。
- 对应的人类 XAE 操作。
- `TargetParentPath`：目标父路径。
- `FieldMeaning`：每个关键字段为什么这样写。
- 与 ObjectId、GUID、AMS port、symbol name 的关系。
- `VerificationEvidence`：已通过的真实 XAE/TwinCAT integration evidence。

没有这些信息时，代码应拒绝执行；应反馈为 API 缺口。

## Acceptance Gate

模板驱动 JSON 构建里程碑完成时，应能在 fresh 短路径目录运行：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json
```

并且结果满足：

- XAE 能打开生成工程。
- Build 成功。
- 必要时 signing 成功。
- Activation 成功。
- ADS readback 能证明 runtime symbol。
- Evidence 能让后续 agent 快速判断每一步证明了什么。
