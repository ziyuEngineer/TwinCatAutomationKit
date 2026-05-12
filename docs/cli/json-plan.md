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
--dry-run
--stop-on-failure=true
--command-timeout-ms=300000
--reuse-engineering-session=true
--summary=.artifacts\complex-plan-summary.json
--var:root=D:\t\my_run
```

CLI 参数同时支持 `--key=value` 和 `--key value`；boolean flag 也可以直接写成 `--dry-run`。无人值守脚本建议统一使用显式值或 quoted 参数，尤其是 PowerShell 中包含 `;` 的参数值。

第一次永远先跑 `--dry-run=true`。Dry-run 会解析变量和 step options，但不会写 payload files、打开 Visual Studio 或修改 TwinCAT project。

`--var:name=value` 可以覆盖 plan 里的变量，不需要改 JSON 文件。例如目标目录不可写时，可以用 `--var:root=D:\t\optcnc-probe` 在短路径工作区跑同一个 plan。也可以用 `--var="root=D:\t\a;includeBuild=false"` 一次覆盖多个变量；PowerShell 中包含 `;` 时要加引号。

无人值守运行建议设置 `--command-timeout-ms`。这是 `run-plan` 对每个 step 的外层 wall-clock 保险：即使某个 Visual Studio/TwinCAT COM 调用因为确认弹窗或 UI prompt 没有返回，CLI 也会按时把该 step 判定为 timeout failure，而不是无限等待。超时后 CLI 会清理本次 step 超时窗口中新启动的 `devenv.exe` / `TcXaeShell.exe` host；运行前已经存在的 IDE 不会被清理。单个 step 可以在 `steps[].options.command-timeout-ms` 覆盖全局值。

engineering steps 默认还会启用 `enable-dialog-auto-dismiss=true`：监控 DTE COM activation、fallback `/Embedding` launch、startup delay 和 active session 期间本轮新启动的 `devenv.exe` / `TcXaeShell.exe`，尝试关闭常见 TwinCAT/VS modal 确认框，并记录 `autoDismissedDialogs` 输出。这个 watcher 不会附着用户已经打开的 IDE；遇到未知弹窗仍以 step timeout 作为最终保险。

如果 Visual Studio profile/registry hive 损坏导致 DTE 启动弹出“未知错误”或 ActivityLog 里出现 `Failed to initialize Registry Root Hive`，可以在 JSON defaults 或 CLI 中设置 `root-suffix`。该参数会传给显式 fallback launch 的 `/RootSuffix`，用于无人值守运行时隔离坏 profile；默认不启用。

如果同一台机器同时装了 Visual Studio 和 Beckhoff `TcXaeShell`，或多个 VS 主版本导致 fallback 选错 host，可以设置 `dte-host-path` 指向具体的 `devenv.exe` 或 `TcXaeShell.exe`。该参数只影响 COM activation 超时后的显式 fallback launch；成功路径仍由 `prog-id` 决定 DTE COM server。

如果 `Activator.CreateInstance` DTE COM activation 已知会先弹“未知错误”或卡住，可以同时设置 `prefer-dte-host-launch=true`。这样 runner 会先启动指定/解析出的 DTE host，再从 ROT attach；失败时仍输出 fallback host、arguments 和 ActivityLog，而不是先等待 COM activation probe。

复杂工程生成建议使用 `--reuse-engineering-session=true`。该模式仍逐个执行 public step，但在同一个 `run-plan` 进程里复用连续 engineering/cpp steps 的 Visual Studio/XAE DTE session，避免每个 step 都重新打开 solution 后遇到 stale DTE、COM 接口丢失或额外确认弹窗。复用段内不会在每个 step 后立刻 SaveAll；遇到 `tsproj.*`、`signing.*`、`validation.*`、`scope.*` 等文件级或非 DTE step 前，runner 会先 SaveAll 并关闭复用 session，防止 XAE 旧内存状态覆盖直接文件 mutation。如果某个 step 已失败并且 `--stop-on-failure=true`，runner 会 abandon 当前 DTE COM session，不再尝试 SaveAll/Close，避免失败清理阶段因为 VS prompt 或 COM hang 卡住。启用复用时，低风险复用 engineering/cpp step 会关闭外层 timeout worker；`engineering.launch-visual-studio`、`engineering.open-xae-solution`、`engineering.export-tree-item-xml`、`engineering.build-solution` 和 `engineering.activate-configuration` 仍保留 `--command-timeout-ms`，超时后 abandon 复用 session 并清理本次 timeout 窗口中新启动的 IDE host。无人值守调用方仍应同时设置 `launch-timeout-ms`、build/activation 自身 timeout，并用外层 child-process/runner timeout 兜底。

`--stop-on-failure=true` 后，后续普通 step 会被直接标记为 skipped；runner 不会再解析这些 skipped step 的 `options`、`enabled` 或未明确为 true 的 `runAfterFailure` 中的 `${steps.id.outputs.x}`。这能保留第一个真实失败，例如 DTE launch/activation failure，而不是被后续未执行 step 的 unresolved-token 错误覆盖。依赖未执行 step 输出的 `files[]` payload 也会延后不写，并在 summary 前输出 unresolved payload 计数。

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
- `steps[].runAfterFailure`：可选 boolean。只有 read-only 诊断 step 应使用；当 `--stop-on-failure=true` 且前面某步失败后，普通 step 会 skipped，标了 `runAfterFailure=true` 的诊断 step 仍会继续执行，用于收集 event log、crash window 或只读状态证据。失败后这个字段必须能解析成 literal `true` 才会继续执行；如果它依赖未执行 step 的 output，runner 会把该 step skipped。

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
