using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.IntegrationTests;

/// <summary>
/// Generates per-step TwinCAT probe projects for manual inspection.
/// Unlike integration tests, probe runs keep artifacts on disk.
/// </summary>
internal static class StepProbeRunner
{
    private sealed record ProbeOptions(
        string OutputRoot,
        bool AllowActivate,
        string? AdsSymbol,
        AdsReadDataType AdsType);

    private sealed record ProbeRunResult(
        string Kind,
        string Status,
        string Summary,
        string RunDirectory,
        string? WorkDirectory,
        string? SolutionPath,
        string? ProjectPath,
        IReadOnlyDictionary<string, string?> Outputs,
        IReadOnlyList<string> Artifacts);

    private sealed class ProbeContext
    {
        public ProbeContext(string kind, string runDirectory, IntegrationTestConfig config)
        {
            Kind = kind;
            RunDirectory = runDirectory;
            Config = config;
            WorkDirectory = Path.Combine(runDirectory, "w");
            Directory.CreateDirectory(WorkDirectory);
        }

        public string Kind { get; }
        public string RunDirectory { get; }
        public string WorkDirectory { get; }
        public IntegrationTestConfig Config { get; }
        public TwinCatEngineeringService Engineering { get; } = new();
        public TwinCatTsprojMutationService Mutation { get; } = new();
        public AdsValidationService Ads { get; } = new();
        public TwinCatEngineeringSession? Session { get; set; }
        public TwinCatProjectInfo? ProjectInfo { get; set; }
        public string CppProjectName { get; set; } = "CppA";
        public string TaskName { get; set; } = "Task1";
        public string? TaskObjectId { get; set; }
        public string? InstanceName { get; set; }
        public string? InstanceObjectId { get; set; }
        public bool PendingFallbackCppInstance { get; set; }
        public string? PlcProjectName { get; set; }
        public string? PlcInstanceName { get; set; }
    }

    public static int Run(string[] args)
    {
        string command = args[0].ToLowerInvariant();
        Dictionary<string, string> options = ParseOptions(args.Skip(1));

        return command switch
        {
            "probe-list" => ProbeList(),
            "probe-run" => ProbeRunSingle(options),
            "probe-run-all" => ProbeRunAll(options),
            _ => 2
        };
    }

    private static int ProbeList()
    {
        foreach (StepContract contract in TwinCatStepCatalog.All.OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(contract.Kind);
        }

        return 0;
    }

    private static int ProbeRunSingle(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("kind", out string? kind) || string.IsNullOrWhiteSpace(kind))
        {
            Console.Error.WriteLine("probe-run requires --kind=<step-kind>.");
            return 2;
        }

        IntegrationTestConfig config = IntegrationTestConfig.Load();
        ProbeOptions probeOptions = BuildProbeOptions(config, options);
        string outputRoot = EnsureOutputRoot(probeOptions.OutputRoot);
        ProbeRunResult result = ExecuteProbe(kind, outputRoot, config, probeOptions, index: 1);
        WriteProbeResultManifest(result);
        PrintProbeResult(result);
        return string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static int ProbeRunAll(Dictionary<string, string> options)
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        ProbeOptions probeOptions = BuildProbeOptions(config, options);
        string outputRoot = EnsureOutputRoot(probeOptions.OutputRoot);

