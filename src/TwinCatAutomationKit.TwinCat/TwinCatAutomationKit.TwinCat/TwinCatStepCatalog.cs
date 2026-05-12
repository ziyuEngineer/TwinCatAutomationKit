using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.TwinCat;

public static class TwinCatStepCatalog
{
    private static readonly IReadOnlyList<StepContract> ContractsInternal = new[]
    {
        new StepContract(
            "engineering.launch-visual-studio",
            "TwinCatEngineeringService.LaunchVisualStudio",
            "engineering",
            "Starts or attaches a DTE session that later steps can use for XAE creation, build, and activation.",
            new[] { "Visual Studio with TwinCAT XAE must be installed on the machine." },
            new[]
            {
                new StepParameterContract("ProgId", "string", false, "Visual Studio DTE ProgId.", TwinCatPathDefaults.DefaultVisualStudioProgId),
                new StepParameterContract("StartupDelayMs", "int", false, "Warm-up delay before the DTE session is used.", "5000"),
                new StepParameterContract("Visible", "bool", false, "Whether the launched Visual Studio window should be visible.", "true"),
                new StepParameterContract("SuppressUi", "bool", false, "Whether DTE.SuppressUI should be enabled for unattended runs.", "true"),
                new StepParameterContract("LaunchTimeoutMs", "int", false, "Maximum time allowed for DTE COM activation before failing the step.", "60000"),
                new StepParameterContract("EnableDialogAutoDismiss", "bool", false, "Whether unattended runs should monitor selected VS/TcXaeShell host processes during DTE COM activation, fallback launch, startup delay, and the active session, then close known modal confirmation dialogs by title or message text.", "true"),
                new StepParameterContract("DialogPollIntervalMs", "int", false, "Polling interval for the unattended dialog auto-dismiss watcher.", "500"),
                new StepParameterContract("AttachToExisting", "bool", false, "Whether launch may attach to an already running DTE session. Keep false for unattended runs so stale headless Visual Studio hosts are not reused.", "false"),
                new StepParameterContract("RootSuffix", "string", false, "Optional Visual Studio /RootSuffix used by explicit fallback launch to isolate a broken user registry hive or profile during unattended DTE startup."),
                new StepParameterContract("DteHostPath", "string", false, "Optional explicit devenv.exe or TcXaeShell.exe path used for fallback DTE host launch when COM activation does not return promptly."),
                new StepParameterContract("PreferDteHostLaunch", "bool", false, "Whether to start the explicit DTE host first and attach through the ROT before attempting Activator.CreateInstance. Useful when COM activation is known to show an unattended registry/profile error.", "false")
            },
            new[]
            {
                new StepOutputContract("session", "TwinCatEngineeringSession", "Live DTE session stored in pipeline state.")
            },
            new[] { "Read back DTE version or solution state to confirm the COM server is reachable.", "For unattended runs, inspect targetProcessIds and autoDismissedDialogs outputs to confirm which host was monitored and whether launch/session modal dialogs were dismissed." }),

        new StepContract(
            "engineering.cleanup-dte-host-processes",
            "TwinCatEngineeringService.CleanupDteHostProcesses",
            "engineering",
            "Lists or explicitly kills unattended Visual Studio/TcXaeShell host processes that can block DTE automation.",
            new[] { "Use dry-run first.", "Default matching only targets host processes without a main window title; windowed IDEs require IncludeWindowed=true or explicit ProcessIds." },
            new[]
            {
                new StepParameterContract("ProcessNames", "string[]", false, "Process names to inspect, separated by ';' or '|'.", "devenv;TcXaeShell"),
                new StepParameterContract("ProcessIds", "int[]", false, "Optional explicit process ids to match."),
                new StepParameterContract("DryRun", "bool", false, "Whether to only report candidates without killing them.", "true"),
                new StepParameterContract("IncludeWindowed", "bool", false, "Whether processes with a main window title are also candidates.", "false"),
                new StepParameterContract("KillProcessTree", "bool", false, "Whether to kill the full process tree for matched processes.", "true")
            },
            new[]
            {
                new StepOutputContract("matchedCount", "int", "Number of candidate processes."),
                new StepOutputContract("killedCount", "int", "Number of processes killed when DryRun=false."),
                new StepOutputContract("processesJson", "json", "Per-process match/cleanup details.")
            },
            new[] { "Run before unattended DTE launch if previous tests left headless devenv/TcXaeShell processes; follow with engineering.launch-visual-studio." }),

        new StepContract(
            "engineering.create-xae-solution",
            "TwinCatEngineeringService.CreateTwinCatSolution",
            "engineering",
            "Creates a fresh TwinCAT XAE solution and binds the automation session to its ITcSysManager root.",
            new[] { "An active DTE session must exist.", "At least one default TwinCAT XAE template path must exist on the machine." },
            new[]
            {
                new StepParameterContract("SolutionDirectory", "string", true, "Directory where the solution should be created."),
                new StepParameterContract("SolutionName", "string", true, "Visual Studio solution name."),
                new StepParameterContract("ProjectName", "string", true, "TwinCAT project name.")
            },
            new[]
            {
                new StepOutputContract("solutionPath", "string", "Absolute .sln path."),
                new StepOutputContract("projectPath", "string", "Absolute .tsproj path.")
            },
            new[] { "Check that the .sln and .tsproj files exist and that ITcSysManager can be retrieved." }),

        new StepContract(
            "engineering.open-xae-solution",
            "TwinCatEngineeringService.OpenTwinCatSolution",
            "engineering",
            "Re-opens an existing TwinCAT solution and re-attaches COM references after .tsproj file mutations.",
            new[] { "An active DTE session must exist.", "The target .sln and .tsproj must already exist." },
            new[]
            {
                new StepParameterContract("SolutionPath", "string", true, "Absolute .sln path."),
                new StepParameterContract("ProjectPath", "string", true, "Absolute .tsproj path.")
            },
            new[]
            {
                new StepOutputContract("solutionPath", "string", "Absolute .sln path."),
                new StepOutputContract("projectPath", "string", "Absolute .tsproj path.")
            },
            new[] { "Confirm that the project can be found again in the DTE solution model." }),

        new StepContract(
            "engineering.create-cpp-project",
            "TwinCatEngineeringService.CreateCppProject",
            "engineering",
            "Creates a TwinCAT C++ project beneath TIXC using a specified Beckhoff wizard id.",
            new[] { "A bound TwinCAT engineering session must exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Requested C++ project name."),
                new StepParameterContract("WizardId", "string", false, "Wizard identifier for the project template.", "TcVersionedDriverWizard")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the C++ project."),
                new StepOutputContract("projectFilePath", "string", "Expected .vcxproj path.")
            },
            new[] { "Verify the TIXC child exists and the .vcxproj file lands on disk." }),

        new StepContract(
            "engineering.create-vs-cpp-project",
            "TwinCatEngineeringService.CreateVisualStudioCppProject",
            "engineering",
            "Creates a regular Visual Studio C++ project inside the current solution, such as an AdsClient console application.",
            new[] { "A solution must already be loaded in the DTE session.", "An installed Visual Studio C++ template or explicit AllowTemplateFallback=true is required." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Visual Studio C++ project name."),
                new StepParameterContract("ProjectDirectory", "string", false, "Optional project directory. Defaults to SolutionDirectory/ProjectName."),
                new StepParameterContract("TemplateKind", "string", false, "Template semantic kind. P0 supports ConsoleApplication.", "ConsoleApplication"),
                new StepParameterContract("CandidateTemplatePaths", "string[]", false, "Optional explicit template paths for machine-specific VS installs."),
                new StepParameterContract("PlatformToolset", "string", false, "Optional PlatformToolset value such as v143."),
                new StepParameterContract("AllowTemplateFallback", "bool", false, "Whether the service may synthesize a minimal MSBuild C++ project if no installed template is found.", "false")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Absolute .vcxproj path."),
                new StepOutputContract("projectGuid", "string", "Project GUID registered in the .vcxproj/.sln."),
                new StepOutputContract("projectDirectory", "string", "Absolute project directory.")
            },
            new[] { "Verify the .sln contains the project, .vcxproj exists, DTE can find it by name, and reopen keeps it visible." }),

        new StepContract(
            "engineering.create-scope-project",
            "TwinCatEngineeringService.CreateScopeProject",
            "engineering",
            "Creates a TwinCAT Scope project skeleton inside the current solution without copying an existing Scope project file.",
            new[] { "A solution must already be loaded in the DTE session." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Scope project name."),
                new StepParameterContract("ProjectDirectory", "string", false, "Optional project directory. Defaults to SolutionDirectory/ProjectName."),
                new StepParameterContract("ConfigurationFileName", "string", false, "Optional .tcscopex file name to include.", "<ProjectName>.tcscopex"),
                new StepParameterContract("CreateEmptyConfiguration", "bool", false, "Whether to create a minimal empty .tcscopex configuration file.", "true"),
                new StepParameterContract("AllowSolutionFileFallback", "bool", false, "Whether to write a typed .sln project entry if DTE AddFromFile does not accept the .tcmproj.", "true")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Absolute .tcmproj path."),
                new StepOutputContract("projectGuid", "string", "Project GUID registered in the .tcmproj/.sln."),
                new StepOutputContract("configurationFilePath", "string", "Optional generated .tcscopex path."),
                new StepOutputContract("usedSolutionFileFallback", "bool", "Whether the .sln entry fallback path was used.")
            },
            new[] { "Verify the .tcmproj exists, optional .tcscopex exists, and the saved .sln contains the Scope project GUID and relative path." }),

        new StepContract(
            "scope.ensure-configuration",
            "TwinCatScopeConfigurationService.EnsureConfiguration",
            "scope",
            "Creates or updates a TwinCAT Scope .tcscopex configuration from typed chart, channel, and ADS symbol definitions.",
            new[] { "The Scope project directory must exist or be creatable.", "Callers must provide typed JSON or explicit fields; do not pass copied .tcscopex XML." },
            new[]
            {
                new StepParameterContract("ConfigurationFilePath", "string", true, "Absolute .tcscopex file path to create or update."),
                new StepParameterContract("ScopeName", "string", false, "Scope display name.", "Scope Project"),
                new StepParameterContract("MainServer", "string", false, "Scope main server AMS NetId.", "127.0.0.1.1.1"),
                new StepParameterContract("RecordTime", "long", false, "Scope record time in 100 ns units.", "6000000000"),
                new StepParameterContract("StopMode", "string", false, "Scope stop mode.", "AutoStop"),
                new StepParameterContract("ChartName", "string", false, "YT chart name.", "YT Chart"),
                new StepParameterContract("ReplaceChannels", "bool", false, "Whether existing ADS acquisitions and chart channels should be replaced.", "false"),
                new StepParameterContract("AdsChannels", "ScopeAdsChannelDefinition[]", false, "Typed ADS acquisition channel definitions."),
                new StepParameterContract("ChartChannels", "ScopeChartChannelDefinition[]", false, "Typed YT chart channel definitions.")
            },
            new[]
            {
                new StepOutputContract("configurationFilePath", "string", "Generated .tcscopex path."),
                new StepOutputContract("adsChannelCount", "int", "Number of ADS acquisition channels in the resulting file."),
                new StepOutputContract("chartChannelCount", "int", "Number of chart channels in the resulting file.")
            },
            new[] { "Re-read the .tcscopex file and assert the requested ADS symbol channels and chart channels exist; do not compare against a copied sample file." }),

        new StepContract(
            "scope.assert-configuration-shape",
            "TwinCatScopeConfigurationService.AssertConfigurationShape",
            "scope",
            "Reads a TwinCAT Scope .tcscopex configuration and asserts typed channel/chart shape without mutating it.",
            new[] { "The .tcscopex file must exist." },
            new[]
            {
                new StepParameterContract("ConfigurationFilePath", "string", true, "Absolute .tcscopex file path to inspect."),
                new StepParameterContract("ExpectedScopeName", "string", false, "Expected Scope display name."),
                new StepParameterContract("ExpectedChartName", "string", false, "Expected YT chart name."),
                new StepParameterContract("ExpectedAdsChannelCount", "int", false, "Expected ADS acquisition channel count."),
                new StepParameterContract("ExpectedChartChannelCount", "int", false, "Expected chart channel count."),
                new StepParameterContract("AdsChannels", "ScopeConfigurationChannelShape[]", false, "Expected ADS channel names and optional SymbolName values."),
                new StepParameterContract("ChartChannels", "ScopeConfigurationChannelShape[]", false, "Expected chart channel names and optional acquisition names.")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether every requested shape condition matched."),
                new StepOutputContract("adsChannelCount", "int", "Observed ADS acquisition channel count."),
                new StepOutputContract("chartChannelCount", "int", "Observed chart channel count."),
                new StepOutputContract("shapeJson", "json", "Observed shape and error details.")
            },
            new[] { "Use after scope.ensure-configuration or after XAE reopen/save to prove the generated channels were retained." }),

        new StepContract(
            "engineering.ensure-solution-project-dependency",
            "TwinCatEngineeringService.EnsureSolutionProjectDependency",
            "engineering",
            "Ensures a Visual Studio solution ProjectDependencies entry exists from one project to another.",
            new[] { "Both projects must already exist in the saved .sln." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Dependent project name."),
                new StepParameterContract("DependsOnProjectName", "string", true, "Project that must build before ProjectName.")
            },
            new[]
            {
                new StepOutputContract("projectGuid", "string", "Dependent project GUID."),
                new StepOutputContract("dependsOnProjectGuid", "string", "Dependency project GUID.")
            },
            new[] { "Inspect .sln ProjectSection(ProjectDependencies) and reload the solution to confirm the dependency remains." }),

