# 2026-05-09 OptCNC SAFEOP 1792 Runtime Settings / OptCNC SAFEOP 1792 运行时设置修复

English summary: OptCNC activation `ADS 1792 (0x700)` was not fully fixed by clearing task process images. The durable fix also requires typed `System/Settings` CPU and I/O idle task settings, but absence of 1792 is not the same as full RUN activation.

本文记录 `D:\3rd_year\auto_sln\OptcncTwinCAT.sln` 激活 `ADS 1792` 的真实修复过程。之前把 `engineering.activate-configuration` 返回成功当成最终成功，这是错误判断；后续必须检查 Windows Application log 里的 `TcSysSrv` 事件。

## Problem

人工在 XAE 激活生成工程时看到：

```text
Init12\IO: Set State TComObj SAFEOP: Set Objects (3) to SAFEOP
AdsError: 1792 (0x700, ADS ERROR: General ADS Error)
```

本机 Beckhoff header 中 `1792 = 0x700 = ADSERR_DEVICE_ERROR`，只是 generic device error。真正有用的线索来自：

- `D:\3rd_year\auto_sln\OptcncTwinCAT\_Boot\TwinCAT OS (x64)\CurrentConfig.xml`
- Windows Application log provider `TcSysSrv`
- 目标样例 `D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj`

## Diagnosis

目标样例的关键形状：

```xml
<System>
  <Settings>
    <Cpu CpuId="1"/>
    <IoIdleTask Priority="6"/>
  </Settings>
  <Tasks>
    <Task Id="2" Priority="4" CycleTime="10000" AmsPort="351" Affinity="#x00000002" AdtTasks="true">
      <Name>ServoLoop</Name>
    </Task>
    <Task Id="4" Priority="8" CycleTime="100000" AmsPort="350" AdtTasks="true">
      <Name>PlcTask</Name>
    </Task>
  </Tasks>
</System>
```

生成工程最初有两个问题：

- `ServoLoop` / `PlcTask` 曾保留 generated task process image `Vars` / `Image`，目标样例没有。
- 后来 task layout 修掉后，仍然缺 `System/Settings/Cpu CpuId="1"` 和 `IoIdleTask Priority="6"`。

缺 `System/Settings` 时，激活生成的 `CurrentConfig.xml` header 显示：

```xml
<AffinityMask>#x1</AffinityMask>
<TComSrvAffinity>#x1</TComSrvAffinity>
<P2 Desc="NumRtCores">1</P2>
```

但 `ServoLoop` 是 `Affinity="#x00000002"`。`CurrentConfig.xml` 的失败命令 data 为：

```text
110000032000010230000102
```

按 4 字节 object id 分组是：

- `11000003`: I/O Idle Task
- `20000102`: `#x02010020` / `ServoLoop`
- `30000102`: `#x02010030` / 当前 `PlcTask`

这说明最后失败的是 task 对象批量进 SAFEOP，不是 MotionControl C++ object 单独创建失败。C++ objects 在本轮日志中已经能逐个进入 SAFEOP/OP。

## False Positive

早先 runner 输出过：

```text
engineering.activate-configuration: succeeded
CurrentConfig.xml has no task Create Image commands.
SAFEOP 1792 stale-XAE repair finished.
```

这是 false positive。Windows Application log 后来确认同一时间仍有：

```text
2026-05-09 15:05:51 TcSysSrv
Sending ams command >> Init12\IO: Set State TComObj SAFEOP: Set Objects (3) to SAFEOP >> AdsError: 1792 (0x700, ADS ERROR: General ADS Error) << failed!
```

人工在 2026-05-09 15:11:42 再次激活也出现同样 `TcSysSrv` 1792。结论：`engineering.activate-configuration` 的 COM 返回成功不能单独作为 runtime proof；必须检查同一激活窗口之后的 `TcSysSrv` 事件。

## Fix

没有复制样例 `.tsproj/.sln/.vcxproj/.tmc`，没有用 `merge-fragment`，也没有直接覆盖工程文件。修复使用 public step：

```text
tsproj.ensure-system-settings
tsproj.ensure-task
tsproj.clear-task-layout
tsproj.set-task-affinity
tsproj.bind-instance-task
signing.set-license
engineering.build-solution
signing.sign-twincat-binary
signing.verify-twincat-binary
engineering.activate-configuration
validation.ads-scan
```

新增 typed step：

```text
tsproj.ensure-system-settings
```

它只 upsert 已理解的 `Project/System/Settings/Cpu` 和 `IoIdleTask` 字段，不要求调用方提供 raw Settings XML，也不替换 `System/Tasks`。

当前修复 plan：

```text
t\optcnc-fix-task-safeop-shape.json
```

关键修复内容：

- `System/Settings/Cpu CpuId="1"`
- `System/Settings/IoIdleTask Priority="6"`
- `ServoLoop`: `Priority=4`, task `CycleTime=10000`, `AmsPort=351`, `Affinity=#x00000002`, `AdtTasks=true`
- `PlcTask`: `Priority=8`, task `CycleTime=100000`, `AmsPort=350`, `AdtTasks=true`
- 删除 `ServoLoop` 和 `PlcTask` 下的 generated task `Vars` / `Image`
- 8 个 MotionControl C++ instance 的 `Context/CycleTime` 统一为 `1000000`

主调用方 JSON 也同步加入：