        List<ProbeRunResult> results = new();
        IReadOnlyList<StepContract> all = TwinCatStepCatalog.All.OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase).ToList();
        for (int index = 0; index < all.Count; index++)
        {
            ProbeRunResult result = ExecuteProbe(all[index].Kind, outputRoot, config, probeOptions, index + 1);
            results.Add(result);
            WriteProbeResultManifest(result);
            PrintProbeResult(result);
        }

        string summaryPath = Path.Combine(outputRoot, "summary.json");
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SUMMARY {summaryPath}");

        return results.Any(item => string.Equals(item.Status, "Failed", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
    }

    private static ProbeRunResult ExecuteProbe(
        string kind,
        string outputRoot,
        IntegrationTestConfig config,
        ProbeOptions options,
        int index)
    {
        string runId = BuildRunId(kind, index);
        string runDirectory = Path.Combine(outputRoot, runId);
        Directory.CreateDirectory(runDirectory);

        ProbeContext ctx = new(kind, runDirectory, config);
        List<string> artifacts = [];
        Dictionary<string, string?> outputs = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (kind.StartsWith("engineering.", StringComparison.OrdinalIgnoreCase) ||
                kind.StartsWith("validation.", StringComparison.OrdinalIgnoreCase))
            {
                return RunEngineeringProbe(kind, ctx, options, outputs, artifacts);
            }

            return RunTsprojProbe(kind, ctx, outputs, artifacts);
        }
        catch (Exception ex)
        {
            string errorPath = Path.Combine(ctx.RunDirectory, "error.txt");
            File.WriteAllText(errorPath, ex.ToString());
            artifacts.Add(errorPath);
            return Failed(ctx, ex.Message, outputs, artifacts);
        }
        finally
        {
            CloseSession(ctx);
        }
    }

    private static ProbeRunResult RunEngineeringProbe(
        string kind,
        ProbeContext ctx,
        ProbeOptions options,
        Dictionary<string, string?> outputs,
        List<string> artifacts)
    {
        switch (kind)
        {
            case "engineering.launch-visual-studio":
                EnsureSession(ctx);
                File.WriteAllText(Path.Combine(ctx.RunDirectory, "launch-ok.txt"), "Visual Studio launched.");
                artifacts.Add(Path.Combine(ctx.RunDirectory, "launch-ok.txt"));
                return Succeeded(ctx, "Visual Studio launch probe succeeded.", outputs, artifacts);

            case "engineering.create-xae-solution":
                EnsureSolution(ctx);
                outputs["solutionPath"] = ctx.ProjectInfo!.SolutionPath;
                outputs["projectPath"] = ctx.ProjectInfo.ProjectPath;
                return Succeeded(ctx, "XAE solution creation probe succeeded.", outputs, artifacts);

            case "engineering.open-xae-solution":
                EnsureSolution(ctx);
                CloseSession(ctx);
                EnsureSession(ctx);
                ctx.Engineering.OpenTwinCatSolution(ctx.Session!,
                    new OpenTwinCatSolutionRequest(ctx.ProjectInfo!.SolutionPath, ctx.ProjectInfo.ProjectPath));
                return Succeeded(ctx, "Open solution probe succeeded.", outputs, artifacts);

            case "engineering.create-cpp-project":
                EnsureCppProject(ctx);
                return Succeeded(ctx, "Create C++ project probe succeeded.", outputs, artifacts);

            case "engineering.create-plc-project":
                EnsurePlcProject(ctx);
                // Reopen and confirm the PLC tree node is queryable by TwinCAT runtime APIs.
                CloseSession(ctx);
                EnsureSession(ctx);
                ctx.Engineering.OpenTwinCatSolution(ctx.Session!,
                    new OpenTwinCatSolutionRequest(ctx.ProjectInfo!.SolutionPath, ctx.ProjectInfo.ProjectPath));
                string plcXml = Path.Combine(ctx.RunDirectory, "plc.xml");
                ctx.Engineering.ExportTreeItemXml(ctx.Session!,
                    new ExportTreeItemXmlRequest($"TIPC^{ctx.PlcProjectName}", plcXml, Recursive: true));
                artifacts.Add(plcXml);
                outputs["plcProjectName"] = ctx.PlcProjectName;
                outputs["plcInstanceName"] = ctx.PlcInstanceName;
                return Succeeded(ctx, "Create PLC project probe succeeded.", outputs, artifacts);

            case "engineering.create-module":
                EnsureCppProject(ctx);
                const string moduleName = "ProbeModule";
                ctx.Engineering.CreateModule(ctx.Session!,
                    new CreateModuleRequest(ctx.CppProjectName, moduleName, "TcModuleClassWizard"));
                ctx.Engineering.SaveAll(ctx.Session!);
                string moduleTmcPath = Path.Combine(ctx.ProjectInfo!.SolutionDirectory, ctx.CppProjectName, ctx.CppProjectName + ".tmc");
                if (!TmcContainsNamedModule(moduleTmcPath, moduleName))
                {
                    throw new InvalidOperationException($"Module '{moduleName}' was not found in '{moduleTmcPath}'.");
                }
                string moduleTmcSnapshot = Path.Combine(ctx.RunDirectory, "module.tmc.xml");
                File.Copy(moduleTmcPath, moduleTmcSnapshot, true);
                artifacts.Add(moduleTmcSnapshot);
                return Succeeded(ctx, "Create module probe executed.", outputs, artifacts);

            case "engineering.add-module-instance":
                EnsureCppInstanceStrict(ctx);
                outputs["instanceName"] = ctx.InstanceName;
                outputs["instanceObjectId"] = ctx.InstanceObjectId;
                return Succeeded(ctx, "Add module instance probe succeeded.", outputs, artifacts);

            case "engineering.ensure-task":
                EnsureTask(ctx);
                outputs["taskName"] = ctx.TaskName;
                outputs["taskObjectId"] = ctx.TaskObjectId;
                return Succeeded(ctx, "Ensure task probe succeeded.", outputs, artifacts);

            case "engineering.export-tree-item-xml":
                EnsureTask(ctx);
                string taskXml = Path.Combine(ctx.RunDirectory, "task.xml");
                ctx.Engineering.ExportTreeItemXml(ctx.Session!,
                    new ExportTreeItemXmlRequest($"TIRT^{ctx.TaskName}", taskXml, Recursive: true));
                artifacts.Add(taskXml);
                return Succeeded(ctx, "Export tree item xml probe succeeded.", outputs, artifacts);

            case "engineering.save-all":
                EnsureSolution(ctx);
                ctx.Engineering.SaveAll(ctx.Session!);
                return Succeeded(ctx, "SaveAll probe succeeded.", outputs, artifacts);

            case "engineering.close-visual-studio":
                EnsureSolution(ctx);
                ctx.Engineering.CloseVisualStudio(ctx.Session!, saveBeforeClose: true);
                ctx.Session = null;
                return Succeeded(ctx, "Close Visual Studio probe succeeded.", outputs, artifacts);

            case "engineering.build-solution":
                EnsureSolution(ctx);
                BuildResult buildResult = ctx.Engineering.BuildCurrentSolution(ctx.Session!, new BuildSolutionRequest());
                outputs["lastBuildInfo"] = buildResult.LastBuildInfo.ToString(CultureInfo.InvariantCulture);
                outputs["succeeded"] = buildResult.Succeeded ? "true" : "false";
                return Succeeded(ctx, "Build solution probe executed.", outputs, artifacts);

            case "engineering.activate-configuration":
                if (!options.AllowActivate)
                {
                    throw new InvalidOperationException("activate-configuration probe requires --allow-activate=true.");
                }

                EnsureSolution(ctx);
                ActivationResult activation = ctx.Engineering.ActivateConfiguration(ctx.Session!, new ActivateConfigurationRequest());
                outputs["activationCommand"] = activation.ActivationCommand;
                outputs["configurationArchivePath"] = activation.ConfigurationArchivePath;
                return Succeeded(ctx, "Activate configuration probe executed.", outputs, artifacts);

            case "validation.ads-read":
                if (string.IsNullOrWhiteSpace(options.AdsSymbol))
                {
                    throw new InvalidOperationException("ads-read probe requires --ads-symbol=<symbol-path>.");
                }

                AdsReadResult ads = ctx.Ads.Read(new AdsReadRequest(
                    ctx.Config.AmsNetId,
                    ctx.Config.AdsPort,
                    options.AdsSymbol,
                    options.AdsType));
                outputs["adsSucceeded"] = ads.Succeeded ? "true" : "false";
                outputs["adsValue"] = ads.Value;
                outputs["adsError"] = ads.ErrorMessage;
                string adsResultPath = Path.Combine(ctx.RunDirectory, "ads-read.json");
                File.WriteAllText(adsResultPath,
                    JsonSerializer.Serialize(ads, new JsonSerializerOptions { WriteIndented = true }));
                artifacts.Add(adsResultPath);
                return Succeeded(ctx, "ADS read probe executed.", outputs, artifacts);

            case "validation.ads-read-symbols":
                if (string.IsNullOrWhiteSpace(options.AdsSymbol))
                {
                    throw new InvalidOperationException("ads-read-symbols probe requires --ads-symbol=<symbol-path>.");
                }

                AdsReadSymbolsResult adsSymbols = ctx.Ads.ReadSymbols(new AdsReadSymbolsRequest(
                    ctx.Config.AmsNetId,
                    ctx.Config.AdsPort,
                    [new AdsReadSymbolRequest(options.AdsSymbol, options.AdsType)]));
                outputs["adsSucceededCount"] = adsSymbols.SucceededCount.ToString(CultureInfo.InvariantCulture);
                outputs["adsFailedCount"] = adsSymbols.FailedCount.ToString(CultureInfo.InvariantCulture);
                outputs["adsValues"] = string.Join(
                    "; ",
                    adsSymbols.Symbols.Select(symbol =>
                        symbol.Succeeded
                            ? $"{symbol.SymbolPath}={symbol.Value}"
                            : $"{symbol.SymbolPath}=<failed: {symbol.ErrorMessage}>"));
                string adsSymbolsResultPath = Path.Combine(ctx.RunDirectory, "ads-read-symbols.json");
                File.WriteAllText(adsSymbolsResultPath,
                    JsonSerializer.Serialize(adsSymbols, new JsonSerializerOptions { WriteIndented = true }));
                artifacts.Add(adsSymbolsResultPath);
                return Succeeded(ctx, "ADS read symbols probe executed.", outputs, artifacts);
        }

        throw new InvalidOperationException($"No engineering probe scenario implemented for '{kind}'.");
    }

    private static ProbeRunResult RunTsprojProbe(
        string kind,
        ProbeContext ctx,
        Dictionary<string, string?> outputs,
        List<string> artifacts)
    {
        switch (kind)
        {
            case "tsproj.ensure-task":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-task", artifacts, path =>
                    ctx.Mutation.EnsureTaskDefinition(path, new EnsureTaskDefinitionRequest("Task1", 15, 10_000_000, 350)));
                return Succeeded(ctx, "tsproj.ensure-task probe succeeded.", outputs, artifacts);

            case "tsproj.clear-task-layout":
                EnsureCppInstanceAndTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                ctx.Mutation.EnsureIoTaskImage(RequireProjectPath(ctx),
                    new EnsureIoTaskImageRequest(ctx.TaskName, ctx.InstanceName!, ImageId: 1));
                AddSnapshotArtifacts(ctx, "clear-task-layout", artifacts, path =>
                    ctx.Mutation.ClearTaskLayout(path, new ClearTaskLayoutRequest(ctx.TaskName)));
                return Succeeded(ctx, "tsproj.clear-task-layout probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-task-vars-group":
                EnsureTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-task-vars-group", artifacts, path =>
                    ctx.Mutation.EnsureTaskVarsGroup(path, new EnsureTaskVarsGroupRequest(
                        ctx.TaskName, "Inputs", 1, 1, "Var ", "REAL", 3, 32, 4)));
                return Succeeded(ctx, "tsproj.ensure-task-vars-group probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-task-image":
                EnsureTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-task-image", artifacts, path =>
                    ctx.Mutation.EnsureTaskImage(path, new EnsureTaskImageRequest(ctx.TaskName, ImageId: 1, IoAtBegin: true)));
                return Succeeded(ctx, "tsproj.ensure-task-image probe succeeded.", outputs, artifacts);

            case "tsproj.bind-instance-context":
                EnsureCppInstanceAndTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "bind-instance-context", artifacts, path =>
                    ctx.Mutation.BindInstanceContext(path, new BindInstanceContextRequest(
                        ctx.InstanceName!,
                        ctx.TaskObjectId!,
                        15,
                        10_000_000,
                        ContextId: 1,
                        ContextName: "TaskCtx",
                        IncludeCyclicCaller: true)));
                return Succeeded(ctx, "tsproj.bind-instance-context probe succeeded.", outputs, artifacts);

            case "tsproj.set-task-affinity":
                EnsureTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "set-task-affinity", artifacts, path =>
                    ctx.Mutation.SetTaskAffinity(path, new SetTaskAffinityRequest(ctx.TaskName, "1", true)));
                return Succeeded(ctx, "tsproj.set-task-affinity probe succeeded.", outputs, artifacts);

            case "tsproj.set-plc-project-properties":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "set-plc-project-properties", artifacts, path =>
                    ctx.Mutation.SetPlcProjectProperties(path, new SetPlcProjectPropertiesRequest(
                        ctx.PlcProjectName!,
                        ProjectFilePath: $"{ctx.PlcProjectName}\\{ctx.PlcProjectName}.plcproj",
                        TmcFilePath: $"{ctx.PlcProjectName}\\{ctx.PlcProjectName}.tmc",
                        ReloadTmc: true,
                        AmsPort: 851,
                        FileArchiveSettings: "#x0002")));
                return Succeeded(ctx, "tsproj.set-plc-project-properties probe succeeded.", outputs, artifacts);

            case "tsproj.set-plc-instance-metadata":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "set-plc-instance-metadata", artifacts, path =>
                    ctx.Mutation.SetPlcInstanceMetadata(path, new SetPlcInstanceMetadataRequest(
                        ctx.PlcProjectName!,
                        ctx.PlcInstanceName!,
                        TcSmClass: "TComPlcObjDef",
                        TmcPath: $"{ctx.PlcProjectName}\\{ctx.PlcProjectName}.tmc",
                        KeepUnrestoredLinks: "2",
                        Clsid: "{08500001-0000-0000-F000-000000000064}",
                        ClassFactory: "TcPlc30")));
                return Succeeded(ctx, "tsproj.set-plc-instance-metadata probe succeeded.", outputs, artifacts);

            case "tsproj.clear-plc-instance-vars":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                ctx.Mutation.EnsurePlcInstanceVarsGroup(RequireProjectPath(ctx), new EnsurePlcInstanceVarsGroupRequest(
                    ctx.PlcProjectName!,
                    ctx.PlcInstanceName!,
                    "PlcTask Outputs",
                    2,
                    AreaNo: 1,
                    Variables: [new PlcInstanceVarItem("MAIN.PlcVar", "DINT")]));
                AddSnapshotArtifacts(ctx, "clear-plc-instance-vars", artifacts, path =>
                    ctx.Mutation.ClearPlcInstanceVars(path, new ClearPlcInstanceVarsRequest(ctx.PlcProjectName!, ctx.PlcInstanceName!)));
                return Succeeded(ctx, "tsproj.clear-plc-instance-vars probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-plc-instance-vars-group":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-plc-instance-vars-group", artifacts, path =>
                    ctx.Mutation.EnsurePlcInstanceVarsGroup(path, new EnsurePlcInstanceVarsGroupRequest(
                        ctx.PlcProjectName!,
                        ctx.PlcInstanceName!,
                        "PlcTask Outputs",
                        2,
                        AreaNo: 1,
                        Variables: [new PlcInstanceVarItem("MAIN.PlcVar", "DINT")])));
                return Succeeded(ctx, "tsproj.ensure-plc-instance-vars-group probe succeeded.", outputs, artifacts);

            case "tsproj.clear-plc-init-symbols":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                ctx.Mutation.EnsureInitSymbol(RequireProjectPath(ctx), new EnsureInitSymbolRequest(
                    ctx.PlcProjectName!,
                    ctx.PlcInstanceName!,
                    "MAIN.fbStateMachine.oidInstance",
                    "#x02010010"));
                AddSnapshotArtifacts(ctx, "clear-plc-init-symbols", artifacts, path =>
                    ctx.Mutation.ClearPlcInitSymbols(path, new ClearPlcInitSymbolsRequest(ctx.PlcProjectName!, ctx.PlcInstanceName!, true)));
                return Succeeded(ctx, "tsproj.clear-plc-init-symbols probe succeeded.", outputs, artifacts);

            case "tsproj.clear-plc-task-pou-oids":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                ctx.Mutation.EnsureTaskPouOid(RequireProjectPath(ctx), new EnsureTaskPouOidRequest(
                    ctx.PlcProjectName!,
                    ctx.PlcInstanceName!,
                    15,
                    "#x02010010"));
                AddSnapshotArtifacts(ctx, "clear-plc-task-pou-oids", artifacts, path =>
                    ctx.Mutation.ClearPlcTaskPouOids(path, new ClearPlcTaskPouOidsRequest(ctx.PlcProjectName!, ctx.PlcInstanceName!, false)));
                return Succeeded(ctx, "tsproj.clear-plc-task-pou-oids probe succeeded.", outputs, artifacts);

            case "tsproj.clear-mappings":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                ctx.Mutation.EnsureMappingLink(RequireProjectPath(ctx), new EnsureMappingLinkRequest("A", "B", "v1", "v2"));
                AddSnapshotArtifacts(ctx, "clear-mappings", artifacts, path =>
                    ctx.Mutation.ClearMappings(path, new ClearMappingsRequest()));
                return Succeeded(ctx, "tsproj.clear-mappings probe succeeded.", outputs, artifacts);

            case "tsproj.replace-mappings-section":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "replace-mappings-section", artifacts, path =>
                    ctx.Mutation.ReplaceMappingsSection(path, new ReplaceMappingsSectionRequest(
                        "<Mappings><OwnerA Name=\"A\"><OwnerB Name=\"B\"><Link VarA=\"x\" VarB=\"y\" /></OwnerB></OwnerA></Mappings>")));
                return Succeeded(ctx, "tsproj.replace-mappings-section probe succeeded.", outputs, artifacts);

            case "tsproj.replace-project-io-section":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "replace-project-io-section", artifacts, path =>
                    ctx.Mutation.ReplaceProjectIoSection(path, new ReplaceProjectIoSectionRequest(
                        "<Io><Device Name=\"ProbeIo\"><Comment>generated</Comment></Device></Io>")));
                return Succeeded(ctx, "tsproj.replace-project-io-section probe succeeded.", outputs, artifacts);

            case "tsproj.replace-data-types-section":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "replace-data-types-section", artifacts, path =>
                    ctx.Mutation.ReplaceDataTypesSection(path, new ReplaceDataTypesSectionRequest(
                        "<DataTypes><DataType><Name>ProbeType</Name></DataType></DataTypes>")));
                return Succeeded(ctx, "tsproj.replace-data-types-section probe succeeded.", outputs, artifacts);

            case "tsproj.replace-system-settings-section":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "replace-system-settings-section", artifacts, path =>
                    ctx.Mutation.ReplaceSystemSettingsSection(path, new ReplaceSystemSettingsSectionRequest(
                        "<Settings><Option Name=\"Mode\">Probe</Option></Settings>")));
                return Succeeded(ctx, "tsproj.replace-system-settings-section probe succeeded.", outputs, artifacts);

            case "tsproj.apply-instance-parameter-plan":
                EnsureCppInstance(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "apply-instance-parameter-plan", artifacts, path =>
                    ctx.Mutation.ApplyInstanceParameterPlan(path, new ApplyInstanceParameterPlanRequest(
                    [
                        new InstanceParameterMutation(ctx.InstanceName!, "TraceLevelMax", EnumText: "tlVerbose"),
                        new InstanceParameterMutation(ctx.InstanceName!, "Parameter.Timeout", ValueText: "60000")
                    ])));
                return Succeeded(ctx, "tsproj.apply-instance-parameter-plan probe succeeded.", outputs, artifacts);

            case "tsproj.apply-instance-interface-pointer-plan":
                EnsureCppInstance(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "apply-instance-interface-pointer-plan", artifacts, path =>
                    ctx.Mutation.ApplyInstanceInterfacePointerPlan(path, new ApplyInstanceInterfacePointerPlanRequest(
                    [
                        new InstanceInterfacePointerMutation(ctx.InstanceName!, "LargeObjPool", "#x02010030"),
                        new InstanceInterfacePointerMutation(ctx.InstanceName!, "DataWrite", "#x02010040")
                    ])));
                return Succeeded(ctx, "tsproj.apply-instance-interface-pointer-plan probe succeeded.", outputs, artifacts);

            case "tsproj.apply-instance-data-pointer-plan":
                EnsureCppInstance(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "apply-instance-data-pointer-plan", artifacts, path =>
                    ctx.Mutation.ApplyInstanceDataPointerPlan(path, new ApplyInstanceDataPointerPlanRequest(
                    [
                        new InstanceDataPointerMutation(ctx.InstanceName!, "DataIn", "#x02010050", 3, 0, 8),
                        new InstanceDataPointerMutation(ctx.InstanceName!, "DataOut", "#x02010060", 3, 8, 8)
                    ])));
                return Succeeded(ctx, "tsproj.apply-instance-data-pointer-plan probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-io-task-image":
                EnsureCppInstanceAndTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-io-task-image", artifacts, path =>
                    ctx.Mutation.EnsureIoTaskImage(path, new EnsureIoTaskImageRequest(
                        ctx.TaskName,
                        ctx.InstanceName!,
                        ImageId: 1,
                        SizeIn: 40,
                        SizeOut: 10)));
                return Succeeded(ctx, "tsproj.ensure-io-task-image probe succeeded.", outputs, artifacts);

            case "tsproj.bind-instance-task":
                EnsureCppInstanceAndTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "bind-instance-task", artifacts, path =>
                    ctx.Mutation.BindInstanceToTask(path, new BindInstanceToTaskRequest(
                        ctx.InstanceName!,
                        ctx.TaskObjectId!,
                        15,
                        10_000_000)));
                return Succeeded(ctx, "tsproj.bind-instance-task probe succeeded.", outputs, artifacts);

            case "tsproj.bind-plc-instance-task":
                EnsurePlcProjectAndTask(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "bind-plc-instance-task", artifacts, path =>
                    ctx.Mutation.BindPlcInstanceToTask(path, new BindPlcInstanceToTaskRequest(
                        ctx.PlcProjectName!,
                        ctx.PlcInstanceName!,
                        "PlcTask",
                        ctx.TaskObjectId!,
                        15,
                        10_000_000)));
                return Succeeded(ctx, "tsproj.bind-plc-instance-task probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-task-pou-oid":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-task-pou-oid", artifacts, path =>
                    ctx.Mutation.EnsureTaskPouOid(path, new EnsureTaskPouOidRequest(
                        ctx.PlcProjectName!,
                        ctx.PlcInstanceName!,
                        15,
                        "#x02010010")));
                return Succeeded(ctx, "tsproj.ensure-task-pou-oid probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-init-symbol":
                EnsurePlcProject(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-init-symbol", artifacts, path =>
                    ctx.Mutation.EnsureInitSymbol(path, new EnsureInitSymbolRequest(
                        ctx.PlcProjectName!,
                        ctx.PlcInstanceName!,
                        "MAIN.fbStateMachine.oidInstance",
                        "#x02010010")));
                return Succeeded(ctx, "tsproj.ensure-init-symbol probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-parameter":
                EnsureCppInstance(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-parameter", artifacts, path =>
                    ctx.Mutation.EnsureParameterValue(path, new EnsureParameterValueRequest(
                        ctx.InstanceName!,
                        "TraceLevelMax",
                        EnumText: "tlAlways")));
                return Succeeded(ctx, "tsproj.ensure-parameter probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-interface-pointer":
                EnsureCppInstance(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-interface-pointer", artifacts, path =>
                    ctx.Mutation.EnsureInterfacePointerValue(path, new EnsureInterfacePointerValueRequest(
                        ctx.InstanceName!,
                        "LargeObjPool",
                        "#x02010030")));
                return Succeeded(ctx, "tsproj.ensure-interface-pointer probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-data-pointer":
                EnsureCppInstance(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-data-pointer", artifacts, path =>
                    ctx.Mutation.EnsureDataPointerValue(path, new EnsureDataPointerValueRequest(
                        ctx.InstanceName!,
                        "DataIn",
                        "#x02010050",
                        3,
                        0,
                        8)));
                return Succeeded(ctx, "tsproj.ensure-data-pointer probe succeeded.", outputs, artifacts);

            case "tsproj.ensure-mapping-link":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "ensure-mapping-link", artifacts, path =>
                    ctx.Mutation.EnsureMappingLink(path, new EnsureMappingLinkRequest("A", "B", "v1", "v2")));
                return Succeeded(ctx, "tsproj.ensure-mapping-link probe succeeded.", outputs, artifacts);

            case "tsproj.merge-fragment":
                EnsureSolution(ctx);
                SaveAndCloseForFileMutation(ctx);
                AddSnapshotArtifacts(ctx, "merge-fragment", artifacts, path =>
                    ctx.Mutation.MergeNamedElementFragment(path, new MergeNamedElementFragmentRequest(
                        "DataTypes",
                        "<DataType><Name>ProbeTypeFromMerge</Name></DataType>",
                        "DataType",
                        "ProbeTypeFromMerge",
                        true,
                        FragmentSource: "integration step probe generated known-good fragment",
                        TargetParentPath: "TcSmProject/DataTypes",
                        FieldMeaning: "Probe-only DataType Name used to verify merge-fragment mechanics; not a reusable engineering recipe.",
                        VerificationEvidence: "StepProbeRunner snapshot plus XAE reopen/build evidence when the integration probe is executed.")));
                return Succeeded(ctx, "tsproj.merge-fragment probe succeeded.", outputs, artifacts);
        }

        throw new InvalidOperationException($"No tsproj probe scenario implemented for '{kind}'.");
    }

    private static void EnsureSession(ProbeContext ctx)
    {
        if (ctx.Session is not null)
        {
            return;
        }

        ctx.Session = ctx.Engineering.LaunchVisualStudio(IntegrationTestHelper.MakeLaunchRequest(ctx.Config));
    }

    private static void EnsureSolution(ProbeContext ctx)
    {
        if (ctx.ProjectInfo is not null)
        {
            return;
        }

        EnsureSession(ctx);
        ctx.ProjectInfo = ctx.Engineering.CreateTwinCatSolution(ctx.Session!,
            new CreateTwinCatSolutionRequest(ctx.WorkDirectory, "S", "S"));
        ctx.Engineering.SaveAll(ctx.Session!);
    }

    private static void EnsureCppProject(ProbeContext ctx)
    {
        EnsureSolution(ctx);
        string cppPath = Path.Combine(ctx.ProjectInfo!.SolutionDirectory, ctx.CppProjectName, ctx.CppProjectName + ".vcxproj");
        if (File.Exists(cppPath))
        {
            return;
        }

        ctx.Engineering.CreateCppProject(ctx.Session!, new CreateCppProjectRequest(ctx.CppProjectName));
        ctx.Engineering.SaveAll(ctx.Session!);
    }

    private static void EnsureCppInstance(ProbeContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.InstanceName))
        {
            return;
        }

        EnsureCppProject(ctx);
        string tmcPath = Path.Combine(ctx.ProjectInfo!.SolutionDirectory, ctx.CppProjectName, ctx.CppProjectName + ".tmc");
        EnsureModuleClassForProbes(ctx, tmcPath);
        if (!IntegrationTestHelper.TmcHasAnyModule(tmcPath))
        {
            // Fallback for file-mutation probes: inject a deterministic instance skeleton later
            // after VS closes so all tsproj.* interfaces can still be inspected manually.
            ctx.InstanceName = "Obj (CFallbackModule)";
            ctx.InstanceObjectId = "#x02010010";
            ctx.PendingFallbackCppInstance = true;
            return;
        }

        TwinCatNodeInfo instance = ctx.Engineering.AddModuleInstance(ctx.Session!,
            new AddModuleInstanceRequest(ctx.CppProjectName, tmcPath, "Obj", ModuleClassName: null));
        ctx.InstanceName = instance.DisplayName;
        ctx.InstanceObjectId = instance.ObjectId ?? "#x02010010";
        ctx.Engineering.SaveAll(ctx.Session!);
        if (!TsprojContainsInstance(RequireProjectPath(ctx), ctx.InstanceName))
        {
            // Some automation environments report success but do not persist the instance to tsproj.
            // Keep a fallback flag so file-mutation probes still get a concrete, inspectable instance node.
            ctx.PendingFallbackCppInstance = true;
            return;
        }

        ctx.PendingFallbackCppInstance = false;
    }

    private static void EnsureCppInstanceStrict(ProbeContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.InstanceName) && !ctx.PendingFallbackCppInstance)
        {
            return;
        }

        EnsureCppProject(ctx);
        string tmcPath = Path.Combine(ctx.ProjectInfo!.SolutionDirectory, ctx.CppProjectName, ctx.CppProjectName + ".tmc");
        EnsureModuleClassForProbes(ctx, tmcPath);
        if (!IntegrationTestHelper.TmcHasAnyModule(tmcPath))
        {
            throw new InvalidOperationException(
                $"'{tmcPath}' has no module class after deterministic module-artifact fallback.");
        }

        TwinCatNodeInfo instance = ctx.Engineering.AddModuleInstance(ctx.Session!,
            new AddModuleInstanceRequest(ctx.CppProjectName, tmcPath, "Obj", ModuleClassName: null));
        ctx.InstanceName = instance.DisplayName;
        ctx.InstanceObjectId = instance.ObjectId ?? "#x02010010";
        ctx.PendingFallbackCppInstance = false;
        ctx.Engineering.SaveAll(ctx.Session!);
    }

    private static void EnsureModuleClassForProbes(ProbeContext ctx, string tmcPath)
    {
        if (IntegrationTestHelper.TmcHasAnyModule(tmcPath))
        {
            return;
        }

        EnsureSession(ctx);
        ctx.Engineering.CreateModule(ctx.Session!, new CreateModuleRequest(ctx.CppProjectName, "ProbeModule", "TcModuleClassWizard"));
        ctx.Engineering.SaveAll(ctx.Session!);
    }

    private static void EnsureTask(ProbeContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.TaskObjectId))
        {
            return;
        }

        EnsureSolution(ctx);
        TwinCatNodeInfo task = ctx.Engineering.EnsureTask(ctx.Session!,
            new EnsureTaskRequest(ctx.TaskName, TaskSubtype: 0, Priority: 15, CycleTimeUs: 10_000, AmsPort: 350));
        ctx.TaskObjectId = task.ObjectId ?? "#x02010010";
        ctx.Engineering.SaveAll(ctx.Session!);
    }

    private static void EnsurePlcProject(ProbeContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.PlcProjectName) && !string.IsNullOrWhiteSpace(ctx.PlcInstanceName))
        {
            return;
        }

        EnsureSolution(ctx);
        ctx.Engineering.CreatePlcProject(ctx.Session!, new CreatePlcProjectRequest("PlcA"));
        ctx.Engineering.SaveAll(ctx.Session!);
        (string projectName, string instanceName) = ExtractFirstPlcNames(ctx.ProjectInfo!.ProjectPath);
        ctx.PlcProjectName = projectName;
        ctx.PlcInstanceName = instanceName;
    }

    private static void EnsureCppInstanceAndTask(ProbeContext ctx)
    {
        EnsureCppInstance(ctx);
        EnsureTask(ctx);
    }

    private static void EnsurePlcProjectAndTask(ProbeContext ctx)
    {
        EnsurePlcProject(ctx);
        EnsureTask(ctx);
    }

    private static void SaveAndCloseForFileMutation(ProbeContext ctx)
    {
        EnsureSolution(ctx);
        if (ctx.Session is not null)
        {
            ctx.Engineering.SaveAll(ctx.Session);
            CloseSession(ctx);
        }

        if (!ctx.PendingFallbackCppInstance &&
            !string.IsNullOrWhiteSpace(ctx.InstanceName) &&
            !TsprojContainsInstance(RequireProjectPath(ctx), ctx.InstanceName))
        {
            ctx.PendingFallbackCppInstance = true;
        }

        if (!string.IsNullOrWhiteSpace(ctx.PlcProjectName) &&
            !string.IsNullOrWhiteSpace(ctx.PlcInstanceName) &&
            !TsprojContainsPlcInstance(RequireProjectPath(ctx), ctx.PlcProjectName, ctx.PlcInstanceName))
        {
            EnsureFallbackPlcProjectInTsproj(ctx);
        }

        if (ctx.PendingFallbackCppInstance)
        {
            EnsureFallbackCppInstanceInTsproj(ctx);
            ctx.PendingFallbackCppInstance = false;
        }
    }

    private static void EnsureFallbackCppInstanceInTsproj(ProbeContext ctx)
    {
        string tsprojPath = RequireProjectPath(ctx);
        string instanceName = string.IsNullOrWhiteSpace(ctx.InstanceName)
            ? "Obj (CFallbackModule)"
            : ctx.InstanceName!;
        string instanceObjectId = string.IsNullOrWhiteSpace(ctx.InstanceObjectId)
            ? "#x02010010"
            : ctx.InstanceObjectId!;

        XDocument document = XDocument.Load(tsprojPath);
        XElement? cppProject = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Cpp" &&
            (string.Equals(
                 element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value,
                 ctx.CppProjectName,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value,
                 ctx.CppProjectName,
                 StringComparison.OrdinalIgnoreCase)));

        if (cppProject is null)
        {
            throw new InvalidOperationException("Unable to locate Cpp/Project node in generated .tsproj for fallback instance injection.");
        }

        XElement? existing = cppProject.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Instance" &&
            string.Equals(
                element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value,
                instanceName,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.SetAttributeValue("OTCID", instanceObjectId);
            document.Save(tsprojPath);
            return;
        }

        XNamespace ns = cppProject.GetDefaultNamespace();
        XElement instance = new(
            ns + "Instance",
            new XAttribute("OTCID", instanceObjectId),
            new XElement(ns + "Name", instanceName),
            new XElement(
                ns + "TmcDesc",
                new XElement(
                    ns + "Contexts",
                    new XElement(
                        ns + "Context",
                        new XElement(ns + "Id", "1"),
                        new XElement(ns + "Name", "FallbackCtx"),
                        new XElement(
                            ns + "ManualConfig",
                            new XElement(ns + "OTCID"),
                            new XElement(ns + "Priority", "0"),
                            new XElement(ns + "CycleTime", "0")))),
                new XElement(ns + "ParameterValues"),
                new XElement(ns + "InterfacePointerValues"),
                new XElement(ns + "DataPointerValues")));

        cppProject.Add(instance);
        document.Save(tsprojPath);
    }

    private static void CloseSession(ProbeContext ctx)
    {
        if (ctx.Session is null)
        {
            return;
        }

        try
        {
            ctx.Engineering.CloseVisualStudio(ctx.Session);
        }
        catch
        {
        }
        finally
        {
            ctx.Session = null;
        }
    }

    private static void AddSnapshotArtifacts(ProbeContext ctx, string label, List<string> artifacts, Action<string> mutate)
    {
        string tsprojPath = RequireProjectPath(ctx);
        string before = Path.Combine(ctx.RunDirectory, label + ".before.tsproj");
        string after = Path.Combine(ctx.RunDirectory, label + ".after.tsproj");
        File.Copy(tsprojPath, before, true);
        mutate(tsprojPath);
        File.Copy(tsprojPath, after, true);
        artifacts.Add(before);
        artifacts.Add(after);
    }

    private static string RequireProjectPath(ProbeContext ctx) =>
        ctx.ProjectInfo?.ProjectPath ?? throw new InvalidOperationException("Project path is not available.");

    private static bool TsprojContainsInstance(string tsprojPath, string? instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName) || !File.Exists(tsprojPath))
        {
            return false;
        }

        XDocument document = XDocument.Load(tsprojPath);
        return document.Descendants().Any(element =>
            element.Name.LocalName == "Instance" &&
            string.Equals(
                element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value,
                instanceName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TsprojContainsPlcInstance(string tsprojPath, string? plcProjectName, string? plcInstanceName)
    {
        if (string.IsNullOrWhiteSpace(plcProjectName) || string.IsNullOrWhiteSpace(plcInstanceName) || !File.Exists(tsprojPath))
        {
            return false;
        }

        XDocument document = XDocument.Load(tsprojPath);
        XElement? project = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Plc" &&
            (string.Equals(
                 element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value,
                 plcProjectName,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value,
                 plcProjectName,
                 StringComparison.OrdinalIgnoreCase)));
        if (project is null)
        {
            return false;
        }

        return project.Elements().Any(element =>
            element.Name.LocalName == "Instance" &&
            string.Equals(
                element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value,
                plcInstanceName,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TmcContainsNamedModule(string tmcPath, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(tmcPath) || string.IsNullOrWhiteSpace(moduleName) || !File.Exists(tmcPath))
        {
            return false;
        }

        XDocument document = XDocument.Load(tmcPath);
        return document.Descendants().Any(element =>
            element.Name.LocalName == "Module" &&
            (string.Equals(
                 element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "ClassName")?.Value,
                 moduleName,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value,
                 moduleName,
                 StringComparison.OrdinalIgnoreCase)));
    }

    private static void EnsureFallbackPlcProjectInTsproj(ProbeContext ctx)
    {
        string tsprojPath = RequireProjectPath(ctx);
        string plcProjectName = string.IsNullOrWhiteSpace(ctx.PlcProjectName) ? "PlcA" : ctx.PlcProjectName!;
        string plcInstanceName = string.IsNullOrWhiteSpace(ctx.PlcInstanceName) ? "PlcA Instance" : ctx.PlcInstanceName!;

        XDocument document = XDocument.Load(tsprojPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XNamespace ns = root.GetDefaultNamespace();

        XElement plc = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Plc")
            ?? new XElement(ns + "Plc");
        if (plc.Parent is null)
        {
            root.Add(plc);
        }

        XElement project = plc.Elements().FirstOrDefault(element =>
                              element.Name.LocalName == "Project" &&
                              string.Equals(
                                  element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value,
                                  plcProjectName,
                                  StringComparison.OrdinalIgnoreCase))
                          ?? new XElement(ns + "Project", new XAttribute("Name", plcProjectName));
        if (project.Parent is null)
        {
            plc.Add(project);
        }

        XElement instance = project.Elements().FirstOrDefault(element =>
                               element.Name.LocalName == "Instance" &&
                               string.Equals(
                                   element.Elements().FirstOrDefault(child => child.Name.LocalName == "Name")?.Value,
                                   plcInstanceName,
                                   StringComparison.OrdinalIgnoreCase))
                           ?? new XElement(ns + "Instance", new XElement(ns + "Name", plcInstanceName));
        if (instance.Parent is null)
        {
            project.Add(instance);
        }

        document.Save(tsprojPath);
        ctx.PlcProjectName = plcProjectName;
        ctx.PlcInstanceName = plcInstanceName;
    }

    private static (string ProjectName, string InstanceName) ExtractFirstPlcNames(string tsprojPath)
    {
        XDocument doc = XDocument.Load(tsprojPath);
        XElement? project = doc.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Plc");
        if (project is null)
        {
            throw new InvalidOperationException("No Plc/Project node found in generated tsproj.");
        }

        string? projectName =
            project.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value ??
            project.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value;
        XElement? instance = project.Elements().FirstOrDefault(element => element.Name.LocalName == "Instance");
        string? instanceName = instance?.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value;

        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(instanceName))
        {
            throw new InvalidOperationException("Unable to resolve Plc project/instance names from generated tsproj.");
        }

        return (projectName, instanceName);
    }

    private static ProbeRunResult Succeeded(
        ProbeContext ctx,
        string summary,
        IReadOnlyDictionary<string, string?> outputs,
        IReadOnlyList<string> artifacts) =>
        new(
            ctx.Kind,
            "Succeeded",
            summary,
            ctx.RunDirectory,
            ctx.WorkDirectory,
            ctx.ProjectInfo?.SolutionPath,
            ctx.ProjectInfo?.ProjectPath,
            outputs,
            artifacts);

    private static ProbeRunResult Failed(
        ProbeContext ctx,
        string summary,
        IReadOnlyDictionary<string, string?> outputs,
        IReadOnlyList<string> artifacts) =>
        new(
            ctx.Kind,
            "Failed",
            summary,
            ctx.RunDirectory,
            ctx.WorkDirectory,
            ctx.ProjectInfo?.SolutionPath,
            ctx.ProjectInfo?.ProjectPath,
            outputs,
            artifacts);

    private static void WriteProbeResultManifest(ProbeRunResult result)
    {
        string path = Path.Combine(result.RunDirectory, "probe-result.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void PrintProbeResult(ProbeRunResult result)
    {
        Console.WriteLine($"{result.Status} {result.Kind}");
        Console.WriteLine($"  RunDir: {result.RunDirectory}");
        if (!string.IsNullOrWhiteSpace(result.SolutionPath))
        {
            Console.WriteLine($"  Sln:    {result.SolutionPath}");
        }

        if (!string.IsNullOrWhiteSpace(result.ProjectPath))
        {
            Console.WriteLine($"  Tsproj: {result.ProjectPath}");
        }

        Console.WriteLine($"  Note:   {result.Summary}");
    }

    private static ProbeOptions BuildProbeOptions(IntegrationTestConfig config, Dictionary<string, string> options)
    {
        string outputRoot = options.TryGetValue("out", out string? outPath) && !string.IsNullOrWhiteSpace(outPath)
            ? Path.GetFullPath(outPath)
            : Path.Combine(config.WorkRootBase, "probes", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

        bool allowActivate = options.TryGetValue("allow-activate", out string? rawAllow) &&
            bool.TryParse(rawAllow, out bool parsedAllow) &&
            parsedAllow;

        string? adsSymbol = options.TryGetValue("ads-symbol", out string? rawSymbol) ? rawSymbol : null;
        AdsReadDataType adsType = AdsReadDataType.Boolean;
        if (options.TryGetValue("ads-type", out string? rawType) &&
            Enum.TryParse(rawType, ignoreCase: true, out AdsReadDataType parsedType))
        {
            adsType = parsedType;
        }

        return new ProbeOptions(outputRoot, allowActivate, adsSymbol, adsType);
    }

    private static string EnsureOutputRoot(string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        return outputRoot;
    }

    private static string BuildRunId(string kind, int index)
    {
        string shortKind = kind.Replace("engineering.", "eng-")
            .Replace("tsproj.", "ts-")
            .Replace("validation.", "val-")
            .Replace(".", "-")
            .Replace("_", "-");
        shortKind = new string(shortKind.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        if (shortKind.Length > 28)
        {
            shortKind = shortKind[..28];
        }

        string hash = ComputeHash6(kind);
        return $"{index:D2}-{shortKind}-{hash}";
    }

    private static string ComputeHash6(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes[..3]).ToLowerInvariant();
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int separatorIndex = arg.IndexOf('=');
            if (separatorIndex < 0)
            {
                options[arg[2..]] = "true";
                continue;
            }

            options[arg[2..separatorIndex]] = arg[(separatorIndex + 1)..];
        }

        return options;
    }
}
