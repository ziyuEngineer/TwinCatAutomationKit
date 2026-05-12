using System.Globalization;
using System.Text.Json;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class StepInvokeCommand
{
    private static StepExecutionOutcome ExecuteEngineeringLaunchVisualStudio(IReadOnlyDictionary<string, string> options)
    {
        LaunchVisualStudioRequest request = BuildLaunchVisualStudioRequest(options);
        if (_engineeringSessionReuseScope is not null)
        {
            TwinCatEngineeringSession reusedSession = _engineeringSessionReuseScope.EnsureLaunched(options);
            return StepExecutionOutcome.Success(
                "Visual Studio launch verified in reused run-plan engineering session.",
                CreateOutputs(
                    ("progId", request.ProgId),
                    ("visible", request.Visible ? "true" : "false"),
                    ("suppressUi", request.SuppressUi ? "true" : "false"),
                    ("enableDialogAutoDismiss", request.EnableDialogAutoDismiss ? "true" : "false"),
                    ("attachToExisting", request.AttachToExisting ? "true" : "false"),
                    ("rootSuffix", request.RootSuffix ?? string.Empty),
                    ("dteHostPath", request.DteHostPath ?? string.Empty),
                    ("preferDteHostLaunch", request.PreferDteHostLaunch ? "true" : "false"),
                    ("attachedToExisting", reusedSession.AttachedToExisting ? "true" : "false"),
                    ("targetProcessIds", string.Join(";", reusedSession.TargetProcessIds)),
                    ("autoDismissedDialogs", JsonSerializer.Serialize(reusedSession.AutoDismissedDialogs, JsonOptions)),
                    ("launchTimeoutMs", request.LaunchTimeoutMs.ToString(CultureInfo.InvariantCulture)),
                    ("dialogPollIntervalMs", request.DialogPollIntervalMs.ToString(CultureInfo.InvariantCulture)),
                    ("startupDelayMs", request.StartupDelayMs.ToString(CultureInfo.InvariantCulture))));
        }

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(request);
            return StepExecutionOutcome.Success(
                "Visual Studio launch verified in stateless CLI mode.",
                CreateOutputs(
                    ("progId", request.ProgId),
                    ("visible", request.Visible ? "true" : "false"),
                    ("suppressUi", request.SuppressUi ? "true" : "false"),
                    ("enableDialogAutoDismiss", request.EnableDialogAutoDismiss ? "true" : "false"),
                    ("attachToExisting", request.AttachToExisting ? "true" : "false"),
                    ("rootSuffix", request.RootSuffix ?? string.Empty),
                    ("dteHostPath", request.DteHostPath ?? string.Empty),
                    ("preferDteHostLaunch", request.PreferDteHostLaunch ? "true" : "false"),
                    ("attachedToExisting", session.AttachedToExisting ? "true" : "false"),
                    ("targetProcessIds", string.Join(";", session.TargetProcessIds)),
                    ("autoDismissedDialogs", JsonSerializer.Serialize(session.AutoDismissedDialogs, JsonOptions)),
                    ("launchTimeoutMs", request.LaunchTimeoutMs.ToString(CultureInfo.InvariantCulture)),
                    ("dialogPollIntervalMs", request.DialogPollIntervalMs.ToString(CultureInfo.InvariantCulture)),
                    ("startupDelayMs", request.StartupDelayMs.ToString(CultureInfo.InvariantCulture))));
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    engineering.CloseVisualStudio(session, saveBeforeClose: false);
                }
                catch
                {
                }
                finally
                {
                    DisposeEngineeringSession(session);
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateXaeSolution(IReadOnlyDictionary<string, string> options)
    {
        string solutionDirectory = Path.GetFullPath(CliOptionParser.RequireOption(options, "solution-directory", "output"));
        string solutionName = CliOptionParser.RequireOption(options, "solution-name");
        string projectName = CliOptionParser.RequireOption(options, "project-name");
        LaunchVisualStudioRequest launchRequest = BuildLaunchVisualStudioRequest(options);

        if (_engineeringSessionReuseScope is not null)
        {
            TwinCatEngineeringSession reusedSession = _engineeringSessionReuseScope.EnsureLaunched(options);
            TwinCatProjectInfo reusedResult = _engineeringSessionReuseScope.Engineering.CreateTwinCatSolution(
                reusedSession,
                new CreateTwinCatSolutionRequest(solutionDirectory, solutionName, projectName));
            _engineeringSessionReuseScope.Engineering.SaveAll(reusedSession);
            _engineeringSessionReuseScope.RememberWorkspace(new CliWorkspacePaths(reusedResult.SolutionPath, reusedResult.ProjectPath));
            return StepExecutionOutcome.Success(
                $"TwinCAT solution {solutionName} created.",
                CreateOutputs(
                    ("solutionPath", reusedResult.SolutionPath),
                    ("projectPath", reusedResult.ProjectPath),
                    ("solutionDirectory", reusedResult.SolutionDirectory)));
        }

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(launchRequest);
            TwinCatProjectInfo result = engineering.CreateTwinCatSolution(
                session,
                new CreateTwinCatSolutionRequest(solutionDirectory, solutionName, projectName));
            engineering.SaveAll(session);
            return StepExecutionOutcome.Success(
                $"TwinCAT solution {solutionName} created.",
                CreateOutputs(
                    ("solutionPath", result.SolutionPath),
                    ("projectPath", result.ProjectPath),
                    ("solutionDirectory", result.SolutionDirectory)));
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    engineering.CloseVisualStudio(session, saveBeforeClose: true);
                }
                catch
                {
                }
                finally
                {
                    DisposeEngineeringSession(session);
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringCleanupDteHostProcesses(IReadOnlyDictionary<string, string> options)
    {
        IReadOnlyList<string>? processNames = ParseOptionalList(CliOptionParser.GetOption(options, "process-names", "process-name"));
        IReadOnlyList<int>? processIds = ParseOptionalIntList(CliOptionParser.GetOption(options, "process-ids", "process-id"));
        bool dryRun = CliOptionParser.GetBoolOption(options, "dry-run", true);
        bool includeWindowed = CliOptionParser.GetBoolOption(options, "include-windowed", false);
        bool killProcessTree = CliOptionParser.GetBoolOption(options, "kill-process-tree", true);

        TwinCatEngineeringService engineering = new();
        CleanupDteHostProcessesResult result = engineering.CleanupDteHostProcesses(
            new CleanupDteHostProcessesRequest(
                processNames,
                processIds,
                dryRun,
                includeWindowed,
                killProcessTree));

        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("dryRun", result.DryRun ? "true" : "false"),
            ("matchedCount", result.MatchedCount.ToString(CultureInfo.InvariantCulture)),
            ("killedCount", result.KilledCount.ToString(CultureInfo.InvariantCulture)),
            ("processesJson", JsonSerializer.Serialize(result.Processes, JsonOptions)));

        return result.Succeeded
            ? StepExecutionOutcome.Success(result.Summary, outputs)
            : new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                result.Summary,
                outputs,
                Array.Empty<EvidenceArtifact>());
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateCppProject(IReadOnlyDictionary<string, string> options)
    {
        string cppProjectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string wizardId = CliOptionParser.GetOption(options, "wizard-id") ?? "TcVersionedDriverWizard";

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                TwinCatNodeInfo result = engineering.CreateCppProject(
                    session,
                    new CreateCppProjectRequest(cppProjectName, wizardId));
                return StepExecutionOutcome.Success(
                    $"C++ project {cppProjectName} created.",
                    CreateOutputs(
                        ("treeItemPath", result.TreeItemPath),
                        ("projectFilePath", result.FilePath)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateVsCppProject(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "vs-project-name", "cpp-project-name", "project-name");
        string? projectDirectory = CliOptionParser.GetOption(options, "project-directory");
        string templateKind = CliOptionParser.GetOption(options, "template-kind") ?? "ConsoleApplication";
        IReadOnlyList<string>? candidateTemplatePaths = ParseOptionalList(CliOptionParser.GetOption(options, "candidate-template-paths", "candidate-template-path"));
        string? platformToolset = CliOptionParser.GetOption(options, "platform-toolset");
        bool allowTemplateFallback = CliOptionParser.GetBoolOption(options, "allow-template-fallback", false);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                VisualStudioCppProjectInfo result = engineering.CreateVisualStudioCppProject(
                    session,
                    new CreateVisualStudioCppProjectRequest(
                        projectName,
                        projectDirectory,
                        templateKind,
                        candidateTemplatePaths,
                        platformToolset,
                        allowTemplateFallback));
                return StepExecutionOutcome.Success(
                    $"Visual Studio C++ project {projectName} created.",
                    CreateOutputs(
                        ("projectFilePath", result.ProjectFilePath),
                        ("projectGuid", result.ProjectGuid),
                        ("projectDirectory", result.ProjectDirectory)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateScopeProject(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "scope-project-name", "project-name");
        string? projectDirectory = CliOptionParser.GetOption(options, "project-directory");
        string? configurationFileName = CliOptionParser.GetOption(options, "configuration-file-name", "config-file-name");
        bool createEmptyConfiguration = CliOptionParser.GetBoolOption(options, "create-empty-configuration", true);
        bool allowSolutionFileFallback = CliOptionParser.GetBoolOption(options, "allow-solution-file-fallback", true);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                ScopeProjectInfo result = engineering.CreateScopeProject(
                    session,
                    new CreateScopeProjectRequest(
                        projectName,
                        projectDirectory,
                        configurationFileName,
                        createEmptyConfiguration,
                        allowSolutionFileFallback));
                return StepExecutionOutcome.Success(
                    $"Scope project {projectName} created.",
                    CreateOutputs(
                        ("projectFilePath", result.ProjectFilePath),
                        ("projectGuid", result.ProjectGuid),
                        ("projectDirectory", result.ProjectDirectory),
                        ("configurationFilePath", result.ConfigurationFilePath),
                        ("addedToSolution", result.AddedToSolution ? "true" : "false"),
                        ("usedSolutionFileFallback", result.UsedSolutionFileFallback ? "true" : "false")));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringEnsureSolutionProjectDependency(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "dependency-project-name", "project-name");
        string dependsOnProjectName = CliOptionParser.RequireOption(options, "depends-on-project-name", "depends-on");

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                SolutionProjectDependencyResult result = engineering.EnsureSolutionProjectDependency(
                    session,
                    new EnsureSolutionProjectDependencyRequest(projectName, dependsOnProjectName));
                return StepExecutionOutcome.Success(
                    $"Solution dependency {projectName} -> {dependsOnProjectName} ensured.",
                    CreateOutputs(
                        ("projectGuid", result.ProjectGuid),
                        ("dependsOnProjectGuid", result.DependsOnProjectGuid)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateIoDevice(IReadOnlyDictionary<string, string> options)
    {
        CreateIoDeviceRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<CreateIoDeviceRequest>(options)
            : new CreateIoDeviceRequest(
                Name: CliOptionParser.RequireOption(options, "name", "device-name"),
                SubType: CliOptionParser.GetIntOption(options, "subtype", 111),
                ParentTreeItemPath: CliOptionParser.GetOption(options, "parent-tree-item-path", "parent-path") ?? "TIID",
                Before: CliOptionParser.GetOption(options, "before"),
                VInfo: CliOptionParser.GetOption(options, "vinfo", "info"),
                Disabled: CliOptionParser.GetNullableBoolOption(options, "disabled"),
                AllowExisting: CliOptionParser.GetBoolOption(options, "allow-existing", true),
                PostCreateDelayMs: CliOptionParser.GetIntOption(options, "post-create-delay-ms", 500));

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                TwinCatNodeInfo result = engineering.CreateIoDevice(session, request);
                return StepExecutionOutcome.Success(
                    $"IO device {request.Name} created or found.",
                    CreateOutputs(
                        ("treeItemPath", result.TreeItemPath),
                        ("displayName", result.DisplayName),
                        ("objectId", result.ObjectId),
                        ("parentTreeItemPath", request.ParentTreeItemPath),
                        ("subType", request.SubType.ToString(CultureInfo.InvariantCulture))));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateEthercatBox(IReadOnlyDictionary<string, string> options)
    {
        CreateEthercatBoxRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<CreateEthercatBoxRequest>(options)
            : new CreateEthercatBoxRequest(
                ParentTreeItemPath: CliOptionParser.RequireOption(options, "parent-tree-item-path", "parent-path"),
                Name: CliOptionParser.RequireOption(options, "name", "box-name"),
                SubType: CliOptionParser.GetIntOption(options, "subtype", 9099),
                Before: CliOptionParser.GetOption(options, "before"),
                ProductRevision: CliOptionParser.GetOption(options, "product-revision", "product"),
                VInfo: CliOptionParser.GetOption(options, "vinfo", "info"),
                Disabled: CliOptionParser.GetNullableBoolOption(options, "disabled"),
                AllowExisting: CliOptionParser.GetBoolOption(options, "allow-existing", true),
                PostCreateDelayMs: CliOptionParser.GetIntOption(options, "post-create-delay-ms", 500));

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                TwinCatNodeInfo result = engineering.CreateEthercatBox(session, request);
                return StepExecutionOutcome.Success(
                    $"EtherCAT box {request.Name} created or found.",
                    CreateOutputs(
                        ("treeItemPath", result.TreeItemPath),
                        ("displayName", result.DisplayName),
                        ("objectId", result.ObjectId),
                        ("parentTreeItemPath", request.ParentTreeItemPath),
                        ("subType", request.SubType.ToString(CultureInfo.InvariantCulture)),
                        ("productRevision", request.ProductRevision)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringGenerateIoMappings(IReadOnlyDictionary<string, string> options)
    {
        GenerateIoMappingsRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<GenerateIoMappingsRequest>(options)
            : new GenerateIoMappingsRequest(
                SuppressUi: CliOptionParser.GetBoolOption(options, "suppress-ui", true),
                AllowDteCommandFallback: CliOptionParser.GetBoolOption(options, "allow-dte-command-fallback", false),
                TimeoutMs: CliOptionParser.GetIntOption(options, "timeout-ms", 120000));

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                EngineeringCommandResult result = engineering.GenerateIoMappings(session, request);
                return StepExecutionOutcome.Success(
                    "TwinCAT IO mappings generated.",
                    CreateOutputs(
                        ("succeeded", result.Succeeded ? "true" : "false"),
                        ("command", result.Command),
                        ("attemptedCommands", string.Join(";", result.AttemptedCommands))));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringSearchIoDevices(IReadOnlyDictionary<string, string> options)
    {
        SearchIoDevicesRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<SearchIoDevicesRequest>(options)
            : new SearchIoDevicesRequest(
                SuppressUi: CliOptionParser.GetBoolOption(options, "suppress-ui", true),
                TimeoutMs: CliOptionParser.GetIntOption(options, "timeout-ms", 120000));

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                EngineeringCommandResult result = engineering.SearchIoDevices(session, request);
                return StepExecutionOutcome.Success(
                    "TwinCAT IO devices searched.",
                    CreateOutputs(
                        ("succeeded", result.Succeeded ? "true" : "false"),
                        ("command", result.Command),
                        ("attemptedCommands", string.Join(";", result.AttemptedCommands))));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringReloadIoDevices(IReadOnlyDictionary<string, string> options)
    {
        ReloadIoDevicesRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<ReloadIoDevicesRequest>(options)
            : new ReloadIoDevicesRequest(
                SuppressUi: CliOptionParser.GetBoolOption(options, "suppress-ui", true),
                TimeoutMs: CliOptionParser.GetIntOption(options, "timeout-ms", 120000));

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                EngineeringCommandResult result = engineering.ReloadIoDevices(session, request);
                return StepExecutionOutcome.Success(
                    "TwinCAT IO devices reloaded.",
                    CreateOutputs(
                        ("succeeded", result.Succeeded ? "true" : "false"),
                        ("command", result.Command),
                        ("attemptedCommands", string.Join(";", result.AttemptedCommands))));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringApplyIoTreePlan(IReadOnlyDictionary<string, string> options)
    {
        ApplyIoTreePlanRequest request = ReadJsonPayload<ApplyIoTreePlanRequest>(options);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                ApplyIoTreePlanResult result = engineering.ApplyIoTreePlan(session, request);
                return StepExecutionOutcome.Success(
                    result.Summary,
                    CreateOutputs(
                        ("deviceCount", result.DeviceCount.ToString(CultureInfo.InvariantCulture)),
                        ("boxCount", result.BoxCount.ToString(CultureInfo.InvariantCulture)),
                        ("treeItemPaths", string.Join(";", result.Nodes.Select(node => node.TreeItemPath))),
                        ("nodesJson", JsonSerializer.Serialize(result.Nodes, JsonOptions))));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringOpenXaeSolution(IReadOnlyDictionary<string, string> options)
    {
        CliWorkspacePaths workspace = ResolveWorkspace(options, requireSolutionPath: true);
        LaunchVisualStudioRequest launchRequest = BuildLaunchVisualStudioRequest(options);

        if (_engineeringSessionReuseScope is not null)
        {
            TwinCatEngineeringSession reusedSession = _engineeringSessionReuseScope.EnsureLaunched(options);
            TwinCatProjectInfo reusedInfo = _engineeringSessionReuseScope.Engineering.OpenTwinCatSolution(
                reusedSession,
                new OpenTwinCatSolutionRequest(workspace.SolutionPath!, workspace.ProjectPath));
            _engineeringSessionReuseScope.Engineering.SaveAll(reusedSession);
            _engineeringSessionReuseScope.RememberWorkspace(new CliWorkspacePaths(reusedInfo.SolutionPath, reusedInfo.ProjectPath));
            return StepExecutionOutcome.Success(
                $"TwinCAT solution reopened from {reusedInfo.SolutionPath}.",
                CreateOutputs(
                    ("solutionPath", reusedInfo.SolutionPath),
                    ("projectPath", reusedInfo.ProjectPath),
                    ("solutionDirectory", reusedInfo.SolutionDirectory)));
        }

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(launchRequest);
            TwinCatProjectInfo info = engineering.OpenTwinCatSolution(
                session,
                new OpenTwinCatSolutionRequest(workspace.SolutionPath!, workspace.ProjectPath));
            engineering.SaveAll(session);
            return StepExecutionOutcome.Success(
                $"TwinCAT solution reopened from {info.SolutionPath}.",
                CreateOutputs(
                    ("solutionPath", info.SolutionPath),
                    ("projectPath", info.ProjectPath),
                    ("solutionDirectory", info.SolutionDirectory)));
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    engineering.CloseVisualStudio(session, saveBeforeClose: false);
                }
                catch
                {
                }
                finally
                {
                    DisposeEngineeringSession(session);
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringCreatePlcProject(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name", "project-name");
        bool allowOfflineFallback = CliOptionParser.GetBoolOption(options, "allow-offline-fallback", true);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                TwinCatNodeInfo result = engineering.CreatePlcProject(
                    session,
                    new CreatePlcProjectRequest(plcProjectName, AllowOfflineFallback: allowOfflineFallback));
                return StepExecutionOutcome.Success(
                    $"PLC project {plcProjectName} created.",
                    CreateOutputs(
                        ("treeItemPath", result.TreeItemPath),
                        ("projectFilePath", result.FilePath)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateModule(IReadOnlyDictionary<string, string> options)
    {
        string cppProjectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string moduleName = CliOptionParser.RequireOption(options, "module-name");
        string wizardId = CliOptionParser.GetOption(options, "wizard-id") ?? "TcModuleClassWizard";
        bool allowOfflineFallback = CliOptionParser.GetBoolOption(options, "allow-offline-fallback", true);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                TwinCatNodeInfo result = engineering.CreateModule(
                    session,
                    new CreateModuleRequest(cppProjectName, moduleName, wizardId, allowOfflineFallback));
                return StepExecutionOutcome.Success(
                    $"Module {moduleName} created.",
                    CreateOutputs(
                        ("treeItemPath", result.TreeItemPath),
                        ("moduleCppPath", result.FilePath),
                        ("objectId", result.ObjectId)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringPublishModules(IReadOnlyDictionary<string, string> options)
    {
        string cppProjectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        int postPublishDelayMs = CliOptionParser.GetIntOption(options, "post-publish-delay-ms", 5000);
        int waitForUpdatedTmcTimeoutMs = CliOptionParser.GetIntOption(options, "wait-for-updated-tmc-timeout-ms", 30000);
        bool runTmcCodeGeneratorFirst = CliOptionParser.GetBoolOption(options, "run-tmc-code-generator-first", false);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                PublishModulesResult result = engineering.PublishModules(
                    session,
                    new PublishModulesRequest(cppProjectName, postPublishDelayMs, waitForUpdatedTmcTimeoutMs, runTmcCodeGeneratorFirst));
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        $"PublishModules completed for {cppProjectName}.",
                        CreateOutputs(
                            ("updatedTmcPath", result.UpdatedTmcPath),
                            ("succeeded", result.Succeeded ? "true" : "false"),
                            ("updated", result.Updated ? "true" : "false")),
                        string.IsNullOrWhiteSpace(result.UpdatedTmcPath)
                            ? Array.Empty<EvidenceArtifact>()
                            : [new EvidenceArtifact("updated-tmc", result.UpdatedTmcPath!, "tmc")])
                    : StepExecutionOutcome.Failed($"PublishModules did not produce a readable TMC for {cppProjectName}.");
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringStartTmcCodeGenerator(IReadOnlyDictionary<string, string> options)
    {
        string cppProjectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        int postStartDelayMs = CliOptionParser.GetIntOption(options, "post-start-delay-ms", 500);
        int waitForUpdatedTmcTimeoutMs = CliOptionParser.GetIntOption(options, "wait-for-updated-tmc-timeout-ms", 30000);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                StartTmcCodeGeneratorResult result = engineering.StartTmcCodeGenerator(
                    session,
                    new StartTmcCodeGeneratorRequest(cppProjectName, postStartDelayMs, waitForUpdatedTmcTimeoutMs));
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        $"TMC code generator completed for {cppProjectName}.",
                        CreateOutputs(
                            ("updatedTmcPath", result.UpdatedTmcPath),
                            ("succeeded", result.Succeeded ? "true" : "false"),
                            ("updated", result.Updated ? "true" : "false")),
                        string.IsNullOrWhiteSpace(result.UpdatedTmcPath)
                            ? Array.Empty<EvidenceArtifact>()
                            : [new EvidenceArtifact("updated-tmc", result.UpdatedTmcPath!, "tmc")])
                    : StepExecutionOutcome.Failed($"TMC code generator did not produce a readable TMC for {cppProjectName}.");
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringVerifyTmcDataAreas(IReadOnlyDictionary<string, string> options)
    {
        VerifyTmcDataAreasRequest request = ReadJsonPayload<VerifyTmcDataAreasRequest>(options);
        string? projectTmcPath = CliOptionParser.GetOption(options, "project-tmc-path", "tmc-path");
        if (!string.IsNullOrWhiteSpace(projectTmcPath))
        {
            request = request with { ProjectTmcPath = projectTmcPath };
        }

        TwinCatEngineeringService engineering = new();
        VerifyTmcDataAreasResult result = engineering.VerifyTmcDataAreas(request);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("projectTmcPath", result.ProjectTmcPath),
            ("expectedModuleCount", result.ExpectedModuleCount.ToString(CultureInfo.InvariantCulture)),
            ("matchedModuleCount", result.MatchedModuleCount.ToString(CultureInfo.InvariantCulture)),
            ("errorsJson", JsonSerializer.Serialize(result.Errors, JsonOptions)));

        return result.Succeeded
            ? StepExecutionOutcome.Success(result.Summary, outputs)
            : new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                result.Summary,
                outputs,
                Array.Empty<EvidenceArtifact>());
    }

    private static StepExecutionOutcome ExecuteEngineeringApplyTmcModuleModel(IReadOnlyDictionary<string, string> options)
    {
        ApplyTmcModuleModelRequest request = ReadJsonPayload<ApplyTmcModuleModelRequest>(options);
        string? projectTmcPath = CliOptionParser.GetOption(options, "project-tmc-path", "tmc-path");
        if (!string.IsNullOrWhiteSpace(projectTmcPath))
        {
            request = request with { ProjectTmcPath = projectTmcPath };
        }

        string? projectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name");
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            request = request with { ProjectName = projectName };
        }

        string? servicesHeaderPath = CliOptionParser.GetOption(options, "generated-services-header-path", "services-header-path");
        if (!string.IsNullOrWhiteSpace(servicesHeaderPath))
        {
            request = request with { GeneratedServicesHeaderPath = servicesHeaderPath };
        }

        string? generatedHeaderPaths = CliOptionParser.GetOption(options, "generated-header-paths", "generated-header-path");
        if (!string.IsNullOrWhiteSpace(generatedHeaderPaths))
        {
            request = request with { GeneratedHeaderPaths = ParseOptionalList(generatedHeaderPaths) };
        }

        TwinCatEngineeringService engineering = new();
        ApplyTmcModuleModelResult result = engineering.ApplyTmcModuleModel(request);
        return StepExecutionOutcome.Success(
            result.Summary,
            CreateOutputs(
                ("projectTmcPath", result.ProjectTmcPath),
                ("moduleCount", result.ModuleCount.ToString(CultureInfo.InvariantCulture))),
            [new EvidenceArtifact("project-tmc", result.ProjectTmcPath, "tmc")]);
    }

    private static StepExecutionOutcome ExecuteEngineeringAddModuleInstance(IReadOnlyDictionary<string, string> options)
    {
        string cppProjectName = CliOptionParser.RequireOption(options, "cpp-project-name");
        string instanceBaseName = CliOptionParser.RequireOption(options, "instance-base-name");
        string? moduleClassName = CliOptionParser.GetOption(options, "module-class-name");
        bool allowOfflineFallback = CliOptionParser.GetBoolOption(options, "allow-offline-fallback", true);

        return RunEngineeringOperation(
            options,
            (engineering, session, workspace) =>
            {
                string projectTmcPath = CliOptionParser.GetOption(options, "project-tmc-path")
                    ?? Path.Combine(Path.GetDirectoryName(workspace.ProjectPath)!, cppProjectName, cppProjectName + ".tmc");
                TwinCatNodeInfo result = engineering.AddModuleInstance(
                    session,
                    new AddModuleInstanceRequest(cppProjectName, projectTmcPath, instanceBaseName, moduleClassName, allowOfflineFallback));
                return StepExecutionOutcome.Success(
                    $"Module instance {instanceBaseName} added.",
                    CreateOutputs(
                        ("treeItemPath", result.TreeItemPath),
                        ("displayName", result.DisplayName),
                        ("objectId", result.ObjectId),
                        ("projectTmcPath", projectTmcPath)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringEnsureTask(IReadOnlyDictionary<string, string> options)
    {
        string taskName = CliOptionParser.RequireOption(options, "task-name");
        int taskSubtype = CliOptionParser.GetIntOption(options, "task-subtype", 0);
        int priority = CliOptionParser.GetIntOption(options, "priority", 20);
        int cycleTimeUs = CliOptionParser.GetIntOption(options, "cycle-time-us", 10_000);
        int amsPort = CliOptionParser.GetIntOption(options, "ams-port", 301);
        bool? ioAtBegin = TryGetNullableBool(options, "io-at-begin");

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                TwinCatNodeInfo result = engineering.EnsureTask(
                    session,
                    new EnsureTaskRequest(taskName, taskSubtype, priority, cycleTimeUs, amsPort, ioAtBegin));
                return StepExecutionOutcome.Success(
                    $"Task {taskName} ensured.",
                    CreateOutputs(
                        ("treeItemPath", result.TreeItemPath),
                        ("objectId", result.ObjectId)));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringExportTreeItemXml(IReadOnlyDictionary<string, string> options)
    {
        string treeItemPath = CliOptionParser.RequireOption(options, "tree-item-path");
        string destinationPath = Path.GetFullPath(CliOptionParser.RequireOption(options, "destination-path"));
        bool recursive = CliOptionParser.GetBoolOption(options, "recursive", false);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                TwinCatNodeInfo result = engineering.ExportTreeItemXml(
                    session,
                    new ExportTreeItemXmlRequest(treeItemPath, destinationPath, recursive));
                return StepExecutionOutcome.Success(
                    $"Tree item XML exported to {destinationPath}.",
                    CreateOutputs(("xmlPath", result.FilePath ?? destinationPath)),
                    [new EvidenceArtifact("exported-xml", destinationPath, "xml")]);
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringSaveAll(IReadOnlyDictionary<string, string> options)
    {
        LaunchVisualStudioRequest launchRequest = BuildLaunchVisualStudioRequest(options);
        CliWorkspacePaths? workspace = ResolveOptionalWorkspace(options);

        if (_engineeringSessionReuseScope is not null)
        {
            TwinCatEngineeringSession reusedSession = workspace?.SolutionPath is not null
                ? _engineeringSessionReuseScope.EnsureWorkspaceOpen(options, workspace)
                : _engineeringSessionReuseScope.EnsureLaunched(options);
            _engineeringSessionReuseScope.Engineering.SaveAll(reusedSession);
            return StepExecutionOutcome.Success(
                "Visual Studio SaveAll executed in reused run-plan engineering session.",
                CreateOutputs(
                    ("solutionPath", workspace?.SolutionPath),
                    ("projectPath", workspace?.ProjectPath)));
        }

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(launchRequest);
            if (workspace?.SolutionPath is not null)
            {
                engineering.OpenTwinCatSolution(session, new OpenTwinCatSolutionRequest(workspace.SolutionPath, workspace.ProjectPath));
            }

            engineering.SaveAll(session);
            return StepExecutionOutcome.Success(
                "Visual Studio SaveAll executed in stateless CLI mode.",
                CreateOutputs(
                    ("solutionPath", workspace?.SolutionPath),
                    ("projectPath", workspace?.ProjectPath)));
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    engineering.CloseVisualStudio(session, saveBeforeClose: false);
                }
                catch
                {
                }
                finally
                {
                    DisposeEngineeringSession(session);
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringCloseVisualStudio(IReadOnlyDictionary<string, string> options)
    {
        bool saveBeforeClose = CliOptionParser.GetBoolOption(options, "save-before-close", true);
        LaunchVisualStudioRequest launchRequest = BuildLaunchVisualStudioRequest(options);
        CliWorkspacePaths? workspace = ResolveOptionalWorkspace(options);

        if (_engineeringSessionReuseScope is not null)
        {
            if (workspace?.SolutionPath is not null)
            {
                _engineeringSessionReuseScope.EnsureWorkspaceOpen(options, workspace);
            }

            _engineeringSessionReuseScope.Close(saveBeforeClose);
            return StepExecutionOutcome.Success(
                "Visual Studio close executed in reused run-plan engineering session.",
                CreateOutputs(
                    ("saveBeforeClose", saveBeforeClose ? "true" : "false"),
                    ("solutionPath", workspace?.SolutionPath),
                    ("projectPath", workspace?.ProjectPath)));
        }

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        bool closed = false;
        try
        {
            session = engineering.LaunchVisualStudio(launchRequest);
            if (workspace?.SolutionPath is not null)
            {
                engineering.OpenTwinCatSolution(session, new OpenTwinCatSolutionRequest(workspace.SolutionPath, workspace.ProjectPath));
            }

            engineering.CloseVisualStudio(session, saveBeforeClose);
            closed = true;
            return StepExecutionOutcome.Success(
                "Visual Studio close executed in stateless CLI mode.",
                CreateOutputs(
                    ("saveBeforeClose", saveBeforeClose ? "true" : "false"),
                    ("solutionPath", workspace?.SolutionPath),
                    ("projectPath", workspace?.ProjectPath)));
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    if (!closed)
                    {
                        engineering.CloseVisualStudio(session, saveBeforeClose: false);
                    }
                }
                catch
                {
                }
                finally
                {
                    DisposeEngineeringSession(session);
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringBuildSolution(IReadOnlyDictionary<string, string> options)
    {
        int timeoutMs = CliOptionParser.GetIntOption(options, "timeout-ms", 300000);
        BuildSolutionEngine buildEngine = ParseBuildSolutionEngine(options);
        string configuration = CliOptionParser.GetOption(options, "configuration") ?? "Release";
        string platform = CliOptionParser.GetOption(options, "platform") ?? "TwinCAT OS (x64)";
        string? devenvPath = CliOptionParser.GetOption(options, "devenv-path");
        string? logFilePath = CliOptionParser.GetOption(options, "log-file-path", "log-file");
        string? msBuildPath = CliOptionParser.GetOption(options, "msbuild-path");
        IReadOnlyList<string>? projectPaths = ParseOptionalList(CliOptionParser.GetOption(options, "project-paths", "build-project-paths"));
        BuildSolutionRequest request = new(
            timeoutMs,
            buildEngine,
            configuration,
            platform,
            devenvPath,
            logFilePath,
            msBuildPath,
            projectPaths);

        if (buildEngine == BuildSolutionEngine.CommandLine)
        {
            CliWorkspacePaths workspace = ResolveWorkspace(options, requireSolutionPath: true);
            TwinCatEngineeringService service = new();
            BuildResult result = service.BuildSolutionFromCommandLine(workspace.SolutionPath!, request);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"BuildCurrentSolution failed. Engine={result.BuildEngine}; ExitCode={result.ExitCode}; LogFilePath={result.LogFilePath}.");
            }

            return BuildSolutionSuccessOutcome(result);
        }

        if (buildEngine == BuildSolutionEngine.MsBuildProjects)
        {
            CliWorkspacePaths workspace = ResolveWorkspace(options, requireSolutionPath: true);
            TwinCatEngineeringService service = new();
            BuildResult result = service.BuildSolutionFromMsBuildProjects(workspace.SolutionPath!, request);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"BuildCurrentSolution failed. Engine={result.BuildEngine}; ExitCode={result.ExitCode}; LogFilePath={result.LogFilePath}.");
            }

            return BuildSolutionSuccessOutcome(result);
        }

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                BuildResult result = engineering.BuildCurrentSolution(session, request);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"BuildCurrentSolution failed. LastBuildInfo={result.LastBuildInfo}; Engine={result.BuildEngine}.");
                }

                return BuildSolutionSuccessOutcome(result);
            });
    }

    private static BuildSolutionEngine ParseBuildSolutionEngine(IReadOnlyDictionary<string, string> options)
    {
        string? raw = CliOptionParser.GetOption(options, "build-engine", "engine");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return BuildSolutionEngine.Dte;
        }

        string normalized = raw.Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse(normalized, ignoreCase: true, out BuildSolutionEngine engine))
        {
            return engine;
        }

        throw new InvalidOperationException($"--build-engine must be Dte, CommandLine, or MsBuildProjects. Actual='{raw}'.");
    }

    private static StepExecutionOutcome BuildSolutionSuccessOutcome(BuildResult result)
    {
        List<EvidenceArtifact> evidence = [];
        if (!string.IsNullOrWhiteSpace(result.LogFilePath) && File.Exists(result.LogFilePath))
        {
            evidence.Add(new EvidenceArtifact("build-log", result.LogFilePath, "log"));
        }

        return StepExecutionOutcome.Success(
            "Solution build succeeded.",
            CreateOutputs(
                ("lastBuildInfo", result.LastBuildInfo.ToString(CultureInfo.InvariantCulture)),
                ("buildEngine", result.BuildEngine),
                ("exitCode", result.ExitCode?.ToString(CultureInfo.InvariantCulture)),
                ("commandLine", result.CommandLine),
                ("logFilePath", result.LogFilePath)),
            evidence);
    }

    private static StepExecutionOutcome ExecuteEngineeringActivateConfiguration(IReadOnlyDictionary<string, string> options)
    {
        bool saveConfigurationArchive = CliOptionParser.GetBoolOption(options, "save-configuration-archive", true);
        string? configurationArchivePath = CliOptionParser.GetOption(options, "configuration-archive-path");
        bool suppressUi = CliOptionParser.GetBoolOption(options, "suppress-ui", true);
        bool allowDteCommandFallback = CliOptionParser.GetBoolOption(options, "allow-dte-command-fallback", false);
        int activationTimeoutMs = CliOptionParser.GetIntOption(options, "activation-timeout-ms", 120000);

        return RunEngineeringOperation(
            options,
            (engineering, session, workspace) =>
            {
                ActivationResult result = engineering.ActivateConfiguration(
                    session,
                    new ActivateConfigurationRequest(
                        saveConfigurationArchive,
                        configurationArchivePath,
                        suppressUi,
                        allowDteCommandFallback,
                        activationTimeoutMs));

                List<EvidenceArtifact> evidence = [];
                if (!string.IsNullOrWhiteSpace(result.ConfigurationArchivePath) &&
                    File.Exists(result.ConfigurationArchivePath))
                {
                    evidence.Add(new EvidenceArtifact("configuration-archive", result.ConfigurationArchivePath, "archive"));
                }

                string currentConfigPath = Path.Combine(
                    Path.GetDirectoryName(workspace.ProjectPath)!,
                    "_Boot",
                    "TwinCAT OS (x64)",
                    "CurrentConfig.xml");

                if (File.Exists(currentConfigPath))
                {
                    evidence.Add(new EvidenceArtifact("current-config", currentConfigPath, "xml"));
                }

                return StepExecutionOutcome.Success(
                    "TwinCAT configuration activated through stateless CLI mode.",
                    CreateOutputs(
                        ("configurationArchivePath", result.ConfigurationArchivePath),
                        ("activationCommand", result.ActivationCommand),
                        ("suppressUi", suppressUi ? "true" : "false"),
                        ("allowDteCommandFallback", allowDteCommandFallback ? "true" : "false"),
                        ("activationTimeoutMs", activationTimeoutMs.ToString()),
                        ("attemptedCommands", string.Join(" | ", result.AttemptedCommands))),
                    evidence);
            });
    }

    private static StepExecutionOutcome ExecuteCppCreateProjectItem(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string relativePath = CliOptionParser.RequireOption(options, "relative-path", "path");
        CppProjectItemType itemType = ParseEnumOption(options, "item-type", CppProjectItemType.Infer);
        string? filter = CliOptionParser.GetOption(options, "filter");
        bool addToProject = CliOptionParser.GetBoolOption(options, "add-to-project", true);
        bool createPhysicalFile = CliOptionParser.GetBoolOption(options, "create-physical-file", true);
        ProjectItemConflictPolicy conflictPolicy = ParseEnumOption(options, "conflict-policy", ProjectItemConflictPolicy.FailIfExists);
        bool allowMsBuildFallback = CliOptionParser.GetBoolOption(options, "allow-msbuild-fallback", true);

        return RunCppProjectFileOperation(
            options,
            (engineering, twinCatProjectDirectory) =>
            {
                CppProjectItemResult result = engineering.CreateCppProjectItem(
                    twinCatProjectDirectory,
                    new CreateCppProjectItemRequest(
                        projectName,
                        relativePath,
                        itemType,
                        filter,
                        addToProject,
                        createPhysicalFile,
                        conflictPolicy,
                        allowMsBuildFallback));
                return StepExecutionOutcome.Success(
                    $"C++ project item {relativePath} created for {projectName}.",
                    CreateOutputs(
                        ("projectFilePath", result.ProjectFilePath),
                        ("filePath", result.FilePath),
                        ("itemType", result.ItemType.ToString()),
                        ("filter", result.Filter),
                        ("addedToProject", result.AddedToProject ? "true" : "false")),
                    [new EvidenceArtifact("cpp-project-file", result.ProjectFilePath, "vcxproj")]);
            });
    }

    private static StepExecutionOutcome ExecuteCppWriteProjectItemContent(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string relativePath = CliOptionParser.RequireOption(options, "relative-path", "path");
        string? contentText = CliOptionParser.GetOption(options, "content-text", "content");
        string? contentFile = CliOptionParser.GetOption(options, "content-file");
        string encoding = CliOptionParser.GetOption(options, "encoding") ?? "utf-8";
        string newLine = CliOptionParser.GetOption(options, "new-line", "newline") ?? "preserve";
        ProjectItemWritePolicy writePolicy = ParseEnumOption(options, "write-policy", ProjectItemWritePolicy.Overwrite);
        bool requireProjectRegistration = CliOptionParser.GetBoolOption(options, "require-project-registration", false);

        return RunCppProjectFileOperation(
            options,
            (engineering, twinCatProjectDirectory) =>
            {
                CppProjectItemContentResult result = engineering.WriteCppProjectItemContent(
                    twinCatProjectDirectory,
                    new WriteCppProjectItemContentRequest(
                        projectName,
                        relativePath,
                        contentText,
                        contentFile,
                        encoding,
                        newLine,
                        writePolicy,
                        requireProjectRegistration));
                return StepExecutionOutcome.Success(
                    $"C++ project item content written for {projectName}:{relativePath}.",
                    CreateOutputs(
                        ("filePath", result.FilePath),
                        ("sha256", result.Sha256),
                        ("bytesWritten", result.BytesWritten.ToString())));
            });
    }

    private static StepExecutionOutcome ExecuteCppRemoveProjectItem(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string relativePath = CliOptionParser.RequireOption(options, "relative-path", "path");
        CppProjectItemType itemType = ParseEnumOption(options, "item-type", CppProjectItemType.Infer);
        bool deletePhysicalFile = CliOptionParser.GetBoolOption(options, "delete-physical-file", true);
        bool removeFilterEntry = CliOptionParser.GetBoolOption(options, "remove-filter-entry", true);
        bool ignoreMissing = CliOptionParser.GetBoolOption(options, "ignore-missing", false);

        return RunCppProjectFileOperation(
            options,
            (engineering, twinCatProjectDirectory) =>
            {
                RemoveCppProjectItemResult result = engineering.RemoveCppProjectItem(
                    twinCatProjectDirectory,
                    new RemoveCppProjectItemRequest(
                        projectName,
                        relativePath,
                        itemType,
                        deletePhysicalFile,
                        removeFilterEntry,
                        ignoreMissing));
                return StepExecutionOutcome.Success(
                    $"C++ project item {relativePath} removed for {projectName}.",
                    CreateOutputs(
                        ("removedFromProject", result.RemovedFromProject ? "true" : "false"),
                        ("deletedFile", result.DeletedFile ? "true" : "false")));
            });
    }

    private static StepExecutionOutcome ExecuteCppSetProjectProperty(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string propertyName = CliOptionParser.RequireOption(options, "property-name");
        string value = CliOptionParser.RequireOption(options, "value");
        string? condition = CliOptionParser.GetOption(options, "condition");
        string? propertyGroupLabel = CliOptionParser.GetOption(options, "property-group-label");

        return RunCppProjectFileOperation(
            options,
            (engineering, twinCatProjectDirectory) =>
            {
                CppProjectPropertyResult result = engineering.SetCppProjectProperty(
                    twinCatProjectDirectory,
                    new SetCppProjectPropertyRequest(projectName, propertyName, value, condition, propertyGroupLabel));
                return StepExecutionOutcome.Success(
                    $"C++ project property {propertyName} set for {projectName}.",
                    CreateOutputs(
                        ("projectFilePath", result.ProjectFilePath),
                        ("propertyName", result.PropertyName),
                        ("condition", result.Condition)));
            });
    }

    private static StepExecutionOutcome ExecuteCppSetItemDefinitionProperty(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string toolName = CliOptionParser.RequireOption(options, "tool-name");
        string propertyName = CliOptionParser.RequireOption(options, "property-name");
        string value = CliOptionParser.RequireOption(options, "value");
        string? condition = CliOptionParser.GetOption(options, "condition");

        return RunCppProjectFileOperation(
            options,
            (engineering, twinCatProjectDirectory) =>
            {
                CppItemDefinitionPropertyResult result = engineering.SetCppItemDefinitionProperty(
                    twinCatProjectDirectory,
                    new SetCppItemDefinitionPropertyRequest(projectName, toolName, propertyName, value, condition));
                return StepExecutionOutcome.Success(
                    $"C++ item definition property {toolName}.{propertyName} set for {projectName}.",
                    CreateOutputs(
                        ("projectFilePath", result.ProjectFilePath),
                        ("toolName", result.ToolName),
                        ("propertyName", result.PropertyName),
                        ("condition", result.Condition)));
            });
    }

    private static StepExecutionOutcome ExecuteCppSetProjectItemMetadata(IReadOnlyDictionary<string, string> options)
    {
        string projectName = CliOptionParser.RequireOption(options, "cpp-project-name", "project-name");
        string relativePath = CliOptionParser.RequireOption(options, "relative-path", "path");
        CppProjectItemType itemType = ParseEnumOption(options, "item-type", CppProjectItemType.Infer);
        string metadataName = CliOptionParser.RequireOption(options, "metadata-name");
        string value = CliOptionParser.RequireOption(options, "value");
        string? condition = CliOptionParser.GetOption(options, "condition");

        return RunCppProjectFileOperation(
            options,
            (engineering, twinCatProjectDirectory) =>
            {
                CppProjectItemMetadataResult result = engineering.SetCppProjectItemMetadata(
                    twinCatProjectDirectory,
                    new SetCppProjectItemMetadataRequest(projectName, relativePath, itemType, metadataName, value, condition));
                return StepExecutionOutcome.Success(
                    $"C++ project item metadata {metadataName} set for {projectName}:{relativePath}.",
                    CreateOutputs(
                        ("projectFilePath", result.ProjectFilePath),
                        ("relativePath", result.RelativePath),
                        ("metadataName", result.MetadataName),
                        ("condition", result.Condition)));
            });
    }

}
