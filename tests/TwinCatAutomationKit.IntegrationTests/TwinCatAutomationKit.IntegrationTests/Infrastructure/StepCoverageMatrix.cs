using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.IntegrationTests;

internal sealed record StepCoverageSpec(
    string Kind,
    string Scenario,
    string Dependencies,
    string PassCriteria,
    bool RequiresRuntime = false);

internal static class StepCoverageMatrix
{
    public static IReadOnlyList<StepCoverageSpec> All { get; } =
    [
        Spec("engineering.launch-visual-studio", "ordered-step-surface", "VS DTE registered", "DTE session launches and SaveAll on a bare session does not throw."),
        Spec("engineering.create-xae-solution", "ordered-step-surface", "launch-visual-studio", ".sln and .tsproj are created from the Beckhoff XAE template and later reopened by XAE."),
        Spec("engineering.create-cpp-project", "ordered-step-surface", "create-xae-solution", "Return tree path/display name/file path are deterministic; .vcxproj ProjectGuid and .tmc exist."),
        Spec("engineering.create-plc-project", "ordered-step-surface", "create-xae-solution", "PLC project/instance names are readable, MAIN.TcPOU is replaced, and build/ADS later prove the PLC payload loaded."),
        Spec("engineering.create-module", "ordered-step-surface", "create-cpp-project", "Aux module returns the requested name, writes header/source/TMC metadata, and full-project reopen/export accepts it."),
        Spec("engineering.add-module-instance", "ordered-step-surface", "create-cpp-project/create-module", "Primary and auxiliary instances return parseable ObjectIds, expected display names, and exact .tsproj identities."),
        Spec("engineering.ensure-task", "ordered-step-surface", "create-xae-solution", "Two tasks return parseable ObjectIds and reopen with exact priority/cycle/AMS port/affinity/layout."),
        Spec("engineering.export-tree-item-xml", "ordered-step-surface", "created C++ project/tasks", "TIXC and TIRT ProduceXml snapshots are parsed for TreeItem root, ItemName, PathName, subtype, and non-empty content."),
        Spec("engineering.save-all", "ordered-step-surface", "open solution", "Saved .tsproj survives close/reopen and later build."),
        Spec("engineering.close-visual-studio", "ordered-step-surface", "open solution", "Session closes before .tsproj file mutation; cleanup also closes the final session."),
        Spec("engineering.open-xae-solution", "ordered-step-surface", "closed solution after file mutation", "Mutated .tsproj reopens and tree export works."),
        Spec("engineering.build-solution", "ordered-step-surface", "PLC-only runtime clone", "BuildCurrentSolution returns Succeeded=true and LastBuildInfo=0 without loading unsigned C++ runtime."),
        Spec("engineering.activate-configuration", "activation-ads-runtime", "built scenario, EnableActivation=true", "ActivateConfiguration succeeds, records the activation command, restarts TwinCAT, and writes a non-empty archive with solution/config evidence.", RequiresRuntime: true),

        Spec("signing.set-license", "signing-metadata", "built C++ project", "A single TcSign PropertyGroup is written with EnableSigning=false, exact license name, no password, and no duplicate signing nodes."),
        Spec("signing.grant-certificate", "excluded-signing-certificate", "local TwinCAT OEM signing certificate/private key", "Excluded from default real-machine verification; signing.set-license and ResolveToolPath still verify the non-certificate signing surface."),
        Spec("signing.sign-twincat-binary", "excluded-signing-certificate", "local TwinCAT OEM signing certificate/private key and built .tmx", "Excluded from default real-machine verification because a real OEM signing credential is machine/vendor state."),
        Spec("signing.verify-twincat-binary", "excluded-signing-certificate", "signed built .tmx", "Excluded from default real-machine verification because sign is excluded without a real OEM signing credential."),

        Spec("tsproj.ensure-task", "ordered-step-surface", "created .tsproj", "RuntimeTask and AuxTask attributes are written; duplicate ensure is harmless."),
        Spec("tsproj.clear-task-layout", "ordered-step-surface", "AuxTask with deliberately stale Vars/Image", "Stale AuxTask Vars/Image are inserted first, removed by clear, and absent after deterministic layout rebuild."),
        Spec("tsproj.ensure-task-vars-group", "ordered-step-surface", "AuxTask", "Input and output Vars groups are rebuilt with exact VarGrpType/InsertType/count/type/offset/external address."),
        Spec("tsproj.ensure-task-image", "ordered-step-surface", "AuxTask", "Task Image Id/address type/image type/size/name attributes are exact and later reopen succeeds."),
        Spec("tsproj.ensure-cpp-instance", "ordered-step-surface", "C++ project", "FileMutationCpp01 instance skeleton is inserted with exact ObjectId, context priority/cycle, and TmcDesc containers."),
        Spec("tsproj.ensure-plc-instance", "ordered-step-surface", "PLC project", "PLC instance node exists before PLC metadata and vars mutations."),
        Spec("tsproj.bind-instance-context", "ordered-step-surface", "aux instance, aux task ObjectId", "Aux instance context ManualConfig and CyclicCaller point at AuxTask."),
        Spec("tsproj.bind-instance-task", "ordered-step-surface", "primary instance, runtime task ObjectId", "Primary instance context ManualConfig and CyclicCaller point at RuntimeTask."),
        Spec("tsproj.bind-plc-instance-task", "ordered-step-surface", "PLC instance, runtime task ObjectId", "PLC context ManualConfig points at RuntimeTask."),
        Spec("tsproj.set-task-affinity", "ordered-step-surface", "RuntimeTask/AuxTask", "Affinity and AdtTasks attributes are written for both tasks."),
        Spec("tsproj.set-plc-project-properties", "ordered-step-surface", "PLC project", "PLC project AmsPort, ReloadTmc, path, and archive attributes are normalized."),
        Spec("tsproj.set-plc-instance-metadata", "ordered-step-surface", "PLC instance", "PLC instance metadata attributes and CLSID/ClassFactory are written without replacing vars."),
        Spec("tsproj.clear-plc-instance-vars", "ordered-step-surface", "PLC instance with deliberately stale Vars", "Stale PLC Vars are inserted first, removed by clear, and absent after deterministic inputs/outputs are rebuilt."),
        Spec("tsproj.ensure-plc-instance-vars-group", "ordered-step-surface", "PLC instance", "PLC input/output Vars groups are present with exact VarGrpType/InsertType/AreaNo/type/offset/external address."),
        Spec("tsproj.clear-plc-init-symbols", "ordered-step-surface", "PLC instance with a stale InitSymbol", "A stale InitSymbol is inserted first, removed by clear, and absent after ObjectId-derived InitSymbols are recreated."),
        Spec("tsproj.clear-plc-task-pou-oids", "ordered-step-surface", "PLC instance with a stale priority-99 TaskPouOid", "A stale TaskPouOid is inserted first, removed by clear, and absent after the runtime TaskPouOid is recreated."),
        Spec("tsproj.ensure-task-pou-oid", "ordered-step-surface", "PLC instance, runtime task ObjectId", "TaskPouOid stores the runtime task ObjectId and priority."),
        Spec("tsproj.ensure-init-symbol", "ordered-step-surface", "PLC instance, runtime/aux task ObjectIds", "InitSymbol Data contains ObjectId-derived bytes for both task symbols."),
        Spec("tsproj.replace-data-types-section", "ordered-step-surface", "created .tsproj with a stale DataType section", "A stale DataType section is written first, then replaced; only deterministic test types and merge audit type remain."),
        Spec("tsproj.replace-system-settings-section", "ordered-step-surface", "created .tsproj with stale Settings marker", "A stale Settings marker is written first, then replaced; the marker is absent while Tasks remain available."),
        Spec("tsproj.replace-project-io-section", "ordered-step-surface", "created .tsproj with stale Io section", "A stale Io section is written first, then replaced by the JSON-owned non-runtime-critical section."),
        Spec("tsproj.ensure-io-task-image", "ordered-step-surface", "RuntimeTask, primary instance", "RuntimeTask Image and primary instance IoTaskImage pointer are created together with exact ObjectId."),
        Spec("tsproj.clear-instance-parameter-values", "ordered-step-surface", "primary/aux instances with stale parameters", "Stale parameter values are inserted first, removed by clear, and absent after plan application."),
        Spec("tsproj.apply-instance-parameter-plan", "ordered-step-surface", "primary/aux instances", "Batch parameter plan writes exact primary and aux Parameter.data1 values, which survive XAE reopen."),
        Spec("tsproj.ensure-parameter", "ordered-step-surface and atomic-wrapper", "primary instance", "Single parameter upsert is idempotent after batch plan and wrapper path writes its own exact probe value."),
        Spec("tsproj.apply-instance-interface-pointer-plan", "ordered-step-surface", "primary/aux instances, task ObjectIds", "Batch interface pointer plan writes CyclicCaller values."),
        Spec("tsproj.ensure-interface-pointer", "ordered-step-surface", "aux instance, task ObjectId", "Single interface pointer upsert is idempotent after batch plan."),
        Spec("tsproj.clear-instance-data-pointer-values", "ordered-step-surface and atomic-wrapper", "primary/aux instances with stale data pointers", "Stale DataPointerValues are inserted first, removed by clear, and absent after deterministic data pointer plan restore."),
        Spec("tsproj.apply-instance-data-pointer-plan", "ordered-step-surface and atomic-wrapper", "primary/aux instances", "Batch data pointer plan writes exact OTCID/AreaNo/ByteOffs/ByteSize values for DataIn/DataOut."),
        Spec("tsproj.ensure-data-pointer", "ordered-step-surface", "primary instance", "Single data pointer upsert is idempotent after batch plan and exact values survive XAE reopen."),
        Spec("tsproj.clear-mappings", "ordered-step-surface", "created .tsproj with stale mapping link", "Stale mapping links are inserted first, removed by clear, and absent after deterministic links are rebuilt."),
        Spec("tsproj.replace-mappings-section", "ordered-step-surface", "created .tsproj with stale Mappings section", "Root Mappings is replaced with a stale known-good section and then an empty section before deterministic links are rebuilt."),
        Spec("tsproj.clear-unrestored-var-links", "ordered-step-surface and atomic-wrapper", "created .tsproj with stale UnrestoredVarLinks", "Stale UnrestoredVarLinks blocks are inserted first and absent after clear before reopen/build or wrapper verification."),
        Spec("tsproj.ensure-mapping-link", "ordered-step-surface", "PLC vars and C++ process image names", "Four PLC/C++ mapping links are present and full-project reopen/export succeeds."),
        Spec("tsproj.upsert-element", "ordered-step-surface", "Project/System path", "Generic element upsert writes exact attributes and child values under the intended parent."),
        Spec("tsproj.upsert-fragment", "ordered-step-surface", "Project/System path", "Generic fragment upsert writes a matched named fragment with exact child values."),
        Spec("tsproj.apply-mutation-plan", "ordered-step-surface", "Project/System path", "Batch generic mutation plan writes exact element and fragment child values."),
        Spec("tsproj.merge-fragment", "ordered-step-surface", "unique DataTypes parent and required evidence fields", "Documented named fragment merge succeeds, leaves exactly the expected DataTypes set, and reopen/build accepts the file."),

        Spec("validation.ads-scan", "activation-ads-runtime", "EnableAdsRead=true, running ADS router/runtime", "The configured runtime ADS port must report a readable state, not merely any scanned port.", RequiresRuntime: true),
        Spec("validation.ads-read-symbols", "activation-ads-runtime", "EnableAdsRead=true, valid symbol list", "Batch ADS read must return the configured parameter, deterministic conversion result, task ObjectId probes, checksum, transform-ok flag, and mismatch count with exact expected values.", RequiresRuntime: true),
        Spec("validation.ads-read", "activation-ads-runtime", "EnableAdsRead=true, first configured symbol", "Single-symbol read succeeds for the same symbol surface used by the strict batch runtime proof.", RequiresRuntime: true),
    ];

    public static IReadOnlyList<string> MissingCatalogKinds()
    {
        HashSet<string> documented = All.Select(item => item.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return TwinCatStepCatalog.All
            .Select(item => item.Kind)
            .Where(kind => !documented.Contains(kind))
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> UnknownMatrixKinds()
    {
        HashSet<string> catalog = TwinCatStepCatalog.All.Select(item => item.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All
            .Select(item => item.Kind)
            .Where(kind => !catalog.Contains(kind))
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static StepCoverageSpec Spec(
        string kind,
        string scenario,
        string dependencies,
        string passCriteria,
        bool RequiresRuntime = false) =>
        new(kind, scenario, dependencies, passCriteria, RequiresRuntime);
}
