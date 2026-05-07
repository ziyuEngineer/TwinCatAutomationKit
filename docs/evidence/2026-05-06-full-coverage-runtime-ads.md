# 2026-05-06 Full Coverage Runtime ADS Proof / 全覆盖运行时 ADS 证明

English summary: This evidence records a successful real-machine run of `examples/full-coverage-runtime/full-coverage-runtime.json` through activation and ADS monitoring.

本文记录 `examples/full-coverage-runtime/full-coverage-runtime.json` 的真实机器运行结果。该 plan 是广覆盖验证版，目标不是最小 demo，而是尽量覆盖 signing、C++/PLC mapping、TMC/TmcDesc mutation、`DataPointerValues`、activation 和 ADS readback。

## 结论

- `full-coverage-runtime.json` 已经成功跑到 activation 后 ADS readback。
- PLC ADS port `851` 可读，`validation.ads-read-symbols` 连续多次返回 `Status: Succeeded`。
- 两条 process-image mapping 链路都在工作：
  - `MAIN.nSeed -> primary C++ Input^DataIn -> primary C++ Output^DataOut -> MAIN.nStage1`
  - `MAIN.nStage2Seed -> aux C++ Input^DataIn -> aux C++ Output^DataOut -> MAIN.nStage2`
- `MAIN.bPipelineOk=True` 且 `MAIN.nMismatchCount=0`，说明 PLC 侧检查逻辑认为两条链路都稳定通过。
- `MAIN.nCycle`、`MAIN.nSeed`、`MAIN.nStage1ChangeCount`、`MAIN.nStage2Seed`、`MAIN.nStage2ChangeCount` 连续增长，说明不是一次性静态值。
- 本 evidence 能证明 `DataPointerValues` 写入没有阻止 activation；但当前 runtime 数值链主要证明的是 process-image mapping，因为 fallback C++ module 的 `CycleUpdate` 使用 `Input^DataIn` / `Output^DataOut` 数据区。

## ADS 输出摘要

用户在 `2026-05-06 14:39:55 +08:00` 左右运行 `examples/full-coverage-runtime/monitor-ads.ps1`，连续 sample 均成功。

sample 3:

```text
MAIN.nCycle=1318
MAIN.nSeed=18
MAIN.nStage1=141
MAIN.nStage1ChangeCount=15
MAIN.nStage2Seed=37
MAIN.nStage2=54
MAIN.nStage2ChangeCount=14
MAIN.bHeartbeat=True
MAIN.bPipelineOk=True
MAIN.nMismatchCount=0
```

关键关系：

```text
MAIN.nStage1 = MAIN.nSeed + 123 = 18 + 123 = 141
MAIN.nStage2 = MAIN.nStage2Seed + 17 = 37 + 17 = 54
```

sample 4:

```text
MAIN.nCycle=1538
MAIN.nSeed=20
MAIN.nStage1=143
MAIN.nStage1ChangeCount=17
MAIN.nStage2Seed=41
MAIN.nStage2=58
MAIN.nStage2ChangeCount=16
MAIN.bHeartbeat=False
MAIN.bPipelineOk=True
MAIN.nMismatchCount=0
```

关键关系：

```text
MAIN.nStage1 = MAIN.nSeed + 123 = 20 + 123 = 143
MAIN.nStage2 = MAIN.nStage2Seed + 17 = 41 + 17 = 58
```

sample 5:

```text
MAIN.nCycle=1754
MAIN.nSeed=22
MAIN.nStage1=145
MAIN.nStage1ChangeCount=19
MAIN.nStage2Seed=45
MAIN.nStage2=62
MAIN.nStage2ChangeCount=18
MAIN.bHeartbeat=True
MAIN.bPipelineOk=True
MAIN.nMismatchCount=0
```

关键关系：

```text
MAIN.nStage1 = MAIN.nSeed + 123 = 22 + 123 = 145
MAIN.nStage2 = MAIN.nStage2Seed + 17 = 45 + 17 = 62
```

## 覆盖到的能力

这次 evidence 覆盖：

- JSON plan 能从 `examples/full-coverage-runtime/full-coverage-runtime.json` 执行到 runtime readback。
- `engineering.activate-configuration` 后 PLC ADS port `851` 可读。
- `validation.ads-read-symbols` 能批量读取 10 个 symbol，`succeededCount=10`、`failedCount=0`。
- PLC output vars、PLC input vars、C++ `Input^DataIn`、C++ `Output^DataOut` 和 root `<Mappings>` 的组合链路有效。
- 两个 C++ module instance 的参数差异生效：primary `Parameter.data1=123`，aux `Parameter.data1=17`。
- `monitor-ads.ps1` 可作为外部 ADS 监控入口，能持续观察变量变化。

## 未完全证明的点

- 这次 ADS 数值关系不能单独证明 C++ smart `DataPointerValues` 被 `CycleUpdate` 消费；当前 fallback C++ runtime 逻辑使用 process-image data areas。
- 如果后续要证明 `DataPointerValues` 的语义消费，需要新增或修改 C++ module 逻辑，让它实际通过 data pointer 读取另一个 provider 的数据，并用 ADS 输出验证该路径。

## 后续判断入口

后续如果 `full-coverage-runtime` 失败，优先检查：

1. plan summary 中 `applyExploratoryDataPointerPlan`、`ensurePrimaryDataPointer`、mapping、build、signing、activation 的 step 状态。
2. `${root}\_json_plan_evidence\cpp.after-offline.xml` 和 `tasks.after-offline.xml`。
3. ADS readback 中 `nStage1 = nSeed + 123`、`nStage2 = nStage2Seed + 17` 是否仍成立。
4. `bPipelineOk` 是否为 `True`，`nMismatchCount` 是否仍为 `0`。