```text
t\optcnc-auto-sln-from-kit.json
```

新增步骤：

```json
{
  "id": "ensureRuntimeCpuSettings",
  "kind": "tsproj.ensure-system-settings",
  "options": {
    "cpu-id": 1,
    "io-idle-task-priority": 6,
    "insert-before-tasks": true
  }
}
```

## Runner Evidence

真实修复运行：

```text
request:  .artifacts\interactive-runner\requests\optcnc-safeop1792-system-settings-repair-20260509.json
response: .artifacts\interactive-runner\responses\optcnc-safeop1792-system-settings-repair-20260509.response.json
stdout:   .artifacts\interactive-runner\logs\optcnc-safeop1792-system-settings-repair-20260509.stdout.log
summary:  .artifacts\optcnc-safeop1792-system-settings-fix-summary.json
summary:  .artifacts\optcnc-safeop1792-system-settings-sign-build-activate-summary.json
result:   fix plan 15/15 succeeded; sign/build/activate/ADS scan 6/6 succeeded
```

关键 stdout：

```text
after-fix-plan: system settings and task shape verified.
after-activation: system settings and task shape verified.
CurrentConfig.xml has no task Create Image commands.
No new TcSysSrv AdsError 1792 events after activation start.
SAFEOP 1792 system settings repair finished.
```

激活窗口：

```text
runner started:  2026-05-09T15:36:52.9533012+08:00
runner finished: 2026-05-09T15:37:41.1534528+08:00
```

Windows Application log after `2026-05-09 15:36:50`：

```text
2026-05-09 15:37:37 TcSysSrv Activate configuration performed
2026-05-09 15:37:37 TcSysSrv TwinCAT system start completed. AdsState: >5<
2026-05-09 15:37:38 TcSysSrv TwinCAT system start completed. AdsState: >15<
```

该窗口内没有新的 `AdsError: 1792` / `0x700` 事件。这只证明 `ADS 1792` 这一层已过，不证明工程已经保持绿色 RUN。

后续人工复查发现同一窗口和后续人工激活窗口仍有新的 blocker：

```text
2026-05-09 15:37:37 TcSysSrv Source: ServoLoop
Exception 'FPU invalid operation'
TwinCAT system start completed. AdsState: >5<
TwinCAT system start completed. AdsState: >15<
```

所以本条 evidence 的结论必须限定为：`System/Settings` 和 task layout 修复消除了 `Init12\IO ... ADS 1792`，但激活仍会因为 `ServoLoop` C++ runtime 异常回到蓝色 Config。完整激活问题继续见：

```text
docs/evidence/2026-05-09-optcnc-activation-fpu-invalid-operation.md
```

当前 `.tsproj` 抽样：

```xml
<System>
  <Settings>
    <Cpu CpuId="1"/>
    <IoIdleTask Priority="6"/>
  </Settings>
  <Tasks>
    <Task Id="2" Priority="4" CycleTime="10000" AmsPort="351" Affinity="#x00000002" AdtTasks="true">
      <Name>ServoLoop</Name>
    </Task>
    <Task Id="3" Priority="8" CycleTime="100000" AmsPort="350" AdtTasks="true">
      <Name>PlcTask</Name>
    </Task>
  </Tasks>
</System>
```

`PlcTask` 当前仍是 `Id="3"`，目标样例是 `Id="4"`。这不是本轮 1792 的必要修复项：15:37 激活已通过“无 1792”验证，但没有通过“保持 RUN”验证。如果后续必须追求 XML 完全一致，再补 typed task-id step，而不要直接改 XML 或复制样例 `.tsproj`。

## Rule For Future Agents

以后遇到 `Init12\IO ... Set Objects ... SAFEOP ... ADS 1792`，按这个顺序查：

1. Windows Application log provider `TcSysSrv`，不要只看 `engineering.activate-configuration` 返回值。
2. `System/Settings/Cpu` 和 `IoIdleTask` 是否匹配目标 runtime core/idle task 配置。
3. task 是否意外带回 `Vars` / `Image`。
4. `AdtTasks=true`、task `Affinity`、instance `Context/CycleTime` 是否匹配目标样例。
5. `.tsproj` LastWriteTime 是否晚于 runner 修复时间；如果是，优先怀疑打开着的 XAE/VS 保存了旧内存状态。
6. 1792 消失后还必须检查同一激活窗口内是否有新的 `TcSysSrv` error，以及最终是否不是 `AdsState: >15<` Config。

不要用复制目标 `.tsproj`、`.tmc`、`.vcxproj` 或 XML bulk fragment 的方式绕过；应该补 public typed step 或修 JSON plan。

## Related Later 1792

本文件里的 `Init12\IO ... SAFEOP ... Set Objects (3)` 是 task/system settings 层的问题。后续又出现过另一个 1792：

```text
Init16\IO: Set State TComObj SAFEOP OP: Set Object AxesGroup0 to OP
AdsError: 1792 (0x700, ADS ERROR: General ADS Error)
```

这不是同一个 root cause。后者的最终原因是 `AxesGroup0` 的 `DataPointerValues` 没有对应 `<Mappings>` link，activate 时被 XAE 删除，导致 `AxesGroup0` 进 OP 失败。完整修复和验收见：

```text
docs/evidence/2026-05-09-optcnc-data-pointer-mapping-activation.md
```
