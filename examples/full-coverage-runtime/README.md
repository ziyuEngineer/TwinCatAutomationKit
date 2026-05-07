# Full Coverage Runtime Example / 全面运行时验证示例

English summary: This example intentionally exercises a broad TwinCAT JSON plan surface, including signing, C++/PLC mappings, exploratory data pointer values, activation, ADS readback, and an external ADS monitor.

这个目录是激进覆盖版示例，不是保守最小链路。目标是尽量多地触发 JSON plan 能力，快速暴露哪些 TwinCAT mutation 接口已经能真实 build/activate/readback，哪些还需要补 evidence 或新 API。

## Plan

运行 dry-run：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\full-coverage-runtime\full-coverage-runtime.json --dry-run=true --summary=.artifacts\full-coverage-runtime-dryrun-summary.json
```

真实执行：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- run-plan --file=examples\full-coverage-runtime\full-coverage-runtime.json --summary=.artifacts\full-coverage-runtime-summary.json
```

默认开启：

- `signing.set-license`，证书名 `optcnc`，密码 `123`。
- build 后显式 `signing.sign-twincat-binary` 和 `signing.verify-twincat-binary`；本地 `optcnc` 测试证书会启用 `allow-test-mode-warning`，接受 TcSignTool 的 test-mode certificate warning。
- `tsproj.apply-instance-data-pointer-plan` 和 `tsproj.ensure-data-pointer`。
- `tsproj.ensure-io-task-image`、multi-task、PLC input/output vars、four mapping links。
- `tsproj.upsert-*`、`tsproj.apply-mutation-plan`、`tsproj.merge-fragment`。
- `engineering.activate-configuration`、`validation.ads-scan`、`validation.ads-read-symbols`、`validation.ads-read`。

`signing.grant-certificate` 默认仍为 `false`，因为它修改本机 TcSignTool 授权状态。需要覆盖时把 `includeCertificateGrant` 改成 `true`。

## Runtime Graph

PLC 每 100 个 cycle 改变两个输出：

- `MAIN.nSeed` -> primary C++ `Input^DataIn` -> primary C++ `Output^DataOut` -> `MAIN.nStage1`
- `MAIN.nStage2Seed` -> aux C++ `Input^DataIn` -> aux C++ `Output^DataOut` -> `MAIN.nStage2`

primary C++ 参数 `Parameter.data1=123`，aux C++ 参数 `Parameter.data1=17`。PLC 允许一拍滞后并统计 change count，所以 ADS 端应能看到 `MAIN.nCycle`、`MAIN.nStage1ChangeCount`、`MAIN.nStage2ChangeCount` 持续变化，`MAIN.bPipelineOk` 在链路稳定后为 `True`。

plan 最后自动读的 `valuesText` 里，关键关系是：

- `MAIN.nStage1 = MAIN.nSeed + 123`
- `MAIN.nStage2 = MAIN.nStage2Seed + 17`
- `MAIN.nStage1ChangeCount` 和 `MAIN.nStage2ChangeCount` 增加，表示 PLC 看到 C++ 输出在变化。
- `MAIN.bHeartbeat` 在相邻读数之间切换，表示 runtime 正在跑。
- `MAIN.bPipelineOk=True` 且 `MAIN.nMismatchCount=0` 是最终 runtime check。

例如 `MAIN.nSeed=5; MAIN.nStage1=128; MAIN.nStage2Seed=11; MAIN.nStage2=28` 表示两条映射链都正常，因为 `5 + 123 = 128`，`11 + 17 = 28`。

## ADS Monitor

activation 后可以单独开一个终端跑外部 ADS 监控程序：

```powershell
dotnet run --project .\examples\full-coverage-runtime\ads-monitor\FullCoverageAdsMonitor.csproj -- --net-id=local --port=851 --interval-ms=500 --count=120
```

如果当前机器的 `dotnet restore/build` 被本地 `obj` 权限或 NuGet source 问题挡住，可以直接用同目录 PowerShell monitor，它循环调用仓库已有 `validation.ads-read-symbols`：

```powershell
.\examples\full-coverage-runtime\monitor-ads.ps1 -IntervalMs 500 -Count 120
```

默认输出会直接解释两条 runtime 链路：

```text
[timestamp] sample 1 cycle=... heartbeat=... pipelineOk=True mismatch=0
  primary: MAIN.nSeed=5 + 123 => MAIN.nStage1=128 [OK]; changes=2
  aux:     MAIN.nStage2Seed=11 + 17 => MAIN.nStage2=28 [OK]; changes=2
```

需要看 `invoke-step` 的完整原始输出时加 `-VerboseStepOutput`。

也可以显式指定 symbol：

```powershell
dotnet run --project .\examples\full-coverage-runtime\ads-monitor\FullCoverageAdsMonitor.csproj -- "--symbols=MAIN.nCycle:UInt32;MAIN.nSeed:UInt32;MAIN.nStage1:UInt32;MAIN.nStage1ChangeCount:UInt32;MAIN.nStage2:UInt32;MAIN.bPipelineOk:Boolean;MAIN.nMismatchCount:UInt32"
```

如果 `bPipelineOk=False` 或 change count 不增加，先看 plan summary 中 data pointer、mapping、task binding 和 activation 相关 step 的输出，再看 `${root}\_json_plan_evidence` 下的 exported XML。
