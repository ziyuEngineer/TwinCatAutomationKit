# JSON Plan Runner / JSON Plan 执行器

English summary: `run-plan` executes an ordered JSON file of `invoke-step` operations. It is the preferred interface for external agents that need to compose many fine-grained TwinCAT actions.

`run-plan` 是外部 agent 最重要的入口：把很多极细粒度 `invoke-step` 操作按顺序写进一个 JSON 文件，然后一次执行。它的设计目标是让用户只输入自己写的 JSON，就能像人类操作 XAE 一样，先使用 TwinCAT 模板创建默认工程，再逐步修改、构建、激活和验证。

## Command

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json --dry-run=true
```

写 plan 时用 `docs/reference/step-catalog.json` 查每个 step 支持哪些参数。它是机器可读 step 接口规范，不是完整 plan 示例；完整示例看 `examples/json-plans/`。

常用选项：

```powershell
--dry-run=true
--stop-on-failure=true
--summary=.artifacts\complex-plan-summary.json
```

第一次永远先跑 `--dry-run=true`。Dry-run 会解析变量和 step options，但不会写 payload files、打开 Visual Studio 或修改 TwinCAT project。

## 文件结构

```json
{
  "schemaVersion": 1,
  "name": "my-plan",
  "variables": {
    "root": "${plan.dir}\\..\\..\\.artifacts\\my-plan",
    "solutionPath": "${root}\\Demo.sln",
    "projectPath": "${root}\\Demo\\Demo.tsproj"
  },
  "defaults": {
    "solution-path": "${solutionPath}",
    "project-path": "${projectPath}",
    "visible": false,
    "startup-delay-ms": 8000
  },
  "files": [
    {
      "path": "${root}\\payloads\\data-types.xml",
      "content": "<DataTypes><DataType><Name>ExampleType</Name></DataType></DataTypes>"
    }
  ],
  "steps": [
    {
      "id": "createSolution",
      "kind": "engineering.create-xae-solution",
      "options": {
        "solution-directory": "${root}",
        "solution-name": "Demo",
        "project-name": "Demo"
      }
    }
  ]
}
```

## 字段说明

- `variables`：可复用变量。值可以引用前面的变量，例如 `${root}`。
- `defaults`：合并到每个 step 的默认 options，step 自己的 `options` 优先。
- `files`：执行时写入的 payload files。`content` 可以是 string、object 或 array。
- `steps`：有序 `invoke-step` 调用。
- `steps[].id`：稳定 id，供后续 output reference 使用。
- `steps[].kind`：任何 `invoke-step` 支持的 kind。
- `steps[].options`：CLI option 名，不写 `--`。
- `steps[].enabled`：可选 boolean 或 `${variable}` string。禁用的 step 会被标记为 skipped。

## Interpolation / 插值

变量引用：

```json
"projectPath": "${root}\\Demo\\Demo.tsproj"
```

引用前面 step 的输出：

```json
"task-object-id": "${steps.ensureTask.outputs.objectId}"
```

内置变量：

- `${plan.dir}`：JSON plan 文件所在目录。
- `${cwd}`：当前工作目录。

## Payload Files

大 XML 或 JSON payload 用 `files[]` 加 `--xml-file` / `--json-file`，不要把复杂内容塞进一长串命令行参数。

Dedicated batch payload 示例：

```json
{
  "path": "${root}\\payloads\\parameter-plan.json",
  "content": [
    {
      "instanceName": "Demo_Obj1 (DemoCpp)",
      "parameterName": "Parameter.data1",
      "valueText": "123"
    }
  ]
}
```

然后调用：

```json
{
  "id": "applyParameterPlan",
  "kind": "tsproj.apply-instance-parameter-plan",
  "options": {
    "json-file": "${root}\\payloads\\parameter-plan.json"
  }
}
```

能用 dedicated primitive 表达时，优先用 `tsproj.ensure-parameter`、`tsproj.apply-instance-parameter-plan` 这类专用 step。Generic mutation plan 只用于低层 XML gap 或 JSON-owned 的小范围结构补齐；如果字段含义、父路径或验证方式说不清，应先记录 API 缺口，不要临时拼 XML。

## Runtime Rules

- TwinCAT output path 要短；XAE build 对长路径敏感。
- 同一个 `.tsproj` 内 task AMS port 必须唯一。
- 只有 real TcCOM instance 绑定到 real task 后，才 build 和 activate。
- 不要 activate fake demonstration instance；activation 只针对真实 runtime 工程链路。
- Runtime plan 应使用 module TMC 中真实存在的名字。当前 complex 示例使用 `Parameter.data1`、`CyclicCaller`、`DataIn` 和 `DataOut`。
- 对 activation 和 ADS readback 使用 `enabled` flags，让机器状态变化显式可见。
- 需要一次读取多个 runtime 变量时，优先用 `validation.ads-read-symbols`，它会在终端 `Result` 中打印 `symbol=value` 列表，并在 summary 的 `valuesJson` 中保留结构化结果。
- `complex-full-project.json` 的 runtime readback 默认读取 `MAIN.nSeed`、`MAIN.nStage1`、`MAIN.bPipelineOk` 和 `MAIN.nMismatchCount`；这些值能同时验证 ADS、PLC task、C++ process-image mapping 和 PLC 逻辑。
- 对 signing settings 也使用 `enabled` flag；共享 plan 不要写 inline password，优先 `password-file` 或 `password-env-var`。
- 第一次 XAE build 可能很慢，`timeout-ms` 要有意识地设置。

## 示例

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json --dry-run=true

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\json-plans\complex-full-project.json --summary=.artifacts\complex-full-project-summary.json
```

`examples\json-plans\complex-full-project.json` 默认写入 `.artifacts`。真实 TwinCAT activation run 建议复制一份 plan，并把 `root` 改成新的短路径，例如 `D:\t\my_run`。

更激进的广覆盖运行时示例放在 `examples\full-coverage-runtime\full-coverage-runtime.json`。它默认开启 signing、显式 binary sign/verify、`DataPointerValues`、`IoTaskImage`、generic XML mutation、activation、ADS readback，并带有单独 ADS monitor；用于尽快暴露接口真实可用性问题，不是保守最小 demo。
