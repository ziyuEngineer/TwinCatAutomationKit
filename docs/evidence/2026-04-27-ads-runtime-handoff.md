# 2026-04-27 ADS Runtime Handoff / ADS 运行时交接

English summary: This evidence group records a real-machine investigation around activation and ADS readback after JSON-plan project generation.

这组 evidence 记录一次真实机器调查：JSON plan 生成 TwinCAT project 后，activation 和 ADS readback 如何判断、失败时先看什么。

## Useful Conclusions / 有用结论

- Activation 必须在 real TcCOM instance 绑定到 task 后执行，并且 runtime pointers 必须匹配 module TMC 中的名字。
- 当前 complex JSON 示例中 activation-safe 的名字是 `Parameter.data1`、`CyclicCaller`、`DataIn` 和 `DataOut`。
- `TraceLevelMax` 和 demonstration-only `OfflineCpp01` mutations 不能作为 real generated module 的 activation-safe evidence。
- Hidden DTE command activation 可能阻塞；应优先尝试 `ITcSysManager.ActivateConfiguration()`。
- ADS readback 失败时，先确认 activation state、AMS user permissions、TwinCAT runtime state 和 exact symbol path，再改代码。
- 历史 raw log 里提到的 `AdsProbe` 是一次性 ADS 排查 helper，不是当前仓库依赖；正式验证入口应使用 `validation.ads-read` 或 JSON plan summary/evidence。

## Raw Logs

调查原始日志保留在 clean repository 的 `docs/evidence/raw/`：

- `ads_activate_and_read.log`
- `ads_admin_repair.log`
- `ads_after_tcsysui_start.log`
- `ads_force_run_and_read.log`
- `ads_probe_elevated.log`
- `dte_activate_menu_and_restart.log`
- `dte_restart_twincat_system.log`
- `dte_twincat_commands.log`
- `tcsystemserver_start_and_read.log`
- `ADS_RUNTIME_HANDOFF.full.md`

这些日志是 machine-specific diagnostics，不是可复用 setup instructions。

Raw log inventory / 原始材料索引：

| Raw log | 保留原因 |
|---|---|
| `ads_activate_and_read.log` | 记录 activation 后 ADS readback 的主要失败信号和命令输出。 |
| `ads_admin_repair.log` | 记录 AMS authorization repair 尝试，提醒后续不要把本机授权命令写成通用脚本。 |
| `ads_after_tcsysui_start.log` | 记录启动 TwinCAT UI 后的 ADS readback 状态。 |
| `ads_force_run_and_read.log` | 记录强制 runtime run 后 readback 仍失败的路径。 |
| `ads_probe_elevated.log` | 记录 elevated ADS probe 的错误码和环境差异。 |
| `dte_activate_menu_and_restart.log` | 记录 DTE menu activation/restart 路径信息很少，不应作为首选 activation proof。 |
| `dte_restart_twincat_system.log` | 记录通过 DTE/TwinCAT system restart 后的状态。 |
| `dte_twincat_commands.log` | 记录可见 TwinCAT DTE commands，供排查 command path 时复核。 |
| `tcsystemserver_start_and_read.log` | 记录启动 TcSystemServer 后 ADS readback 仍需进一步区分的问题。 |
| `ADS_RUNTIME_HANDOFF.full.md` | 历史交接全文，只作背景附录；当前结论以本文为准。 |

## Follow-Up Rules / 后续规则

- ADS authorization repair commands 不要放进 reusable scripts，除非参数化并明确标记为 machine-state changes。
- 未来 runtime evidence 用新的 dated Markdown 文件记录，并从 `docs/evidence/index.md` 链接 raw logs。
- 不要只用 raw log name 当 evidence。每个真实机器结果都要写清楚“证明了什么”。
- 如果 activation 成功但 ADS readback 失败，不要马上改 `.tsproj` mutation；先区分 AMS 权限、runtime state、symbol path 和 target port。
