using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class StepInvokeCommand
{
    private static StepExecutionOutcome ExecuteEngineeringLaunchVisualStudio(IReadOnlyDictionary<string, string> options)
    {
        string progId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0";
        bool visible = CliOptionParser.GetBoolOption(options, "visible", false);
        int startupDelayMs = CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000);

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(new LaunchVisualStudioRequest(progId, startupDelayMs, visible));
            return StepExecutionOutcome.Success(
                "Visual Studio launch verified in stateless CLI mode.",
                CreateOutputs(
                    ("progId", progId),
                    ("visible", visible ? "true" : "false"),
                    ("startupDelayMs", startupDelayMs.ToString())));
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
                    session.Dispose();
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringCreateXaeSolution(IReadOnlyDictionary<string, string> options)
    {
        string solutionDirectory = Path.GetFullPath(CliOptionParser.RequireOption(options, "solution-directory", "output"));
        string solutionName = CliOptionParser.RequireOption(options, "solution-name");
        string projectName = CliOptionParser.RequireOption(options, "project-name");
        bool visible = CliOptionParser.GetBoolOption(options, "visible", false);
        int startupDelayMs = CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000);

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(new LaunchVisualStudioRequest(Visible: visible, StartupDelayMs: startupDelayMs));
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
                    session.Dispose();
                }
            }
        }
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

    private static StepExecutionOutcome ExecuteEngineeringOpenXaeSolution(IReadOnlyDictionary<string, string> options)
    {
        CliWorkspacePaths workspace = ResolveWorkspace(options, requireSolutionPath: true);
        bool visible = CliOptionParser.GetBoolOption(options, "visible", false);
        int startupDelayMs = CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000);

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(new LaunchVisualStudioRequest(Visible: visible, StartupDelayMs: startupDelayMs));
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
                    session.Dispose();
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
        bool visible = CliOptionParser.GetBoolOption(options, "visible", false);
        int startupDelayMs = CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000);
        CliWorkspacePaths? workspace = ResolveOptionalWorkspace(options);

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(new LaunchVisualStudioRequest(Visible: visible, StartupDelayMs: startupDelayMs));
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
                    session.Dispose();
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringCloseVisualStudio(IReadOnlyDictionary<string, string> options)
    {
        bool saveBeforeClose = CliOptionParser.GetBoolOption(options, "save-before-close", true);
        bool visible = CliOptionParser.GetBoolOption(options, "visible", false);
        int startupDelayMs = CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000);
        CliWorkspacePaths? workspace = ResolveOptionalWorkspace(options);

        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        bool closed = false;
        try
        {
            session = engineering.LaunchVisualStudio(new LaunchVisualStudioRequest(Visible: visible, StartupDelayMs: startupDelayMs));
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
                    session.Dispose();
                }
            }
        }
    }

    private static StepExecutionOutcome ExecuteEngineeringBuildSolution(IReadOnlyDictionary<string, string> options)
    {
        int timeoutMs = CliOptionParser.GetIntOption(options, "timeout-ms", 300000);

        return RunEngineeringOperation(
            options,
            (engineering, session, _) =>
            {
                BuildResult result = engineering.BuildCurrentSolution(session, new BuildSolutionRequest(timeoutMs));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"BuildCurrentSolution failed. LastBuildInfo={result.LastBuildInfo}.");
                }

                return StepExecutionOutcome.Success(
                    "Solution build succeeded.",
                    CreateOutputs(("lastBuildInfo", result.LastBuildInfo.ToString())));
            });
    }

    private static StepExecutionOutcome ExecuteEngineeringActivateConfiguration(IReadOnlyDictionary<string, string> options)
    {
        bool saveConfigurationArchive = CliOptionParser.GetBoolOption(options, "save-configuration-archive", true);
        string? configurationArchivePath = CliOptionParser.GetOption(options, "configuration-archive-path");

        return RunEngineeringOperation(
            options,
            (engineering, session, workspace) =>
            {
                ActivationResult result = engineering.ActivateConfiguration(
                    session,
                    new ActivateConfigurationRequest(saveConfigurationArchive, configurationArchivePath));

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
                        ("attemptedCommands", string.Join(" | ", result.AttemptedCommands))),
                    evidence);
            });
    }

}
