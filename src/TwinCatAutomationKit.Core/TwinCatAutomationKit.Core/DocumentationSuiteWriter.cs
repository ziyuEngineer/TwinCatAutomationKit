using System.Text;
using System.Text.Json;
using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.Core;

public static class DocumentationSuiteWriter
{
    public static void WriteReferenceSuite(
        string outputDirectory,
        IReadOnlyList<StepContract> catalog,
        IReadOnlyList<string> recommendedExecutionOrder)
    {
        Directory.CreateDirectory(outputDirectory);
        WriteCatalogMarkdown(Path.Combine(outputDirectory, "step-catalog.md"), catalog);
        WriteCatalogJson(Path.Combine(outputDirectory, "step-catalog.json"), catalog, recommendedExecutionOrder);
        WriteExecutionOrderMarkdown(Path.Combine(outputDirectory, "recommended-execution-order.md"), catalog, recommendedExecutionOrder);
        WriteProgramRealCapabilityMapMarkdown(Path.Combine(outputDirectory, "program-real-samples-capability-map.md"));
        WriteTsprojXmlMutationApiMarkdown(Path.Combine(outputDirectory, "tsproj-xml-mutation-api.md"));
        WriteInterfaceExtensionWorkflowMarkdown(Path.Combine(outputDirectory, "interface-extension-workflow.md"));
    }

    public static void WriteCatalogMarkdown(string outputPath, IReadOnlyList<StepContract> catalog)
    {
        IReadOnlyList<StepContract> orderedCatalog = OrderCatalog(catalog);

        StringBuilder markdown = new();
        markdown.AppendLine("# TwinCatAutomationKit Step Catalog / 步骤接口索引");
        markdown.AppendLine();
        markdown.AppendLine("English summary: This generated Markdown file is the human-readable companion to `step-catalog.json`; both files must be regenerated together from `TwinCatStepCatalog.cs`.");
        markdown.AppendLine();
        markdown.AppendLine("这个文件是自动生成的人类可读 step 接口索引，列出每个 CLI step 的名字、对应方法、输入、输出和验证要求。不要手改本文件；要改内容请修改 `TwinCatStepCatalog.cs` 或文档生成器后运行 `generate-docs`。");
        markdown.AppendLine();
        markdown.AppendLine("`step-catalog.json` 是同一份 step 接口规范的机器可读版本，供调用 agent 和工具读取；它不是一个完整 JSON plan 示例。完整 plan 写法看 `docs/cli/json-plan.md`，完整例子看 `examples/json-plans/*.json`。");
        markdown.AppendLine();
        markdown.AppendLine("English API text is intentionally preserved for stability. 中文标签只帮助阅读和审核。");
        markdown.AppendLine();
        markdown.AppendLine("## 阅读方式 / Reading Guide");
        markdown.AppendLine();
        markdown.AppendLine("- 人类审查优先看下面的分类简表，再进入具体 step 详情。");
        markdown.AppendLine("- Agent 或工具默认读取 `step-catalog.json`；如果需要解释性上下文，再读本 Markdown，但不要把两份文件当成两个独立来源。");
        markdown.AppendLine("- `step-catalog.json` 和本文件必须由同一次 `generate-docs` 生成；如果只改了其中一个，视为文档不同步。");
        markdown.AppendLine("- 需要改 step 接口时，先改 `TwinCatStepCatalog.cs`，必要时改 `DocumentationSuiteWriter.cs`，然后重新生成整个 `docs/reference`。");
        markdown.AppendLine();
        markdown.AppendLine("## 分类简表 / Category Overview");
        markdown.AppendLine();
        markdown.AppendLine("| Category | Step kind | Summary |");
        markdown.AppendLine("|---|---|---|");
        foreach (StepContract contract in orderedCatalog)
        {
            markdown.AppendLine($"| `{contract.Category}` | `{contract.Kind}` | {contract.Summary} |");
        }

        markdown.AppendLine();
        markdown.AppendLine("## 详细接口 / Step Details");
        markdown.AppendLine();

        foreach (StepContract contract in orderedCatalog)
        {
            markdown.AppendLine($"### `{contract.Kind}`");
            markdown.AppendLine();
            markdown.AppendLine($"- 方法 Method: `{contract.MethodName}`");
            markdown.AppendLine($"- 分类 Category: `{contract.Category}`");
            markdown.AppendLine($"- 功能摘要 Summary: {contract.Summary}");
            markdown.AppendLine("- 前置条件 Preconditions:");
            if (contract.Preconditions.Count == 0)
            {
                markdown.AppendLine("  - (none)");
            }

            foreach (string item in contract.Preconditions)
            {
                markdown.AppendLine($"  - {item}");
            }

            markdown.AppendLine("- 输入 Inputs:");
            if (contract.Inputs.Count == 0)
            {
                markdown.AppendLine("  - (none)");
            }

            foreach (StepParameterContract input in contract.Inputs)
            {
                string example = string.IsNullOrWhiteSpace(input.Example) ? string.Empty : $" Example: `{input.Example}`.";
                markdown.AppendLine($"  - `{input.Name}` (`{input.Type}`): {input.Description}{example}");
            }

            markdown.AppendLine("- 输出 Outputs:");
            if (contract.Outputs.Count == 0)
            {
                markdown.AppendLine("  - (none)");
            }

            foreach (StepOutputContract output in contract.Outputs)
            {
                markdown.AppendLine($"  - `{output.Name}` (`{output.Type}`): {output.Description}");
            }

            markdown.AppendLine("- 验证 Verification:");
            if (contract.VerificationNotes.Count == 0)
            {
                markdown.AppendLine("  - (none)");
            }

            foreach (string item in contract.VerificationNotes)
            {
                markdown.AppendLine($"  - {item}");
            }

            markdown.AppendLine();
        }

        File.WriteAllText(outputPath, markdown.ToString(), Encoding.UTF8);
    }

