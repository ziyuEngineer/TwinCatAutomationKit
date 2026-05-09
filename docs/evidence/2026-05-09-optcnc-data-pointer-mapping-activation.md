# 2026-05-09 OptCNC Data Pointer Mapping Activation / OptCNC 数据指针映射激活

English summary: OptCNC finally stayed in TwinCAT RUN only after `DataPointerValues` were backed by explicit mapping links; writing data pointers alone was not durable across XAE activation.

本文记录 `D:\3rd_year\auto_sln\OptcncTwinCAT.sln` 从“无可见报错但激活后回蓝色 Config”到真正保持绿色 RUN 的最终原因和修复。结论是：`AxesGroup0` 的 `DataPointerValues` 不能只写值，还必须存在对应的 `<Mappings>` link；否则 XAE activate/save 会删除这些 data pointer，随后 `AxesGroup0` 进 OP 失败，表现为 `ADS 1792` 或回到 Config。

## Problem

前置修复已经消除了早期的 task layout `Init12\IO ... SAFEOP ... ADS 1792`，也消除了后续 `ServoLoop` 的 `FPU invalid operation`。但再次激活时仍出现：

```text
Init16\IO: Set State TComObj SAFEOP OP: Set Object AxesGroup0 to OP
AdsError: 1792 (0x700, ADS ERROR: General ADS Error)
```

当时 `.tsproj` 中 `AxesGroup0`、`Axis0`、`Axis1`、`Axis2`、`CommandsExecuter` 的 instance 参数和 interface pointer 已经接近目标样例，但 `AxesGroup0` 的 data pointer 在激活后消失。

## Probe

用于证明 data pointer 被 activate 阶段删除的 probe：

```text
response: .artifacts\interactive-runner\responses\optcnc-datapointer-build-vs-activate-probe-20260509.response.json
stdout:   .artifacts\interactive-runner\logs\optcnc-datapointer-build-vs-activate-probe-20260509.stdout.log
summary:  .artifacts\optcnc-datapointer-build-vs-activate-probe-summary.json
summary:  .artifacts\optcnc-probe-activate-only-summary.json
```

关键计数：

```text
beforeBuild:    6
afterBuild:     6
afterReapply:   6
afterActivate:  0
```

结论：`engineering.build-solution` 不会删除 `DataPointerValues`；`engineering.activate-configuration` 触发的 XAE save/activate 会删除没有 mapping link 支撑的 data pointer。只看 `engineering.activate-configuration: Succeeded` 是不够的，因为该 COM 调用成功不等于 runtime 最终保持 RUN。

## Fix

没有复制目标样例 `.sln`、`.tsproj`、`.vcxproj`、`.tmc`，没有使用 `tsproj.merge-fragment`，也没有 bulk XML 插入。本轮修复使用 public steps 和 JSON：

```text
tsproj.apply-instance-parameter-plan
tsproj.apply-instance-interface-pointer-plan
tsproj.apply-instance-data-pointer-plan
tsproj.clear-mappings
tsproj.ensure-mapping-link
signing.set-license
engineering.build-solution
signing.sign-twincat-binary
signing.verify-twincat-binary
engineering.activate-configuration
validation.ads-scan
```

新增的 mapping plan：

```text
t\optcnc-motion-data-pointer-mappings.json
```

该 plan 先 `tsproj.clear-mappings`，再建立 8 条 link：

```text
TIXC^MotionControl^AxesGroup0 Data Pointer^AxisProcessData[0] <-> TIXC^MotionControl^Axis0 Data^ProcessData
TIXC^MotionControl^AxesGroup0 Data Pointer^AxisStateData[0]   <-> TIXC^MotionControl^Axis0 Data^StateData
TIXC^MotionControl^AxesGroup0 Data Pointer^AxisProcessData[1] <-> TIXC^MotionControl^Axis1 Data^ProcessData
TIXC^MotionControl^AxesGroup0 Data Pointer^AxisStateData[1]   <-> TIXC^MotionControl^Axis1 Data^StateData
TIXC^MotionControl^AxesGroup0 Data Pointer^AxisProcessData[2] <-> TIXC^MotionControl^Axis2 Data^ProcessData
TIXC^MotionControl^AxesGroup0 Data Pointer^AxisStateData[2]   <-> TIXC^MotionControl^Axis2 Data^StateData
TIXC^MotionControl^AxesGroup0 Data^ProcessData                <-> TIXC^MotionControl^CommandsExecuter Data Pointer^AxesGroupProcessData[0]
TIXC^MotionControl^AxesGroup0 Data^StateData                  <-> TIXC^MotionControl^CommandsExecuter Data Pointer^AxesGroupStateData[0]
```

修复 runner：

```text
response: .artifacts\interactive-runner\responses\optcnc-datapointer-mappings-activate-20260509.response.json
stdout:   .artifacts\interactive-runner\logs\optcnc-datapointer-mappings-activate-20260509.stdout.log
summary:  .artifacts\optcnc-datapointer-mappings-sign-build-activate-summary.json
```

`interactive-runner\requests` 目录在本轮没有保留 request 文件；可复查材料以 response、stdout 和 summary 为准。

最终复跑 runner：

```text
request:  .artifacts\interactive-runner\requests\optcnc-datapointer-mappings-reactivate-20260509-r2.json
response: .artifacts\interactive-runner\responses\optcnc-datapointer-mappings-reactivate-20260509-r2.response.json
stdout:   .artifacts\interactive-runner\logs\optcnc-datapointer-mappings-reactivate-20260509-r2.stdout.log
summary:  .artifacts\optcnc-datapointer-mappings-sign-build-activate-summary.json
```

## Result

激活前后 `AxesGroup0` data pointer 和 mapping link 都保留：

```text
Before activation: AxesGroup0 data pointer count=6, mapping link count=8
After activation:  AxesGroup0 data pointer count=6, mapping link count=8
```

同一激活窗口的 `TcSysSrv` 事件：

```text
2026-05-09T16:47:33.879... Activate configuration performed
2026-05-09T16:47:34.368... TwinCAT System Start: AdsState: 5
2026-05-09T16:47:34.610... TwinCAT system start completed. AdsState: >5<
```

`validation.ads-scan` 在激活后读取到：

```text
port 200:   Run
port 300:   Run
port 10000: Run
```

这次没有新的 `TcSysSrv` error，最终也没有回到 `AdsState: >15<` Config。结论：这才是完整 activation success，不只是 step 返回 success。

最终复核当前磁盘工程：

```text
D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj
AxesGroup0 DataPointerValues Data node count: 6
Mapping Link count: 8
LastWriteTime: 2026-05-09T16:47:34.1565073+08:00
```

## Rule For Future Agents

后续 agent 处理 TwinCAT C++ module data pointer 时，必须同时保证两层：

- `DataPointerValues` 写入了正确的 `Pointer`、`BitOffs`、`BitSize` 等值。
- `.tsproj` 根部 `<Mappings>` 中存在对应的 `tsproj.ensure-mapping-link` link。

如果只写 `DataPointerValues` 而不建 mapping link，XAE activate/save 可能会把 data pointer 删除。删除后 `AxesGroup0` 可能在 OP 阶段报 generic `ADS 1792`，而不是给出直接的 mapping 缺失错误。

验收规则：

- 不能只看 `engineering.activate-configuration` 成功。
- 必须检查同一激活窗口没有新的 `TcSysSrv` error。
- 必须检查最终 `validation.ads-scan` 中 port `10000` 是 `Run`。
- 激活后重新读取 `.tsproj`，`AxesGroup0` 的 6 个 data pointer 和 8 条 mapping link 必须仍在。
