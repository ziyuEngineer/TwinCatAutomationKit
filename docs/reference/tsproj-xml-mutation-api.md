# Tsproj XML Mutation API / `.tsproj` XML 变更 API

本文件由支持的 `.tsproj` mutation surface 生成，供审核者和 agent 快速定位可用接口。

## Where The API Lives / API 位置

- Service: `src/TwinCatAutomationKit.TwinCat/TwinCatAutomationKit.TwinCat/TwinCatTsprojMutationService.cs`
- DTOs: `src/TwinCatAutomationKit.Abstractions/TwinCatAutomationKit.Abstractions/TwinCatRequests.cs`
- Tests: `tests/TwinCatAutomationKit.IntegrationTests/TwinCatAutomationKit.IntegrationTests/Tests/OrderedTwinCatScenarioTests.cs`

## Generic Methods / 通用方法

1. `UpsertElement(string tsprojPath, TsprojElementUpsertRequest request)`
2. `UpsertFragment(string tsprojPath, TsprojFragmentUpsertRequest request)`
3. `ApplyMutationPlan(string tsprojPath, ApplyTsprojMutationPlanRequest request)`

## Generic CLI Step Kinds / 通用 CLI step

- `tsproj.upsert-element`
- `tsproj.upsert-fragment`
- `tsproj.apply-mutation-plan`

## Usage Policy / 使用策略

- 绝大多数 `.tsproj` 修改都应走专用 API，例如 task、instance、binding、parameter、pointer、mapping 和 PLC metadata。
- 如果没有现成专用 API，先收集缺口信息：人类操作目标、变更前后 XML、应插入的父路径、字段含义、可验证 evidence。
- `tsproj.upsert-*` 只适合小范围、已理解的 XML gap；调用者必须知道每个字段为什么存在。
- `tsproj.merge-fragment` 是高风险 escape hatch。代码会要求 `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence`，并拒绝不唯一的 parent element。
- 有 dedicated primitive 时不要用 `tsproj.merge-fragment`；如果确实发现 API gap，把人类操作目标和 XML evidence 反馈给开发者补专用 API。

## Dedicated Stable Primitives / 专用稳定 primitive

- `EnsureTaskDefinition`
- `ClearTaskLayout`
- `EnsureTaskVarsGroup`
- `EnsureTaskImage`
- `BindInstanceContext`
- `SetTaskAffinity`
- `SetPlcProjectProperties`
- `SetPlcInstanceMetadata`
- `ClearPlcInstanceVars`
- `EnsurePlcInstanceVarsGroup`
- `ClearPlcInitSymbols`
- `ClearPlcTaskPouOids`
- `ClearMappings`
- `ReplaceMappingsSection`
- `ReplaceProjectIoSection`
- `ReplaceDataTypesSection`
- `ReplaceSystemSettingsSection`
- `ApplyInstanceParameterPlan`
- `ApplyInstanceInterfacePointerPlan`
- `ApplyInstanceDataPointerPlan`
- `BindInstanceToTask`
- `BindPlcInstanceToTask`
- `EnsureTaskPouOid`
- `EnsureInitSymbol`
- `EnsureIoTaskImage`
- `EnsureParameterValue`
- `EnsureInterfacePointerValue`
- `EnsureDataPointerValue`
- `EnsureMappingLink`

## Escape Hatch / 高风险逃生口

- `MergeNamedElementFragment`

## ObjectId Utilities / ObjectId 工具

- `DeriveNextObjectId(string currentObjectId, uint increment = 1)`
- `DeriveIoTaskImageObjectId(int imageId)`
- `ConvertObjectIdToInitSymbolData(string objectId)`

## Conflict Policy / 冲突策略

- `ReplaceExisting`: overwrite existing values/fragments.
- `KeepExisting`: keep existing values/fragments.
- `FailOnConflict`: throw on mismatched existing values/fragments.

## Review Rule / 审核规则

任何新的公开 XML mutation 能力，都必须进入真实 TwinCAT/VS integration test；如果 CLI/JSON caller 要使用它，还必须加入 `TwinCatStepCatalog`。真实 XAE/TwinCAT 行为最终要用 build、activation、ADS 或等价 evidence 验证。
