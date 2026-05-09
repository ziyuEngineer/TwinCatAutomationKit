# 2026-05-09 OptCNC Activation FPU Invalid Operation / OptCNC 激活 FPU 异常

English summary: After the OptCNC generated solution stopped producing ADS 1792, activation still returned to Config because the `ServoLoop` task raised a C++ `FPU invalid operation` during runtime start.

本文记录 `D:\3rd_year\auto_sln\OptcncTwinCAT.sln` 当前激活不变绿色 RUN 的真实 blocker。`ADS 1792` 已不再出现，但这不是激活成功。

## Current Symptom

人工在 XAE 点 Activate Configuration，确认后界面短暂刷新，最后回到蓝色 Config。VS 错误列表没有弹出新的 1792。

Windows Application log provider `TcSysSrv` 给出的真实错误是：

```text
2026-05-09 15:37:37 Source: ServoLoop
Exception 'FPU invalid operation'
Stack:
0x1ee80bb8077
0x1803fc0f3
0x1803feb74
0x18058037c
```

后续人工激活在 `2026-05-09 15:44:13`、`2026-05-09 15:44:27`、`2026-05-09 15:48:05`、`2026-05-09 15:48:08` 重复同样错误。每次都先到：

```text
TwinCAT system start completed. AdsState: >5<
```

随后停止并回到：

```text
TwinCAT system start completed. AdsState: >15<
```

结论：当前不是 `Init12\IO` 的 SAFEOP 1792，而是 `ServoLoop` 周期任务中加载的 `MotionControl` C++ 运行时代码或其输入配置触发 FPU 异常。

## Important Distinction

`engineering.activate-configuration` 返回 success、`validation.ads-scan` 能读取端口、以及事件窗口内没有 `AdsError: 1792`，都不能单独证明激活成功。

完整激活验收至少需要：

- 激活窗口之后没有新的 `TcSysSrv` error。
- 最终状态没有回到 `AdsState: >15<` Config。
- 如果读取 ADS runtime state，不能只接受 port 存在；需要确认目标 runtime/session 处在可运行状态。

## Investigation Entry Points

优先排查：

- 生成工程 `MotionControl.tmx` / `MotionControl.pdb` 是否能把 `0x1803fc0f3`、`0x1803feb74`、`0x18058037c` 映射到函数。
- 目标样例和生成工程的 `MotionControl.vcxproj` 编译设置差异。
- 目标样例和生成工程的 `.tsproj` 中 `MotionControl` instance `ParameterValues`、`DataPointerValues`、`InterfacePointerValues`、task binding 和 context 差异。
- C++ 初始化代码是否在未收到有效 PLC/IO 输入时执行除零、非法 `sqrt`、非法 `acos`、未初始化浮点数或 `std::numeric_limits<double>::signaling_NaN()` 相关路径。

## Rule For Future Agents

不要把“没有 1792”写成“激活成功”。后续修复必须用同一激活窗口的 `TcSysSrv` event log 和运行态证明 `ServoLoop` 不再抛 `FPU invalid operation`，否则仍然是未完成。

## Later Resolution

本文件记录的是中间 blocker，不是最终状态。后续用 `tsproj.apply-instance-parameter-plan` 补齐 MotionControl instance 参数后，`ServoLoop` 的 `FPU invalid operation` 消失；随后又暴露出 `AxesGroup0` data pointer 在 activate 时被 XAE 删除的问题。

最终完整 RUN 修复见：

```text
docs/evidence/2026-05-09-optcnc-data-pointer-mapping-activation.md
```

最终结论是：FPU 问题由参数差异触发；最后一次 `ADS 1792` 和“激活刷一下又回蓝色”由缺少 data pointer mapping links 触发。完整验收必须看同一激活窗口的 `TcSysSrv`、`validation.ads-scan` port `10000 = Run`，以及激活后 `.tsproj` 中 `DataPointerValues` 和 `<Mappings>` 是否仍然保留。
