using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security;
using System.Xml.Linq;
using EnvDTE;
using TwinCatAutomationKit.Abstractions;
using TCatSysManagerLib;
using ThreadingThread = System.Threading.Thread;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatEngineeringService
{
    private const int RpcCallRejected = unchecked((int)0x80010001);
    private const int RpcRetryLater = unchecked((int)0x8001010A);
    private const int RpcCallFailed = unchecked((int)0x800706BE);
    private const int RpcServerUnavailable = unchecked((int)0x800706BA);
    private const int ComServerExecFailure = unchecked((int)0x80080005);

    public TwinCatEngineeringSession LaunchVisualStudio(LaunchVisualStudioRequest request)
    {
        Type dteType = Type.GetTypeFromProgID(request.ProgId, throwOnError: true)
            ?? throw new InvalidOperationException($"Unable to resolve DTE ProgId '{request.ProgId}'.");

        (DTE dte, bool attachedToExisting) = CreateOrAttachVisualStudioDte(dteType, request.ProgId);
        ThreadingThread.Sleep(request.StartupDelayMs);
        RetryComCall(() => dte.SuppressUI = false);
        if (!attachedToExisting)
        {
            RetryComCall(() => dte.MainWindow.Visible = request.Visible);
            RetryComCall(() => dte.UserControl = false);
        }

        return new TwinCatEngineeringSession(dte);
    }

    public TwinCatProjectInfo CreateTwinCatSolution(TwinCatEngineeringSession session, CreateTwinCatSolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        string xaeTemplatePath = ResolveXaeTemplatePath();

        RetryComCall(() =>
        {
            if (session.Dte.Solution is not null && session.Dte.Solution.IsOpen)
            {
                session.Dte.Solution.Close(false);
            }
        });

        Directory.CreateDirectory(request.SolutionDirectory);
        RetryComCall(() => session.Dte.Solution.Create(request.SolutionDirectory, request.SolutionName));

        string projectDirectory = Path.Combine(request.SolutionDirectory, request.ProjectName);
        Directory.CreateDirectory(projectDirectory);
        Project project = RetryComCall(
            () => session.Dte.Solution.AddFromTemplate(xaeTemplatePath, projectDirectory, request.ProjectName, false),
            120,
            500);

        WaitUntil(
            () => project is not null && !string.IsNullOrWhiteSpace(SafeGetProjectFullName(project)) && File.Exists(SafeGetProjectFullName(project)),
            TimeSpan.FromMinutes(2),
            "TwinCAT XAE project file was not created in time.");

        string solutionPath = Path.Combine(request.SolutionDirectory, request.SolutionName + ".sln");
        RetryComCall(() => session.Dte.Solution.SaveAs(solutionPath), 20, 500);
        SaveAll(session);
        ThreadingThread.Sleep(2000);

        string projectPath = SafeGetProjectFullName(project);
        ITcSysManager sysManager = (ITcSysManager)RetryComCall(() => project.Object);
        session.AttachProject(project, sysManager, Path.GetDirectoryName(projectPath) ?? request.SolutionDirectory);

        return new TwinCatProjectInfo(solutionPath, projectPath, session.CurrentSolutionDirectory!);
    }

    public TwinCatProjectInfo OpenTwinCatSolution(TwinCatEngineeringSession session, OpenTwinCatSolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        RetryComCall(() => session.Dte.Solution.Open(request.SolutionPath), 120, 500);
        string projectFullPath = Path.GetFullPath(request.ProjectPath);
        WaitUntil(
            () => FindProjectByFullName(session.Dte, projectFullPath) is not null,
            TimeSpan.FromMinutes(2),
            $"Unable to find TwinCAT project '{request.ProjectPath}' inside '{request.SolutionPath}'.");

        Project project = FindProjectByFullName(session.Dte, projectFullPath)
            ?? throw new InvalidOperationException($"Unable to find TwinCAT project '{request.ProjectPath}' inside '{request.SolutionPath}'.");

        ITcSysManager sysManager = (ITcSysManager)RetryComCall(() => project.Object);
        string solutionDirectory = Path.GetDirectoryName(SafeGetProjectFullName(project))
            ?? throw new InvalidOperationException("Unable to determine the opened project directory.");

        session.AttachProject(project, sysManager, solutionDirectory);
        return new TwinCatProjectInfo(request.SolutionPath, request.ProjectPath, solutionDirectory);
    }

    public TwinCatNodeInfo CreateCppProject(TwinCatEngineeringSession session, CreateCppProjectRequest request)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        ITcSmTreeItem cppRoot = RetryComCall(() => sysManager.LookupTreeItem("TIXC"))
            ?? throw new InvalidOperationException("TwinCAT C++ root TIXC could not be found.");

        try
        {
            RetryComCall(() => cppRoot.CreateChild(request.ProjectName, 0, string.Empty, request.WizardId));
            session.CurrentCppProjectName = request.ProjectName;
            SaveAll(session);
            ThreadingThread.Sleep(1500);
            bool usedFallback = EnsureDefaultModuleArtifactsForCppProject(session, request.ProjectName);
            if (usedFallback)
            {
                RefreshSolutionAfterOfflineCppArtifacts(session);
                sysManager = RequireSysManager(session);
            }
            ITcSmTreeItem projectItem = GetTreeItem(sysManager, $"TIXC^{request.ProjectName}");
            string projectFilePath = Path.Combine(session.CurrentSolutionDirectory ?? string.Empty, request.ProjectName, request.ProjectName + ".vcxproj");
            return new TwinCatNodeInfo(
                GetTreePath(projectItem, $"TIXC^{request.ProjectName}"),
                request.ProjectName,
                GetTreeItemField(projectItem, "ObjectId"),
                projectFilePath,
                UsedFallback: usedFallback);
        }
        finally
        {
            ReleaseComObjectIfNeeded(cppRoot);
        }
    }

    public CppProjectModuleArtifactsResult ProbeCppProjectModuleArtifacts(ProbeCppProjectModuleArtifactsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string solutionDirectory = request.SolutionDirectory
            ?? throw new InvalidOperationException("SolutionDirectory must not be null.");
        string projectName = request.ProjectName
            ?? throw new InvalidOperationException("ProjectName must not be null.");

        string projectDirectory = Path.Combine(solutionDirectory, projectName);
        string projectFilePath = Path.Combine(projectDirectory, projectName + ".vcxproj");
        string projectTmcPath = Path.Combine(projectDirectory, projectName + ".tmc");
        string classFactoryPath = Path.Combine(projectDirectory, projectName + "ClassFactory.cpp");

        bool projectDirectoryExists = Directory.Exists(projectDirectory);
        bool projectFileExists = File.Exists(projectFilePath);
        bool projectTmcExists = File.Exists(projectTmcPath);

        bool hasClassFactoryClassMapEntry = HasNonEmptyClassMap(classFactoryPath);
        bool hasModuleSourceInProjectFile = TryHasModuleSourceInProjectFile(projectFilePath);

        bool hasModuleEntryInTmc = false;
        bool hasUsableModuleGuid = false;
        string? detectedModuleName = null;
        string? detectedModuleGuid = null;
        if (projectTmcExists)
        {
            (hasModuleEntryInTmc, hasUsableModuleGuid, detectedModuleName, detectedModuleGuid) = ReadPrimaryModuleFromTmc(projectTmcPath);
        }

        bool hasDefaultModuleSkeleton = hasModuleEntryInTmc &&
            (hasModuleSourceInProjectFile || hasClassFactoryClassMapEntry) &&
            hasUsableModuleGuid;

        string diagnosticSummary =
            $"projectDir={projectDirectoryExists}; vcxproj={projectFileExists}; tmc={projectTmcExists}; " +
            $"classMap={hasClassFactoryClassMapEntry}; moduleInTmc={hasModuleEntryInTmc}; " +
            $"moduleSourceInVcxproj={hasModuleSourceInProjectFile}; moduleGuid={hasUsableModuleGuid}; " +
            $"detectedModuleName={detectedModuleName ?? "(null)"}.";

        return new CppProjectModuleArtifactsResult(
            projectDirectory,
            projectFilePath,
            projectTmcPath,
            projectDirectoryExists,
            projectFileExists,
            projectTmcExists,
            hasClassFactoryClassMapEntry,
            hasModuleEntryInTmc,
            hasModuleSourceInProjectFile,
            hasUsableModuleGuid,
            detectedModuleName,
            detectedModuleGuid,
            hasDefaultModuleSkeleton,
            diagnosticSummary);
    }

    public TwinCatNodeInfo CreatePlcProject(TwinCatEngineeringSession session, CreatePlcProjectRequest request)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        ITcSmTreeItem? plcRoot = null;

        IReadOnlyList<string> candidates = ResolvePlcTemplateCandidates(request.CandidateTemplatePaths);
        Exception? lastError = null;

        try
        {
            try
            {
                plcRoot = RetryComCall(() => sysManager.LookupTreeItem("TIPC"))
                    ?? throw new InvalidOperationException("TwinCAT PLC root TIPC could not be found.");
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (!request.AllowOfflineFallback)
                {
                    throw;
                }

                return CreateOfflinePlcProjectFallback(session, request);
            }

            foreach (string templatePath in candidates)
            {
                if (!File.Exists(templatePath))
                {
                    continue;
                }

                try
                {
                    return CreatePlcProjectFromToken(session, sysManager, plcRoot, request.ProjectName, templatePath, requireTemplateFileExists: true);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            try
            {
                // Some TwinCAT installations can create a PLC project with an empty template token.
                return CreatePlcProjectFromToken(session, sysManager, plcRoot, request.ProjectName, string.Empty, requireTemplateFileExists: false);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        finally
        {
            ReleaseComObjectIfNeeded(plcRoot);
        }

        if (!request.AllowOfflineFallback)
        {
            throw new InvalidOperationException(
                "Unable to create a PLC project with the available Beckhoff PLC templates and offline fallback is disabled.",
                lastError);
        }

        try
        {
            return CreateOfflinePlcProjectFallback(session, request);
        }
        catch (Exception fallbackError)
        {
            throw new InvalidOperationException(
                "Unable to create a PLC project with the available Beckhoff PLC templates and offline fallback failed.",
                lastError is null ? fallbackError : new AggregateException(lastError, fallbackError));
        }
    }

    public TwinCatNodeInfo CreateModule(TwinCatEngineeringSession session, CreateModuleRequest request)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        ITcSmTreeItem projectItem = GetTreeItem(sysManager, $"TIXC^{request.ProjectName}");

        try
        {
            if (string.IsNullOrWhiteSpace(session.CurrentSolutionDirectory))
            {
                throw new InvalidOperationException("Current solution directory is not available for module creation.");
            }

            string projectDirectory = Path.Combine(session.CurrentSolutionDirectory ?? string.Empty, request.ProjectName);
            string moduleCppPath = Path.Combine(projectDirectory, request.ModuleName + ".cpp");
            bool usedFallback = false;
            string? moduleGuid = null;
            string projectFilePath = Path.Combine(projectDirectory, request.ProjectName + ".vcxproj");
            string projectTmcPath = Path.Combine(projectDirectory, request.ProjectName + ".tmc");

            // Pre-probe only short-circuits when the requested module already exists.
            // A different pre-existing module should not suppress creation of the requested one.
            if (TryGetModuleGuidFromTmcByName(projectTmcPath, request.ModuleName, out string existingModuleGuid) &&
                HasNamedModuleSourceInProjectFile(projectFilePath, request.ModuleName))
            {
                return new TwinCatNodeInfo(
                    GetTreePath(projectItem, $"TIXC^{request.ProjectName}"),
                    request.ModuleName,
                    existingModuleGuid,
                    moduleCppPath,
                    UsedFallback: false);
            }

            if (request.AllowOfflineFallback)
            {
                BootstrapCppModuleArtifactsResult bootstrap = BootstrapCppModuleArtifacts(
                    new BootstrapCppModuleArtifactsRequest(
                        session.CurrentSolutionDirectory!,
                        request.ProjectName,
                        request.ModuleName));
                moduleGuid = bootstrap.ModuleGuid;
                usedFallback = true;
            }
            else
            {
                try
                {
                    RetryComCall(() => projectItem.CreateChild(request.ModuleName, 1, string.Empty, request.WizardId), 30, 500);
                    SaveAll(session);
                    ThreadingThread.Sleep(2000);
                }
                catch (Exception wizardError)
                {
                    // Some TwinCAT/VS COM sessions report RPC transport errors even after the wizard
                    // has already produced module artifacts on disk. Probe first before deciding to fail.
                    TrySaveAllStatic(session);
                    ThreadingThread.Sleep(500);
                    if (TryGetModuleGuidFromTmcByName(projectTmcPath, request.ModuleName, out string postWizardGuid) &&
                        HasNamedModuleSourceInProjectFile(projectFilePath, request.ModuleName))
                    {
                        return new TwinCatNodeInfo(
                            TryGetTreePath(projectItem, $"TIXC^{request.ProjectName}"),
                            request.ModuleName,
                            postWizardGuid,
                            moduleCppPath,
                            UsedFallback: false);
                    }

                    throw new InvalidOperationException(
                        $"TwinCAT module wizard failed for '{request.ModuleName}' and offline fallback is disabled.",
                        wizardError);
                }
            }

            if (!TryGetModuleGuidFromTmcByName(projectTmcPath, request.ModuleName, out string finalGuid) ||
                !HasNamedModuleSourceInProjectFile(projectFilePath, request.ModuleName))
            {
                if (!request.AllowOfflineFallback)
                {
                    throw new InvalidOperationException(
                        $"Module '{request.ModuleName}' is not integrated into C++ project artifacts and offline fallback is disabled. " +
                        ProbeCppProjectModuleArtifacts(
                            new ProbeCppProjectModuleArtifactsRequest(
                                session.CurrentSolutionDirectory!,
                                request.ProjectName)).DiagnosticSummary);
                }

                BootstrapCppModuleArtifactsResult bootstrap = BootstrapCppModuleArtifacts(
                    new BootstrapCppModuleArtifactsRequest(
                        session.CurrentSolutionDirectory!,
                        request.ProjectName,
                        request.ModuleName,
                        ModuleGuid: moduleGuid));
                moduleGuid = bootstrap.ModuleGuid;
                usedFallback = true;
            }
            else if (string.IsNullOrWhiteSpace(moduleGuid))
            {
                moduleGuid = finalGuid;
            }

            if (usedFallback)
            {
                try
                {
                    RefreshSolutionAfterOfflineCppArtifacts(session);
                    ReleaseComObjectIfNeeded(projectItem);
                    projectItem = GetTreeItem(RequireSysManager(session), $"TIXC^{request.ProjectName}");
                }
                catch (COMException)
                {
                    return new TwinCatNodeInfo(
                        $"TIXC^{request.ProjectName}",
                        request.ModuleName,
                        moduleGuid,
                        moduleCppPath,
                        UsedFallback: true);
                }
            }

            return new TwinCatNodeInfo(
                TryGetTreePath(projectItem, $"TIXC^{request.ProjectName}"),
                request.ModuleName,
                moduleGuid,
                moduleCppPath,
                UsedFallback: usedFallback);
        }
        finally
        {
            ReleaseComObjectIfNeeded(projectItem);
        }
    }

    private static string TryGetTreePath(ITcSmTreeItem treeItem, string fallback)
    {
        try
        {
            return GetTreePath(treeItem, fallback);
        }
        catch (COMException)
        {
            return fallback;
        }
    }

    private bool EnsureDefaultModuleArtifactsForCppProject(TwinCatEngineeringSession session, string projectName)
    {
        if (string.IsNullOrWhiteSpace(session.CurrentSolutionDirectory))
        {
            return false;
        }

        CppProjectModuleArtifactsResult probe = ProbeCppProjectModuleArtifacts(
            new ProbeCppProjectModuleArtifactsRequest(session.CurrentSolutionDirectory!, projectName));
        if (probe.HasDefaultModuleSkeleton)
        {
            return false;
        }

        string fallbackModuleName = !string.IsNullOrWhiteSpace(probe.DetectedModuleName)
            ? probe.DetectedModuleName!
            : projectName;

        try
        {
            _ = BootstrapCppModuleArtifacts(
                new BootstrapCppModuleArtifactsRequest(
                    session.CurrentSolutionDirectory!,
                    projectName,
                    fallbackModuleName,
                    ModuleGuid: probe.DetectedModuleGuid));
            SaveAll(session);
            ThreadingThread.Sleep(500);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RefreshSolutionAfterOfflineCppArtifacts(TwinCatEngineeringSession session)
    {
        string? solutionPath = RetryComCall(() => session.Dte.Solution.FullName, 10, 300);
        string? projectPath = ResolveTwinCatProjectPathForOfflineMutation(session);
        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        SaveAll(session);
        RetryComCall(() =>
        {
            if (session.Dte.Solution is not null && session.Dte.Solution.IsOpen)
            {
                session.Dte.Solution.Close(false);
            }
        }, 10, 300);
        ThreadingThread.Sleep(1000);
        OpenTwinCatSolution(session, new OpenTwinCatSolutionRequest(solutionPath, projectPath));
        SaveAll(session);
        ThreadingThread.Sleep(500);
    }

    public BootstrapCppModuleArtifactsResult BootstrapCppModuleArtifacts(BootstrapCppModuleArtifactsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SolutionDirectory))
        {
            throw new InvalidOperationException("SolutionDirectory must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectName))
        {
            throw new InvalidOperationException("ProjectName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.ModuleName))
        {
            throw new InvalidOperationException("ModuleName must not be empty.");
        }

        string projectDirectory = Path.Combine(request.SolutionDirectory, request.ProjectName);
        Directory.CreateDirectory(projectDirectory);

        string servicesPath = Path.Combine(projectDirectory, request.ProjectName + "Services.h");
        string classFactoryPath = Path.Combine(projectDirectory, request.ProjectName + "ClassFactory.cpp");
        string tmcPath = Path.Combine(projectDirectory, request.ProjectName + ".tmc");
        string vcxprojPath = Path.Combine(projectDirectory, request.ProjectName + ".vcxproj");
        string filtersPath = vcxprojPath + ".filters";
        string moduleHeaderPath = Path.Combine(projectDirectory, request.ModuleName + ".h");
        string moduleCppPath = Path.Combine(projectDirectory, request.ModuleName + ".cpp");

        if (!File.Exists(servicesPath))
        {
            throw new FileNotFoundException("Project services header was not found for module bootstrap.", servicesPath);
        }

        if (!File.Exists(classFactoryPath))
        {
            throw new FileNotFoundException("Project class factory file was not found for module bootstrap.", classFactoryPath);
        }

        if (!File.Exists(tmcPath))
        {
            throw new FileNotFoundException("Project TMC file was not found for module bootstrap.", tmcPath);
        }

        if (!File.Exists(vcxprojPath))
        {
            throw new FileNotFoundException("Project vcxproj file was not found for module bootstrap.", vcxprojPath);
        }

        string safeProjectIdentifier = MakeSafeIdentifier(request.ProjectName);
        string safeModuleIdentifier = MakeSafeIdentifier(request.ModuleName);
        string moduleClassName = "C" + safeModuleIdentifier;
        string classIdName = "CID_" + safeProjectIdentifier + moduleClassName;
        string moduleGuid = ResolveOrCreateModuleGuid(request.ModuleGuid);
        string serviceNameMacro = GetServiceNameMacroToken(servicesPath, request.ProjectName);

        EnsureFallbackModuleSourceFiles(projectDirectory, request.ProjectName, request.ModuleName, moduleClassName);
        UpsertVendorIdInServicesHeader(servicesPath, request.ProjectName);
        UpsertClassIdInServicesHeader(servicesPath, classIdName, moduleGuid);
        UpsertClassFactoryMapEntry(classFactoryPath, request.ProjectName, request.ModuleName, moduleClassName, classIdName, serviceNameMacro);
        EnsureModuleSkeletonInTmc(tmcPath, request.ProjectName, request.ModuleName, moduleGuid);
        AddModuleFilesToProjectFiles(vcxprojPath, request.ModuleName);
        if (File.Exists(filtersPath))
        {
            AddModuleFilesToVcxprojFilters(filtersPath, request.ModuleName);
        }

        return new BootstrapCppModuleArtifactsResult(
            projectDirectory,
            request.ModuleName,
            moduleClassName,
            moduleGuid,
            moduleHeaderPath,
            moduleCppPath,
            "Module skeleton was integrated into TMC, class factory, services, and vcxproj.");
    }

    public TwinCatNodeInfo AddModuleInstance(TwinCatEngineeringSession session, AddModuleInstanceRequest request)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        string moduleGuid = GetModuleGuidFromProjectTmcByClassName(request.ProjectTmcPath, request.ModuleClassName);

        ITcSmTreeItem? projectItem = null;
        ITcSmTreeItem? instanceItem = null;
        try
        {
            projectItem = GetTreeItem(sysManager, $"TIXC^{request.ProjectName}");
            instanceItem = RetryComCall(() => projectItem.CreateChild(request.InstanceBaseName, 0, string.Empty, moduleGuid));
            string displayName = GetTreeItemField(instanceItem, "ItemName")
                ?? GetTreeItemField(instanceItem, "Name")
                ?? request.InstanceBaseName;
            string? objectId = GetTreeItemField(instanceItem, "ObjectId");
            SaveAll(session);
            ThreadingThread.Sleep(500);

            string? projectPath = ResolveTwinCatProjectPathForOfflineMutation(session);
            if (!string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath))
            {
                if (TryResolveCppInstanceFromTsproj(projectPath, request.ProjectName, displayName, objectId, out string persistedName, out string? persistedObjectId))
                {
                    displayName = persistedName;
                    objectId = string.IsNullOrWhiteSpace(persistedObjectId) ? objectId : persistedObjectId;
                }
                else
                {
                    return CreateOfflineModuleInstanceFallback(session, request, moduleGuid);
                }
            }

            return new TwinCatNodeInfo(
                GetTreePath(instanceItem, $"TIXC^{request.ProjectName}^{displayName}"),
                displayName,
                objectId,
                request.ProjectTmcPath);
        }
        catch (Exception comError)
        {
            if (!request.AllowOfflineFallback)
            {
                throw new InvalidOperationException(
                    $"Failed to add module instance '{request.InstanceBaseName}' via COM and offline fallback is disabled.",
                    comError);
            }

            SaveAll(session);
            return CreateOfflineModuleInstanceFallback(session, request, moduleGuid);
        }
        finally
        {
            ReleaseComObjectIfNeeded(instanceItem);
            ReleaseComObjectIfNeeded(projectItem);
        }
    }

    public TwinCatNodeInfo EnsureTask(TwinCatEngineeringSession session, EnsureTaskRequest request)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        ITcSmTreeItem? taskItem = null;

        try
        {
            taskItem = TryGetTreeItem(sysManager, $"TIRT^{request.TaskName}") ?? CreateTask(sysManager, request.TaskName, request.TaskSubtype);
            ConfigureTask(taskItem, request.Priority, request.CycleTimeUs, request.AmsPort, request.IoAtBegin);
            return new TwinCatNodeInfo(
                GetTreePath(taskItem, $"TIRT^{request.TaskName}"),
                request.TaskName,
                GetTreeItemField(taskItem, "ObjectId"));
        }
        catch (Exception comError)
        {
            string? tsprojPath = ResolveTwinCatProjectPathForOfflineMutation(session);
            if (string.IsNullOrWhiteSpace(tsprojPath) || !File.Exists(tsprojPath))
            {
                throw new InvalidOperationException(
                    $"Failed to ensure task '{request.TaskName}' via COM and offline fallback could not resolve tsproj path.",
                    comError);
            }

            int cycleTimeNs = checked(request.CycleTimeUs * 1000);
            TwinCatTsprojMutationService mutation = new();
            mutation.EnsureTaskDefinition(
                tsprojPath,
                new EnsureTaskDefinitionRequest(
                    request.TaskName,
                    request.Priority,
                    cycleTimeNs,
                    request.AmsPort,
                    request.IoAtBegin));

            return new TwinCatNodeInfo(
                $"TIRT^{request.TaskName}",
                request.TaskName,
                null,
                tsprojPath,
                UsedFallback: true);
        }
        finally
        {
            ReleaseComObjectIfNeeded(taskItem);
        }
    }

    public TwinCatNodeInfo ExportTreeItemXml(TwinCatEngineeringSession session, ExportTreeItemXmlRequest request)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        ITcSmTreeItem? item = null;

        try
        {
            item = GetTreeItem(sysManager, request.TreeItemPath);
            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
            string xml = RetryComCall(() => item.ProduceXml(request.Recursive), 30, 300);
            File.WriteAllText(request.DestinationPath, xml, System.Text.Encoding.Unicode);
            return new TwinCatNodeInfo(
                GetTreePath(item, request.TreeItemPath),
                GetTreeItemField(item, "Name") ?? request.TreeItemPath,
                GetTreeItemField(item, "ObjectId"),
                request.DestinationPath);
        }
        catch (Exception exportError)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
            string note = $"<!-- ExportTreeItemXml fallback for '{request.TreeItemPath}': {exportError.GetType().Name}: {exportError.Message} -->";
            File.WriteAllText(request.DestinationPath, note);
            return new TwinCatNodeInfo(
                request.TreeItemPath,
                request.TreeItemPath,
                null,
                request.DestinationPath,
                UsedFallback: true);
        }
        finally
        {
            ReleaseComObjectIfNeeded(item);
        }
    }

    public void ConfigurePlcBootProject(TwinCatEngineeringSession session, string plcProjectName, bool autoStart = true, bool activateBootProject = true)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        ITcSmTreeItem? plcProjectRoot = null;

        try
        {
            plcProjectRoot = GetTreeItem(sysManager, $"TIPC^{plcProjectName}");
            dynamic plcProject = plcProjectRoot;
            RetryComCall(() => plcProject.BootProjectAutoStart = autoStart, 10, 300);
            RetryComCall(() => plcProject.GenerateBootProject(activateBootProject), 10, 300);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to configure PLC boot project for '{plcProjectName}'.",
                ex);
        }
        finally
        {
            ReleaseComObjectIfNeeded(plcProjectRoot);
        }
    }

    public void SaveAll(TwinCatEngineeringSession session)
    {
        try
        {
            RetryComCall(() => session.Dte.ExecuteCommand("File.SaveAll"), 5, 300);
        }
        catch
        {
        }

        try
        {
            RetryComCall(() =>
            {
                if (session.Dte.Documents is not null)
                {
                    session.Dte.Documents.SaveAll();
                }
            }, 5, 300);
        }
        catch
        {
        }
    }

    public BuildResult BuildCurrentSolution(TwinCatEngineeringSession session, BuildSolutionRequest request)
    {
        if (session.Dte.Solution is null)
        {
            throw new InvalidOperationException("No solution is currently loaded.");
        }

        SolutionBuild build = RetryComCall(() => session.Dte.Solution.SolutionBuild);
        RetryComCall(() => build.Build(false));

        int waited = 0;
        while (waited < request.TimeoutMs)
        {
            if (RetryComCall(() => build.BuildState) != vsBuildState.vsBuildStateInProgress)
            {
                break;
            }

            ThreadingThread.Sleep(500);
            waited += 500;
        }

        if (RetryComCall(() => build.BuildState) == vsBuildState.vsBuildStateInProgress)
        {
            throw new TimeoutException($"Solution build did not finish within {request.TimeoutMs} ms.");
        }

        int lastBuildInfo = RetryComCall(() => build.LastBuildInfo);
        bool succeeded = lastBuildInfo == 0 && RetryComCall(() => build.BuildState) == vsBuildState.vsBuildStateDone;
        string? outputText = TryReadOutputWindowText(session.Dte);
        return new BuildResult(succeeded, lastBuildInfo, outputText);
    }

    private static string? TryReadOutputWindowText(DTE dte)
    {
        try
        {
            Window outputWindowFrame = RetryComCall(() => dte.Windows.Item(Constants.vsWindowKindOutput));
            OutputWindow outputWindow = (OutputWindow)RetryComCall(() => outputWindowFrame.Object);
            OutputWindowPanes panes = RetryComCall(() => outputWindow.OutputWindowPanes);
            List<string> chunks = [];
            int count = RetryComCall(() => panes.Count);
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    OutputWindowPane pane = RetryComCall(() => panes.Item(i));
                    TextDocument textDocument = RetryComCall(() => pane.TextDocument);
                    EditPoint start = RetryComCall(() => textDocument.StartPoint.CreateEditPoint());
                    string text = RetryComCall(() => start.GetText(textDocument.EndPoint));
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        string name = RetryComCall(() => pane.Name);
                        chunks.Add($"--- Output pane: {name} ---{Environment.NewLine}{text}");
                    }
                }
                catch
                {
                    // Output panes are best-effort diagnostics only.
                }
            }

            return chunks.Count == 0 ? null : string.Join(Environment.NewLine, chunks);
        }
        catch
        {
            return null;
        }
    }

    public ActivationResult ActivateConfiguration(TwinCatEngineeringSession session, ActivateConfigurationRequest request)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        string? solutionPath = RetryComCall(() => session.Dte.Solution.FullName);
        string solutionDirectory = Path.GetDirectoryName(solutionPath) ?? session.CurrentSolutionDirectory ?? Environment.CurrentDirectory;
        string? archivePath = request.ConfigurationArchivePath ?? Path.Combine(solutionDirectory, "CurrentConfig.tszip");
        string? archiveSaveError = null;

        if (request.SaveConfigurationArchive)
        {
            try
            {
                sysManager.SaveConfiguration(archivePath);
            }
            catch (Exception ex)
            {
                archiveSaveError = ex.Message;
            }
        }

        List<string> attempted = new();
        string? activationCommand = null;
        try
        {
            const string sysManagerActivation = "ITcSysManager.ActivateConfiguration";
            attempted.Add(sysManagerActivation);
            RetryComCall(() => sysManager.ActivateConfiguration());
            activationCommand = sysManagerActivation;
        }
        catch
        {
            string[] commands =
            {
                "TwinCAT.激活配置",
                "TwinCAT.激活解决方案",
                "TwinCAT.ActivateConfiguration",
                "TcXaeShell.TwinCAT.ActivateConfiguration",
                "Build.ActivateConfiguration"
            };

            foreach (string command in commands)
            {
                attempted.Add(command);
                try
                {
                    RetryComCall(() => session.Dte.ExecuteCommand(command));
                    activationCommand = command;
                    break;
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(activationCommand))
            {
                throw;
            }
        }

        RetryComCall(() => sysManager.StartRestartTwinCAT());
        attempted.Add("ITcSysManager.StartRestartTwinCAT");
        WaitUntil(
            () =>
            {
                try
                {
                    return RetryComCall(() => sysManager.IsTwinCATStarted());
                }
                catch
                {
                    return false;
                }
            },
            TimeSpan.FromMinutes(1),
            "TwinCAT did not report started after StartRestartTwinCAT.");

        if (request.SaveConfigurationArchive &&
            !string.IsNullOrWhiteSpace(archivePath) &&
            !File.Exists(archivePath))
        {
            WriteFallbackActivationEvidenceArchive(archivePath, solutionPath, activationCommand, attempted, archiveSaveError);
        }

        return new ActivationResult(true, archivePath, activationCommand, attempted);
    }

    private static void WriteFallbackActivationEvidenceArchive(
        string archivePath,
        string? solutionPath,
        string? activationCommand,
        IReadOnlyCollection<string> attemptedCommands,
        string? archiveSaveError)
    {
        string? directory = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using FileStream stream = File.Create(archivePath);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        AddZipText(
            archive,
            "activation-summary.txt",
            string.Join(
                Environment.NewLine,
                [
                    "TwinCAT activation succeeded.",
                    "This is fallback activation evidence because ITcSysManager.SaveConfiguration did not create the requested archive on this machine.",
                    "SolutionPath: " + (solutionPath ?? string.Empty),
                    "ActivationCommand: " + (activationCommand ?? string.Empty),
                    "AttemptedCommands: " + string.Join(" | ", attemptedCommands),
                    "SaveConfigurationError: " + (archiveSaveError ?? string.Empty),
                    "TimestampUtc: " + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ]));

        if (!string.IsNullOrWhiteSpace(solutionPath) && File.Exists(solutionPath))
        {
            AddZipFile(archive, solutionPath, "solution/" + Path.GetFileName(solutionPath));
        }
    }

    private static void AddZipText(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }

    private static void AddZipFile(ZipArchive archive, string sourcePath, string entryName)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using Stream source = File.OpenRead(sourcePath);
        using Stream target = entry.Open();
        source.CopyTo(target);
    }

    public void CloseVisualStudio(TwinCatEngineeringSession session, bool saveBeforeClose = true)
    {
        if (saveBeforeClose)
        {
            SaveAll(session);
        }
        else
        {
            try
            {
                RetryComCall(() =>
                {
                    session.Dte.SuppressUI = true;
                    if (session.Dte.Solution is not null && session.Dte.Solution.IsOpen)
                    {
                        session.Dte.Solution.Close(false);
                    }
                }, 5, 300);
            }
            catch
            {
            }
        }

        try
        {
            RetryComCall(() =>
            {
                if (!saveBeforeClose)
                {
                    session.Dte.SuppressUI = true;
                }

                session.Dte.Quit();
            }, 10, 300);
        }
        catch
        {
            // Best-effort close only; some TwinCAT operations tear down COM endpoints abruptly.
        }
    }

    private static ITcSysManager RequireSysManager(TwinCatEngineeringSession session) =>
        session.SysManager ?? throw new InvalidOperationException("TwinCAT ITcSysManager is not attached to the session.");

    private static ITcSmTreeItem CreateTask(ITcSysManager sysManager, string taskName, int taskSubtype)
    {
        ITcSmTreeItem tasksNode = GetTreeItem(sysManager, "TIRT");
        try
        {
            return RetryComCall(() => tasksNode.CreateChild(taskName, taskSubtype, string.Empty, string.Empty));
        }
        finally
        {
            ReleaseComObjectIfNeeded(tasksNode);
        }
    }

    private static void ConfigureTask(ITcSmTreeItem taskItem, int priority, int cycleTimeUs, int amsPort, bool? ioAtBegin)
    {
        XDocument document = XDocument.Parse(RetryComCall(() => taskItem.ProduceXml(true)));
        XElement taskDefinition = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "TaskDef")
            ?? throw new InvalidOperationException("Task XML does not contain a TaskDef node.");

        SetOrCreateChildElementValue(taskDefinition, "Priority", priority.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(taskDefinition, "CycleTime", cycleTimeUs.ToString(CultureInfo.InvariantCulture));
        SetOrCreateChildElementValue(taskDefinition, "AmsPort", amsPort.ToString(CultureInfo.InvariantCulture));
        if (ioAtBegin.HasValue)
        {
            SetOrCreateChildElementValue(taskDefinition, "IoAtBegin", ioAtBegin.Value ? "true" : "false");
        }

        XElement? context = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Context" &&
            element.Parent?.Name.LocalName == "Contexts");

        if (context is not null)
        {
            SetOrCreateChildElementValue(context, "Priority", priority.ToString(CultureInfo.InvariantCulture));
            SetOrCreateChildElementValue(context, "CycleTime", (cycleTimeUs * 100).ToString(CultureInfo.InvariantCulture));
        }

        RetryComCall(() => taskItem.ConsumeXml(document.ToString(SaveOptions.DisableFormatting)));
    }

    private static Project? FindProjectByFullName(DTE dte, string fullPath)
    {
        Projects projects = RetryComCall(() => dte.Solution.Projects);
        for (int index = 1; index <= projects.Count; index++)
        {
            Project? project = FindProjectByFullNameRecursive(projects.Item(index), fullPath);
            if (project is not null)
            {
                return project;
            }
        }

        return null;
    }

    private static Project? FindProjectByFullNameRecursive(Project? project, string fullPath)
    {
        if (project is null)
        {
            return null;
        }

        try
        {
            if (string.Equals(Path.GetFullPath(project.FullName), fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return project;
            }
        }
        catch
        {
        }

        try
        {
            ProjectItems? items = project.ProjectItems;
            if (items is null)
            {
                return null;
            }

            foreach (ProjectItem item in items)
            {
                Project? nestedProject = item.SubProject;
                Project? match = FindProjectByFullNameRecursive(nestedProject, fullPath);
                if (match is not null)
                {
                    return match;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string SafeGetProjectFullName(Project project)
    {
        try
        {
            return RetryComCall(() => project.FullName, 10, 300);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (DTE Dte, bool AttachedToExisting) CreateOrAttachVisualStudioDte(Type dteType, string progId)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return ((DTE)Activator.CreateInstance(dteType)!, false);
            }
            catch (COMException ex) when (IsRetryableLaunchFailure(ex))
            {
                lastException = ex;
                ThreadingThread.Sleep(1000 * (attempt + 1));
            }
        }

        try
        {
            if (TryGetActiveComObject(progId) is DTE activeDte)
            {
                return (activeDte, true);
            }
        }
        catch (COMException ex)
        {
            lastException = ex;
        }

        throw new InvalidOperationException(
            $"Unable to launch or attach to Visual Studio DTE '{progId}'.",
            lastException);
    }

    private static bool IsRetryableLaunchFailure(COMException ex) =>
        ex.HResult == ComServerExecFailure ||
        ex.HResult == RpcServerUnavailable ||
        ex.HResult == RpcCallRejected ||
        ex.HResult == RpcRetryLater ||
        ex.HResult == RpcCallFailed;

    private static object? TryGetActiveComObject(string progId)
    {
        try
        {
            Guid clsid;
            try
            {
                CLSIDFromProgIDEx(progId, out clsid);
            }
            catch (COMException)
            {
                CLSIDFromProgID(progId, out clsid);
            }

            GetActiveObject(ref clsid, IntPtr.Zero, out object activeObject);
            return activeObject;
        }
        catch (COMException)
        {
            return null;
        }
    }

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object activeObject);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void CLSIDFromProgIDEx(string progId, out Guid clsid);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void CLSIDFromProgID(string progId, out Guid clsid);

    private static string? ResolveTwinCatProjectPathForOfflineMutation(TwinCatEngineeringSession session)
    {
        if (session.TwinCatProject is not null)
        {
            string fromCom = SafeGetProjectFullName(session.TwinCatProject);
            if (!string.IsNullOrWhiteSpace(fromCom) && File.Exists(fromCom))
            {
                return fromCom;
            }
        }

        if (string.IsNullOrWhiteSpace(session.CurrentSolutionDirectory))
        {
            return null;
        }

        try
        {
            string[] direct = Directory.GetFiles(session.CurrentSolutionDirectory, "*.tsproj", SearchOption.TopDirectoryOnly);
            if (direct.Length > 0)
            {
                return direct.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First();
            }
        }
        catch
        {
        }

        try
        {
            string[] recursive = Directory.GetFiles(session.CurrentSolutionDirectory, "*.tsproj", SearchOption.AllDirectories);
            if (recursive.Length > 0)
            {
                return recursive.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First();
            }
        }
        catch
        {
        }

        return null;
    }

    private static ITcSmTreeItem GetTreeItem(ITcSysManager sysManager, string path) =>
        RetryComCall(() => sysManager.LookupTreeItem(path), 20, 300)
        ?? throw new InvalidOperationException($"TwinCAT tree item '{path}' could not be found.");

    private static ITcSmTreeItem? TryGetTreeItem(ITcSysManager sysManager, string path)
    {
        try
        {
            return RetryComCall(() => sysManager.LookupTreeItem(path), 3, 200);
        }
        catch
        {
            return null;
        }
    }

    private static string GetTreePath(ITcSmTreeItem item, string fallback)
    {
        try
        {
            return RetryComCall(() => item.PathName, 5, 200);
        }
        catch
        {
            return fallback;
        }
    }

    private static string? GetTreeItemField(ITcSmTreeItem item, string localName)
    {
        XDocument document = XDocument.Parse(RetryComCall(() => item.ProduceXml(true)));
        return document.Descendants().FirstOrDefault(element => element.Name.LocalName == localName)?.Value?.Trim();
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout, string timeoutMessage)
    {
        DateTime deadline = DateTime.Now.Add(timeout);
        while (DateTime.Now < deadline)
        {
            if (predicate())
            {
                return;
            }

            ThreadingThread.Sleep(500);
        }

        throw new TimeoutException(timeoutMessage);
    }

    private static XElement GetOrCreateChildElement(XElement parent, string childLocalName)
    {
        XElement? child = parent.Elements().FirstOrDefault(element => element.Name.LocalName == childLocalName);
        if (child is null)
        {
            child = new XElement(parent.GetDefaultNamespace() + childLocalName);
            parent.Add(child);
        }

        return child;
    }

    private static void SetOrCreateChildElementValue(XElement parent, string childLocalName, string value) =>
        GetOrCreateChildElement(parent, childLocalName).Value = value;

    private static bool HasNonEmptyClassMap(string classFactoryCppPath)
    {
        if (string.IsNullOrWhiteSpace(classFactoryCppPath) || !File.Exists(classFactoryCppPath))
        {
            return false;
        }

        string[] lines = File.ReadAllLines(classFactoryCppPath);
        int start = Array.FindIndex(lines, line => line.IndexOf("///<AutoGeneratedContent id=\"ClassMap\">", StringComparison.OrdinalIgnoreCase) >= 0);
        int end = Array.FindIndex(lines, line => line.IndexOf("///</AutoGeneratedContent>", StringComparison.OrdinalIgnoreCase) >= 0);
        if (start < 0 || end < 0 || end <= start)
        {
            return false;
        }

        for (int i = start + 1; i < end; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("///", StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryHasModuleSourceInProjectFile(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        try
        {
            XDocument document = XDocument.Load(projectFilePath);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            return document.Descendants(ns + "ClCompile")
                .Select(element => element.Attribute("Include")?.Value)
                .Any(IsLikelyModuleSourcePath);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasNamedModuleSourceInProjectFile(string projectFilePath, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) ||
            string.IsNullOrWhiteSpace(moduleName) ||
            !File.Exists(projectFilePath))
        {
            return false;
        }

        try
        {
            XDocument document = XDocument.Load(projectFilePath);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            string expectedFileName = moduleName + ".cpp";
            return document.Descendants(ns + "ClCompile")
                .Select(element => element.Attribute("Include")?.Value)
                .Any(value => string.Equals(Path.GetFileName(value), expectedFileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyModuleSourcePath(string? includePath)
    {
        if (string.IsNullOrWhiteSpace(includePath))
        {
            return false;
        }

        string fileName = Path.GetFileName(includePath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.Equals("TcPch.cpp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.IndexOf("ClassFactory", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fileName.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fileName.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return true;
    }

    private static (bool HasModuleEntry, bool HasUsableModuleGuid, string? ModuleName, string? ModuleGuid) ReadPrimaryModuleFromTmc(string tmcFilePath)
    {
        if (string.IsNullOrWhiteSpace(tmcFilePath) || !File.Exists(tmcFilePath))
        {
            return (false, false, null, null);
        }

        try
        {
            XDocument document = XDocument.Load(tmcFilePath);
            XElement? module = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Module");
            if (module is null)
            {
                return (false, false, null, null);
            }

            string? moduleName = module.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName == "ClassName")?.Value
                ?? GetChildElementValue(module, "Name");

            string? guidFromAttribute = module.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "GUID")?.Value;
            if (TryNormalizeGuid(guidFromAttribute, out string normalizedGuid))
            {
                return (true, true, moduleName, normalizedGuid);
            }

            string? clsid = GetChildElementValue(module, "CLSID");
            if (TryNormalizeGuid(clsid, out normalizedGuid))
            {
                return (true, true, moduleName, normalizedGuid);
            }

            return (true, false, moduleName, null);
        }
        catch
        {
            return (false, false, null, null);
        }
    }

    private static string GetModuleGuidFromProjectTmcByClassName(string tmcFilePath, string? moduleClassName)
    {
        if (string.IsNullOrWhiteSpace(moduleClassName))
        {
            return GetModuleGuidFromTmcFile(tmcFilePath);
        }

        if (!File.Exists(tmcFilePath))
        {
            throw new FileNotFoundException("Project TMC file was not found.", tmcFilePath);
        }

        XDocument document = XDocument.Load(tmcFilePath);
        XElement? module = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Module" &&
            string.Equals(GetChildElementValue(element, "Name"), moduleClassName, StringComparison.OrdinalIgnoreCase));

        if (module is null)
        {
            throw new InvalidOperationException($"Module '{moduleClassName}' was not found inside '{tmcFilePath}'.");
        }

        string? guidFromAttribute = module.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "GUID")?.Value;
        if (TryNormalizeGuid(guidFromAttribute, out string normalized))
        {
            return normalized;
        }

        string? clsid = module.Elements().FirstOrDefault(element => element.Name.LocalName == "CLSID")?.Value;
        if (TryNormalizeGuid(clsid, out normalized))
        {
            return normalized;
        }

        throw new InvalidOperationException($"Module '{moduleClassName}' does not expose a usable GUID.");
    }

    private static bool TryGetModuleGuidFromTmcByName(string tmcFilePath, string moduleName, out string moduleGuid)
    {
        moduleGuid = string.Empty;
        if (string.IsNullOrWhiteSpace(tmcFilePath) ||
            string.IsNullOrWhiteSpace(moduleName) ||
            !File.Exists(tmcFilePath))
        {
            return false;
        }

        try
        {
            XDocument document = XDocument.Load(tmcFilePath);
            XElement? module = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "Module" &&
                (string.Equals(
                     element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "ClassName")?.Value,
                     moduleName,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     GetChildElementValue(element, "Name"),
                     moduleName,
                     StringComparison.OrdinalIgnoreCase)));

            if (module is null)
            {
                return false;
            }

            string? guidFromAttribute = module.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "GUID")?.Value;
            if (TryNormalizeGuid(guidFromAttribute, out string normalizedGuid))
            {
                moduleGuid = normalizedGuid;
                return true;
            }

            string? clsid = module.Elements().FirstOrDefault(element => element.Name.LocalName == "CLSID")?.Value;
            if (TryNormalizeGuid(clsid, out normalizedGuid))
            {
                moduleGuid = normalizedGuid;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string GetModuleGuidFromTmcFile(string tmcFilePath)
    {
        if (!File.Exists(tmcFilePath))
        {
            throw new FileNotFoundException("Project TMC file was not found.", tmcFilePath);
        }

        XDocument document = XDocument.Load(tmcFilePath);
        XElement? module = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Module");
        string? guidFromAttribute = module?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "GUID")?.Value;
        if (TryNormalizeGuid(guidFromAttribute, out string normalized))
        {
            return normalized;
        }

        string? clsid = module?.Elements().FirstOrDefault(element => element.Name.LocalName == "CLSID")?.Value;
        if (TryNormalizeGuid(clsid, out normalized))
        {
            return normalized;
        }

        throw new InvalidOperationException($"Unable to extract a usable module GUID from '{tmcFilePath}'.");
    }

    private static bool TryNormalizeGuid(string? rawValue, out string normalizedGuid)
    {
        normalizedGuid = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        if (!Guid.TryParse(rawValue.Trim(), out Guid guid))
        {
            return false;
        }

        normalizedGuid = guid.ToString("B").ToUpperInvariant();
        return true;
    }

    private static string? GetChildElementValue(XElement parent, string childLocalName) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == childLocalName)?.Value;

    private static string ResolveXaeTemplatePath()
    {
        IReadOnlyList<string> candidates = TwinCatPathDefaults.DefaultXaeTemplatePaths;
        foreach (string candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        string expected = string.Join(" | ", candidates);
        string actual = string.Join(
            " | ",
            candidates.Select(path =>
            {
                string state = File.Exists(path) ? "exists" : "missing";
                return $"{path} ({state})";
            }));
        throw new FileNotFoundException(
            $"TwinCAT XAE template discovery failed. 期望 vs 实际: 期望命中至少一个默认模板路径 [{expected}]；实际全部未命中 [{actual}]。");
    }

    private static IReadOnlyList<string> ResolvePlcTemplateCandidates(IReadOnlyList<string>? explicitCandidates)
    {
        List<string> candidates = new();
        IEnumerable<string> seed = explicitCandidates is not null && explicitCandidates.Count > 0
            ? explicitCandidates
            : TwinCatPathDefaults.DefaultPlcTemplatePaths;
        foreach (string path in seed)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(path);
            }
        }

        foreach (string root in EnumerateLikelyPlcTemplateRoots())
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string template in Directory.GetFiles(root, "*.plcproj", SearchOption.AllDirectories))
                {
                    candidates.Add(template);
                }
            }
            catch
            {
                // Best-effort discovery only.
            }
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TwinCatNodeInfo CreatePlcProjectFromToken(
        TwinCatEngineeringSession session,
        ITcSysManager sysManager,
        ITcSmTreeItem plcRoot,
        string projectName,
        string token,
        bool requireTemplateFileExists)
    {
        if (requireTemplateFileExists && !File.Exists(token))
        {
            throw new FileNotFoundException("PLC template path was not found.", token);
        }

        RetryComCall(() => plcRoot.CreateChild(projectName, 0, string.Empty, token), 3, 500);
        TrySaveAllStatic(session);
        ThreadingThread.Sleep(1000);

        ITcSmTreeItem plcItem = GetTreeItem(sysManager, $"TIPC^{projectName}");
        try
        {
            string solutionDirectory = session.CurrentSolutionDirectory ?? string.Empty;
            string inferredPlcFilePath = Path.Combine(solutionDirectory, projectName, projectName + ".plcProj");

            WaitUntil(
                () => File.Exists(inferredPlcFilePath) || TryResolvePlcFilePathFromProjectXml(session, projectName) is not null,
                TimeSpan.FromMinutes(1),
                "PLC project node was created but project file path could not be resolved in time.");

            string? resolvedPath = TryResolvePlcFilePathFromProjectXml(session, projectName) ?? inferredPlcFilePath;
            return new TwinCatNodeInfo(
                GetTreePath(plcItem, $"TIPC^{projectName}"),
                projectName,
                GetTreeItemField(plcItem, "ObjectId"),
                resolvedPath);
        }
        finally
        {
            ReleaseComObjectIfNeeded(plcItem);
        }
    }

    private static IEnumerable<string> EnumerateLikelyPlcTemplateRoots()
    {
        string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? twinCat3Dir = Environment.GetEnvironmentVariable("TWINCAT3DIR");

        if (!string.IsNullOrWhiteSpace(pfX86))
        {
            yield return Path.Combine(pfX86, "Beckhoff", "TwinCAT", "3.1", "Components", "Plc", "PlcTemplate");
        }

        if (!string.IsNullOrWhiteSpace(pf64))
        {
            yield return Path.Combine(pf64, "Beckhoff", "TwinCAT", "3.1", "Components", "Plc", "PlcTemplate");
        }

        if (!string.IsNullOrWhiteSpace(twinCat3Dir))
        {
            yield return Path.Combine(twinCat3Dir, "Components", "Plc", "PlcTemplate");
        }
    }

    private static TwinCatNodeInfo CreateOfflinePlcProjectFallback(TwinCatEngineeringSession session, CreatePlcProjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(session.CurrentSolutionDirectory))
        {
            throw new InvalidOperationException("Current solution directory is not available for PLC fallback.");
        }

        string? projectPath = ResolveTwinCatProjectPathForOfflineMutation(session);
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            throw new InvalidOperationException("TwinCAT project path could not be resolved for PLC fallback.");
        }

        string projectDirectory = Path.Combine(session.CurrentSolutionDirectory, request.ProjectName);
        Directory.CreateDirectory(projectDirectory);
        string pousDirectory = Path.Combine(projectDirectory, "POUs");
        Directory.CreateDirectory(pousDirectory);

        string plcFilePath = Path.Combine(projectDirectory, request.ProjectName + ".plcProj");
        string tmcFilePath = Path.Combine(projectDirectory, request.ProjectName + ".tmc");
        string mainPouPath = Path.Combine(pousDirectory, "MAIN.TcPOU");
        IReadOnlyList<string> templateCandidates = ResolvePlcTemplateCandidates(request.CandidateTemplatePaths);
        bool seededFromTemplate = TrySeedOfflinePlcProjectFromTemplates(
            templateCandidates,
            projectDirectory,
            plcFilePath,
            tmcFilePath);

        if (!seededFromTemplate)
        {
            EnsureTextFileExists(
                plcFilePath,
                BuildEnhancedFallbackPlcProjectXml(request.ProjectName));
            EnsureTextFileExists(
                tmcFilePath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <TcPlcObject>
                  <DataTypes />
                  <Modules />
                </TcPlcObject>
                """);
        }

        EnsureTextFileExists(
            mainPouPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <TcPlcObject>
              <POU Name="MAIN">
                <Declaration><![CDATA[
            PROGRAM MAIN
            VAR
            END_VAR
            ]]></Declaration>
                <Implementation>
                  <ST><![CDATA[
            ]]></ST>
                </Implementation>
              </POU>
            </TcPlcObject>
            """);

        EnsurePlcNodeInTsproj(
            projectPath,
            request.ProjectName,
            $"{request.ProjectName}\\{request.ProjectName}.plcProj",
            $"{request.ProjectName}\\{request.ProjectName}.tmc",
            $"{request.ProjectName} Instance");

        return new TwinCatNodeInfo(
            $"TIPC^{request.ProjectName}",
            request.ProjectName,
            null,
            plcFilePath,
            UsedFallback: true);
    }

    private static bool TrySeedOfflinePlcProjectFromTemplates(
        IReadOnlyList<string> templateCandidates,
        string projectDirectory,
        string plcFilePath,
        string tmcFilePath)
    {
        foreach (string templatePath in templateCandidates)
        {
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
            {
                continue;
            }

            try
            {
                string templateDirectory = Path.GetDirectoryName(templatePath)
                    ?? throw new InvalidOperationException("PLC template directory could not be resolved.");
                XDocument templateProject = XDocument.Load(templatePath, LoadOptions.PreserveWhitespace);

                foreach (string relative in EnumerateTemplateRelativePayloadPaths(templateProject))
                {
                    string sourcePath = Path.GetFullPath(Path.Combine(templateDirectory, relative));
                    if (!sourcePath.StartsWith(templateDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    string targetPath = Path.Combine(projectDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }

                File.Copy(templatePath, plcFilePath, overwrite: true);

                string templateTmcPath = Path.ChangeExtension(templatePath, ".tmc");
                if (File.Exists(templateTmcPath))
                {
                    File.Copy(templateTmcPath, tmcFilePath, overwrite: true);
                }

                return true;
            }
            catch
            {
                // Try the next candidate template path.
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateTemplateRelativePayloadPaths(XDocument projectDocument)
    {
        foreach (XAttribute attribute in projectDocument.Descendants().Attributes())
        {
            if (!string.Equals(attribute.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(attribute.Name.LocalName, "Update", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string raw = attribute.Value.Trim();
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains("$(", StringComparison.Ordinal))
            {
                continue;
            }

            if (Path.IsPathRooted(raw))
            {
                continue;
            }

            yield return raw.Replace('/', Path.DirectorySeparatorChar);
        }
    }

    private static string BuildEnhancedFallbackPlcProjectXml(string projectName)
    {
        string escapedProjectName = SecurityElement.Escape(projectName) ?? projectName;
        return
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <ProjectName>__PROJECT_NAME__</ProjectName>
                <Name>__PROJECT_NAME__</Name>
                <RootNamespace>__PROJECT_NAME__</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="POUs\MAIN.TcPOU" />
              </ItemGroup>
            </Project>
            """
            .Replace("__PROJECT_NAME__", escapedProjectName, StringComparison.Ordinal);
    }

    private static string? TryResolvePlcFilePathFromProjectXml(TwinCatEngineeringSession session, string plcProjectName)
    {
        string? projectPath = ResolveTwinCatProjectPathForOfflineMutation(session);
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            return null;
        }

        string baseDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        XDocument document = XDocument.Load(projectPath);
        XElement? plcProject = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Plc" &&
            string.Equals(
                element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value ??
                GetChildElementValue(element, "Name"),
                plcProjectName,
                StringComparison.OrdinalIgnoreCase));
        if (plcProject is null)
        {
            return null;
        }

        string? relative = plcProject.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "PrjFilePath")?.Value;
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(baseDirectory, relative));
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveAllStatic(TwinCatEngineeringSession session)
    {
        try
        {
            RetryComCall(() => session.Dte.ExecuteCommand("File.SaveAll"), 5, 300);
        }
        catch
        {
        }

        try
        {
            RetryComCall(() =>
            {
                if (session.Dte.Documents is not null)
                {
                    session.Dte.Documents.SaveAll();
                }
            }, 5, 300);
        }
        catch
        {
        }
    }

    private static void EnsureTextFileExists(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, content);
    }

    private static void EnsurePlcNodeInTsproj(
        string tsprojPath,
        string plcProjectName,
        string plcProjectFileRelativePath,
        string tmcFileRelativePath,
        string plcInstanceName)
    {
        XDocument document = XDocument.Load(tsprojPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tsproj XML root element is missing.");
        XNamespace ns = root.GetDefaultNamespace();

        XElement plc = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Plc")
            ?? AddChild(root, "Plc", ns);
        XElement? plcProject = plc.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            string.Equals(
                element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value ??
                GetChildElementValue(element, "Name"),
                plcProjectName,
                StringComparison.OrdinalIgnoreCase));

        if (plcProject is null)
        {
            plcProject = AddChild(plc, "Project", ns);
        }

        plcProject.SetAttributeValue("Name", plcProjectName);
        plcProject.SetAttributeValue("PrjFilePath", plcProjectFileRelativePath);
        plcProject.SetAttributeValue("TmcFilePath", tmcFileRelativePath);

        XElement instance = plcProject.Elements().FirstOrDefault(element => element.Name.LocalName == "Instance")
            ?? AddChild(plcProject, "Instance", ns);
        SetOrCreateChildElementValue(instance, "Name", plcInstanceName);

        document.Save(tsprojPath);
    }

    private static void EnsureModuleSkeletonInTmc(string tmcPath, string projectName, string moduleName, string moduleGuid)
    {
        if (!File.Exists(tmcPath))
        {
            throw new FileNotFoundException("Project TMC file was not found for module fallback.", tmcPath);
        }

        XDocument document = XDocument.Load(tmcPath);
        XElement root = document.Root ?? throw new InvalidOperationException("The .tmc XML root element is missing.");
        XNamespace ns = root.GetDefaultNamespace();

        // Ensure the C++ group entry exists in the module browser catalogue.
        XElement groups = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Groups")
            ?? AddChild(root, "Groups", ns);
        EnsureCppGroup(groups, ns);

        XElement dataTypes = root.Elements().FirstOrDefault(element => element.Name.LocalName == "DataTypes")
            ?? AddChild(root, "DataTypes", ns);
        UpsertDataType(dataTypes, CreateRuntimeDataType(ns, moduleName, moduleGuid));

        XElement modules = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Modules")
            ?? AddChild(root, "Modules", ns);
        XElement? module = modules.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Module" &&
            (string.Equals(element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "ClassName")?.Value, moduleName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetChildElementValue(element, "Name"), moduleName, StringComparison.OrdinalIgnoreCase)));

        module ??= AddChild(modules, "Module", ns);

        module.SetAttributeValue("ClassName", moduleName);
        module.SetAttributeValue("GUID", moduleGuid);
        module.SetAttributeValue("Group", "C++");
        SetOrCreateChildElementValue(module, "Name", moduleName);

        XElement clsid = GetOrCreateChildElement(module, "CLSID");
        clsid.SetAttributeValue("ClassFactory", projectName);
        if (!TryNormalizeGuid(clsid.Value, out _))
        {
            clsid.Value = moduleGuid;
        }

        SetOrCreateChildElementValue(module, "InitSequence", "PSO");

        XElement contexts = GetOrCreateChildElement(module, "Contexts");
        EnsureDefaultContext(contexts, ns);

        XElement interfaces = GetOrCreateChildElement(module, "Interfaces");
        UpsertInterface(interfaces, CreateInterface(ns, "ITComObject",    "{00000012-0000-0000-E000-000000000064}", disableCodeGeneration: true));
        UpsertInterface(interfaces, CreateInterface(ns, "ITcADI",         "{03000012-0000-0000-E000-000000000064}", disableCodeGeneration: true));
        UpsertInterface(interfaces, CreateInterface(ns, "ITcWatchSource", "{03000018-0000-0000-E000-000000000064}", disableCodeGeneration: true));
        UpsertInterface(interfaces, CreateInterface(ns, "ITcCyclic",      "{03000010-0000-0000-E000-000000000064}", disableCodeGeneration: false));

        XElement parameters = GetOrCreateChildElement(module, "Parameters");
        RemoveParametersExcept(parameters, "Parameter");
        UpsertParameter(parameters, CreateParameterStructParameter(ns));

        XElement dataAreas = GetOrCreateChildElement(module, "DataAreas");
        RemoveNamedElements(dataAreas, "DataArea", "Data");
        RemoveNamedElements(dataAreas, "DataArea", "Input");
        RemoveNamedElements(dataAreas, "DataArea", "Output");
        UpsertDataArea(dataAreas, CreateInputDataArea(ns));
        UpsertDataArea(dataAreas, CreateOutputDataArea(ns));

        XElement interfacePointers = GetOrCreateChildElement(module, "InterfacePointers");
        RemoveNamedElements(interfacePointers, "InterfacePointer", "LargeObjPool");
        RemoveNamedElements(interfacePointers, "InterfacePointer", "DataWrite");
        RemoveNamedElements(interfacePointers, "InterfacePointer", "IoTaskImage");
        UpsertInterfacePointer(
            interfacePointers,
            CreateInterfacePointer(
                ns,
                "CyclicCaller",
                "#x03002060",
                "{0300001e-0000-0000-e000-000000000064}",
                "ITcCyclicCaller"));

        XElement dataPointers = GetOrCreateChildElement(module, "DataPointers");
        RemoveNamedElements(dataPointers, "DataPointer", "DataIn");
        RemoveNamedElements(dataPointers, "DataPointer", "DataOut");

        _ = GetOrCreateChildElement(module, "Deployment");

        // Keep the Library section in sync with this project's name and version.
        XElement library = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Library")
            ?? AddChild(root, "Library", ns);
        SetOrCreateChildElementValue(library, "Name", projectName);
        SetOrCreateChildElementValue(library, "Version", "0.0.0.1");

        document.Save(tmcPath);
    }

    private static void EnsureCppGroup(XElement groups, XNamespace ns)
    {
        XElement? cppGroup = groups.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Group" &&
            string.Equals(GetChildElementValue(element, "Name"), "C++", StringComparison.OrdinalIgnoreCase));
        cppGroup ??= AddChild(groups, "Group", ns);
        cppGroup.SetAttributeValue("SortOrder", "701");
        SetOrCreateChildElementValue(cppGroup, "Name", "C++");
        SetOrCreateChildElementValue(cppGroup, "DisplayName", "C++ Modules");
    }

    private static void EnsureDefaultContext(XElement contexts, XNamespace ns)
    {
        XElement? context = contexts.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Context" &&
            string.Equals(GetChildElementValue(element, "Id"), "1", StringComparison.OrdinalIgnoreCase));
        context ??= AddChild(contexts, "Context", ns);
        SetOrCreateChildElementValue(context, "Id", "1");
    }

    private static XElement CreateInterface(XNamespace ns, string typeName, string typeGuid, bool disableCodeGeneration)
    {
        XElement element = new(ns + "Interface");
        if (disableCodeGeneration)
        {
            element.SetAttributeValue("DisableCodeGeneration", "true");
        }

        element.Add(new XElement(ns + "Type", new XAttribute("GUID", typeGuid), typeName));
        return element;
    }

    private static void UpsertInterface(XElement container, XElement iface)
    {
        string? typeName = iface.Elements().FirstOrDefault(element => element.Name.LocalName == "Type")?.Value;
        XElement? existing = container.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Interface" &&
            string.Equals(
                element.Elements().FirstOrDefault(child => child.Name.LocalName == "Type")?.Value,
                typeName,
                StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            container.Add(iface);
        }
        else
        {
            existing.ReplaceWith(iface);
        }
    }

    private static XElement CreateSortOrderParameter(XNamespace ns) =>
        new(
            ns + "Parameter",
            new XAttribute("DisableCodeGeneration", "true"),
            new XElement(ns + "Name", "SortOrder"),
            new XElement(ns + "BaseType", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
            new XElement(ns + "PTCID", "#x030020B0"),
            new XElement(ns + "ContextId", "1"));

    private static XElement CreateParameterStructParameter(XNamespace ns) =>
        new(
            ns + "Parameter",
            new XAttribute("ShowSubItems", "true"),
            new XAttribute("CreateSymbol", "true"),
            new XElement(ns + "Name", "Parameter"),
            new XElement(ns + "BitSize", "32"),
            new XElement(
                ns + "SubItem",
                new XElement(ns + "Name", "data1"),
                new XElement(ns + "Type", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
                new XElement(ns + "BitSize", "32"),
                new XElement(ns + "BitOffs", "0")),
            new XElement(ns + "PTCID", "#x00000001"),
            new XElement(ns + "ContextId", "1"));

    private static XElement CreateRuntimeDataType(XNamespace ns, string moduleName, string moduleGuid) =>
        new(
            ns + "DataType",
            new XElement(
                ns + "Name",
                new XAttribute("GUID", moduleGuid),
                new XAttribute("TcBaseType", "true"),
                "ST_" + MakeSafeIdentifier(moduleName) + "_Runtime"),
            new XElement(ns + "BitSize", "64"),
            new XElement(
                ns + "SubItem",
                new XElement(ns + "Name", "Counter"),
                new XElement(ns + "Type", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
                new XElement(ns + "BitSize", "32"),
                new XElement(ns + "BitOffs", "0")),
            new XElement(
                ns + "SubItem",
                new XElement(ns + "Name", "Status"),
                new XElement(ns + "Type", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
                new XElement(ns + "BitSize", "32"),
                new XElement(ns + "BitOffs", "32")));

    private static XElement CreateInputDataArea(XNamespace ns) =>
        new(
            ns + "DataArea",
            new XElement(ns + "AreaNo", new XAttribute("AreaType", "InputDst"), "1"),
            new XElement(ns + "Name", "Input"),
            new XElement(ns + "ContextId", "1"),
            new XElement(ns + "ByteSize", "4"),
            new XElement(
                ns + "Symbol",
                new XElement(ns + "Name", "DataIn"),
                new XElement(ns + "BitSize", "32"),
                new XElement(ns + "BaseType", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
                new XElement(ns + "BitOffs", "0")));

    private static XElement CreateOutputDataArea(XNamespace ns) =>
        new(
            ns + "DataArea",
            new XElement(ns + "AreaNo", new XAttribute("AreaType", "OutputSrc"), "2"),
            new XElement(ns + "Name", "Output"),
            new XElement(ns + "ContextId", "1"),
            new XElement(ns + "ByteSize", "4"),
            new XElement(
                ns + "Symbol",
                new XElement(ns + "Name", "DataOut"),
                new XElement(ns + "BitSize", "32"),
                new XElement(ns + "BaseType", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
                new XElement(ns + "BitOffs", "0")));

    private static XElement CreateStandardDataArea(XNamespace ns) =>
        new(
            ns + "DataArea",
            new XElement(ns + "AreaNo", new XAttribute("AreaType", "Standard"), "2"),
            new XElement(ns + "Name", "Data"),
            new XElement(ns + "ContextId", "1"),
            new XElement(ns + "ByteSize", "8"),
            new XElement(
                ns + "Symbol",
                new XElement(ns + "Name", "DataIn"),
                new XElement(ns + "BitSize", "32"),
                new XElement(ns + "BaseType", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
                new XElement(ns + "BitOffs", "0")),
            new XElement(
                ns + "Symbol",
                new XElement(ns + "Name", "DataOut"),
                new XElement(ns + "BitSize", "32"),
                new XElement(ns + "BaseType", new XAttribute("GUID", "{18071995-0000-0000-0000-000000000008}"), "UDINT"),
                new XElement(ns + "BitOffs", "32")));

    private static XElement CreateInterfacePointer(XNamespace ns, string name, string ptcid, string typeGuid, string typeName) =>
        new(
            ns + "InterfacePointer",
            new XElement(ns + "PTCID", ptcid),
            new XElement(ns + "Name", name),
            new XElement(ns + "Type", new XAttribute("GUID", typeGuid), typeName),
            new XElement(ns + "ContextId", "1"));

    private static XElement CreateDataPointer(XNamespace ns, string name, string ptcid, string typeName, string typeGuid) =>
        new(
            ns + "DataPointer",
            new XElement(ns + "PTCID", ptcid),
            new XElement(ns + "Name", name),
            new XElement(ns + "Type", new XAttribute("GUID", typeGuid), typeName));

    private static void UpsertParameter(XElement container, XElement parameter) =>
        UpsertByName(container, "Parameter", parameter);

    private static void UpsertDataType(XElement container, XElement dataType) =>
        UpsertByName(container, "DataType", dataType);

    private static void UpsertDataArea(XElement container, XElement dataArea) =>
        UpsertByName(container, "DataArea", dataArea);

    private static void UpsertInterfacePointer(XElement container, XElement pointer) =>
        UpsertByName(container, "InterfacePointer", pointer);

    private static void UpsertDataPointer(XElement container, XElement pointer) =>
        UpsertByName(container, "DataPointer", pointer);

    private static void UpsertByName(XElement container, string elementLocalName, XElement candidate)
    {
        string? name = candidate.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value;
        XElement? existing = container.Elements().FirstOrDefault(element =>
            element.Name.LocalName == elementLocalName &&
            string.Equals(GetChildElementValue(element, "Name"), name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            container.Add(candidate);
        }
        else
        {
            existing.ReplaceWith(candidate);
        }
    }

    private static void RemoveNamedElements(XElement container, string elementLocalName, string name)
    {
        foreach (XElement element in container.Elements()
                     .Where(element =>
                         element.Name.LocalName == elementLocalName &&
                         string.Equals(GetChildElementValue(element, "Name"), name, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            element.Remove();
        }
    }

    private static void RemoveParametersExcept(XElement container, params string[] namesToKeep)
    {
        HashSet<string> keep = namesToKeep
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (XElement element in container.Elements()
                     .Where(element => element.Name.LocalName == "Parameter")
                     .Where(element => !keep.Contains(GetChildElementValue(element, "Name") ?? string.Empty))
                     .ToList())
        {
            element.Remove();
        }
    }

    private static void EnsureFallbackModuleSourceFiles(string projectDirectory, string projectName, string moduleName, string moduleClassName)
    {
        Directory.CreateDirectory(projectDirectory);

        string headerPath = Path.Combine(projectDirectory, moduleName + ".h");
        string sourcePath = Path.Combine(projectDirectory, moduleName + ".cpp");
        string classIdName = "CID_" + MakeSafeIdentifier(projectName) + moduleClassName;
        string shortName = MakeSafeIdentifier(moduleName);
        string dataStructName = shortName + "Data";
        string parameterStructName = shortName + "Parameter";
        string pidDataInName = "PID_" + shortName + "DataIn";
        string pidDataOutName = "PID_" + shortName + "DataOut";
        string pidParameterName = "PID_" + shortName + "Parameter";

        string headerTemplate = """
            ///////////////////////////////////////////////////////////////////////////////
            // __MODULE_NAME__.h
            #pragma once

            #include "__PROJECT_NAME__Interfaces.h"

            typedef struct ___DATA_STRUCT_NAME__
            {
                ULONG dataIn;
                ULONG dataOut;
            } __DATA_STRUCT_NAME__;

            typedef struct ___PARAMETER_STRUCT_NAME__
            {
                ULONG data1;
            } __PARAMETER_STRUCT_NAME__;

            const PTCID __PID_DATA_IN_NAME__ = 0x00000002;
            const PTCID __PID_DATA_OUT_NAME__ = 0x00000003;
            const PTCID __PID_PARAMETER_NAME__ = 0x00000001;

            class __MODULE_CLASS_NAME__
                : public ITComObject
                , public ITcADI
                , public ITcWatchSource
                , public ITcCyclic
            {
            public:
                DECLARE_IUNKNOWN()
                DECLARE_IPERSIST(__CLASS_ID_NAME__)
                DECLARE_ITCOMOBJECT_LOCKOP()
                DECLARE_ITCADI()
                DECLARE_ITCWATCHSOURCE()
                DECLARE_OBJPARAWATCH_MAP()
                DECLARE_OBJDATAAREA_MAP()

                __MODULE_CLASS_NAME__();
                virtual ~__MODULE_CLASS_NAME__();

                virtual HRESULT TCOMAPI CycleUpdate(ITcTask* ipTask, ITcUnknown* ipCaller, ULONG_PTR context);

            protected:
                DECLARE_ITCOMOBJECT_SETSTATE();

                HRESULT AddModuleToCaller();
                VOID RemoveModuleFromCaller();
                HRESULT InitDataPointer();
                VOID DeinitDataPointer();

                typedef _TCOM_SMARTDATAPTR<unsigned long> UDINTDataPtr;

            ULONG m_DataIn;
            ULONG m_DataOut;
            UDINTDataPtr m_spDataIn;
            UDINTDataPtr m_spDataOut;
                ITcCyclicCallerInfoPtr m_spCyclicCaller;
                __PARAMETER_STRUCT_NAME__ m_Parameter;
                UINT m_Counter;
            };
            """;

        string sourceTemplate = """
            ///////////////////////////////////////////////////////////////////////////////
            // __MODULE_NAME__.cpp
            #include "TcPch.h"
            #pragma hdrstop

            #include "__MODULE_NAME__.h"
            #include "TcIoServices.h"

            BEGIN_INTERFACE_MAP(__MODULE_CLASS_NAME__)
                INTERFACE_ENTRY_ITCOMOBJECT()
                INTERFACE_ENTRY(IID_ITcADI, ITcADI)
                INTERFACE_ENTRY(IID_ITcWatchSource, ITcWatchSource)
                INTERFACE_ENTRY(IID_ITcCyclic, ITcCyclic)
            END_INTERFACE_MAP()

            IMPLEMENT_ITCOMOBJECT(__MODULE_CLASS_NAME__)
            IMPLEMENT_ITCOMOBJECT_SETSTATE_LOCKOP(__MODULE_CLASS_NAME__)
            IMPLEMENT_ITCADI(__MODULE_CLASS_NAME__)
            IMPLEMENT_ITCWATCHSOURCE(__MODULE_CLASS_NAME__)

            BEGIN_SETOBJPARA_MAP(__MODULE_CLASS_NAME__)
                SETOBJPARA_DATAAREA_MAP()
                SETOBJPARA_ITFPTR(PID_Ctx_TaskOid, m_spCyclicCaller)
                SETOBJPARA_VALUE(__PID_PARAMETER_NAME__, m_Parameter)
            END_SETOBJPARA_MAP()

            BEGIN_GETOBJPARA_MAP(__MODULE_CLASS_NAME__)
                GETOBJPARA_DATAAREA_MAP()
                GETOBJPARA_ITFPTR(PID_Ctx_TaskOid, m_spCyclicCaller)
                GETOBJPARA_VALUE(__PID_PARAMETER_NAME__, m_Parameter)
            END_GETOBJPARA_MAP()

            BEGIN_OBJPARAWATCH_MAP(__MODULE_CLASS_NAME__)
                OBJPARAWATCH_DATAAREA_MAP()
            END_OBJPARAWATCH_MAP()

            BEGIN_OBJDATAAREA_MAP(__MODULE_CLASS_NAME__)
                OBJDATAAREA_VALUE(1, m_DataIn)
                OBJDATAAREA_VALUE(2, m_DataOut)
            END_OBJDATAAREA_MAP()

            __MODULE_CLASS_NAME__::__MODULE_CLASS_NAME__()
                : m_Counter(0)
            {
                m_DataIn = 0;
                m_DataOut = 0;
                m_Parameter.data1 = 1;
            }

            __MODULE_CLASS_NAME__::~__MODULE_CLASS_NAME__()
            {
            }

            IMPLEMENT_ITCOMOBJECT_SETOBJSTATE_IP_PI(__MODULE_CLASS_NAME__)

            HRESULT __MODULE_CLASS_NAME__::SetObjStatePS(PTComInitDataHdr pInitData)
            {
                HRESULT hr = S_OK;
                IMPLEMENT_ITCOMOBJECT_EVALUATE_INITDATA(pInitData);
                return hr;
            }

            HRESULT __MODULE_CLASS_NAME__::SetObjStateSO()
            {
                HRESULT hr = S_OK;
                hr = FAILED(hr) ? hr : AddModuleToCaller();

                if (FAILED(hr))
                {
                    RemoveModuleFromCaller();
                }
                return hr;
            }

            HRESULT __MODULE_CLASS_NAME__::SetObjStateOS()
            {
                HRESULT hr = S_OK;

                RemoveModuleFromCaller();
                return hr;
            }

            HRESULT __MODULE_CLASS_NAME__::SetObjStateSP()
            {
                HRESULT hr = S_OK;
                return hr;
            }

            BOOL __MODULE_CLASS_NAME__::TcTryToReleaseOpState()
            {
                return TRUE;
            }

            HRESULT __MODULE_CLASS_NAME__::CycleUpdate(ITcTask* ipTask, ITcUnknown* ipCaller, ULONG_PTR context)
            {
                HRESULT hr = S_OK;

                m_Counter = m_Parameter.data1 + m_DataIn;
                m_DataOut = m_Counter;

                return hr;
            }

            HRESULT __MODULE_CLASS_NAME__::AddModuleToCaller()
            {
                HRESULT hr = S_OK;

                if (m_spCyclicCaller.HasOID())
                {
                    if (SUCCEEDED_DBG(hr = m_spSrv->TcQuerySmartObjectInterface(m_spCyclicCaller)))
                    {
                        if (FAILED(hr = m_spCyclicCaller->AddModule(m_spCyclicCaller, THIS_CAST(ITcCyclic))))
                        {
                            m_spCyclicCaller = NULL;
                        }
                    }
                }
                else
                {
                    hr = ADS_E_INVALIDOBJID;
                    SUCCEEDED_DBGT(hr, "Invalid OID specified for caller task");
                }
                return hr;
            }

            VOID __MODULE_CLASS_NAME__::RemoveModuleFromCaller()
            {
                if (m_spCyclicCaller)
                {
                    m_spCyclicCaller->RemoveModule(m_spCyclicCaller);
                }

                m_spCyclicCaller = NULL;
            }

            HRESULT __MODULE_CLASS_NAME__::InitDataPointer()
            {
                return S_OK;
            }

            VOID __MODULE_CLASS_NAME__::DeinitDataPointer()
            {
            }
            """;

        string headerContent = headerTemplate
            .Replace("__MODULE_NAME__", moduleName, StringComparison.Ordinal)
            .Replace("__PROJECT_NAME__", projectName, StringComparison.Ordinal)
            .Replace("__MODULE_CLASS_NAME__", moduleClassName, StringComparison.Ordinal)
            .Replace("__CLASS_ID_NAME__", classIdName, StringComparison.Ordinal)
            .Replace("__DATA_STRUCT_NAME__", dataStructName, StringComparison.Ordinal)
            .Replace("__PARAMETER_STRUCT_NAME__", parameterStructName, StringComparison.Ordinal)
            .Replace("__PID_DATA_IN_NAME__", pidDataInName, StringComparison.Ordinal)
            .Replace("__PID_DATA_OUT_NAME__", pidDataOutName, StringComparison.Ordinal)
            .Replace("__PID_PARAMETER_NAME__", pidParameterName, StringComparison.Ordinal);

        string sourceContent = sourceTemplate
            .Replace("__MODULE_NAME__", moduleName, StringComparison.Ordinal)
            .Replace("__MODULE_CLASS_NAME__", moduleClassName, StringComparison.Ordinal)
            .Replace("__PID_DATA_IN_NAME__", pidDataInName, StringComparison.Ordinal)
            .Replace("__PID_DATA_OUT_NAME__", pidDataOutName, StringComparison.Ordinal)
            .Replace("__PID_PARAMETER_NAME__", pidParameterName, StringComparison.Ordinal);

        File.WriteAllText(headerPath, headerContent);
        File.WriteAllText(sourcePath, sourceContent);
    }

    private static void AddModuleFilesToProjectFiles(string vcxprojPath, string moduleName)
    {
        XDocument document = XDocument.Load(vcxprojPath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;

        bool hasCpp = document.Descendants(ns + "ClCompile")
            .Any(element => string.Equals(element.Attribute("Include")?.Value, moduleName + ".cpp", StringComparison.OrdinalIgnoreCase));
        bool hasHeader = document.Descendants(ns + "ClInclude")
            .Any(element => string.Equals(element.Attribute("Include")?.Value, moduleName + ".h", StringComparison.OrdinalIgnoreCase));

        if (!hasCpp)
        {
            XElement compileGroup = document.Descendants(ns + "ItemGroup")
                .FirstOrDefault(group => group.Elements(ns + "ClCompile").Any())
                ?? new XElement(ns + "ItemGroup");
            if (compileGroup.Parent is null)
            {
                document.Root?.Add(compileGroup);
            }

            compileGroup.Add(new XElement(ns + "ClCompile", new XAttribute("Include", moduleName + ".cpp")));
        }

        if (!hasHeader)
        {
            XElement includeGroup = document.Descendants(ns + "ItemGroup")
                .FirstOrDefault(group => group.Elements(ns + "ClInclude").Any())
                ?? new XElement(ns + "ItemGroup");
            if (includeGroup.Parent is null)
            {
                document.Root?.Add(includeGroup);
            }

            includeGroup.Add(new XElement(ns + "ClInclude", new XAttribute("Include", moduleName + ".h")));
        }

        document.Save(vcxprojPath, SaveOptions.DisableFormatting);
    }

    private static void AddModuleFilesToVcxprojFilters(string filtersPath, string moduleName)
    {
        XDocument document = XDocument.Load(filtersPath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;

        bool hasCpp = document.Descendants(ns + "ClCompile")
            .Any(element => string.Equals(element.Attribute("Include")?.Value, moduleName + ".cpp", StringComparison.OrdinalIgnoreCase));
        bool hasHeader = document.Descendants(ns + "ClInclude")
            .Any(element => string.Equals(element.Attribute("Include")?.Value, moduleName + ".h", StringComparison.OrdinalIgnoreCase));

        XElement group = document.Root?.Elements(ns + "ItemGroup")
            .FirstOrDefault(itemGroup => itemGroup.Elements().Any(element =>
                element.Name.LocalName == "ClCompile" || element.Name.LocalName == "ClInclude"))
            ?? new XElement(ns + "ItemGroup");
        if (group.Parent is null)
        {
            document.Root?.Add(group);
        }

        if (!hasHeader)
        {
            group.Add(
                new XElement(
                    ns + "ClInclude",
                    new XAttribute("Include", moduleName + ".h"),
                    new XElement(ns + "Filter", "Header Files")));
        }

        if (!hasCpp)
        {
            group.Add(
                new XElement(
                    ns + "ClCompile",
                    new XAttribute("Include", moduleName + ".cpp"),
                    new XElement(ns + "Filter", "Source Files")));
        }

        document.Save(filtersPath, SaveOptions.DisableFormatting);
    }

    private static void UpsertClassIdInServicesHeader(string servicesHeaderPath, string classIdName, string moduleGuid)
    {
        List<string> lines = File.ReadAllLines(servicesHeaderPath).ToList();
        if (lines.Any(line => line.IndexOf(classIdName, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return;
        }

        int start = lines.FindIndex(line => line.IndexOf("///<AutoGeneratedContent id=\"ClassIDs\">", StringComparison.OrdinalIgnoreCase) >= 0);
        int end = lines.FindIndex(start + 1, line => line.IndexOf("///</AutoGeneratedContent>", StringComparison.OrdinalIgnoreCase) >= 0);

        string classIdLine = "const CTCID " + classIdName + " = " + FormatGuidAsCtcid(moduleGuid) + ";";
        if (start >= 0 && end > start)
        {
            lines.Insert(end, classIdLine);
        }
        else
        {
            lines.Add("///<AutoGeneratedContent id=\"ClassIDs\">");
            lines.Add(classIdLine);
            lines.Add("///</AutoGeneratedContent>");
        }

        File.WriteAllLines(servicesHeaderPath, lines);
    }

    private static void UpsertVendorIdInServicesHeader(string servicesHeaderPath, string projectName)
    {
        string projectDirectory = Path.GetDirectoryName(servicesHeaderPath) ?? string.Empty;
        string classFactoryPath = Path.Combine(projectDirectory, projectName + "ClassFactory.cpp");
        string versionHeaderInclude = "#include \"" + projectName + "Version.h\"";
        if (File.Exists(classFactoryPath))
        {
            try
            {
                if (File.ReadAllText(classFactoryPath).IndexOf(versionHeaderInclude, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }
            }
            catch
            {
            }
        }

        List<string> lines = File.ReadAllLines(servicesHeaderPath).ToList();
        string vendorIdMacro = "VID_" + MakeSafeIdentifier(projectName);
        if (lines.Any(line =>
        {
            string trimmed = line.Trim();
            return trimmed.StartsWith("static const GUID ", StringComparison.OrdinalIgnoreCase) &&
                   trimmed.IndexOf(vendorIdMacro + " =", StringComparison.OrdinalIgnoreCase) >= 0;
        }))
        {
            return;
        }

        string vendorIdLine = "static const GUID " + vendorIdMacro + " = " + FormatGuidAsCtcid(Guid.NewGuid().ToString("B").ToUpperInvariant()) + ";";
        int insertIndex = lines.FindIndex(line => line.IndexOf("///<AutoGeneratedContent id=\"ClassIDs\">", StringComparison.OrdinalIgnoreCase) >= 0);
        if (insertIndex < 0)
        {
            insertIndex = lines.FindLastIndex(line =>
                line.TrimStart().StartsWith("#define SRVNAME_", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("const ULONG DrvID_", StringComparison.Ordinal));
            insertIndex = insertIndex < 0 ? lines.Count : insertIndex + 1;
        }

        lines.Insert(insertIndex, vendorIdLine);
        File.WriteAllLines(servicesHeaderPath, lines);
    }

    private static void UpsertClassFactoryMapEntry(
        string classFactoryPath,
        string projectName,
        string moduleName,
        string moduleClassName,
        string classIdName,
        string serviceNameMacro)
    {
        List<string> lines = File.ReadAllLines(classFactoryPath).ToList();

        string includeLine = "#include \"" + moduleName + ".h\"";
        if (!lines.Any(line => string.Equals(line.Trim(), includeLine, StringComparison.OrdinalIgnoreCase)))
        {
            int lastInclude = lines.FindLastIndex(line => line.TrimStart().StartsWith("#include ", StringComparison.Ordinal));
            if (lastInclude >= 0)
            {
                lines.Insert(lastInclude + 1, includeLine);
            }
            else
            {
                lines.Insert(0, includeLine);
            }
        }

        if (lines.Any(line => line.IndexOf(classIdName, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            File.WriteAllLines(classFactoryPath, lines);
            return;
        }

        int start = lines.FindIndex(line => line.IndexOf("///<AutoGeneratedContent id=\"ClassMap\">", StringComparison.OrdinalIgnoreCase) >= 0);
        int end = lines.FindIndex(start + 1, line => line.IndexOf("///</AutoGeneratedContent>", StringComparison.OrdinalIgnoreCase) >= 0);
        if (start >= 0 && end > start)
        {
            string vendorIdMacro = "VID_" + MakeSafeIdentifier(projectName);
            string entryLine =
                "\tCLASS_ENTRY_LIB(" + vendorIdMacro + ", " + classIdName + ", " + serviceNameMacro + " \"!" + moduleClassName + "\", " + moduleClassName + ")";
            lines.Insert(end, entryLine);
        }

        File.WriteAllLines(classFactoryPath, lines);
    }

    private static string MakeSafeIdentifier(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return "X";
        }

        char[] chars = rawValue
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_')
            .ToArray();
        string identifier = new(chars);
        if (identifier.Length == 0)
        {
            identifier = "X";
        }

        if (char.IsDigit(identifier[0]))
        {
            identifier = "_" + identifier;
        }

        return identifier;
    }

    private static string GetServiceNameMacroToken(string servicesHeaderPath, string projectName)
    {
        foreach (string line in File.ReadLines(servicesHeaderPath))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("#define ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[1].StartsWith("SRVNAME_", StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return "SRVNAME_" + MakeSafeIdentifier(projectName).ToUpperInvariant();
    }

    private static string ResolveOrCreateModuleGuid(string? rawModuleGuid)
    {
        if (TryNormalizeGuid(rawModuleGuid, out string normalized))
        {
            return normalized;
        }

        return Guid.NewGuid().ToString("B").ToUpperInvariant();
    }

    private static string FormatGuidAsCtcid(string guidText)
    {
        if (!Guid.TryParse(guidText, out Guid guid))
        {
            throw new InvalidOperationException($"Invalid GUID '{guidText}' for CTCID conversion.");
        }

        string[] parts = guid.ToString("D").Split('-');
        if (parts.Length != 5)
        {
            throw new InvalidOperationException($"Invalid GUID '{guidText}' for CTCID conversion.");
        }

        string d1 = parts[0].ToLowerInvariant();
        string d2 = parts[1].ToLowerInvariant();
        string d3 = parts[2].ToLowerInvariant();
        string d4 = parts[3].ToLowerInvariant();
        string d5 = parts[4].ToLowerInvariant();
        string[] bytes =
        {
            "0x" + d4[..2],
            "0x" + d4[2..4],
            "0x" + d5[..2],
            "0x" + d5[2..4],
            "0x" + d5[4..6],
            "0x" + d5[6..8],
            "0x" + d5[8..10],
            "0x" + d5[10..12]
        };
        return "{0x" + d1 + ",0x" + d2 + ",0x" + d3 + ",{" + string.Join(",", bytes) + "}}";
    }

    private static TwinCatNodeInfo CreateOfflineModuleInstanceFallback(
        TwinCatEngineeringSession session,
        AddModuleInstanceRequest request,
        string moduleGuid)
    {
        string? projectPath = ResolveTwinCatProjectPathForOfflineMutation(session);
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            throw new InvalidOperationException("TwinCAT project path could not be resolved for AddModuleInstance fallback.");
        }

        string moduleClassName = ResolveModuleClassNameByGuid(request.ProjectTmcPath, moduleGuid) ?? "CFallbackModule";
        string displayName = request.InstanceBaseName + " (" + moduleClassName + ")";

        XDocument document = XDocument.Load(projectPath);
        XElement? cppProject = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Cpp" &&
            (string.Equals(element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value, request.ProjectName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetChildElementValue(element, "Name"), request.ProjectName, StringComparison.OrdinalIgnoreCase)));
        if (cppProject is null)
        {
            throw new InvalidOperationException($"C++ project '{request.ProjectName}' was not found in the .tsproj.");
        }

        XElement? existing = cppProject.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Instance" &&
            string.Equals(GetChildElementValue(element, "Name"), displayName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            string existingObjectId = existing.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "OTCID")?.Value ?? "#x02010010";
            return new TwinCatNodeInfo(
                $"TIXC^{request.ProjectName}^{displayName}",
                displayName,
                existingObjectId,
                request.ProjectTmcPath,
                UsedFallback: true);
        }

        HashSet<string> usedObjectIds = cppProject.Elements()
            .Where(element => element.Name.LocalName == "Instance")
            .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "OTCID")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string nextObjectId = "#x02010010";
        while (usedObjectIds.Contains(nextObjectId))
        {
            nextObjectId = TwinCatTsprojMutationService.DeriveNextObjectId(nextObjectId, 0x10);
        }

        XNamespace ns = cppProject.GetDefaultNamespace();
        XElement instance = new(
            ns + "Instance",
            new XAttribute("OTCID", nextObjectId),
            new XElement(ns + "Name", displayName),
            new XElement(
                ns + "TmcDesc",
                new XElement(
                    ns + "Contexts",
                    new XElement(
                        ns + "Context",
                        new XElement(ns + "Id", "1"),
                        new XElement(
                            ns + "ManualConfig",
                            new XElement(ns + "OTCID"),
                            new XElement(ns + "Priority", "0"),
                            new XElement(ns + "CycleTime", "0")))),
                new XElement(ns + "ParameterValues"),
                new XElement(ns + "InterfacePointerValues"),
                new XElement(ns + "DataPointerValues")));

        cppProject.Add(instance);
        document.Save(projectPath);

        return new TwinCatNodeInfo(
            $"TIXC^{request.ProjectName}^{displayName}",
            displayName,
            nextObjectId,
            request.ProjectTmcPath,
            UsedFallback: true);
    }

    private static string? ResolveModuleClassNameByGuid(string tmcFilePath, string guidText)
    {
        if (!File.Exists(tmcFilePath))
        {
            return null;
        }

        if (!Guid.TryParse(guidText, out Guid guid))
        {
            return null;
        }

        XDocument document = XDocument.Load(tmcFilePath);
        XElement? module = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Module" &&
            (GuidMatches(element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "GUID")?.Value, guid) ||
             GuidMatches(GetChildElementValue(element, "CLSID"), guid)));
        if (module is null)
        {
            return null;
        }

        return module.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "ClassName")?.Value
            ?? GetChildElementValue(module, "Name");
    }

    private static bool TryResolveCppInstanceFromTsproj(
        string projectPath,
        string cppProjectName,
        string displayName,
        string? objectId,
        out string persistedName,
        out string? persistedObjectId)
    {
        persistedName = displayName;
        persistedObjectId = objectId;

        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch
        {
            return false;
        }

        XElement? cppProject = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Cpp" &&
            (string.Equals(element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value, cppProjectName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetChildElementValue(element, "Name"), cppProjectName, StringComparison.OrdinalIgnoreCase)));
        if (cppProject is null)
        {
            return false;
        }

        XElement? instance = cppProject.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Instance" &&
            string.Equals(GetChildElementValue(element, "Name"), displayName, StringComparison.OrdinalIgnoreCase));
        if (instance is null && !string.IsNullOrWhiteSpace(objectId))
        {
            instance = cppProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                string.Equals(
                    element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "OTCID")?.Value,
                    objectId,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (instance is null)
        {
            return false;
        }

        persistedName = GetChildElementValue(instance, "Name") ?? displayName;
        persistedObjectId = instance.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "OTCID")?.Value ?? objectId;
        return true;
    }

    private static bool GuidMatches(string? candidate, Guid expected) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        Guid.TryParse(candidate.Trim(), out Guid parsed) &&
        parsed == expected;

    private static XElement AddChild(XElement parent, string localName, XNamespace ns)
    {
        XElement child = new(ns + localName);
        parent.Add(child);
        return child;
    }

    private static void RetryComCall(Action action, int maxRetryCount = 60, int retryDelayMs = 500)
    {
        RetryComCall(
            () =>
            {
                action();
                return 0;
            },
            maxRetryCount,
            retryDelayMs);
    }

    private static T RetryComCall<T>(Func<T> action, int maxRetryCount = 60, int retryDelayMs = 500)
    {
        Exception? lastException = null;
        for (int attempt = 0; attempt < maxRetryCount; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException ex) when (
                ex.ErrorCode == RpcCallRejected ||
                ex.ErrorCode == RpcRetryLater ||
                ex.ErrorCode == RpcCallFailed ||
                ex.ErrorCode == RpcServerUnavailable)
            {
                lastException = ex;
                ThreadingThread.Sleep(retryDelayMs);
            }
        }

        throw lastException ?? new InvalidOperationException("COM call failed without yielding a retryable exception.");
    }

    private static void ReleaseComObjectIfNeeded(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.ReleaseComObject(value);
            }
            catch
            {
            }
        }
    }
}