    public static void WriteCatalogJson(string outputPath, IReadOnlyList<StepContract> catalog)
    {
        WriteCatalogJson(outputPath, catalog, Array.Empty<string>());
    }

    public static void WriteCatalogJson(
        string outputPath,
        IReadOnlyList<StepContract> catalog,
        IReadOnlyList<string> recommendedExecutionOrder)
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        StepCatalogReferenceDocument document = new(
            DocumentKind: "TwinCatAutomationKit.StepCatalog",
            SchemaVersion: 2,
            SourceOfTruth: "src/TwinCatAutomationKit.TwinCat/TwinCatAutomationKit.TwinCat/TwinCatStepCatalog.cs",
            GeneratedBy: "dotnet run --project ./src/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli/TwinCatAutomationKit.Cli.csproj -- generate-docs",
            CompanionMarkdown: "docs/reference/step-catalog.md",
            RelatedDocs:
            [
                "docs/cli/json-plan.md",
                "docs/reference/recommended-execution-order.md",
                "examples/json-plans/*.json"
            ],
            ReadThisFirst:
            [
                "This file is the machine-readable step interface catalog, not a run-plan JSON file.",
                "Use Steps as the only step contract list in this file; do not infer extra steps from prose or examples.",
                "Use RecommendedExecutionOrder as the default ordering guide when assembling a new plan.",
                "Use docs/cli/json-plan.md for the surrounding run-plan file shape and variable syntax.",
                "Use examples/json-plans/*.json for complete plan examples."
            ],
            AgentUsageRules:
            [
                "Select a step by exact Kind.",
                "Supply every input with Required=true unless the plan intentionally obtains it from prior step outputs or defaults documented elsewhere.",
                "Treat Preconditions as operational requirements that must be satisfied before the step runs.",
                "Treat VerificationNotes as evidence requirements for judging success.",
                "Do not use tsproj.merge-fragment or generic tsproj.upsert-* when a dedicated tsproj primitive exists.",
                "If needed behavior is missing from Steps, report the missing human operation, XML before/after shape, target path, field meanings, verification method, and suggested API name instead of inventing a hidden step."
            ],
            SyncPolicy:
            [
                "docs/reference/step-catalog.json and docs/reference/step-catalog.md are generated companions from the same catalog source.",
                "Do not hand-edit either generated file.",
                "Any change to step specs or generated reference wording must regenerate both files in the same change.",
                "If the JSON and Markdown disagree, treat the generated pair as stale and rerun generate-docs; do not merge the disagreement manually.",
                "For public step contract changes, update TwinCatStepCatalog.cs, implementation/CLI/tests as needed, then run generate-docs."
            ],
            StepFieldMeanings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Kind"] = "Stable public step identifier used in invoke-step and JSON plan steps.",
                ["MethodName"] = "Service method that implements the step behavior.",
                ["Category"] = "High-level step family such as engineering, tsproj, signing, or validation.",
                ["Summary"] = "Short behavior description.",
                ["Preconditions"] = "Required state or environment before running the step.",
                ["Inputs"] = "Request fields accepted by the step payload or direct CLI options.",
                ["Inputs.Required"] = "True means the caller must provide a value unless a wrapper supplies it explicitly.",
                ["Inputs.Example"] = "Scalar example for that field; it is not a full step payload.",
                ["Outputs"] = "Named values exposed by the step outcome and available to later JSON plan steps.",
                ["VerificationNotes"] = "Evidence or readback expected after running the step."
            },
            PlanAuthoringRules:
            [
                "A run-plan file has variables/files/steps structure from docs/cli/json-plan.md; this catalog only describes valid step kinds and payload fields.",
                "Each plan step should reference one catalog Kind and pass a payload matching Inputs.",
                "Prefer small explicit steps over macro commands when durable evidence or precise repair is needed.",
                "Keep deviations from RecommendedExecutionOrder intentional and explain them in plan comments or evidence."
            ],
            RecommendedExecutionOrder: recommendedExecutionOrder.ToArray(),
            Steps: OrderCatalog(catalog));

        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, options), Encoding.UTF8);
    }

    public static void WriteExecutionOrderMarkdown(
        string outputPath,
        IReadOnlyList<StepContract> catalog,
        IReadOnlyList<string> recommendedExecutionOrder)
    {
        Dictionary<string, StepContract> byKind = catalog.ToDictionary(item => item.Kind, StringComparer.OrdinalIgnoreCase);
        StringBuilder markdown = new();
        markdown.AppendLine("# Recommended Execution Order / 推荐执行顺序");
        markdown.AppendLine();
        markdown.AppendLine("下面是新 TwinCAT 自动化运行的默认控制顺序。项目可以跳过某些步骤，但应在自己的 plan 或 evidence 中说明原因。");
        markdown.AppendLine();

        for (int index = 0; index < recommendedExecutionOrder.Count; index++)
        {
            string kind = recommendedExecutionOrder[index];
            if (!byKind.TryGetValue(kind, out StepContract? contract))
            {
                continue;
            }

            markdown.AppendLine($"{index + 1}. `{contract.Kind}`");
            markdown.AppendLine($"   `{contract.MethodName}`");
            markdown.AppendLine($"   {contract.Summary}");
        }

        File.WriteAllText(outputPath, markdown.ToString(), Encoding.UTF8);
    }

    public static void WriteProgramRealCapabilityMapMarkdown(string outputPath)
    {
        StringBuilder markdown = new();
        markdown.AppendLine("# Program.RealSamples Capability Map / 旧能力映射");
        markdown.AppendLine();
        markdown.AppendLine("本文件由文档生成器输出，用来把旧 `Program.RealSamples.cs` 的能力形状映射到现在的 TwinCatAutomationKit step 接口规范。");
        markdown.AppendLine();
        markdown.AppendLine("## Capability Buckets / 能力分组");
        markdown.AppendLine();
        markdown.AppendLine("| Legacy shape in `Program.RealSamples.cs` | TwinCatAutomationKit surface | Notes / 说明 |");
        markdown.AppendLine("|---|---|---|");
        markdown.AppendLine("| `LaunchNewVisualStudioInstance`, `CreateNewTwinCatXaeProject`, `OpenExistingTwinCatProject` | `engineering.launch-visual-studio`, `engineering.create-xae-solution`, `engineering.open-xae-solution` | DTE lifecycle and project attachment are explicit step interfaces. |");
        markdown.AppendLine("| `CreateTcCppProject`, `CreatePlcProject`, `CreateModuleWithFallbackForVersionedProject` | `engineering.create-cpp-project`, `engineering.create-plc-project`, `engineering.create-module` | Project/module generation stays in engineering service methods. |");
        markdown.AppendLine("| `AddCppProjectInstance` and instance blocks | `engineering.add-module-instance` | TcCOM instance creation is isolated from orchestration. |");
        markdown.AppendLine("| `CreateTask`, `GetOrCreateTask`, task patch blocks | `engineering.ensure-task`, `tsproj.ensure-task`, `tsproj.set-task-affinity` | Both COM and XAE-generated `.tsproj` file mutation paths are available. |");
        markdown.AppendLine("| `PatchTsprojInstanceBinding`, `PatchInstanceContext` | `tsproj.bind-instance-task`, `tsproj.bind-instance-context` | Instance context binding now has both compatibility and fine-grained context-id/name variants. |");
        markdown.AppendLine("| PLC context patching inside `PatchTsprojForS07/S11/S13` | `tsproj.bind-plc-instance-task`, `tsproj.ensure-task-pou-oid`, `tsproj.ensure-init-symbol` | PLC task binding, TaskPouOid, and InitSymbol are first-class primitives. |");
        markdown.AppendLine("| `PatchTsprojForS02` IO image logic | `tsproj.clear-task-layout`, `tsproj.ensure-task-vars-group`, `tsproj.ensure-task-image`, `tsproj.ensure-io-task-image` | S02 can be assembled either with explicit micro-steps or with the compatibility macro-step. |");
        markdown.AppendLine("| PLC project/instance attribute patching in `PatchTsprojForS07/S11/S13` | `tsproj.set-plc-project-properties`, `tsproj.set-plc-instance-metadata` | PLC metadata edits are now explicit primitives instead of embedded sample logic. |");
        markdown.AppendLine("| PLC instance Vars/InitSymbols/TaskPouOids reset blocks in `PatchTsprojForS07/S11/S13` | `tsproj.clear-plc-instance-vars`, `tsproj.ensure-plc-instance-vars-group`, `tsproj.clear-plc-init-symbols`, `tsproj.clear-plc-task-pou-oids` | PLC-specific reset and rebuild actions are now isolated, composable primitives. |");
        markdown.AppendLine("| `ReplaceS12Mappings`, `ReplaceS13Mappings`, `ReplaceS20aMappings` | `tsproj.clear-mappings`, `tsproj.ensure-mapping-link`, `tsproj.replace-mappings-section` | Supports both link-by-link reconstruction and full Mappings section replacement. |");
        markdown.AppendLine("| `ReplaceS22IoSection` | `tsproj.replace-project-io-section` | Root Project/Io replacement is now a dedicated primitive. |");
        markdown.AppendLine("| `ReplaceS23DataTypesSection`, `ReplaceS24DataTypesSection` | `tsproj.replace-data-types-section` | Root DataTypes replacement no longer requires ad-hoc section editing. |");
        markdown.AppendLine("| `ReplaceS24SystemSettings` | `tsproj.replace-system-settings-section` | System/Settings replacement is explicit and reusable. |");
        markdown.AppendLine("| `SetOrCreateParameterValue`, interface/data pointer patch blocks | `tsproj.ensure-parameter`, `tsproj.ensure-interface-pointer`, `tsproj.ensure-data-pointer` | Parameter/pointer operations remain explicit and testable. |");
        markdown.AppendLine("| Multi-instance fan-out blocks in `PatchTsprojForS24` | `tsproj.apply-instance-parameter-plan`, `tsproj.apply-instance-interface-pointer-plan`, `tsproj.apply-instance-data-pointer-plan` | Batch plan steps apply repeated instance mutations in one pass while preserving explicit per-item step specs. |");
        markdown.AppendLine("| Remaining XML gaps that still lack a dedicated primitive | `tsproj.merge-fragment` | Dangerous escape hatch only for documented known-good fragments with exact target path, field meaning, source evidence, and tests. Do not use it for `DataTypes`, `Io`, `System/Settings`, mappings, parameters, pointers, tasks, or instances when a dedicated step exists. |");
        markdown.AppendLine("| TwinCAT C++ binary signing before activation | `signing.grant-certificate`, `signing.set-license`, `signing.sign-twincat-binary`, `signing.verify-twincat-binary` | TcSignTool operations and build-time signing settings are explicit so signed module evidence is available before activation. |");
        markdown.AppendLine("| `BuildCurrentSolution`, `ActivateTcConfiguration`, ADS checks | `engineering.build-solution`, `engineering.activate-configuration`, `validation.ads-read`, `validation.ads-read-symbols` | Runtime close-the-loop remains part of the standard flow; batch symbol read is preferred for final JSON-plan runtime proof. |");
        markdown.AppendLine();
        markdown.AppendLine("## Practical Rule / 使用规则");
        markdown.AppendLine();
        markdown.AppendLine("默认应使用上表中最小、最稳定的专用 primitive。发现没有可用 API 时，优先记录缺口、需要的 XML 形状、evidence 和建议接口，再反馈给开发者补 API；不要临时复制未知 fragment。");
        File.WriteAllText(outputPath, markdown.ToString(), Encoding.UTF8);
    }

    public static void WriteTsprojXmlMutationApiMarkdown(string outputPath)
    {
        StringBuilder markdown = new();
        markdown.AppendLine("# Tsproj XML Mutation API / `.tsproj` XML 变更 API");
        markdown.AppendLine();
        markdown.AppendLine("本文件由支持的 `.tsproj` mutation surface 生成，供审核者和 agent 快速定位可用接口。");
        markdown.AppendLine();
        markdown.AppendLine("## Where The API Lives / API 位置");
        markdown.AppendLine();
        markdown.AppendLine("- Service: `src/TwinCatAutomationKit.TwinCat/TwinCatAutomationKit.TwinCat/TwinCatTsprojMutationService.cs`");
        markdown.AppendLine("- DTOs: `src/TwinCatAutomationKit.Abstractions/TwinCatAutomationKit.Abstractions/TwinCatRequests.cs`");
        markdown.AppendLine("- Tests: `tests/TwinCatAutomationKit.IntegrationTests/TwinCatAutomationKit.IntegrationTests/Tests/OrderedTwinCatScenarioTests.cs`");
        markdown.AppendLine();
        markdown.AppendLine("## Generic Methods / 通用方法");
        markdown.AppendLine();
        markdown.AppendLine("1. `UpsertElement(string tsprojPath, TsprojElementUpsertRequest request)`");
        markdown.AppendLine("2. `UpsertFragment(string tsprojPath, TsprojFragmentUpsertRequest request)`");
        markdown.AppendLine("3. `ApplyMutationPlan(string tsprojPath, ApplyTsprojMutationPlanRequest request)`");
        markdown.AppendLine();
        markdown.AppendLine("## Generic CLI Step Kinds / 通用 CLI step");
        markdown.AppendLine();
        markdown.AppendLine("- `tsproj.upsert-element`");
        markdown.AppendLine("- `tsproj.upsert-fragment`");
        markdown.AppendLine("- `tsproj.apply-mutation-plan`");
        markdown.AppendLine();
        markdown.AppendLine("## Usage Policy / 使用策略");
        markdown.AppendLine();
        markdown.AppendLine("- 绝大多数 `.tsproj` 修改都应走专用 API，例如 task、instance、binding、parameter、pointer、mapping 和 PLC metadata。");
        markdown.AppendLine("- 如果没有现成专用 API，先收集缺口信息：人类操作目标、变更前后 XML、应插入的父路径、字段含义、可验证 evidence。");
        markdown.AppendLine("- `tsproj.upsert-*` 只适合小范围、已理解的 XML gap；调用者必须知道每个字段为什么存在。");
        markdown.AppendLine("- `tsproj.merge-fragment` 是高风险 escape hatch。代码会要求 `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence`，并拒绝不唯一的 parent element。");
        markdown.AppendLine("- 有 dedicated primitive 时不要用 `tsproj.merge-fragment`；如果确实发现 API gap，把人类操作目标和 XML evidence 反馈给开发者补专用 API。");
        markdown.AppendLine();
        markdown.AppendLine("## Dedicated Stable Primitives / 专用稳定 primitive");
        markdown.AppendLine();
        markdown.AppendLine("- `EnsureTaskDefinition`");
        markdown.AppendLine("- `ClearTaskLayout`");
        markdown.AppendLine("- `EnsureTaskVarsGroup`");
        markdown.AppendLine("- `EnsureTaskImage`");
        markdown.AppendLine("- `BindInstanceContext`");
        markdown.AppendLine("- `SetTaskAffinity`");
        markdown.AppendLine("- `SetPlcProjectProperties`");
        markdown.AppendLine("- `SetPlcInstanceMetadata`");
        markdown.AppendLine("- `ClearPlcInstanceVars`");
        markdown.AppendLine("- `EnsurePlcInstanceVarsGroup`");
        markdown.AppendLine("- `ClearPlcInitSymbols`");
        markdown.AppendLine("- `ClearPlcTaskPouOids`");
        markdown.AppendLine("- `ClearMappings`");
        markdown.AppendLine("- `ReplaceMappingsSection`");
        markdown.AppendLine("- `ReplaceProjectIoSection`");
        markdown.AppendLine("- `ReplaceDataTypesSection`");
        markdown.AppendLine("- `ReplaceSystemSettingsSection`");
        markdown.AppendLine("- `ApplyInstanceParameterPlan`");
        markdown.AppendLine("- `ApplyInstanceInterfacePointerPlan`");
        markdown.AppendLine("- `ApplyInstanceDataPointerPlan`");
        markdown.AppendLine("- `BindInstanceToTask`");
        markdown.AppendLine("- `BindPlcInstanceToTask`");
        markdown.AppendLine("- `EnsureTaskPouOid`");
        markdown.AppendLine("- `EnsureInitSymbol`");
        markdown.AppendLine("- `EnsureIoTaskImage`");
        markdown.AppendLine("- `EnsureParameterValue`");
        markdown.AppendLine("- `EnsureInterfacePointerValue`");
        markdown.AppendLine("- `EnsureDataPointerValue`");
        markdown.AppendLine("- `EnsureMappingLink`");
        markdown.AppendLine();
        markdown.AppendLine("## Escape Hatch / 高风险逃生口");
        markdown.AppendLine();
        markdown.AppendLine("- `MergeNamedElementFragment`");
        markdown.AppendLine();
        markdown.AppendLine("## ObjectId Utilities / ObjectId 工具");
        markdown.AppendLine();
        markdown.AppendLine("- `DeriveNextObjectId(string currentObjectId, uint increment = 1)`");
        markdown.AppendLine("- `DeriveIoTaskImageObjectId(int imageId)`");
        markdown.AppendLine("- `ConvertObjectIdToInitSymbolData(string objectId)`");
        markdown.AppendLine();
        markdown.AppendLine("## Conflict Policy / 冲突策略");
        markdown.AppendLine();
        markdown.AppendLine("- `ReplaceExisting`: overwrite existing values/fragments.");
        markdown.AppendLine("- `KeepExisting`: keep existing values/fragments.");
        markdown.AppendLine("- `FailOnConflict`: throw on mismatched existing values/fragments.");
        markdown.AppendLine();
        markdown.AppendLine("## Review Rule / 审核规则");
        markdown.AppendLine();
        markdown.AppendLine("任何新的公开 XML mutation 能力，都必须进入真实 TwinCAT/VS integration test；如果 CLI/JSON caller 要使用它，还必须加入 `TwinCatStepCatalog`。真实 XAE/TwinCAT 行为最终要用 build、activation、ADS 或等价 evidence 验证。");
        File.WriteAllText(outputPath, markdown.ToString(), Encoding.UTF8);
    }

    public static void WriteInterfaceExtensionWorkflowMarkdown(string outputPath)
    {
        StringBuilder markdown = new();
        markdown.AppendLine("# Interface Extension Workflow / 接口扩展流程");
        markdown.AppendLine();
        markdown.AppendLine("本文件定义新增公开 TwinCatAutomationKit step 接口时的流程。");
        markdown.AppendLine();
        markdown.AppendLine("## Extension Chain / 扩展链路");
        markdown.AppendLine();
        markdown.AppendLine("1. 在 `TwinCatAutomationKit.Abstractions` 定义或调整 request/response DTO。");
        markdown.AppendLine("2. 在 `TwinCatAutomationKit.TwinCat` service method 中实现确定性行为。");
        markdown.AppendLine("3. 在 `TwinCatStepCatalog` 注册或调整 step 规范，写清 preconditions 和 verification notes。");
        markdown.AppendLine("4. 如果 pipeline/JSON 运行需要使用该能力，添加 `TwinCatAtomicSteps` wrapper。");
        markdown.AppendLine("5. 用 CLI `generate-docs` 重新生成文档。");
        markdown.AppendLine("6. 在 `tests/TwinCatAutomationKit.IntegrationTests` 新增或扩展真实 TwinCAT/VS integration tests。");
        markdown.AppendLine("7. 用 `dotnet run --project .\\tests\\TwinCatAutomationKit.IntegrationTests\\TwinCatAutomationKit.IntegrationTests\\TwinCatAutomationKit.IntegrationTests.csproj` 验证。");
        markdown.AppendLine();
        markdown.AppendLine("## Quality Gate / 质量门槛");
        markdown.AppendLine();
        markdown.AppendLine("- Step kind 必须使用 `<category>.<verb>-<target>` 命名。");
        markdown.AppendLine("- 行为应尽量幂等；如果不能幂等，必须显式说明。");
        markdown.AppendLine("- Verification guidance 必须描述可观察 evidence，例如 XML readback、ADS read 或 summary artifact。");
        markdown.AppendLine("- 合并前 generated docs 必须反映新 step 规范。");
        markdown.AppendLine();
        markdown.AppendLine("## Anti-Patterns / 反模式");
        markdown.AppendLine();
        markdown.AppendLine("- 把 ad-hoc XML edit 分散在多个文件，而不是收敛到 `TwinCatTsprojMutationService`。");
        markdown.AppendLine("- 有 public capability，但没有 `TwinCatStepCatalog` step 规范。");
        markdown.AppendLine("- 改了 step 规范但没有 regenerated docs，也没有真实 TwinCAT integration coverage。");
        File.WriteAllText(outputPath, markdown.ToString(), Encoding.UTF8);
    }

    private static IReadOnlyList<StepContract> OrderCatalog(IReadOnlyList<StepContract> catalog) =>
        catalog
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private sealed record StepCatalogReferenceDocument(
        string DocumentKind,
        int SchemaVersion,
        string SourceOfTruth,
        string GeneratedBy,
        string CompanionMarkdown,
        IReadOnlyList<string> RelatedDocs,
        IReadOnlyList<string> ReadThisFirst,
        IReadOnlyList<string> AgentUsageRules,
        IReadOnlyList<string> SyncPolicy,
        IReadOnlyDictionary<string, string> StepFieldMeanings,
        IReadOnlyList<string> PlanAuthoringRules,
        IReadOnlyList<string> RecommendedExecutionOrder,
        IReadOnlyList<StepContract> Steps);
}