        new StepContract(
            "engineering.create-io-device",
            "TwinCatEngineeringService.CreateIoDevice",
            "engineering",
            "Creates an IO device under a TwinCAT tree parent through ITcSmTreeItem.CreateChild.",
            new[] { "A bound XAE solution must be open.", "This is an engineering COM/XAE operation; use command timeouts and unattended dialog auto-dismiss for headless runs.", "For an EtherCAT master use ParentTreeItemPath=TIID and SubType=111." },
            new[]
            {
                new StepParameterContract("Name", "string", true, "Device display name to create or find."),
                new StepParameterContract("SubType", "int", false, "TwinCAT CreateChild subtype, for example 111 for an EtherCAT master.", "111"),
                new StepParameterContract("ParentTreeItemPath", "string", false, "TwinCAT tree parent path.", "TIID"),
                new StepParameterContract("Before", "string", false, "Optional sibling name/path passed to CreateChild before insertion."),
                new StepParameterContract("VInfo", "string", false, "Optional CreateChild vInfo payload for device-specific identity fields."),
                new StepParameterContract("Disabled", "bool?", false, "Optional Disabled state to apply after creation or lookup."),
                new StepParameterContract("AllowExisting", "bool", false, "Whether an existing child at ParentTreeItemPath^Name is accepted instead of failing.", "true"),
                new StepParameterContract("PostCreateDelayMs", "int", false, "Delay after CreateChild so XAE can persist generated IO metadata.", "500")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "Created or existing tree item path."),
                new StepOutputContract("displayName", "string", "Resolved TwinCAT display name."),
                new StepOutputContract("objectId", "string", "Tree item ObjectId when XAE exposes one.")
            },
            new[] { "Export or describe TIID after SaveAll; the created Device should appear without copying a Project/Io XML section." }),

        new StepContract(
            "engineering.create-ethercat-box",
            "TwinCatEngineeringService.CreateEthercatBox",
            "engineering",
            "Creates an EtherCAT box or terminal under an EtherCAT parent through ITcSmTreeItem.CreateChild.",
            new[] { "An EtherCAT device or parent box path must already exist.", "Use subtype 9099 for E-Bus terminals/boxes whose product identity comes from ProductRevision/vInfo.", "This step asks XAE/ESI to generate box metadata; it must not be replaced by copied sample XML." },
            new[]
            {
                new StepParameterContract("ParentTreeItemPath", "string", true, "TwinCAT parent path, for example TIID^Device 3 (EtherCAT)."),
                new StepParameterContract("Name", "string", true, "Box or terminal display name to create or find."),
                new StepParameterContract("SubType", "int", false, "TwinCAT CreateChild subtype. 9099 is the Automation Interface subtype for product-revision based EtherCAT boxes.", "9099"),
                new StepParameterContract("Before", "string", false, "Optional sibling name/path passed to CreateChild before insertion."),
                new StepParameterContract("ProductRevision", "string", false, "Product/revision identity passed as vInfo when VInfo is not set, for example EK1100-0000-0017."),
                new StepParameterContract("VInfo", "string", false, "Optional raw CreateChild vInfo string when Beckhoff documents a different identity payload."),
                new StepParameterContract("Disabled", "bool?", false, "Optional Disabled state to apply after creation or lookup."),
                new StepParameterContract("AllowExisting", "bool", false, "Whether an existing child at ParentTreeItemPath^Name is accepted instead of failing.", "true"),
                new StepParameterContract("PostCreateDelayMs", "int", false, "Delay after CreateChild so XAE can generate PDO/SyncMan/FMMU metadata.", "500")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "Created or existing tree item path."),
                new StepOutputContract("displayName", "string", "Resolved TwinCAT display name."),
                new StepOutputContract("objectId", "string", "Tree item ObjectId when XAE exposes one.")
            },
            new[] { "Export or describe the EtherCAT tree after SaveAll; PDO/process-image metadata should come from XAE/ESI generation rather than a copied .tsproj fragment." }),

        new StepContract(
            "engineering.generate-io-mappings",
            "TwinCatEngineeringService.GenerateIoMappings",
            "engineering",
            "Invokes TwinCAT GenerateMappings so XAE can rebuild variable mappings from the current IO tree.",
            new[] { "A bound XAE solution with IO topology must be open.", "The step first requires ITcSmCommands.GenerateMappings on the current SysManager COM object.", "DTE command fallback is disabled by default because menu commands can show interactive prompts." },
            new[]
            {
                new StepParameterContract("SuppressUi", "bool", false, "Whether DTE.SuppressUI should be set before invoking the command.", "true"),
                new StepParameterContract("AllowDteCommandFallback", "bool", false, "Whether to try TwinCAT GenerateMappings DTE commands if ITcSmCommands is not available.", "false"),
                new StepParameterContract("TimeoutMs", "int", false, "Maximum time allowed for the GenerateMappings operation.", "120000")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether GenerateMappings completed."),
                new StepOutputContract("command", "string", "The COM or DTE command path that completed."),
                new StepOutputContract("attemptedCommands", "string", "Semicolon-separated attempted command paths.")
            },
            new[] { "Save and describe/compare the IO topology and root Mappings after this step; do not treat command success alone as IO parity." }),

        new StepContract(
            "engineering.search-io-devices",
            "TwinCatEngineeringService.SearchIoDevices",
            "engineering",
            "Invokes TwinCAT SearchDevices through ITcSmCommands so XAE can scan local IO devices without using menu commands.",
            new[] { "A bound XAE solution must be open.", "This operation can change the IO tree and can depend on local hardware/driver state.", "No DTE menu fallback is provided; unattended runs must rely on the COM command, timeout, suppress-ui, and dialog auto-dismiss." },
            new[]
            {
                new StepParameterContract("SuppressUi", "bool", false, "Whether DTE.SuppressUI should be set before invoking the command.", "true"),
                new StepParameterContract("TimeoutMs", "int", false, "Maximum time allowed for SearchDevices.", "120000")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether SearchDevices completed."),
                new StepOutputContract("command", "string", "The COM command path that completed."),
                new StepOutputContract("attemptedCommands", "string", "Semicolon-separated attempted command paths.")
            },
            new[] { "Save, export TIID, and compare/describe IO topology after this step. A successful scan command is not enough to claim OptCNC IO parity." }),

        new StepContract(
            "engineering.reload-io-devices",
            "TwinCatEngineeringService.ReloadIoDevices",
            "engineering",
            "Invokes TwinCAT ReloadDevices through ITcSmCommands so XAE can reload IO device metadata without using menu commands.",
            new[] { "A bound XAE solution with IO devices must be open.", "This operation can regenerate IO metadata from installed device descriptions.", "No DTE menu fallback is provided; unattended runs must rely on the COM command, timeout, suppress-ui, and dialog auto-dismiss." },
            new[]
            {
                new StepParameterContract("SuppressUi", "bool", false, "Whether DTE.SuppressUI should be set before invoking the command.", "true"),
                new StepParameterContract("TimeoutMs", "int", false, "Maximum time allowed for ReloadDevices.", "120000")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether ReloadDevices completed."),
                new StepOutputContract("command", "string", "The COM command path that completed."),
                new StepOutputContract("attemptedCommands", "string", "Semicolon-separated attempted command paths.")
            },
            new[] { "Save, export TIID, and compare/describe IO topology after this step. Reopen with XAE or run topology guards before activation." }),

        new StepContract(
            "engineering.apply-io-tree-plan",
            "TwinCatEngineeringService.ApplyIoTreePlan",
            "engineering",
            "Applies a batch IO tree payload by orchestrating engineering.create-io-device and engineering.create-ethercat-box operations.",
            new[] { "A bound XAE solution must be open.", "The payload is a convenience wrapper around CreateChild operations only; it must not contain .tsproj XML metadata.", "Use this for large IO trees, then save/export/describe topology to prove what XAE generated." },
            new[]
            {
                new StepParameterContract("Devices", "IReadOnlyList<CreateIoDeviceRequest>", false, "IO device CreateChild requests to apply in order."),
                new StepParameterContract("Boxes", "IReadOnlyList<CreateEthercatBoxRequest>", false, "EtherCAT box/terminal CreateChild requests to apply in order.")
            },
            new[]
            {
                new StepOutputContract("deviceCount", "int", "Number of device requests applied."),
                new StepOutputContract("boxCount", "int", "Number of box requests applied."),
                new StepOutputContract("treeItemPaths", "string", "Semicolon-separated tree paths returned by XAE."),
                new StepOutputContract("nodesJson", "json", "Serialized node info returned by the underlying CreateChild operations.")
            },
            new[] { "Run tsproj.describe-io-topology or export TIID after SaveAll; this wrapper does not prove PDO/process-image parity by itself." }),

        new StepContract(
            "ethercat.assert-product-revisions",
            "TwinCatEtherCatDeviceDescriptionService.AssertProductRevisions",
            "ethercat",
            "Asserts that EtherCAT productRevision strings used for CreateChild are present in the installed Beckhoff ESI/device-description XML files.",
            new[] { "The machine must have Beckhoff EtherCAT device description XML files installed.", "This step is file-only; it does not launch XAE and does not mutate a .tsproj.", "Use it before engineering.create-ethercat-box or engineering.apply-io-tree-plan when a JSON plan depends on productRevision/vInfo values." },
            new[]
            {
                new StepParameterContract("ProductRevisions", "IReadOnlyList<string>", false, "ProductRevision/vInfo values to verify, such as EK1100-0000-0018 or AX5125-0000-0214."),
                new StepParameterContract("Items", "IReadOnlyList<EtherCatProductRevisionRequirement>", false, "Structured productRevision checks with optional ProductCode and RevisionNo constraints."),
                new StepParameterContract("SearchDirectories", "IReadOnlyList<string>", false, "Optional EtherCAT ESI XML directories. Defaults to Beckhoff TwinCAT install/program-data locations."),
                new StepParameterContract("IncludeHiddenTypes", "bool", false, "Whether HideType elements should count as matches.", "false")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether all requested product revisions were found."),
                new StepOutputContract("requestedCount", "int", "Number of requested product revisions."),
                new StepOutputContract("matchedCount", "int", "Number of matched product revisions."),
                new StepOutputContract("missingCount", "int", "Number of missing product revisions."),
                new StepOutputContract("scannedFileCount", "int", "Number of ESI XML files scanned."),
                new StepOutputContract("assertionsJson", "json", "Per-product match result including ProductCode, RevisionNo and source file.")
            },
            new[] { "Run before XAE CreateChild IO tree steps; a match proves the local ESI catalog contains the requested productRevision string, but final IO topology still needs XAE tree and .tsproj guards." }),

        new StepContract(
            "engineering.create-plc-project",
            "TwinCatEngineeringService.CreatePlcProject",
            "engineering",
            "Creates a PLC project using the first compatible Beckhoff PLC template available on the machine.",
            new[] { "A bound TwinCAT engineering session must exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "PLC project name."),
                new StepParameterContract("AllowOfflineFallback", "bool", false, "Whether CreatePlcProject may synthesize a minimal PLC project file mutation when COM/template creation fails.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the PLC project."),
                new StepOutputContract("projectFilePath", "string", "Expected .plcproj path.")
            },
            new[] { "Check that the PLC node exists and that the .plcproj file was created." }),

        new StepContract(
            "engineering.create-module",
            "TwinCatEngineeringService.CreateModule",
            "engineering",
            "Creates a module class inside a TwinCAT C++ project via the module wizard id.",
            new[] { "A TwinCAT C++ project must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Owning C++ project name."),
                new StepParameterContract("ModuleName", "string", true, "Requested module class name."),
                new StepParameterContract("WizardId", "string", true, "Beckhoff module wizard id.", "TcModuleClassWizard"),
                new StepParameterContract("AllowOfflineFallback", "bool", false, "Whether CreateModule may patch module skeleton artifacts when module wizard automation fails.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the created module node."),
                new StepOutputContract("moduleCppPath", "string", "Expected .cpp path for the module.")
            },
            new[] { "Confirm that the module files exist and the tree node is visible after save/refresh." }),

        new StepContract(
            "engineering.publish-modules",
            "TwinCatEngineeringService.PublishModules",
            "engineering",
            "Invokes the TwinCAT C++ project PublishModules method so updated module source regenerates TMC metadata.",
            new[] { "The target TwinCAT C++ project must exist and expose PublishModules in its tree XML." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "TwinCAT C++ project name."),
                new StepParameterContract("PostPublishDelayMs", "int", false, "Delay after triggering PublishModules.", "5000"),
                new StepParameterContract("WaitForUpdatedTmcTimeoutMs", "int", false, "Maximum wait for the project .tmc timestamp to update.", "30000"),
                new StepParameterContract("RunTmcCodeGeneratorFirst", "bool", false, "Whether to invoke StartTmcCodeGenerator before PublishModules.", "false")
            },
            new[]
            {
                new StepOutputContract("updatedTmcPath", "string", "Project .tmc path observed after publish."),
                new StepOutputContract("succeeded", "bool", "Whether publish left a readable project .tmc."),
                new StepOutputContract("updated", "bool", "Whether the .tmc timestamp or content changed during this publish call.")
            },
            new[] { "Check that the .tmc is readable and contains the expected module classes; updated=true means the timestamp or content changed during this publish call." }),

        new StepContract(
            "engineering.start-tmc-code-generator",
            "TwinCatEngineeringService.StartTmcCodeGenerator",
            "engineering",
            "Invokes the TwinCAT C++ project StartTmcCodeGenerator method so source annotations regenerate TMC metadata.",
            new[] { "The target TwinCAT C++ project must exist and expose StartTmcCodeGenerator in its tree XML." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "TwinCAT C++ project name."),
                new StepParameterContract("PostStartDelayMs", "int", false, "Delay after triggering StartTmcCodeGenerator.", "500"),
                new StepParameterContract("WaitForUpdatedTmcTimeoutMs", "int", false, "Maximum wait for the project .tmc timestamp to update.", "30000")
            },
            new[]
            {
                new StepOutputContract("updatedTmcPath", "string", "Project .tmc path observed after code generation."),
                new StepOutputContract("succeeded", "bool", "Whether code generation left a readable project .tmc."),
                new StepOutputContract("updated", "bool", "Whether the .tmc timestamp or content changed during this code generation call.")
            },
            new[] { "Check that the .tmc is readable and contains source-derived DataAreas, Parameters, Interfaces, and module classes; updated=false can still be acceptable if content was already current." }),

        new StepContract(
            "engineering.verify-tmc-data-areas",
            "TwinCatEngineeringService.VerifyTmcDataAreas",
            "engineering",
            "Reads a TwinCAT C++ .tmc file and verifies expected module DataAreas and symbols before instances are created.",
            new[] { "Run engineering.start-tmc-code-generator and engineering.publish-modules first when source annotations were just written." },
            new[]
            {
                new StepParameterContract("ProjectTmcPath", "string", true, "Absolute path to the project .tmc file."),
                new StepParameterContract("Modules", "IReadOnlyList<TmcModuleExpectation>", true, "Expected module names, DataAreas, AreaTypes, and symbols."),
                new StepParameterContract("FailOnUnexpectedModule", "bool", false, "Whether modules not listed in Modules should fail verification.", "false")
            },
            new[]
            {
                new StepOutputContract("projectTmcPath", "string", "Verified project .tmc path."),
                new StepOutputContract("expectedModuleCount", "int", "Expected module count from the request."),
                new StepOutputContract("matchedModuleCount", "int", "Number of expected modules found."),
                new StepOutputContract("errorsJson", "json", "Detailed mismatch list when verification fails.")
            },
            new[] { "Use this after C++ code generation and before add-module-instance; a fallback skeleton TMC with Input/DataIn and Output/DataOut should fail this step." }),

        new StepContract(
            "engineering.apply-tmc-module-model",
            "TwinCatEngineeringService.ApplyTmcModuleModel",
            "engineering",
            "Applies a structured JSON module model to a TwinCAT C++ project .tmc without copying a whole known-good TMC file.",
            new[] { "The target project .tmc must already exist.", "GeneratedServicesHeaderPath should point at the project Services.h; GeneratedHeaderPaths can add companion headers such as Interfaces.h for custom interface GUIDs." },
            new[]
            {
                new StepParameterContract("ProjectTmcPath", "string", true, "Absolute path to the project .tmc file."),
                new StepParameterContract("ProjectName", "string", true, "TwinCAT C++ project / class factory name."),
                new StepParameterContract("Modules", "IReadOnlyList<TmcModuleModel>", true, "Structured module definitions containing GUIDs, interfaces, parameters, DataAreas, interface pointers, data pointers, and event classes."),
                new StepParameterContract("GeneratedServicesHeaderPath", "string", false, "Optional generated Services.h path used to resolve custom type GUIDs and DataTypes."),
                new StepParameterContract("GeneratedHeaderPaths", "IReadOnlyList<string>", false, "Optional additional generated headers used to resolve companion custom type or interface GUIDs."),
                new StepParameterContract("LibraryName", "string", false, "Optional Library/Name override. Defaults to ProjectName."),
                new StepParameterContract("LibraryVersion", "string", false, "Library/Version value.", "0.0.0.1"),
                new StepParameterContract("RemoveUnexpectedModules", "bool", false, "Whether module entries not present in Modules should be removed.", "false"),
                new StepParameterContract("ReplaceDataTypesFromGeneratedHeader", "bool", false, "Whether generated header DataTypes should replace the .tmc DataTypes section.", "true")
            },
            new[]
            {
                new StepOutputContract("projectTmcPath", "string", "Mutated project .tmc path."),
                new StepOutputContract("moduleCount", "int", "Number of module models applied.")
            },
            new[] { "Run engineering.verify-tmc-data-areas afterwards and inspect the .tmc DataTypes/Modules sections; this step must not receive raw module XML fragments." }),

        new StepContract(
            "engineering.add-module-instance",
            "TwinCatEngineeringService.AddModuleInstance",
            "engineering",
            "Adds a TcCOM instance from a project TMC by resolving the module GUID and calling CreateChild on the project node.",
            new[] { "The target C++ project and its .tmc file must exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Owning C++ project name."),
                new StepParameterContract("ProjectTmcPath", "string", true, "Absolute path to the project TMC."),
                new StepParameterContract("InstanceBaseName", "string", true, "Base name to use when creating the instance."),
                new StepParameterContract("ModuleClassName", "string", false, "Optional module class name when the TMC contains multiple classes."),
                new StepParameterContract("AllowOfflineFallback", "bool", false, "Whether AddModuleInstance may inject a deterministic instance skeleton through .tsproj file mutation when COM CreateChild fails.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the instance."),
                new StepOutputContract("objectId", "string", "Resolved instance ObjectId / OTCID.")
            },
            new[] { "Export the instance XML and confirm that the expected CLSID and Name are present." }),

        new StepContract(
            "engineering.ensure-task",
            "TwinCatEngineeringService.EnsureTask",
            "engineering",
            "Creates or reuses a Task under TIRT and normalizes its timing, priority, and AMS port.",
            new[] { "A bound TwinCAT engineering session must exist." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("TaskSubtype", "int", true, "TwinCAT task subtype passed to CreateChild."),
                new StepParameterContract("Priority", "int", true, "Task priority."),
                new StepParameterContract("CycleTimeUs", "int", true, "Task cycle time in microseconds."),
                new StepParameterContract("AmsPort", "int", true, "Assigned AMS port."),
                new StepParameterContract("IoAtBegin", "bool", false, "Optional TaskDef IoAtBegin setting.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the task."),
                new StepOutputContract("objectId", "string", "Task OTCID / ObjectId.")
            },
            new[] { "Read back ProduceXml(true) and confirm TaskDef/Context values were updated." }),

        new StepContract(
            "engineering.export-tree-item-xml",
            "TwinCatEngineeringService.ExportTreeItemXml",
            "engineering",
            "Exports ProduceXml output for any TwinCAT tree node as durable evidence.",
            new[] { "A bound TwinCAT engineering session must exist.", "The target tree path must be valid." },
            new[]
            {
                new StepParameterContract("TreeItemPath", "string", true, "TwinCAT tree path such as TIXC^MyProject."),
                new StepParameterContract("DestinationPath", "string", true, "Absolute output file path."),
                new StepParameterContract("Recursive", "bool", false, "Whether ProduceXml(true) should be used.", "false")
            },
            new[]
            {
                new StepOutputContract("xmlPath", "string", "Saved XML evidence file.")
            },
            new[] { "Re-open the XML file and verify it contains the expected node metadata." }),

        new StepContract(
            "engineering.save-all",
            "TwinCatEngineeringService.SaveAll",
            "engineering",
            "Flushes pending Visual Studio document and solution changes before build, reopen, or .tsproj file mutation steps.",
            new[] { "A live DTE session must exist." },
            Array.Empty<StepParameterContract>(),
            Array.Empty<StepOutputContract>(),
            new[] { "Re-check file timestamps or solution dirtiness after the command." }),

        new StepContract(
            "engineering.close-visual-studio",
            "TwinCatEngineeringService.CloseVisualStudio",
            "engineering",
            "Closes the current Visual Studio DTE session, optionally issuing SaveAll first.",
            new[] { "A live DTE session must exist." },
            new[]
            {
                new StepParameterContract("SaveBeforeClose", "bool", false, "Whether SaveAll should run before closing the session.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Confirm that the DTE process exits and the session object is no longer usable." }),

        new StepContract(
            "engineering.build-solution",
            "TwinCatEngineeringService.BuildCurrentSolution",
            "engineering",
            "Builds the loaded solution through DTE, an unattended devenv.com command-line build, or an MSBuild project sequence.",
            new[] { "DTE engine requires a loaded solution.", "CommandLine engine requires a solution path and installed Visual Studio/XAE command-line build support.", "MsBuildProjects engine requires C++ project paths that can be built outside the XAE solution shell." },
            new[]
            {
                new StepParameterContract("TimeoutMs", "int", false, "Maximum build wait time.", "300000"),
                new StepParameterContract("BuildEngine", "BuildSolutionEngine", false, "Build backend: Dte, CommandLine, or MsBuildProjects.", "Dte"),
                new StepParameterContract("Configuration", "string", false, "Solution configuration for command-line build.", "Release"),
                new StepParameterContract("Platform", "string", false, "Solution platform for command-line build.", "TwinCAT OS (x64)"),
                new StepParameterContract("DevenvPath", "string", false, "Optional explicit devenv.com path for command-line build."),
                new StepParameterContract("MsBuildPath", "string", false, "Optional explicit MSBuild.exe path for MSBuildProjects engine."),
                new StepParameterContract("ProjectPaths", "string[]", false, "Semicolon-separated C++ project path sequence for MSBuildProjects engine. Relative paths are resolved from the solution directory.", "OptcncTwinCAT\\Ruckig\\Ruckig.vcxproj;OptcncTwinCAT\\Tinyxml2\\Tinyxml2.vcxproj;OptcncTwinCAT\\MotionControl\\MotionControl.vcxproj"),
                new StepParameterContract("LogFilePath", "string", false, "Optional devenv /Out log destination.")
            },
            new[]
            {
                new StepOutputContract("lastBuildInfo", "int", "DTE LastBuildInfo value, or command-line exit code for CommandLine engine."),
                new StepOutputContract("buildEngine", "string", "Build backend used."),
                new StepOutputContract("exitCode", "int", "Process exit code when CommandLine engine is used."),
                new StepOutputContract("logFilePath", "string", "Build log path when available.")
            },
            new[] { "Treat LastBuildInfo == 0 or process exit code 0 as the engineering success condition.", "For unattended runs, CommandLine and MsBuildProjects avoid Visual Studio confirmation dialogs blocking DTE automation." }),

        new StepContract(
            "cpp.create-project-item",
            "TwinCatEngineeringService.CreateCppProjectItem",
            "cpp",
            "Creates a C++/header/resource/None project item, optionally creating the physical file and registering it in .vcxproj and .filters.",
            new[] { "The target C++ .vcxproj must already exist.", "RelativePath must be inside the project directory." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ItemType", "CppProjectItemType", false, "MSBuild item type, or Infer from extension.", "Infer"),
                new StepParameterContract("Filter", "string", false, "Optional .vcxproj.filters display filter."),
                new StepParameterContract("AddToProject", "bool", false, "Whether to register the item in .vcxproj.", "true"),
                new StepParameterContract("CreatePhysicalFile", "bool", false, "Whether to create an empty physical file.", "true"),
                new StepParameterContract("ConflictPolicy", "ProjectItemConflictPolicy", false, "FailIfExists, KeepExisting, or ReplaceProjectRegistration.", "FailIfExists"),
                new StepParameterContract("AllowMsBuildFallback", "bool", false, "Allow typed MSBuild XML update when DTE ProjectItems is not stable.", "true")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("filePath", "string", "Physical project item path."),
                new StepOutputContract("itemType", "CppProjectItemType", "Resolved MSBuild item type."),
                new StepOutputContract("addedToProject", "bool", "Whether .vcxproj registration exists after the step.")
            },
            new[] { "Verify the file exists, .vcxproj contains the requested Include, .filters contains filter mapping when requested, and reopen shows the item." }),

        new StepContract(
            "cpp.write-project-item-content",
            "TwinCatEngineeringService.WriteCppProjectItemContent",
            "cpp",
            "Writes source/resource/text payload into an existing C++ project item file and returns its content hash.",
            new[] { "The target C++ project must already exist.", "ContentText and ContentFile are mutually exclusive." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ContentText", "string", false, "Inline source payload."),
                new StepParameterContract("ContentFile", "string", false, "Payload file path produced by the JSON plan files[] section or another explicit caller-owned payload."),
                new StepParameterContract("Encoding", "string", false, "utf-8, utf-8-bom, or ascii.", "utf-8"),
                new StepParameterContract("NewLine", "string", false, "preserve, crlf, or lf.", "preserve"),
                new StepParameterContract("WritePolicy", "ProjectItemWritePolicy", false, "FailIfMissing, FailIfNonEmpty, or Overwrite.", "Overwrite"),
                new StepParameterContract("RequireProjectRegistration", "bool", false, "Fail if .vcxproj does not already register the item.", "false")
            },
            new[]
            {
                new StepOutputContract("filePath", "string", "Written file path."),
                new StepOutputContract("sha256", "string", "SHA256 hash of the written bytes."),
                new StepOutputContract("bytesWritten", "long", "Number of bytes written.")
            },
            new[] { "Compare the output hash with the payload and reopen the item in VS." }),

        new StepContract(
            "cpp.remove-project-item",
            "TwinCatEngineeringService.RemoveCppProjectItem",
            "cpp",
            "Removes a C++ project item registration and optional filter entry and physical file.",
            new[] { "The target C++ .vcxproj must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ItemType", "CppProjectItemType", false, "MSBuild item type, or Infer from extension.", "Infer"),
                new StepParameterContract("DeletePhysicalFile", "bool", false, "Whether to delete the physical file.", "true"),
                new StepParameterContract("RemoveFilterEntry", "bool", false, "Whether to remove the .vcxproj.filters item mapping.", "true"),
                new StepParameterContract("IgnoreMissing", "bool", false, "Treat missing item/file as success.", "false")
            },
            new[]
            {
                new StepOutputContract("removedFromProject", "bool", "Whether .vcxproj registration was removed."),
                new StepOutputContract("deletedFile", "bool", "Whether a physical file was deleted.")
            },
            new[] { "Verify .vcxproj/.filters no longer reference the item and the file is absent when DeletePhysicalFile=true." }),

        new StepContract(
            "cpp.set-project-property",
            "TwinCatEngineeringService.SetCppProjectProperty",
            "cpp",
            "Sets a project-level or configuration-level MSBuild property in a C++ .vcxproj.",
            new[] { "The target C++ .vcxproj must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("PropertyName", "string", true, "MSBuild property element name."),
                new StepParameterContract("Value", "string", true, "Property value."),
                new StepParameterContract("Condition", "string", false, "Optional PropertyGroup Condition."),
                new StepParameterContract("PropertyGroupLabel", "string", false, "Optional PropertyGroup Label, such as Globals or Configuration.")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("propertyName", "string", "Property name."),
                new StepOutputContract("condition", "string", "Applied condition, if any.")
            },
            new[] { "Inspect .vcxproj and confirm the PropertyGroup contains the requested value; build log should reflect build-affecting properties." }),

        new StepContract(
            "cpp.set-item-definition-property",
            "TwinCatEngineeringService.SetCppItemDefinitionProperty",
            "cpp",
            "Sets a C++ ItemDefinitionGroup tool property such as ClCompile include paths or Link dependencies.",
            new[] { "The target C++ .vcxproj must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("ToolName", "string", true, "Tool element name such as ClCompile, Link, ResourceCompile, or PostBuildEvent."),
                new StepParameterContract("PropertyName", "string", true, "Tool property element name."),
                new StepParameterContract("Value", "string", true, "Property value, including inherited macros when required."),
                new StepParameterContract("Condition", "string", false, "Optional ItemDefinitionGroup Condition.")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("toolName", "string", "Tool element name."),
                new StepOutputContract("propertyName", "string", "Property element name."),
                new StepOutputContract("condition", "string", "Applied condition, if any.")
            },
            new[] { "Inspect ItemDefinitionGroup XML and confirm build logs use include paths, library paths, language standard, dependencies, or events." }),

        new StepContract(
            "cpp.set-project-item-metadata",
            "TwinCatEngineeringService.SetCppProjectItemMetadata",
            "cpp",
            "Sets file-level MSBuild metadata for a C++ project item, such as PrecompiledHeader or ExcludedFromBuild.",
            new[] { "The target item must already be registered in .vcxproj." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ItemType", "CppProjectItemType", true, "MSBuild item type containing the item."),
                new StepParameterContract("MetadataName", "string", true, "Metadata element name."),
                new StepParameterContract("Value", "string", true, "Metadata value."),
                new StepParameterContract("Condition", "string", false, "Optional item Condition.")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("relativePath", "string", "Item Include path."),
                new StepOutputContract("metadataName", "string", "Metadata element name."),
                new StepOutputContract("condition", "string", "Applied condition, if any.")
            },
            new[] { "Inspect the item XML and confirm build output respects include/exclude or PCH metadata." }),

        new StepContract(
            "signing.grant-certificate",
            "TwinCatSigningService.GrantCertificate",
            "signing",
            "Grants or removes local TcSignTool authorization for a TwinCAT signing certificate.",
            new[] { "TwinCAT SDK TcSignTool.exe must be installed.", "The certificate file must exist on this machine." },
            new[]
            {
                new StepParameterContract("CertificatePath", "string", true, "Path to the .tccert certificate file."),
                new StepParameterContract("Password", "string", false, "Certificate password. Prefer CLI password-file or password-env-var input; it is never emitted in step outputs."),
                new StepParameterContract("RemoveGrant", "bool", false, "Remove the local grant instead of adding it.", "false"),
                new StepParameterContract("Quiet", "bool", false, "Pass /q to TcSignTool.", "true"),
                new StepParameterContract("ToolPath", "string", false, "Optional TcSignTool.exe override.", TwinCatPathDefaults.DefaultTcSignToolPath)
            },
            new[]
            {
                new StepOutputContract("exitCode", "int", "TcSignTool process exit code."),
                new StepOutputContract("commandLine", "string", "Redacted command line used for evidence.")
            },
            new[] { "Run signing.sign-twincat-binary and signing.verify-twincat-binary after grant to close the loop." }),

        new StepContract(
            "signing.sign-twincat-binary",
            "TwinCatSigningService.Sign",
            "signing",
            "Signs one or more built TwinCAT C++ binaries with Beckhoff TcSignTool.",
            new[] { "engineering.build-solution must have produced the target .tmx/.sys binary.", "The certificate file must exist on this machine." },
            new[]
            {
                new StepParameterContract("CertificatePath", "string", true, "Path to the .tccert certificate file."),
                new StepParameterContract("TargetPaths", "string[]", true, "One or more TwinCAT binary paths to sign."),
                new StepParameterContract("Password", "string", false, "Certificate password. Prefer CLI password-file or password-env-var input; it is piped to TcSignTool instead of placed on the process command line."),
                new StepParameterContract("Quiet", "bool", false, "Pass /q to TcSignTool.", "true"),
                new StepParameterContract("ToolPath", "string", false, "Optional TcSignTool.exe override.", TwinCatPathDefaults.DefaultTcSignToolPath)
            },
            new[]
            {
                new StepOutputContract("targetPaths", "string", "Semicolon-separated signed binary paths."),
                new StepOutputContract("exitCode", "int", "TcSignTool process exit code."),
                new StepOutputContract("commandLine", "string", "Redacted command line used for evidence.")
            },
            new[] { "Run signing.verify-twincat-binary and then activate the configuration with the signed binary." }),

        new StepContract(
            "signing.set-license",
            "TwinCatSigningService.SetLicense",
            "signing",
            "Writes TwinCAT C++ project signing license settings used by MSBuild/TcSignTool.",
            new[] { "The C++ .vcxproj file must exist.", "Run this before engineering.build-solution so the generated .tmx is signed during build." },
            new[]
            {
                new StepParameterContract("ProjectFilePath", "string", true, "Path to the C++ .vcxproj file, or resolve it from ProjectPath and CppProjectName."),
                new StepParameterContract("LicenseName", "string", false, "TwinCAT signing certificate/license name written to TcSignTwinCatCertName.", "optcnc"),
                new StepParameterContract("Password", "string", false, "TwinCAT signing certificate/license password written to TcSignTwinCatCertPW. Prefer password-file or password-env-var outside demo plans."),
                new StepParameterContract("EnableSigning", "bool", false, "Write TcSignTwinCat=true so TwinCAT MSBuild signs the output.", "true")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated C++ .vcxproj path."),
                new StepOutputContract("licenseName", "string", "Configured TwinCAT signing license name."),
                new StepOutputContract("enableSigning", "bool", "Whether TwinCAT signing is enabled."),
                new StepOutputContract("passwordWritten", "bool", "Whether a password value was written; the password itself is never emitted.")
            },
            new[] { "Inspect the C++ .vcxproj or generated TcSignTwinCatCmd.txt after build; do not expect ADS/runtime state to change until the project is rebuilt and activated." }),

        new StepContract(
            "signing.verify-twincat-binary",
            "TwinCatSigningService.Verify",
            "signing",
            "Verifies that one or more TwinCAT binaries carry a valid TcSignTool signature.",
            new[] { "The target TwinCAT binary must already exist." },
            new[]
            {
                new StepParameterContract("TargetPaths", "string[]", true, "One or more TwinCAT binary paths to verify."),
                new StepParameterContract("Quiet", "bool", false, "Pass /q to TcSignTool.", "true"),
                new StepParameterContract("ToolPath", "string", false, "Optional TcSignTool.exe override.", TwinCatPathDefaults.DefaultTcSignToolPath),
                new StepParameterContract("AllowTestModeWarning", "bool", false, "Treat TcSignTool exit code 2 as success when the output says the file has a signature but the OEM certificate is not signed by Beckhoff.", "false")
            },
            new[]
            {
                new StepOutputContract("targetPaths", "string", "Semicolon-separated verified binary paths."),
                new StepOutputContract("exitCode", "int", "TcSignTool process exit code."),
                new StepOutputContract("acceptedTestModeWarning", "bool", "Whether a TcSignTool test-mode certificate warning was accepted."),
                new StepOutputContract("commandLine", "string", "Redacted command line used for evidence.")
            },
            new[] { "Treat exit code 0 as signed/verified; for local test certificates, set AllowTestModeWarning only when test-mode activation is acceptable.", "Keep the signed binary path as activation evidence." }),

        new StepContract(
            "engineering.activate-configuration",
            "TwinCatEngineeringService.ActivateConfiguration",
            "engineering",
            "Saves the current configuration archive when possible and then activates TwinCAT via ITcSysManager; DTE command fallback is opt-in for interactive troubleshooting.",
            new[] { "A solution must be loaded and its ITcSysManager must be available." },
            new[]
            {
                new StepParameterContract("SaveConfigurationArchive", "bool", false, "Whether to attempt SaveConfiguration before activate.", "true"),
                new StepParameterContract("ConfigurationArchivePath", "string", false, "Optional override for the generated .tszip path."),
                new StepParameterContract("SuppressUi", "bool", false, "Whether DTE.SuppressUI should be enabled before activation.", "true"),
                new StepParameterContract("AllowDteCommandFallback", "bool", false, "Whether activation may fall back to DTE ExecuteCommand names that can show interactive prompts.", "false"),
                new StepParameterContract("ActivationTimeoutMs", "int", false, "Maximum wall-clock time for ActivateConfiguration plus StartRestartTwinCAT before failing unattended.", "120000")
            },
            new[]
            {
                new StepOutputContract("activationCommand", "string", "The command or fallback path used for activation.")
            },
            new[] { "Do not treat this step alone as runtime proof; follow with ADS state assertions and TcSysSrv error-window checks." }),

        new StepContract(
            "tsproj.ensure-task",
            "TwinCatTsprojMutationService.EnsureTaskDefinition",
            "tsproj",
            "Creates or updates the declarative task node inside a .tsproj generated by XAE.",
            new[] { "The target .tsproj must already exist on disk." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("Priority", "int", true, "Task priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "Task cycle time in nanoseconds."),
                new StepParameterContract("AmsPort", "int", true, "Assigned AMS port."),
                new StepParameterContract("IoAtBegin", "bool", false, "Optional TaskDef IoAtBegin setting.", "true"),
                new StepParameterContract("TaskId", "int?", false, "Optional Task Id attribute for matching a known runtime object id layout.")
            },
            new[]
            {
                new StepOutputContract("projectPath", "string", "Updated .tsproj path."),
                new StepOutputContract("taskName", "string", "Task node name."),
                new StepOutputContract("taskId", "int?", "Task Id attribute when provided."),
                new StepOutputContract("objectId", "string?", "Derived task ObjectId when TaskId is provided, for example #x02010020.")
            },
            new[] { "Re-open the .tsproj and verify the Task attributes match the requested values." }),

        new StepContract(
            "tsproj.clear-task-layout",
            "TwinCatTsprojMutationService.ClearTaskLayout",
            "tsproj",
            "Removes Vars and/or Image child nodes from a Task so task layout can be rebuilt deterministically.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("RemoveVars", "bool", false, "Whether to remove all Vars groups under the task.", "true"),
                new StepParameterContract("RemoveImage", "bool", false, "Whether to remove all Image entries under the task.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the selected child node categories were removed from the target Task." }),

        new StepContract(
            "tsproj.ensure-task-vars-group",
            "TwinCatTsprojMutationService.EnsureTaskVarsGroup",
            "tsproj",
            "Ensures a named Vars group exists under a Task with deterministic variable shape and addressing.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("GroupName", "string", true, "Vars group Name element value, for example Inputs."),
                new StepParameterContract("VarGrpType", "int", true, "Vars VarGrpType attribute value."),
                new StepParameterContract("InsertType", "int", true, "Vars InsertType attribute value."),
                new StepParameterContract("BaseVarName", "string", true, "Base text used for generated Var names."),
                new StepParameterContract("TypeName", "string", true, "TwinCAT variable type, for example REAL."),
                new StepParameterContract("Count", "int", true, "Number of Var nodes to generate."),
                new StepParameterContract("BitStride", "int", true, "Bit offset stride between generated Vars."),
                new StepParameterContract("ExternalAddressStride", "int", true, "ExternalAddress increment per generated Var after the first."),
                new StepParameterContract("FirstExternalAddress", "int", false, "Optional explicit first ExternalAddress value."),
                new StepParameterContract("StartIndex", "int", false, "Starting suffix index for generated Var names.", "1"),
                new StepParameterContract("ReplaceExistingGroup", "bool", false, "Whether to replace existing Vars groups with the same Name.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the target Task has the expected Vars group name, variable count, and field values." }),

        new StepContract(
            "tsproj.ensure-task-image",
            "TwinCatTsprojMutationService.EnsureTaskImage",
            "tsproj",
            "Ensures a Task Image entry exists with deterministic Id, size, and addressing attributes.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("ImageId", "int", false, "Task Image Id attribute value.", "1"),
                new StepParameterContract("AddressType", "int", false, "Task Image AddrType attribute value.", "1"),
                new StepParameterContract("ImageType", "int", false, "Task Image ImageType attribute value.", "1"),
                new StepParameterContract("SizeIn", "int", false, "Task Image input byte size.", "40"),
                new StepParameterContract("SizeOut", "int", false, "Task Image output byte size.", "10"),
                new StepParameterContract("ImageName", "string", false, "Task Image Name child value.", "Image"),
                new StepParameterContract("IoAtBegin", "bool", false, "Optional Task IoAtBegin attribute update."),
                new StepParameterContract("ReplaceExistingImage", "bool", false, "Whether existing Task Image nodes should be replaced.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the target Task contains the expected Image attributes and Name value." }),

        new StepContract(
            "tsproj.ensure-cpp-instance",
            "TwinCatTsprojMutationService.EnsureCppInstance",
            "tsproj",
            "Ensures a named C++ Instance node exists under Cpp/Project and carries a minimal TmcDesc skeleton.",
            new[] { "The target C++ Project must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("CppProjectName", "string", true, "C++ project node name under Cpp."),
                new StepParameterContract("InstanceName", "string", true, "C++ instance display name."),
                new StepParameterContract("ObjectId", "string", true, "Instance OTCID/ObjectId, for example #x02010010."),
                new StepParameterContract("ContextId", "int", false, "Context Id value used for lookup/create.", "1"),
                new StepParameterContract("ContextName", "string", false, "Context Name value to enforce.", "FallbackCtx"),
                new StepParameterContract("Priority", "int", false, "ManualConfig/Priority value used for skeleton.", "0"),
                new StepParameterContract("CycleTimeNs", "int", false, "ManualConfig/CycleTime value used for skeleton.", "0")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify Cpp/Project/Instance plus TmcDesc/Contexts/ParameterValues/InterfacePointerValues/DataPointerValues exist." }),

        new StepContract(
            "tsproj.ensure-plc-instance",
            "TwinCatTsprojMutationService.EnsurePlcInstance",
            "tsproj",
            "Ensures Plc/Project/Instance nodes exist for a named PLC project and instance.",
            new[] { "The target .tsproj must already exist on disk." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify Plc/Project(Name)/Instance(Name) exists for the requested names." }),

        new StepContract(
            "tsproj.bind-instance-context",
            "TwinCatTsprojMutationService.BindInstanceContext",
            "tsproj",
            "Writes Instance TmcDesc Context/ManualConfig binding with configurable context id/name and CyclicCaller behavior.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("TaskObjectId", "string", true, "Task OTCID, for example #x02010020."),
                new StepParameterContract("Priority", "int", true, "Context priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "Context cycle time in nanoseconds."),
                new StepParameterContract("ContextId", "int", false, "Context Id value used for lookup/create.", "1"),
                new StepParameterContract("ContextName", "string", false, "Optional Context Name value to enforce."),
                new StepParameterContract("IncludeCyclicCaller", "bool", false, "Whether to ensure InterfacePointerValues/CyclicCaller exists.", "true"),
                new StepParameterContract("RemoveCyclicCallerWhenExcluded", "bool", false, "Whether to remove CyclicCaller when IncludeCyclicCaller is false.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm Context Id/ManualConfig/OTCID plus optional CyclicCaller state." }),

        new StepContract(
            "tsproj.set-task-affinity",
            "TwinCatTsprojMutationService.SetTaskAffinity",
            "tsproj",
            "Sets Task Affinity and/or AdtTasks attributes for scheduler placement-sensitive workloads.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("Affinity", "string", false, "Optional TwinCAT affinity mask or list, for example 1 or 0,2. Omit to update only AdtTasks."),
                new StepParameterContract("EnableAdtTasks", "bool", false, "Whether to force AdtTasks=true on the task.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the Task node includes the requested Affinity and/or AdtTasks attributes." }),

        new StepContract(
            "tsproj.set-plc-project-properties",
            "TwinCatTsprojMutationService.SetPlcProjectProperties",
            "tsproj",
            "Updates Plc/Project attribute-level properties such as project paths, reload behavior, AMS port, and archive settings.",
            new[] { "The target PLC Project must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("ProjectFilePath", "string", false, "Value for the PrjFilePath attribute."),
                new StepParameterContract("TmcFilePath", "string", false, "Value for the TmcFilePath attribute."),
                new StepParameterContract("ReloadTmc", "bool", false, "Optional ReloadTmc attribute value."),
                new StepParameterContract("AmsPort", "int", false, "Optional PLC project AmsPort attribute value."),
                new StepParameterContract("FileArchiveSettings", "string", false, "Optional FileArchiveSettings attribute value, for example #x0002.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the PLC Project attribute values were applied as requested." }),

        new StepContract(
            "tsproj.set-plc-instance-metadata",
            "TwinCatTsprojMutationService.SetPlcInstanceMetadata",
            "tsproj",
            "Updates Plc Instance metadata attributes and optional CLSID/ClassFactory fields without replacing unrelated nodes.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("TcSmClass", "string", false, "Optional TcSmClass attribute value."),
                new StepParameterContract("TmcPath", "string", false, "Optional TmcPath attribute value."),
                new StepParameterContract("KeepUnrestoredLinks", "string", false, "Optional KeepUnrestoredLinks attribute value."),
                new StepParameterContract("Clsid", "string", false, "Optional CLSID element value."),
                new StepParameterContract("ClassFactory", "string", false, "Optional ClassFactory attribute on CLSID.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify PLC instance attributes and CLSID metadata values." }),

        new StepContract(
            "tsproj.set-cpp-instance-metadata",
            "TwinCatTsprojMutationService.SetCppInstanceMetadata",
            "tsproj",
            "Updates C++ Instance metadata attributes such as Disabled, KeepUnrestoredLinks, ClassFactoryId, or ObjectId without replacing TmcDesc.",
            new[] { "The target C++ Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "C++ instance display name."),
                new StepParameterContract("Disabled", "bool?", false, "Optional Disabled attribute value. false removes the Disabled attribute.", "true"),
                new StepParameterContract("KeepUnrestoredLinks", "string", false, "Optional KeepUnrestoredLinks attribute value."),
                new StepParameterContract("ClassFactoryId", "string", false, "Optional ClassFactoryId attribute value."),
                new StepParameterContract("ObjectId", "string", false, "Optional instance Id/ObjectId attribute value, for example #x01010010.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the C++ Instance metadata attributes were applied while TmcDesc remains intact." }),

        new StepContract(
            "tsproj.clear-plc-instance-vars",
            "TwinCatTsprojMutationService.ClearPlcInstanceVars",
            "tsproj",
            "Removes all Vars groups under a PLC Instance node so PLC variable layout can be rebuilt deterministically.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the PLC instance contains no Vars child elements." }),

        new StepContract(
            "tsproj.ensure-plc-instance-vars-group",
            "TwinCatTsprojMutationService.EnsurePlcInstanceVarsGroup",
            "tsproj",
            "Ensures a named Vars group exists under a PLC Instance with deterministic Var definitions.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("GroupName", "string", true, "Vars group Name value, for example PlcTask Outputs."),
                new StepParameterContract("VarGrpType", "int", true, "Vars VarGrpType attribute value."),
                new StepParameterContract("InsertType", "int", false, "Vars InsertType attribute value.", "1"),
                new StepParameterContract("AreaNo", "int", false, "Optional Vars AreaNo attribute value."),
                new StepParameterContract("Variables", "IReadOnlyList<PlcInstanceVarItem>", false, "Variable definitions written under Vars/Var."),
                new StepParameterContract("ReplaceExistingGroup", "bool", false, "Whether to replace an existing Vars group with the same Name.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the PLC instance Vars group attributes and Var entries." }),

        new StepContract(
            "tsproj.clear-plc-init-symbols",
            "TwinCatTsprojMutationService.ClearPlcInitSymbols",
            "tsproj",
            "Clears all InitSymbol entries under a PLC Instance InitSymbols section.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Whether empty InitSymbols container should be removed.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the PLC instance has no InitSymbol entries." }),

        new StepContract(
            "tsproj.clear-plc-task-pou-oids",
            "TwinCatTsprojMutationService.ClearPlcTaskPouOids",
            "tsproj",
            "Clears all TaskPouOid entries under a PLC Instance TaskPouOids section.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Whether empty TaskPouOids container should be removed.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the PLC instance has no TaskPouOid entries." }),

        new StepContract(
            "tsproj.clear-mappings",
            "TwinCatTsprojMutationService.ClearMappings",
            "tsproj",
            "Removes all root-level Mappings sections so mapping links can be rebuilt from a known empty state.",
            new[] { "The target .tsproj must already exist on disk." },
            Array.Empty<StepParameterContract>(),
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify root-level Mappings nodes are absent." }),

        new StepContract(
            "tsproj.clear-unrestored-var-links",
            "TwinCatTsprojMutationService.ClearUnrestoredVarLinks",
            "tsproj",
            "Removes stale UnrestoredVarLinks blocks so unresolved TwinCAT links do not survive into activation.",
            new[] { "The target .tsproj must already exist on disk." },
            Array.Empty<StepParameterContract>(),
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify no UnrestoredVarLinks nodes remain before rebuilding mappings." }),

        new StepContract(
            "tsproj.replace-mappings-section",
            "TwinCatTsprojMutationService.ReplaceMappingsSection",
            "tsproj",
            "Replaces the root-level Mappings section with a caller-provided fragment.",
            new[] { "The target .tsproj must already exist on disk.", "The fragment must be a valid Mappings element." },
            new[]
            {
                new StepParameterContract("MappingsXml", "string", true, "Mappings XML fragment to write, rooted at <Mappings>.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and compare the Mappings section against the requested fragment." }),

        new StepContract(
            "tsproj.replace-project-io-section",
            "TwinCatTsprojMutationService.ReplaceProjectIoSection",
            "tsproj",
            "Replaces the root Project/Io section with a caller-provided Io fragment.",
            new[] { "A root-level Project node must exist in the .tsproj.", "The fragment must be a valid Io element." },
            new[]
            {
                new StepParameterContract("IoXml", "string", true, "Io XML fragment to write, rooted at <Io>.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the root Project contains the requested Io section." }),

        new StepContract(
            "tsproj.ensure-io-section",
            "TwinCatTsprojMutationService.EnsureIoSection",
            "tsproj",
            "Ensures the root Project/Io section exists without replacing other Project children.",
            new[] { "A root-level Project node must exist in the .tsproj." },
            Array.Empty<StepParameterContract>(),
            new[]
            {
                new StepOutputContract("created", "bool", "Whether Project/Io was created by this call."),
                new StepOutputContract("deviceCount", "int", "Number of direct Device children currently under Project/Io."),
                new StepOutputContract("projectPath", "string", "Mutated .tsproj path.")
            },
            new[] { "Re-open the .tsproj and verify Project/Io exists and existing Project/System/Plc/Cpp children remain intact." }),

        new StepContract(
            "tsproj.ensure-io-device",
            "TwinCatTsprojMutationService.EnsureIoDevice",
            "tsproj",
            "Creates or updates a Project/Io Device with structured TwinCAT IO identity fields.",
            new[] { "Project/Io will be created if missing.", "Raw AddressInfo or extra fragments require source, parent path, field meaning, and verification evidence." },
            new[]
            {
                new StepParameterContract("DeviceId", "int", true, "Device Id attribute under Project/Io."),
                new StepParameterContract("Name", "string", true, "Device display name."),
                new StepParameterContract("DevType", "int", true, "TwinCAT Device DevType, for example 109 for RT-Ethernet Adapter or 111 for EtherCAT."),
                new StepParameterContract("Disabled", "bool?", false, "Optional Disabled attribute. false removes the attribute."),
                new StepParameterContract("DevFlags", "string", false, "Optional DevFlags value such as #x0003."),
                new StepParameterContract("AmsPort", "int?", false, "Optional device AMS port."),
                new StepParameterContract("AmsNetId", "string", false, "Optional device AMS NetId."),
                new StepParameterContract("RemoteName", "string", false, "Optional RemoteName attribute."),
                new StepParameterContract("InfoImageId", "int?", false, "Optional InfoImageId attribute."),
                new StepParameterContract("AddressInfo", "IoAddressInfo", false, "Structured TcCom/Pnp AddressInfo or documented raw AddressInfo XML."),
                new StepParameterContract("Images", "IReadOnlyList<IoImageDefinition>", false, "Optional direct Image children for process images."),
                new StepParameterContract("EtherCatAttributes", "IReadOnlyList<TsprojXmlAttribute>", false, "Structured attributes for a direct Device/EtherCAT child."),
                new StepParameterContract("EtherCatElements", "IReadOnlyList<IoStructuredElement>", false, "Structured repeated Device/EtherCAT child elements such as DcMode when their meaning is known."),
                new StepParameterContract("ReplaceEtherCatElements", "bool", false, "Whether same-name Device/EtherCAT child elements are replaced before EtherCatElements are added.", "true"),
                new StepParameterContract("EthernetAttributes", "IReadOnlyList<TsprojXmlAttribute>", false, "Structured attributes for a direct Device/Ethernet child."),
                new StepParameterContract("EthernetElements", "IReadOnlyList<IoStructuredElement>", false, "Structured repeated Device/Ethernet child elements such as Esl."),
                new StepParameterContract("ReplaceEthernetElements", "bool", false, "Whether same-name Device/Ethernet child elements are replaced before EthernetElements are added.", "true"),
                new StepParameterContract("ExtraFragments", "IReadOnlyList<IoRawXmlFragment>", false, "Known-good extra Device child fragments with required source/meaning/evidence metadata.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the Device Id, Name, DevType, AMS fields, AddressInfo, and direct Image children match the requested topology." }),

        new StepContract(
            "tsproj.ensure-ethercat-box",
            "TwinCatTsprojMutationService.EnsureEthercatBox",
            "tsproj",
            "Creates or updates an EtherCAT Box under a Device or parent Box, preserving nested topology.",
            new[] { "The target Device must already exist.", "ParentBoxId, when set, must identify exactly one existing Box under that Device." },
            new[]
            {
                new StepParameterContract("DeviceId", "int", true, "Owning Device Id."),
                new StepParameterContract("ParentBoxId", "int?", false, "Optional parent Box Id for nested terminals or safety modules."),
                new StepParameterContract("BoxId", "int", true, "Box Id attribute."),
                new StepParameterContract("Name", "string", true, "Box display name."),
                new StepParameterContract("BoxType", "int", true, "TwinCAT BoxType value."),
                new StepParameterContract("Disabled", "bool?", false, "Optional Disabled attribute. false removes the attribute."),
                new StepParameterContract("BoxFlags", "string", false, "Optional BoxFlags value."),
                new StepParameterContract("ImageId", "int?", false, "Optional ImageId child value."),
                new StepParameterContract("EtherCatAttributes", "IReadOnlyList<TsprojXmlAttribute>", false, "Structured attributes for the Box/EtherCAT child."),
                new StepParameterContract("EtherCatChildValues", "IReadOnlyList<TsprojXmlChildValue>", false, "Simple child values for Box/EtherCAT, such as SyncMan or Fmmu only when their meaning is known."),
                new StepParameterContract("EtherCatElements", "IReadOnlyList<IoStructuredElement>", false, "Structured repeated Box/EtherCAT child elements such as SyncMan, Fmmu, DcMode, BootStrapData, MBoxUserCmdData, CoeProfile, DcData, or Slot."),
                new StepParameterContract("ReplaceEtherCatElements", "bool", false, "Whether same-name Box/EtherCAT child elements are replaced before EtherCatElements are added.", "true"),
                new StepParameterContract("ExtraFragments", "IReadOnlyList<IoRawXmlFragment>", false, "Known-good extra Box child fragments with required source/meaning/evidence metadata.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the Box appears under the expected Device/parent Box with correct Id, Name, ImageId, and EtherCAT metadata." }),

        new StepContract(
            "tsproj.ensure-io-pdo",
            "TwinCatTsprojMutationService.EnsureIoPdo",
            "tsproj",
            "Creates or updates a Box/EtherCAT Pdo and its Entry children using structured PDO fields.",
            new[] { "The target Device and Box must already exist.", "Use ExtraFragments only for known-good PDO child XML with documented field meanings and evidence." },
            new[]
            {
                new StepParameterContract("DeviceId", "int", true, "Owning Device Id."),
                new StepParameterContract("BoxId", "int", true, "Owning Box Id under the Device."),
                new StepParameterContract("Name", "string", true, "Pdo Name attribute."),
                new StepParameterContract("Index", "string", true, "Pdo Index attribute, for example #x1a00 or #x1600."),
                new StepParameterContract("InOut", "string", false, "Optional InOut attribute."),
                new StepParameterContract("Flags", "string", false, "Optional Flags attribute."),
                new StepParameterContract("SyncMan", "int?", false, "Optional SyncMan attribute."),
                new StepParameterContract("Entries", "IReadOnlyList<IoPdoEntry>", false, "PDO Entry definitions containing name/index/sub/type and optional attributes/child values."),
                new StepParameterContract("ReplaceExistingEntries", "bool", false, "Whether existing Entry children are replaced before Entries are applied.", "true"),
                new StepParameterContract("ExtraFragments", "IReadOnlyList<IoRawXmlFragment>", false, "Known-good extra Pdo child fragments with required source/meaning/evidence metadata.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the target Box/EtherCAT/Pdo entries have the expected Index/Sub/Type/BitLen fields and mapping-visible names." }),

        new StepContract(
            "tsproj.ensure-io-box-image",
            "TwinCatTsprojMutationService.EnsureIoBoxImage",
            "tsproj",
            "Creates or updates a Box ImageId and optional image metadata without replacing the Box.",
            new[] { "The target Device and Box must already exist." },
            new[]
            {
                new StepParameterContract("DeviceId", "int", true, "Owning Device Id."),
                new StepParameterContract("BoxId", "int", true, "Target Box Id."),
                new StepParameterContract("ImageId", "int", true, "ImageId child value."),
                new StepParameterContract("MetadataValues", "IReadOnlyList<TsprojXmlChildValue>", false, "Optional simple metadata child values written under the Box."),
                new StepParameterContract("MetadataFragments", "IReadOnlyList<IoRawXmlFragment>", false, "Known-good image metadata fragments with required source/meaning/evidence metadata.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the target Box keeps its topology while ImageId and requested image metadata are present." }),

        new StepContract(
            "tsproj.ensure-mapping-info",
            "TwinCatTsprojMutationService.EnsureMappingInfo",
            "tsproj",
            "Creates or updates a root Mappings/MappingInfo entry.",
            new[] { "Root Mappings will be created if missing.", "Identifier and Id must be known from TwinCAT-generated or documented topology." },
            new[]
            {
                new StepParameterContract("Identifier", "string", true, "MappingInfo Identifier attribute."),
                new StepParameterContract("Id", "string", true, "MappingInfo Id/ObjectId attribute, for example #x02030010."),
                new StepParameterContract("Attributes", "IReadOnlyList<TsprojXmlAttribute>", false, "Optional additional MappingInfo attributes; Identifier and Id are owned by dedicated fields.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify root Mappings contains the requested MappingInfo Identifier/Id pair exactly once." }),

        new StepContract(
            "tsproj.ensure-io-mapping-link",
            "TwinCatTsprojMutationService.EnsureIoMappingLink",
            "tsproj",
            "Creates or updates a mapping OwnerA/OwnerB/Link with optional IO/TcCOM mapping attributes.",
            new[] { "Owner names and Var paths must match the IO/PDO or TcCOM paths visible to XAE.", "Use structured LinkAttributes for Size, RestoreInfo, GrpA, TypeA, InOutA, GuidA, and similar known mapping metadata." },
            new[]
            {
                new StepParameterContract("OwnerAName", "string", true, "OwnerA Name attribute, for example TIID^Device 3 (EtherCAT)."),
                new StepParameterContract("OwnerBName", "string", true, "OwnerB Name attribute, for example TIXC^MotionControl^BeckhoffDriver1."),
                new StepParameterContract("VarA", "string", true, "Link VarA attribute."),
                new StepParameterContract("VarB", "string", true, "Link VarB attribute."),
                new StepParameterContract("OwnerAPrefix", "string", false, "Optional OwnerA Prefix attribute."),
                new StepParameterContract("OwnerAType", "string", false, "Optional OwnerA Type attribute."),
                new StepParameterContract("OwnerBPrefix", "string", false, "Optional OwnerB Prefix attribute."),
                new StepParameterContract("OwnerBType", "string", false, "Optional OwnerB Type attribute."),
                new StepParameterContract("LinkAttributes", "IReadOnlyList<TsprojXmlAttribute>", false, "Optional additional Link attributes such as Size, RestoreInfo, GrpA, TypeA, InOutA, or GuidA."),
                new StepParameterContract("ReplaceExistingAttributes", "bool", false, "Whether LinkAttributes overwrite existing attribute values.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify Mappings contains the exact OwnerA/OwnerB/Link path and any requested Size/RestoreInfo/GuidA metadata." }),

        new StepContract(
            "tsproj.apply-io-topology-plan",
            "TwinCatTsprojMutationService.ApplyIoTopologyPlan",
            "tsproj",
            "Applies a batch IO topology payload by orchestrating dedicated IO Device, Box, PDO, MappingInfo, and Link primitives.",
            new[] { "The payload must use structured dedicated fields and may only use documented raw fragments for known-good XML gaps.", "Device entries must precede Boxes logically; the service applies Devices, Boxes, BoxImages, Pdos, MappingInfos, then Links." },
            new[]
            {
                new StepParameterContract("Devices", "IReadOnlyList<EnsureIoDeviceRequest>", false, "Device definitions to ensure."),
                new StepParameterContract("Boxes", "IReadOnlyList<EnsureEthercatBoxRequest>", false, "Box definitions to ensure."),
                new StepParameterContract("Pdos", "IReadOnlyList<EnsureIoPdoRequest>", false, "PDO definitions to ensure."),
                new StepParameterContract("BoxImages", "IReadOnlyList<EnsureIoBoxImageRequest>", false, "Box ImageId/image metadata updates."),
                new StepParameterContract("MappingInfos", "IReadOnlyList<EnsureMappingInfoRequest>", false, "MappingInfo entries to ensure."),
                new StepParameterContract("Links", "IReadOnlyList<EnsureIoMappingLinkRequest>", false, "Mapping links to ensure."),
                new StepParameterContract("EnsureIoSection", "bool", false, "Whether Project/Io should be created before applying the plan.", "true")
            },
            new[]
            {
                new StepOutputContract("deviceCount", "int", "Number of Device entries applied from the payload."),
                new StepOutputContract("boxCount", "int", "Number of Box entries applied from the payload."),
                new StepOutputContract("pdoCount", "int", "Number of PDO entries applied from the payload."),
                new StepOutputContract("boxImageCount", "int", "Number of Box image updates applied from the payload."),
                new StepOutputContract("mappingInfoCount", "int", "Number of MappingInfo entries applied from the payload."),
                new StepOutputContract("linkCount", "int", "Number of Link entries applied from the payload.")
            },
            new[] { "Re-open XAE and compare normalized Project/Io and root Mappings snapshots; for disabled hardware topologies, build plus normalized XML is the first acceptance gate." }),

        new StepContract(
            "tsproj.replace-data-types-section",
            "TwinCatTsprojMutationService.ReplaceDataTypesSection",
            "tsproj",
            "Replaces the root DataTypes section with a caller-provided fragment.",
            new[] { "The target .tsproj must already exist on disk.", "The fragment must be a valid DataTypes element." },
            new[]
            {
                new StepParameterContract("DataTypesXml", "string", true, "DataTypes XML fragment to write, rooted at <DataTypes>."),
                new StepParameterContract("InsertBeforeProject", "bool", false, "Whether to place DataTypes before the root Project element when present.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify DataTypes appears in the expected root position with requested content." }),

        new StepContract(
            "tsproj.replace-system-settings-section",
            "TwinCatTsprojMutationService.ReplaceSystemSettingsSection",
            "tsproj",
            "Replaces System/Settings with a caller-provided Settings fragment while preserving other System children.",
            new[] { "The target .tsproj must already exist on disk.", "The fragment must be a valid Settings element." },
            new[]
            {
                new StepParameterContract("SettingsXml", "string", true, "Settings XML fragment to write, rooted at <Settings>."),
                new StepParameterContract("InsertBeforeTasks", "bool", false, "Whether to insert Settings before System/Tasks when Tasks exists.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify System/Settings content and insertion position." }),

        new StepContract(
            "tsproj.ensure-system-settings",
            "TwinCatTsprojMutationService.EnsureSystemSettings",
            "tsproj",
            "Ensures typed System/Settings values such as Cpu and IoIdleTask without replacing the whole Settings section.",
            new[] { "The target .tsproj must already exist on disk.", "At least one typed setting must be provided." },
            new[]
            {
                new StepParameterContract("CpuId", "int?", false, "Optional System/Settings/Cpu CpuId attribute."),
                new StepParameterContract("IoIdleTaskPriority", "int?", false, "Optional System/Settings/IoIdleTask Priority attribute."),
                new StepParameterContract("InsertBeforeTasks", "bool", false, "Whether to insert Settings before System/Tasks when Settings is created and Tasks exists.", "true"),
                new StepParameterContract("MaxCpus", "int?", false, "Optional System/Settings MaxCpus attribute."),
                new StepParameterContract("NonWinCpus", "int?", false, "Optional System/Settings NonWinCpus attribute."),
                new StepParameterContract("CpuEntries", "IReadOnlyList<SystemCpuSetting>", false, "Optional full Cpu entry list, including entries without CpuId."),
                new StepParameterContract("ReplaceCpuEntries", "bool", false, "Whether existing Cpu children are replaced before CpuEntries are added.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify System/Settings contains the requested Cpu and IoIdleTask attributes while existing Tasks remain intact." }),

        new StepContract(
            "tsproj.clear-instance-parameter-values",
            "TwinCatTsprojMutationService.ClearInstanceParameterValues",
            "tsproj",
            "Clears ParameterValues under a C++ instance TmcDesc before applying a deterministic parameter plan.",
            new[] { "The referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Remove the ParameterValues container instead of leaving it empty.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm stale ParameterValues entries are gone before new values are applied." }),

        new StepContract(
            "tsproj.apply-instance-parameter-plan",
            "TwinCatTsprojMutationService.ApplyInstanceParameterPlan",
            "tsproj",
            "Applies a batch of instance parameter writes in one deterministic .tsproj mutation pass.",
            new[] { "Each referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("Items", "IReadOnlyList<InstanceParameterMutation>", true, "Batch entries containing InstanceName, ParameterName, and optional Value/Enum/String payloads.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm each target instance received the expected ParameterValues updates." }),

        new StepContract(
            "tsproj.clear-instance-data-pointer-values",
            "TwinCatTsprojMutationService.ClearInstanceDataPointerValues",
            "tsproj",
            "Clears DataPointerValues under a C++ instance TmcDesc before activation or before applying a known-good data pointer plan.",
            new[] { "The referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Remove the DataPointerValues container instead of leaving it empty.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm stale DataPointerValues entries are gone before activation." }),

        new StepContract(
            "tsproj.apply-instance-interface-pointer-plan",
            "TwinCatTsprojMutationService.ApplyInstanceInterfacePointerPlan",
            "tsproj",
            "Applies a batch of instance interface pointer writes in one deterministic .tsproj mutation pass.",
            new[] { "Each referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("Items", "IReadOnlyList<InstanceInterfacePointerMutation>", true, "Batch entries containing InstanceName, PointerName, and ObjectId.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify InterfacePointerValues entries for each requested instance/pointer pair." }),

        new StepContract(
            "tsproj.apply-instance-data-pointer-plan",
            "TwinCatTsprojMutationService.ApplyInstanceDataPointerPlan",
            "tsproj",
            "Applies a batch of instance data pointer writes in one deterministic .tsproj mutation pass.",
            new[] { "Each referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("Items", "IReadOnlyList<InstanceDataPointerMutation>", true, "Batch entries containing InstanceName, PointerName, ObjectId, AreaNo, ByteOffset, ByteSize, and optional ArrayIndex for array DataPointerValues.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify DataPointerValues entries for each requested instance/pointer pair." }),

        new StepContract(
            "tsproj.refresh-cpp-instance-tmc-desc",
            "TwinCatTsprojMutationService.RefreshCppInstanceTmcDesc",
            "tsproj",
            "Refreshes existing C++ instance TmcDesc metadata from the project .tmc while preserving instance context and value sections.",
            new[] { "The target C++ project instances must already exist in the .tsproj.", "The project .tmc must contain the requested module class names." },
            new[]
            {
                new StepParameterContract("CppProjectName", "string", true, "Owning C++ project name in the .tsproj."),
                new StepParameterContract("ProjectTmcPath", "string", true, "Absolute path to the project .tmc file."),
                new StepParameterContract("Instances", "IReadOnlyList<CppInstanceTmcDescRefreshItem>", true, "InstanceName to ModuleClassName mapping entries."),
                new StepParameterContract("PreserveValueSections", "bool", false, "Preserve existing ParameterValues, InterfacePointerValues, and DataPointerValues.", "true"),
                new StepParameterContract("PreserveContextValues", "bool", false, "Preserve existing TmcDesc Contexts and task ManualConfig.", "true"),
                new StepParameterContract("ImportDataTypesFromTmc", "bool", false, "Replace root .tsproj DataTypes from the project .tmc DataTypes.", "true"),
                new StepParameterContract("FailIfMissingModule", "bool", false, "Fail when an instance mapping references a missing TMC module.", "true")
            },
            new[]
            {
                new StepOutputContract("projectPath", "string", "Mutated .tsproj path."),
                new StepOutputContract("refreshedCount", "int", "Number of instance TmcDesc sections refreshed."),
                new StepOutputContract("errorsJson", "json", "Detailed mismatch list when refresh fails.")
            },
            new[] { "Re-open the .tsproj and confirm instance DataAreas/Parameters/Pointers match the project .tmc while prior values and task bindings remain." }),

        new StepContract(
            "tsproj.ensure-io-task-image",
            "TwinCatTsprojMutationService.EnsureIoTaskImage",
            "tsproj",
            "Ensures IO Task Image structure on a task and binds the instance IoTaskImage pointer to the derived or specified Image ObjectId.",
            new[] { "The target Task and instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("ImageId", "int", false, "Task Image Id used to derive the default ObjectId.", "1"),
                new StepParameterContract("SizeIn", "int", false, "Task Image input size in bytes.", "40"),
                new StepParameterContract("SizeOut", "int", false, "Task Image output size in bytes.", "10"),
                new StepParameterContract("PointerName", "string", false, "Interface pointer name to bind on the instance.", "IoTaskImage"),
                new StepParameterContract("EnsureDefaultTaskVariables", "bool", false, "Whether to regenerate default Inputs/Outputs Vars groups.", "true"),
                new StepParameterContract("InputRealCount", "int", false, "Default input REAL variable count.", "10"),
                new StepParameterContract("OutputByteCount", "int", false, "Default output BYTE variable count.", "10"),
                new StepParameterContract("IoAtBegin", "bool", false, "Whether to set Task IoAtBegin.", "true"),
                new StepParameterContract("ImageObjectId", "string", false, "Optional explicit image ObjectId override.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm Task Image plus instance InterfacePointerValues/IoTaskImage were updated." }),

        new StepContract(
            "tsproj.bind-instance-task",
            "TwinCatTsprojMutationService.BindInstanceToTask",
            "tsproj",
            "Writes ManualConfig/OTCID and context timing values back into an instance TmcDesc, optionally also updating CyclicCaller.",
            new[] { "The target .tsproj must already exist on disk.", "The task ObjectId must already be known." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("TaskObjectId", "string", true, "Task OTCID, for example #x02010010."),
                new StepParameterContract("Priority", "int", true, "Context priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "Context cycle time in nanoseconds."),
                new StepParameterContract("IncludeCyclicCaller", "bool", false, "Whether to also bind InterfacePointerValues/CyclicCaller.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj or produce instance XML again and confirm OTCID/CycleTime/Priority are present." }),

        new StepContract(
            "tsproj.bind-plc-instance-task",
            "TwinCatTsprojMutationService.BindPlcInstanceToTask",
            "tsproj",
            "Writes PLC instance Context/ManualConfig binding so PLC execution context tracks an explicit task object id.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("PlcTaskName", "string", true, "PLC task name written into Context/Name."),
                new StepParameterContract("TaskObjectId", "string", true, "Task OTCID, for example #x02010020."),
                new StepParameterContract("Priority", "int", true, "PLC context priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "PLC context cycle time in nanoseconds."),
                new StepParameterContract("ContextId", "int", false, "PLC context Id value.", "0")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the PLC instance Context/ManualConfig contains OTCID/Priority/CycleTime." }),

        new StepContract(
            "tsproj.ensure-task-pou-oid",
            "TwinCatTsprojMutationService.EnsureTaskPouOid",
            "tsproj",
            "Ensures a PLC TaskPouOid entry exists with the requested priority and optional OTCID.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("Priority", "int", true, "TaskPouOid Prio attribute value."),
                new StepParameterContract("ObjectId", "string", false, "Optional TaskPouOid OTCID value.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm TaskPouOids/TaskPouOid has the requested Prio and OTCID." }),

        new StepContract(
            "tsproj.ensure-init-symbol",
            "TwinCatTsprojMutationService.EnsureInitSymbol",
            "tsproj",
            "Ensures a PLC InitSymbol exists and writes Data from the provided ObjectId using TwinCAT little-endian encoding.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("SymbolName", "string", true, "InitSymbol name, for example MAIN.fbStateMachine.oidInstance."),
                new StepParameterContract("ObjectId", "string", true, "ObjectId used to produce InitSymbol Data, for example #x02010010."),
                new StepParameterContract("TypeName", "string", false, "InitSymbol Type element value.", "OTCID"),
                new StepParameterContract("TypeGuid", "string", false, "GUID attribute written on InitSymbol Type.", "{18071995-0000-0000-0000-00000000000F}"),
                new StepParameterContract("AreaNo", "string", false, "InitSymbol AreaNo value.", "#x00000003")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm InitSymbols contains the requested symbol name/type/area/data." }),

        new StepContract(
            "tsproj.ensure-parameter",
            "TwinCatTsprojMutationService.EnsureParameterValue",
            "tsproj",
            "Upserts a parameter default under TmcDesc/ParameterValues for an instance.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("ParameterName", "string", true, "Parameter name."),
                new StepParameterContract("ValueText", "string", false, "Scalar value text."),
                new StepParameterContract("EnumText", "string", false, "Enum value text."),
                new StepParameterContract("StringText", "string", false, "String value text.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the Value node now exists with the expected child elements." }),

        new StepContract(
            "tsproj.ensure-interface-pointer",
            "TwinCatTsprojMutationService.EnsureInterfacePointerValue",
            "tsproj",
            "Writes or updates an InterfacePointerValues entry beneath an instance TmcDesc.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("PointerName", "string", true, "Interface pointer field name."),
                new StepParameterContract("ObjectId", "string", true, "Referenced OTCID.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the named InterfacePointerValues/Value entry contains the OTCID." }),

        new StepContract(
            "tsproj.ensure-data-pointer",
            "TwinCatTsprojMutationService.EnsureDataPointerValue",
            "tsproj",
            "Writes or updates a DataPointerValues entry beneath an instance TmcDesc.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("PointerName", "string", true, "Data pointer field name."),
                new StepParameterContract("ObjectId", "string", true, "Referenced OTCID."),
                new StepParameterContract("AreaNo", "int", true, "TwinCAT data area number."),
                new StepParameterContract("ByteOffset", "int", true, "Offset inside the area."),
                new StepParameterContract("ByteSize", "int", true, "Byte width of the pointed segment."),
                new StepParameterContract("ArrayIndex", "int?", false, "Optional array index. When set, writes a Data child with ArrayIndex under the named DataPointerValues entry.", "0")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the named DataPointerValues/Value entry matches the requested offsets." }),

        new StepContract(
            "tsproj.ensure-mapping-link",
            "TwinCatTsprojMutationService.EnsureMappingLink",
            "tsproj",
            "Ensures a deterministic Mapping OwnerA/OwnerB/Link triple exists without replacing unrelated mappings.",
            new[] { "Owner names and variable names must match TwinCAT path/value expectations." },
            new[]
            {
                new StepParameterContract("OwnerAName", "string", true, "OwnerA Name attribute, for example TIXC^Demo^ObjA."),
                new StepParameterContract("OwnerBName", "string", true, "OwnerB Name attribute, for example TIXC^Demo^ObjB."),
                new StepParameterContract("VarA", "string", true, "VarA attribute value."),
                new StepParameterContract("VarB", "string", true, "VarB attribute value.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm Mappings contains the requested OwnerA/OwnerB/Link entry." }),

        new StepContract(
            "tsproj.assert-data-pointer-shape",
            "TwinCatTsprojMutationService.AssertDataPointerShape",
            "tsproj",
            "Reads a .tsproj and asserts that C++ instance DataPointerValues and root Mappings links still match the requested shape.",
            new[] { "Use after data pointer and mapping steps, and again after XAE save/activate, to catch TwinCAT deleting unresolved DataPointerValues." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact C++ instance display name to inspect."),
                new StepParameterContract("DataPointers", "IReadOnlyList<ExpectedDataPointerValueShape>", false, "Required DataPointerValues entries, optional per-entry record count, and optional ArrayIndex set."),
                new StepParameterContract("ExpectedDataPointerRecordCount", "int?", false, "Expected total number of data pointer records across all DataPointerValues entries."),
                new StepParameterContract("MappingLinks", "IReadOnlyList<ExpectedMappingLinkShape>", false, "Root Mappings OwnerA/OwnerB/Link entries that must be present."),
                new StepParameterContract("ExpectedDataPointerMappingLinkCount", "int?", false, "Expected number of root Mappings Link entries whose VarA or VarB is a Data Pointer reference."),
                new StepParameterContract("ExpectedRootMappingLinkCount", "int?", false, "Expected total root Mappings Link count.")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether all requested shape assertions passed."),
                new StepOutputContract("dataPointerRecordCount", "int", "Total DataPointerValues record count for the inspected instance."),
                new StepOutputContract("dataPointerMappingLinkCount", "int", "Root Mappings Link count limited to Data Pointer links."),
                new StepOutputContract("rootMappingLinkCount", "int", "Total root Mappings Link count."),
                new StepOutputContract("errorsText", "string", "Human-readable assertion failures."),
                new StepOutputContract("shapeJson", "json", "Full observed shape and error details.")
            },
            new[] { "Run against the generated .tsproj before activation and after activation/save; for OptCNC, AxesGroup0 should retain six data pointer records and eight data pointer mapping links." }),

        new StepContract(
            "tsproj.assert-io-topology-shape",
            "TwinCatTsprojMutationService.AssertIoTopologyShape",
            "tsproj",
            "Reads a .tsproj and asserts the Project/Io topology and root Mappings shape without mutating TwinCAT metadata.",
            new[] { "Use after dedicated IO topology primitives or after XAE import/scan evidence; this step is a guard and does not create devices or copy IO XML." },
            new[]
            {
                new StepParameterContract("ExpectedDeviceCount", "int?", false, "Expected Project/Io Device count."),
                new StepParameterContract("ExpectedBoxCount", "int?", false, "Expected total Box count under Project/Io."),
                new StepParameterContract("ExpectedImageCount", "int?", false, "Expected total Image count under Project/Io."),
                new StepParameterContract("ExpectedPdoCount", "int?", false, "Expected total Pdo count under Project/Io."),
                new StepParameterContract("ExpectedPdoEntryCount", "int?", false, "Expected total Pdo Entry count under Project/Io."),
                new StepParameterContract("ExpectedMappingInfoCount", "int?", false, "Expected root Mappings/MappingInfo count."),
                new StepParameterContract("ExpectedOwnerACount", "int?", false, "Expected root Mappings/OwnerA count."),
                new StepParameterContract("ExpectedRootMappingLinkCount", "int?", false, "Expected total root Mappings Link count."),
                new StepParameterContract("Devices", "IReadOnlyList<ExpectedIoDeviceShape>", false, "Specific Device Id/name/box-count/InfoImageId/Image-count/direct-child-count assertions."),
                new StepParameterContract("Boxes", "IReadOnlyList<ExpectedIoBoxShape>", false, "Specific Device/Box Id/name/ImageId/BoxFlags/parent/PDO/PDO-entry/direct-child-count assertions."),
                new StepParameterContract("MappingLinks", "IReadOnlyList<ExpectedMappingLinkShape>", false, "Root Mappings OwnerA/OwnerB/Link entries that must be present.")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether all requested IO shape assertions passed."),
                new StepOutputContract("deviceCount", "int", "Observed Project/Io Device count."),
                new StepOutputContract("boxCount", "int", "Observed total Box count."),
                new StepOutputContract("imageCount", "int", "Observed total Image count."),
                new StepOutputContract("pdoCount", "int", "Observed total Pdo count."),
                new StepOutputContract("pdoEntryCount", "int", "Observed total Pdo Entry count."),
                new StepOutputContract("mappingInfoCount", "int", "Observed root Mappings/MappingInfo count."),
                new StepOutputContract("rootMappingLinkCount", "int", "Observed root Mappings Link count."),
                new StepOutputContract("errorsText", "string", "Human-readable assertion failures."),
                new StepOutputContract("shapeJson", "json", "Full observed shape and error details.")
            },
            new[] { "For OptCNC sample parity, use this to prove the generated .tsproj has the expected 5 Device / 28 Box / 107 PDO / 2 MappingInfo skeleton before claiming IO parity." }),

        new StepContract(
            "tsproj.assert-io-image-references",
            "TwinCatTsprojMutationService.AssertIoImageReferences",
            "tsproj",
            "Reads a .tsproj and asserts IO process-image references without mutating TwinCAT metadata.",
            new[] { "Use after IO topology creation to catch half-populated process-image shape, for example Device InfoImageId without a direct Image node or ImageId values with no known backing." },
            new[]
            {
                new StepParameterContract("ExpectedRootImageDataCount", "int?", false, "Expected root TcSmProject/ImageDatas/ImageData count."),
                new StepParameterContract("ExpectedDeviceImageCount", "int?", false, "Expected direct Device/Image count under Project/Io."),
                new StepParameterContract("ExpectedImageReferenceCount", "int?", false, "Expected ImageId reference count under Project/Io."),
                new StepParameterContract("RequireDeviceImageForInfoImageId", "bool", false, "Fail when a Device has InfoImageId but no direct Image node.", "true"),
                new StepParameterContract("RequireImageIdBacking", "bool", false, "Fail when an ImageId does not match root ImageData, direct Device Image, Device InfoImageId, or AllowedUnbackedImageIds.", "true"),
                new StepParameterContract("AllowedUnbackedImageIds", "IReadOnlyList<string>", false, "Known system image ids that are valid without root ImageData, for example TwinSAFE module image id 118.")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether all requested IO image reference assertions passed."),
                new StepOutputContract("rootImageDataCount", "int", "Observed root ImageData count."),
                new StepOutputContract("deviceImageCount", "int", "Observed direct Device/Image count."),
                new StepOutputContract("deviceWithInfoImageCount", "int", "Observed Device count with InfoImageId."),
                new StepOutputContract("deviceInfoWithoutImageCount", "int", "Observed Device count with InfoImageId but no direct Image node."),
                new StepOutputContract("imageReferenceCount", "int", "Observed Project/Io ImageId reference count."),
                new StepOutputContract("unbackedImageReferenceCount", "int", "Observed ImageId reference count without known backing."),
                new StepOutputContract("errorsText", "string", "Human-readable assertion failures."),
                new StepOutputContract("shapeJson", "json", "Full observed IO image reference shape and error details.")
            },
            new[] { "For OptCNC sample parity, require four direct Device/Image nodes and allow only documented system image ids as unbacked." }),

        new StepContract(
            "tsproj.describe-io-topology",
            "TwinCatTsprojMutationService.DescribeIoTopology",
            "tsproj",
            "Reads a .tsproj and emits a normalized IO topology summary without copying or mutating TwinCAT metadata XML.",
            new[] { "Use this as evidence before designing IO topology steps; it reports stable IDs, names, counts, process images, PDO entries, mapping owners, and optional attributes but never returns raw XML fragments." },
            new[]
            {
                new StepParameterContract("IncludeDevices", "bool", false, "Whether Device summaries should be included.", "true"),
                new StepParameterContract("IncludeBoxes", "bool", false, "Whether Box summaries should be included.", "true"),
                new StepParameterContract("IncludePdos", "bool", false, "Whether PDO and PDO Entry summaries should be included.", "true"),
                new StepParameterContract("IncludeMappings", "bool", false, "Whether root Mappings/MappingInfo/OwnerA/Link summaries should be included.", "true"),
                new StepParameterContract("IncludeAttributes", "bool", false, "Whether normalized attribute name/value summaries should be included for inspected nodes.", "false"),
                new StepParameterContract("MaxItemsPerCollection", "int", false, "Optional cap per output collection; zero means no truncation.", "0")
            },
            new[]
            {
                new StepOutputContract("deviceCount", "int", "Observed Project/Io Device count."),
                new StepOutputContract("boxCount", "int", "Observed total Box count."),
                new StepOutputContract("imageCount", "int", "Observed normalized IO Image summary count in the output JSON."),
                new StepOutputContract("pdoCount", "int", "Observed total Pdo count."),
                new StepOutputContract("pdoEntryCount", "int", "Observed total Pdo Entry count."),
                new StepOutputContract("mappingInfoCount", "int", "Observed root Mappings/MappingInfo count."),
                new StepOutputContract("ownerACount", "int", "Observed root Mappings/OwnerA count."),
                new StepOutputContract("rootMappingLinkCount", "int", "Observed root Mappings Link count."),
                new StepOutputContract("truncated", "bool", "Whether collection output was truncated by MaxItemsPerCollection."),
                new StepOutputContract("shapeJson", "json", "Normalized IO topology description for evidence and diffing.")
            },
            new[] { "Run on the target sample and generated .tsproj, compare shapeJson counts and selected IDs/names, then design missing typed IO steps without using copied metadata XML." }),

        new StepContract(
            "tsproj.compare-io-topology",
            "TwinCatTsprojMutationService.CompareIoTopology",
            "tsproj",
            "Compares two .tsproj files through normalized IO topology facts and reports stable count/key/field differences.",
            new[] { "This is a read-only guard for evidence and acceptance. It never imports the reference topology, never emits raw IO XML, and must not be used as a metadata copy path." },
            new[]
            {
                new StepParameterContract("ReferenceProjectPath", "string", true, "Reference .tsproj path to compare against the candidate project path."),
                new StepParameterContract("IncludeMappings", "bool", false, "Whether root Mappings/MappingInfo/OwnerA/Link facts should be compared.", "true"),
                new StepParameterContract("IncludePdos", "bool", false, "Whether PDO and PDO Entry facts should be compared.", "true"),
                new StepParameterContract("IncludeAttributes", "bool", false, "Whether normalized attributes should be included in the underlying descriptions.", "false"),
                new StepParameterContract("MaxDifferences", "int", false, "Maximum number of differences to return; zero means no cap.", "200")
            },
            new[]
            {
                new StepOutputContract("succeeded", "bool", "Whether all compared IO topology facts match."),
                new StepOutputContract("differenceCount", "int", "Number of reported differences."),
                new StepOutputContract("truncated", "bool", "Whether differences were truncated by MaxDifferences."),
                new StepOutputContract("comparisonJson", "json", "Count comparisons and stable topology differences.")
            },
            new[] { "Use after generation and again after XAE save/activate; it compares process-image and PDO-entry facts as normalized fields, so a mismatch proves IO parity is not complete without requiring or exposing sample metadata XML." }),

        new StepContract(
            "tsproj.upsert-element",
            "TwinCatTsprojMutationService.UpsertElement",
            "tsproj",
            "Upserts a generic XML element under a path-based .tsproj parent using a declared conflict policy.",
            new[] { "The target .tsproj must already exist on disk.", "Prefer dedicated tsproj primitives when one exists for the same intent." },
            new[]
            {
                new StepParameterContract("ParentPath", "IReadOnlyList<TsprojPathSegment>", true, "Path from root to the parent element, optionally matching <Name> values."),
                new StepParameterContract("ElementName", "string", true, "Element local name to insert or update."),
                new StepParameterContract("MatchNameValue", "string", false, "Optional <Name> child value used to match an existing element."),
                new StepParameterContract("Attributes", "IReadOnlyList<TsprojXmlAttribute>", false, "Attributes to write on the target element."),
                new StepParameterContract("ChildValues", "IReadOnlyList<TsprojXmlChildValue>", false, "Simple child element values to write under the target element."),
                new StepParameterContract("ConflictPolicy", "TsprojMutationConflictPolicy", false, "ReplaceExisting, KeepExisting, or FailOnConflict.", "ReplaceExisting")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the resolved parent contains exactly the intended element shape." }),

        new StepContract(
            "tsproj.upsert-fragment",
            "TwinCatTsprojMutationService.UpsertFragment",
            "tsproj",
            "Upserts a generic XML fragment under a path-based .tsproj parent using a declared conflict policy.",
            new[] { "The target .tsproj must already exist on disk.", "The fragment must already be shaped like Beckhoff expects." },
            new[]
            {
                new StepParameterContract("ParentPath", "IReadOnlyList<TsprojPathSegment>", true, "Path from root to the parent element, optionally matching <Name> values."),
                new StepParameterContract("FragmentXml", "string", true, "XML fragment to insert or update."),
                new StepParameterContract("MatchElementName", "string", false, "Local element name used to match an existing sibling."),
                new StepParameterContract("MatchNameValue", "string", false, "Optional <Name> value used to match an existing sibling."),
                new StepParameterContract("ConflictPolicy", "TsprojMutationConflictPolicy", false, "ReplaceExisting, KeepExisting, or FailOnConflict.", "ReplaceExisting")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and compare the resolved parent before/after the fragment upsert." }),

        new StepContract(
            "tsproj.apply-mutation-plan",
            "TwinCatTsprojMutationService.ApplyMutationPlan",
            "tsproj",
            "Applies generic element and fragment upserts in one deterministic .tsproj mutation pass.",
            new[] { "The target .tsproj must already exist on disk.", "Use this for JSON-owned low-level mutations that do not yet have a dedicated primitive." },
            new[]
            {
                new StepParameterContract("ElementUpserts", "IReadOnlyList<TsprojElementUpsertRequest>", false, "Element upserts to apply in order."),
                new StepParameterContract("FragmentUpserts", "IReadOnlyList<TsprojFragmentUpsertRequest>", false, "Fragment upserts to apply in order.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify all requested low-level mutations landed; FailOnConflict aborts the plan." }),

        new StepContract(
            "tsproj.merge-fragment",
            "TwinCatTsprojMutationService.MergeNamedElementFragment",
            "tsproj",
            "Merges a captured XML fragment into a named container, acting as the escape hatch only for remaining TwinCAT XML gaps without a dedicated primitive.",
            new[]
            {
                "The target .tsproj must already exist on disk.",
                "Use a dedicated tsproj primitive instead when one exists for the same intent.",
                "The fragment must come from a known-good .tsproj source, with documented target parent path, field meanings, source evidence, and test or real-machine evidence.",
                "The target parent local name must be unambiguous in the .tsproj."
            },
            new[]
            {
                new StepParameterContract("ParentElementName", "string", true, "Container local name inside the .tsproj."),
                new StepParameterContract("FragmentXml", "string", true, "XML fragment to merge."),
                new StepParameterContract("MatchElementName", "string", false, "Local element name used to find and replace an existing sibling."),
                new StepParameterContract("MatchNameValue", "string", false, "Optional <Name> value used to find the existing sibling."),
                new StepParameterContract("ReplaceExisting", "bool", false, "Whether a matched sibling should be replaced.", "true"),
                new StepParameterContract("FragmentSource", "string", true, "Known-good .tsproj/evidence source for the fragment."),
                new StepParameterContract("TargetParentPath", "string", true, "Human-readable target parent path/layer in the .tsproj."),
                new StepParameterContract("FieldMeaning", "string", true, "Short explanation of the fragment fields and why they are required."),
                new StepParameterContract("VerificationEvidence", "string", true, "Test, XAE reopen/build/activation, or dated evidence proving this fragment is safe enough to use.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and compare the target container before/after the merge.", "For engineering behavior, follow with XAE reopen/build/activation or a dated evidence note explaining why runtime proof is not available." }),

        new StepContract(
            "validation.ads-scan",
            "AdsValidationService.ScanPorts",
            "validation",
            "Scans ADS target ports and reports whether the runtime endpoint is reachable before symbol-level ADS reads are attempted.",
            new[] { "TwinCAT runtime or router must be installed on the target machine.", "Use this before symbol reads when activation or ADS routing is uncertain." },
            new[]
            {
                new StepParameterContract("NetId", "string", false, "AMS NetId, or local for the local router.", "local"),
                new StepParameterContract("Ports", "int[]", false, "Comma-separated ADS ports to probe.", "100,200,300,800,851,852,10000")
            },
            new[]
            {
                new StepOutputContract("openPortCount", "int", "Number of ports with readable ADS state."),
                new StepOutputContract("failedPortCount", "int", "Number of ports that failed to connect or read state."),
                new StepOutputContract("portsJson", "json", "Per-port ADS state or error message.")
            },
            new[] { "If 100 or 10000 are not reachable, treat later PLC symbol read failures as runtime/router/permission failures rather than symbol-path failures." }),

        new StepContract(
            "validation.assert-ads-state",
            "AdsValidationService.AssertStates",
            "validation",
            "Asserts that specific ADS ports are reachable and in the expected ADS state, turning activation false positives into hard failures.",
            new[] { "TwinCAT runtime or router must be installed on the target machine.", "Use after activation when a plan needs exact ports such as 10000, 200, and 300 to be Run." },
            new[]
            {
                new StepParameterContract("NetId", "string", false, "AMS NetId, or local for the local router.", "local"),
                new StepParameterContract("ExpectedPorts", "IReadOnlyList<ExpectedAdsPortState>", true, "Port/state assertions. Direct CLI accepts --expected=10000=Run;200=Run;300=Run, --ports=10000,200,300 --ads-state=Run, or --json-file."),
                new StepParameterContract("DeviceState", "short?", false, "Optional device state assertion per port.")
            },
            new[]
            {
                new StepOutputContract("succeededCount", "int", "Number of ports that matched the expected state."),
                new StepOutputContract("failedCount", "int", "Number of ports that were unreachable or mismatched."),
                new StepOutputContract("statesText", "string", "Human-readable port=state assertions."),
                new StepOutputContract("statesJson", "json", "Per-port expected and actual ADS/device state.")
            },
            new[] { "For OptCNC activation proof, require 10000=Run and system ports 200/300=Run instead of accepting a generic activation step success." }),

        new StepContract(
            "validation.mark-event-log-window",
            "AdsValidationService.MarkEventLogWindow",
            "validation",
            "Marks the current Windows event-log position so a later step can assert only events from the same activation window.",
            new[] { "Use immediately before engineering.activate-configuration.", "This step is read-only and does not require Visual Studio or ADS." },
            new[]
            {
                new StepParameterContract("LogName", "string", false, "Windows event log name.", "Application"),
                new StepParameterContract("ProviderName", "string", false, "Event source/provider to mark.", "TcSysSrv"),
                new StepParameterContract("MarkerFilePath", "string?", false, "Optional JSON file path where the marker should be written for a later plan step.")
            },
            new[]
            {
                new StepOutputContract("markedAt", "datetime", "Marker timestamp."),
                new StepOutputContract("lastEntryIndex", "int?", "Last observed provider event index at marker time."),
                new StepOutputContract("markerJson", "json", "Marker payload usable by validation.assert-event-log-window.")
            },
            new[] { "For OptCNC, write the marker to evidenceDir before activation, then pass the marker file to validation.assert-event-log-window after ADS assertions." }),

        new StepContract(
            "validation.assert-event-log-window",
            "AdsValidationService.AssertEventLogWindow",
            "validation",
            "Asserts that no forbidden TcSysSrv Windows event-log entries appeared after a marker or within a recent time window.",
            new[] { "Use after activation and ADS state checks.", "By default it fails on Error/Critical TcSysSrv events and AdsState: >15< Config messages." },
            new[]
            {
                new StepParameterContract("Marker", "EventLogWindowMarker?", false, "Inline marker returned by validation.mark-event-log-window."),
                new StepParameterContract("MarkerFilePath", "string?", false, "Marker JSON file written by validation.mark-event-log-window."),
                new StepParameterContract("LogName", "string", false, "Windows event log name when no marker is supplied.", "Application"),
                new StepParameterContract("ProviderName", "string", false, "Event source/provider when no marker is supplied.", "TcSysSrv"),
                new StepParameterContract("LookbackSeconds", "int", false, "Fallback lookback window when no marker is supplied.", "300"),
                new StepParameterContract("FailOnErrorOrCritical", "bool", false, "Whether Error/Critical entries fail the step.", "true"),
                new StepParameterContract("FailOnConfigAdsState", "bool", false, "Whether AdsState: >15< Config messages fail the step.", "true"),
                new StepParameterContract("FailMessageContains", "IReadOnlyList<string>?", false, "Additional message substrings that should fail the step."),
                new StepParameterContract("MaxEvents", "int", false, "Maximum events to include in output; zero means no cap.", "50")
            },
            new[]
            {
                new StepOutputContract("observedEventCount", "int", "Provider event count observed in the window."),
                new StepOutputContract("errorOrCriticalCount", "int", "Error/Critical event count in the window."),
                new StepOutputContract("configAdsStateCount", "int", "AdsState >15< Config message count in the window."),
                new StepOutputContract("errorsText", "string", "Human-readable assertion failures."),
                new StepOutputContract("assertionJson", "json", "Full event window assertion result.")
            },
            new[] { "For OptCNC activation proof, this step closes the gap where engineering.activate-configuration succeeds but TcSysSrv immediately reports an error or returns to Config." }),

        new StepContract(
            "validation.assert-process-crash-window",
            "AdsValidationService.AssertProcessCrashWindow",
            "validation",
            "Asserts that no matching Windows Application crash events appeared after a marker or within a recent time window.",
            new[] { "Use after Visual Studio/XAE launch, solution open, build, or activation when unattended runs must distinguish a COM/RPC disconnect from a crashed IDE host.", "By default it checks Application Error, .NET Runtime, and Windows Error Reporting events for devenv.exe, TcXaeShell.exe, and TwinCAT System Manager modules." },
            new[]
            {
                new StepParameterContract("Marker", "EventLogWindowMarker?", false, "Inline marker returned by validation.mark-event-log-window."),
                new StepParameterContract("MarkerFilePath", "string?", false, "Marker JSON file written by validation.mark-event-log-window."),
                new StepParameterContract("LogName", "string", false, "Windows event log name.", "Application"),
                new StepParameterContract("LookbackSeconds", "int", false, "Fallback lookback window when no marker is supplied.", "300"),
                new StepParameterContract("ProviderNames", "IReadOnlyList<string>?", false, "Event sources to scan. Direct CLI accepts semicolon-separated provider names."),
                new StepParameterContract("ProcessNames", "IReadOnlyList<string>?", false, "Process names that should fail when found in an event message."),
                new StepParameterContract("ModuleNames", "IReadOnlyList<string>?", false, "Fault module names that should fail when found in an event message."),
                new StepParameterContract("MessageContains", "IReadOnlyList<string>?", false, "Additional message substrings that should fail the step."),
                new StepParameterContract("MaxEvents", "int", false, "Maximum events to include in output; zero means no cap.", "100")
            },
            new[]
            {
                new StepOutputContract("observedEventCount", "int", "Application event count observed in the window."),
                new StepOutputContract("matchingEventCount", "int", "Crash event count matching the requested process/module/message filters."),
                new StepOutputContract("errorsText", "string", "Human-readable assertion failures."),
                new StepOutputContract("matchingEventsJson", "json", "Matching crash event snapshots."),
                new StepOutputContract("assertionJson", "json", "Full process crash assertion result.")
            },
            new[] { "For OptCNC, use the same marker file as the activation TcSysSrv event guard so a Visual Studio/XAE crash becomes an explicit validation failure instead of only an RPC error." }),

        new StepContract(
            "validation.ads-read",
            "AdsValidationService.Read",
            "validation",
            "Reads a PLC or TcCOM symbol over ADS so the engineering pipeline can close the loop with a runtime assertion.",
            new[] { "The target runtime must be reachable over ADS.", "The symbol path and AMS endpoint must already be known." },
            new[]
            {
                new StepParameterContract("NetId", "string", true, "AMS NetId, for example 127.0.0.1.1.1."),
                new StepParameterContract("Port", "int", true, "ADS port, for example 851."),
                new StepParameterContract("SymbolPath", "string", true, "Fully qualified PLC or TcCOM symbol name."),
                new StepParameterContract("DataType", "AdsReadDataType", true, "Expected runtime data type."),
                new StepParameterContract("AutoReconnect", "bool", false, "Whether to force a TwinCAT reconnect attempt before reading.", "false")
            },
            new[]
            {
                new StepOutputContract("value", "string", "Stringified ADS value.")
            },
            new[] { "Treat a successful read plus a domain assertion on the returned value as the runtime acceptance check." }),

        new StepContract(
            "validation.ads-read-symbols",
            "AdsValidationService.ReadSymbols",
            "validation",
            "Reads multiple PLC or TcCOM symbols over ADS and prints their values as a runtime validation checkpoint.",
            new[] { "The target runtime must be reachable over ADS.", "The symbol paths, data types, and AMS endpoint must already be known." },
            new[]
            {
                new StepParameterContract("NetId", "string", true, "AMS NetId, for example 127.0.0.1.1.1."),
                new StepParameterContract("Port", "int", true, "ADS port, for example 851."),
                new StepParameterContract("Symbols", "IReadOnlyList<AdsReadSymbolRequest>", true, "Symbols to read; direct CLI accepts --symbols=MAIN.nSeed:UInt32;MAIN.bPipelineOk:Boolean or --json-file."),
                new StepParameterContract("AutoReconnect", "bool", false, "Whether to force a TwinCAT reconnect attempt before reading.", "false"),
                new StepParameterContract("ContinueOnError", "bool", false, "Whether the step should succeed when one or more symbols fail.", "false")
            },
            new[]
            {
                new StepOutputContract("succeededCount", "int", "Number of symbols read successfully."),
                new StepOutputContract("failedCount", "int", "Number of symbols that failed to read."),
                new StepOutputContract("valuesText", "string", "Human-readable symbol=value list printed in the step result."),
                new StepOutputContract("valuesJson", "json", "Per-symbol ADS read results including data types and errors.")
            },
            new[] { "Use after activation and ADS port scan; treat the printed values plus expected domain assertions as runtime proof." })
    };

    private static readonly IReadOnlyDictionary<string, StepContract> Lookup =
        ContractsInternal.ToDictionary(item => item.Kind, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<StepContract> All => ContractsInternal;

    public static IReadOnlyList<string> RecommendedExecutionOrder { get; } = new[]
    {
        "engineering.cleanup-dte-host-processes",
        "engineering.launch-visual-studio",
        "engineering.create-xae-solution",
        "engineering.create-cpp-project",
        "engineering.create-vs-cpp-project",
        "engineering.create-scope-project",
        "scope.ensure-configuration",
        "scope.assert-configuration-shape",
        "engineering.ensure-solution-project-dependency",
        "engineering.create-io-device",
        "engineering.create-ethercat-box",
        "engineering.apply-io-tree-plan",
        "ethercat.assert-product-revisions",
        "engineering.search-io-devices",
        "engineering.reload-io-devices",
        "engineering.generate-io-mappings",
        "cpp.remove-project-item",
        "cpp.create-project-item",
        "cpp.write-project-item-content",
        "cpp.set-project-property",
        "cpp.set-item-definition-property",
        "cpp.set-project-item-metadata",
        "engineering.create-module",
        "engineering.start-tmc-code-generator",
        "engineering.publish-modules",
        "engineering.verify-tmc-data-areas",
        "engineering.apply-tmc-module-model",
        "engineering.add-module-instance",
        "engineering.ensure-task",
        "engineering.export-tree-item-xml",
        "engineering.save-all",
        "engineering.close-visual-studio",
        "tsproj.ensure-task",
        "tsproj.clear-task-layout",
        "tsproj.ensure-task-vars-group",
        "tsproj.ensure-task-image",
        "tsproj.ensure-cpp-instance",
        "tsproj.ensure-plc-instance",
        "tsproj.bind-instance-context",
        "tsproj.set-task-affinity",
        "tsproj.set-plc-project-properties",
        "tsproj.set-plc-instance-metadata",
        "tsproj.clear-plc-instance-vars",
        "tsproj.ensure-plc-instance-vars-group",
        "tsproj.clear-plc-task-pou-oids",
        "tsproj.clear-plc-init-symbols",
        "tsproj.clear-mappings",
        "tsproj.clear-unrestored-var-links",
        "tsproj.replace-mappings-section",
        "tsproj.replace-project-io-section",
        "tsproj.ensure-io-section",
        "tsproj.ensure-io-device",
        "tsproj.ensure-ethercat-box",
        "tsproj.ensure-io-box-image",
        "tsproj.ensure-io-pdo",
        "tsproj.ensure-mapping-info",
        "tsproj.ensure-io-mapping-link",
        "tsproj.apply-io-topology-plan",
        "tsproj.replace-data-types-section",
        "tsproj.replace-system-settings-section",
        "tsproj.ensure-system-settings",
        "tsproj.ensure-io-task-image",
        "tsproj.bind-instance-task",
        "tsproj.bind-plc-instance-task",
        "tsproj.ensure-task-pou-oid",
        "tsproj.ensure-init-symbol",
        "tsproj.clear-instance-parameter-values",
        "tsproj.clear-instance-data-pointer-values",
        "tsproj.apply-instance-parameter-plan",
        "tsproj.apply-instance-interface-pointer-plan",
        "tsproj.apply-instance-data-pointer-plan",
        "tsproj.refresh-cpp-instance-tmc-desc",
        "tsproj.ensure-parameter",
        "tsproj.ensure-interface-pointer",
        "tsproj.ensure-data-pointer",
        "tsproj.ensure-mapping-link",
        "tsproj.assert-data-pointer-shape",
        "tsproj.assert-io-topology-shape",
        "tsproj.assert-io-image-references",
        "tsproj.describe-io-topology",
        "tsproj.compare-io-topology",
        "tsproj.upsert-element",
        "tsproj.upsert-fragment",
        "tsproj.apply-mutation-plan",
        "tsproj.merge-fragment",
        "engineering.open-xae-solution",
        "engineering.build-solution",
        "signing.grant-certificate",
        "signing.set-license",
        "signing.sign-twincat-binary",
        "signing.verify-twincat-binary",
        "engineering.activate-configuration",
        "validation.ads-scan",
        "validation.assert-ads-state",
        "validation.mark-event-log-window",
        "validation.assert-event-log-window",
        "validation.ads-read",
        "validation.ads-read-symbols"
    };

    public static StepContract Require(string kind)
    {
        if (!Lookup.TryGetValue(kind, out StepContract? contract))
        {
            throw new KeyNotFoundException($"Unknown TwinCatAutomationKit contract '{kind}'.");
        }

        return contract;
    }
}
