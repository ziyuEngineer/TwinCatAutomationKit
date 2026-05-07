# Program.RealSamples Capability Map / 旧能力映射

本文件由文档生成器输出，用来把旧 `Program.RealSamples.cs` 的能力形状映射到现在的 TwinCatAutomationKit step 接口规范。

## Capability Buckets / 能力分组

| Legacy shape in `Program.RealSamples.cs` | TwinCatAutomationKit surface | Notes / 说明 |
|---|---|---|
| `LaunchNewVisualStudioInstance`, `CreateNewTwinCatXaeProject`, `OpenExistingTwinCatProject` | `engineering.launch-visual-studio`, `engineering.create-xae-solution`, `engineering.open-xae-solution` | DTE lifecycle and project attachment are explicit step interfaces. |
| `CreateTcCppProject`, `CreatePlcProject`, `CreateModuleWithFallbackForVersionedProject` | `engineering.create-cpp-project`, `engineering.create-plc-project`, `engineering.create-module` | Project/module generation stays in engineering service methods. |
| `AddCppProjectInstance` and instance blocks | `engineering.add-module-instance` | TcCOM instance creation is isolated from orchestration. |
| `CreateTask`, `GetOrCreateTask`, task patch blocks | `engineering.ensure-task`, `tsproj.ensure-task`, `tsproj.set-task-affinity` | Both COM and XAE-generated `.tsproj` file mutation paths are available. |
| `PatchTsprojInstanceBinding`, `PatchInstanceContext` | `tsproj.bind-instance-task`, `tsproj.bind-instance-context` | Instance context binding now has both compatibility and fine-grained context-id/name variants. |
| PLC context patching inside `PatchTsprojForS07/S11/S13` | `tsproj.bind-plc-instance-task`, `tsproj.ensure-task-pou-oid`, `tsproj.ensure-init-symbol` | PLC task binding, TaskPouOid, and InitSymbol are first-class primitives. |
| `PatchTsprojForS02` IO image logic | `tsproj.clear-task-layout`, `tsproj.ensure-task-vars-group`, `tsproj.ensure-task-image`, `tsproj.ensure-io-task-image` | S02 can be assembled either with explicit micro-steps or with the compatibility macro-step. |
| PLC project/instance attribute patching in `PatchTsprojForS07/S11/S13` | `tsproj.set-plc-project-properties`, `tsproj.set-plc-instance-metadata` | PLC metadata edits are now explicit primitives instead of embedded sample logic. |
| PLC instance Vars/InitSymbols/TaskPouOids reset blocks in `PatchTsprojForS07/S11/S13` | `tsproj.clear-plc-instance-vars`, `tsproj.ensure-plc-instance-vars-group`, `tsproj.clear-plc-init-symbols`, `tsproj.clear-plc-task-pou-oids` | PLC-specific reset and rebuild actions are now isolated, composable primitives. |
| `ReplaceS12Mappings`, `ReplaceS13Mappings`, `ReplaceS20aMappings` | `tsproj.clear-mappings`, `tsproj.ensure-mapping-link`, `tsproj.replace-mappings-section` | Supports both link-by-link reconstruction and full Mappings section replacement. |
| `ReplaceS22IoSection` | `tsproj.replace-project-io-section` | Root Project/Io replacement is now a dedicated primitive. |
| `ReplaceS23DataTypesSection`, `ReplaceS24DataTypesSection` | `tsproj.replace-data-types-section` | Root DataTypes replacement no longer requires ad-hoc section editing. |
| `ReplaceS24SystemSettings` | `tsproj.replace-system-settings-section` | System/Settings replacement is explicit and reusable. |
| `SetOrCreateParameterValue`, interface/data pointer patch blocks | `tsproj.ensure-parameter`, `tsproj.ensure-interface-pointer`, `tsproj.ensure-data-pointer` | Parameter/pointer operations remain explicit and testable. |
| Multi-instance fan-out blocks in `PatchTsprojForS24` | `tsproj.apply-instance-parameter-plan`, `tsproj.apply-instance-interface-pointer-plan`, `tsproj.apply-instance-data-pointer-plan` | Batch plan steps apply repeated instance mutations in one pass while preserving explicit per-item step specs. |
| Remaining XML gaps that still lack a dedicated primitive | `tsproj.merge-fragment` | Dangerous escape hatch only for documented known-good fragments with exact target path, field meaning, source evidence, and tests. Do not use it for `DataTypes`, `Io`, `System/Settings`, mappings, parameters, pointers, tasks, or instances when a dedicated step exists. |
| TwinCAT C++ binary signing before activation | `signing.grant-certificate`, `signing.set-license`, `signing.sign-twincat-binary`, `signing.verify-twincat-binary` | TcSignTool operations and build-time signing settings are explicit so signed module evidence is available before activation. |
| `BuildCurrentSolution`, `ActivateTcConfiguration`, ADS checks | `engineering.build-solution`, `engineering.activate-configuration`, `validation.ads-read`, `validation.ads-read-symbols` | Runtime close-the-loop remains part of the standard flow; batch symbol read is preferred for final JSON-plan runtime proof. |

## Practical Rule / 使用规则

默认应使用上表中最小、最稳定的专用 primitive。发现没有可用 API 时，优先记录缺口、需要的 XML 形状、evidence 和建议接口，再反馈给开发者补 API；不要临时复制未知 fragment。
