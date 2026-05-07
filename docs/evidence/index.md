# Evidence Index / 证据索引

English summary: Evidence files contain durable real-machine facts that future agents need before changing TwinCAT, XAE, activation, signing, or ADS behavior.

Evidence 是仓库里的真实机器事实记录。它不是聊天记录，也不是 dump everything 的日志目录。每条 evidence 应回答三个问题：证明了什么、在哪里能看原始材料、以后改相关代码时要注意什么。目标是让后续 agent 先读 summary 就能判断方向，只在必要时才打开 raw log。

## Entries

- [2026-05-07 Strict Integration Export Fallback](2026-05-07-strict-integration-export-fallback.md)
- [2026-05-06 JSON Activation ADS Session Check](2026-05-06-json-activation-ads-session-check.md)
- [2026-05-06 Full Coverage Runtime ADS Proof](2026-05-06-full-coverage-runtime-ads.md)
- [2026-05-06 Full Coverage Signing Path And Test-Mode Verify](2026-05-06-full-coverage-signing-path-and-test-mode.md)
- [2026-04-30 ADS Port 0x748 Runtime State](2026-04-30-ads-port-0748-runtime-state.md)
- [2026-04-27 ADS Runtime Handoff](2026-04-27-ads-runtime-handoff.md)

## Raw Logs

保留的 raw logs 放在 `docs/evidence/raw/`。raw log 不是 agent 的第一阅读入口；每个 raw log 都需要有一个 summary entry 解释它为什么值得保留。

raw log 可以保留诊断细节，但不能变成垃圾堆。新 evidence 的默认做法是先写 dated Markdown summary，再按需附少量 raw log；如果一个 raw log 的结论已经被 summary 覆盖、没有独立复查价值，就不要提交。机器专属路径、用户目录和授权命令不是可复用信息，必要时应在 raw log 中做最小化 redaction，并在 summary 里说明。

Retention rules / 留存规则：

- `docs/evidence/raw/` 里不允许 orphan log；每个文件必须被某个 dated evidence entry 引用。
- 同一轮调查的 raw logs 应由一个 summary entry 分组，不要在 index 里无限展开。
- 后续 agent 先读 summary；只有需要复核命令输出、错误码或 XML 片段时才打开 raw log。
- 新增 raw log 前先判断是否能用 summary JSON、XML snapshot、build log excerpt 或 ADS readback 替代。
- 过时或重复 raw log 应删除或压缩为 summary；保留原因必须能一句话说清。

Retained 2026-04-27 group:

- `raw/ads_activate_and_read.log`
- `raw/ads_admin_repair.log`
- `raw/ads_after_tcsysui_start.log`
- `raw/ads_force_run_and_read.log`
- `raw/ads_probe_elevated.log`
- `raw/dte_activate_menu_and_restart.log`
- `raw/dte_restart_twincat_system.log`
- `raw/dte_twincat_commands.log`
- `raw/tcsystemserver_start_and_read.log`
- `raw/ADS_RUNTIME_HANDOFF.full.md`

这些文件只服务于 [2026-04-27 ADS Runtime Handoff](2026-04-27-ads-runtime-handoff.md)。`raw/ADS_RUNTIME_HANDOFF.full.md` 是历史交接原文，不按当前中文优先风格重写；以本目录的 summary 为准。raw 内容里出现的旧本机 workspace 路径和临时 `AdsProbe` helper 不是当前仓库依赖，也不能作为调用示例；ADS 验证以 `validation.ads-read` 和新的 dated evidence 为准。

## Rules

- 不要把 `*.log` 放在 repository root。
- 不要提交 machine secrets、certificate passwords 或 user-specific authorization commands。
- 优先保留 XML snapshots、run summaries、activation artifacts 和 ADS readbacks。
- raw log 被保留时，必须在 Markdown evidence entry 中写明原因和结论。
- ADS/TwinCAT runtime 相关证据要写清楚机器状态：runtime 是否 running、activation 是否成功、AMS 权限是否处理过、symbol path 是什么。
- 每条 evidence entry 至少写清 command/plan、workspace、结果、证明结论、未覆盖风险和下一步排查入口。
- 每次真实问题解决后，把可复用经验写回调用文档、改库手册或 evidence summary；不要只留下 raw log。
