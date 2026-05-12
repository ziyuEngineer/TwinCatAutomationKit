using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EnvDTE;
using Microsoft.Win32;
using TwinCatAutomationKit.Abstractions;
using TCatSysManagerLib;
using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ThreadingThread = System.Threading.Thread;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatEngineeringService
{
    private const int RpcCallRejected = unchecked((int)0x80010001);
    private const int RpcRetryLater = unchecked((int)0x8001010A);
    private const int RpcCallFailed = unchecked((int)0x800706BE);
    private const int RpcServerUnavailable = unchecked((int)0x800706BA);
    private const int ComServerExecFailure = unchecked((int)0x80080005);
    private const string TcModuleClassSchema = "http://www.beckhoff.com/schemas/2009/05/TcModuleClass";
    private const string TcCppLicenseId = "{304D006A-8299-4560-AB79-438534B50288}";
    private const string TwinCatScopeProjectTypeGuid = "{FD9F1D59-E000-42F3-8744-88DE1BE93C06}";

    private static readonly IReadOnlyDictionary<string, int> EmptyAutoDismissedDialogs =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, TmcTypeInfo> BuiltInTmcTypes =
        new Dictionary<string, TmcTypeInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["BIT"] = new("BIT", "{18071995-0000-0000-0000-000000000010}", 1),
            ["BOOL"] = new("BOOL", "{18071995-0000-0000-0000-000000000030}", 8),
            ["SINT"] = new("SINT", "{18071995-0000-0000-0000-000000000003}", 8),
            ["USINT"] = new("USINT", "{18071995-0000-0000-0000-000000000004}", 8),
            ["BYTE"] = new("USINT", "{18071995-0000-0000-0000-000000000004}", 8),
            ["INT"] = new("INT", "{18071995-0000-0000-0000-000000000006}", 16),
            ["SHORT"] = new("INT", "{18071995-0000-0000-0000-000000000006}", 16),
            ["UINT"] = new("UINT", "{18071995-0000-0000-0000-000000000005}", 16),
            ["USHORT"] = new("UINT", "{18071995-0000-0000-0000-000000000005}", 16),
            ["DINT"] = new("DINT", "{18071995-0000-0000-0000-000000000009}", 32),
            ["LONG"] = new("DINT", "{18071995-0000-0000-0000-000000000009}", 32),
            ["UDINT"] = new("UDINT", "{18071995-0000-0000-0000-000000000008}", 32),
            ["ULONG"] = new("UDINT", "{18071995-0000-0000-0000-000000000008}", 32),
            ["LREAL"] = new("LREAL", "{18071995-0000-0000-0000-00000000000E}", 64),
            ["DOUBLE"] = new("LREAL", "{18071995-0000-0000-0000-00000000000E}", 64),
            ["HRESULT"] = new("HRESULT", "{18071995-0000-0000-0000-000000000019}", 32),
            ["OTCID"] = new("OTCID", "{18071995-0000-0000-0000-00000000000F}", 32),
            ["TcTraceLevel"] = new("TcTraceLevel", "{8007AE3B-86BB-40F2-B385-EF87FCC239A4}", 32),
            ["ITcUnknown"] = new("ITcUnknown", "{00000001-0000-0000-E000-000000000064}", 32),
            ["ITComObject"] = new("ITComObject", "{00000012-0000-0000-E000-000000000064}", 32),
            ["ITcCyclic"] = new("ITcCyclic", "{03000010-0000-0000-E000-000000000064}", 32),
            ["ITcADI"] = new("ITcADI", "{03000012-0000-0000-E000-000000000064}", 32),
            ["ITcWatchSource"] = new("ITcWatchSource", "{03000018-0000-0000-E000-000000000064}", 32),
            ["ITcCyclicCaller"] = new("ITcCyclicCaller", "{0300001E-0000-0000-E000-000000000064}", 32),
        };

    public TwinCatEngineeringSession LaunchVisualStudio(LaunchVisualStudioRequest request)
    {
        Type dteType = Type.GetTypeFromProgID(request.ProgId, throwOnError: true)
            ?? throw new InvalidOperationException($"Unable to resolve DTE ProgId '{request.ProgId}'.");

        (
            DTE dte,
            bool attachedToExisting,
            IReadOnlyCollection<int> targetProcessIds,
            IReadOnlyDictionary<string, int> launchAutoDismissedDialogs) =
            CreateOrAttachVisualStudioDte(
                dteType,
                request.ProgId,
                request.LaunchTimeoutMs,
                request.AttachToExisting,
                request.EnableDialogAutoDismiss,
                request.DialogPollIntervalMs,
                request.RootSuffix,
                request.DteHostPath,
                request.PreferDteHostLaunch);
        TwinCatEngineeringSession.TwinCatDialogAutoDismissScope? startupDialogScope = null;
        try
        {
            if (request.EnableDialogAutoDismiss && targetProcessIds.Count > 0)
            {
                startupDialogScope = TwinCatEngineeringSession.TwinCatDialogAutoDismissScope.Start(
                    targetProcessIds,
                    request.DialogPollIntervalMs);
            }

            ThreadingThread.Sleep(request.StartupDelayMs);
            RetryComCall(() => dte.SuppressUI = request.SuppressUi);
            if (!attachedToExisting)
            {
                RetryComCall(() => dte.MainWindow.Visible = request.Visible);
                RetryComCall(() => dte.UserControl = false);
            }

            return new TwinCatEngineeringSession(
                dte,
                attachedToExisting,
                targetProcessIds,
                request.EnableDialogAutoDismiss,
                request.DialogPollIntervalMs,
                MergeAutoDismissedDialogs(launchAutoDismissedDialogs, startupDialogScope?.Snapshot()));
        }
        finally
        {
            startupDialogScope?.Dispose();
        }
    }

    public CleanupDteHostProcessesResult CleanupDteHostProcesses(CleanupDteHostProcessesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<string> processNames = request.ProcessNames is { Count: > 0 }
            ? request.ProcessNames
            : DteHostProcessNames;
        HashSet<int> requestedProcessIds = request.ProcessIds is { Count: > 0 }
            ? new HashSet<int>(request.ProcessIds)
            : [];

        List<DteHostProcessCleanupItem> items = [];
        foreach (string processName in processNames.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (DiagnosticsProcess process in DiagnosticsProcess.GetProcessesByName(processName))
            {
                using (process)
                {
                    int processId;
                    try
                    {
                        processId = process.Id;
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    string? title = TryReadProcessString(() => process.MainWindowTitle);
                    string? path = TryReadProcessString(() => process.MainModule?.FileName);
                    DateTimeOffset? startTime = TryReadProcessTime(process);
                    bool explicitProcessMatch = requestedProcessIds.Count == 0 || requestedProcessIds.Contains(processId);
                    bool windowMatch = request.IncludeWindowed || string.IsNullOrWhiteSpace(title);
                    bool matched = explicitProcessMatch && windowMatch;
                    bool killed = false;
                    string? error = null;

                    if (matched && !request.DryRun)
                    {
                        try
                        {
                            process.Kill(request.KillProcessTree);
                            killed = true;
                        }
                        catch (InvalidOperationException)
                        {
                            error = "Process exited before cleanup.";
                        }
                        catch (System.ComponentModel.Win32Exception ex)
                        {
                            error = ex.Message;
                        }
                        catch (NotSupportedException ex)
                        {
                            error = ex.Message;
                        }
                    }

                    items.Add(new DteHostProcessCleanupItem(
                        processId,
                        process.ProcessName,
                        title,
                        path,
                        startTime,
                        matched,
                        killed,
                        error));
                }
            }
        }

        int matchedCount = items.Count(item => item.Matched);
        int killedCount = items.Count(item => item.Killed);
        bool succeeded = request.DryRun || items.Where(item => item.Matched).All(item => item.Killed || item.ErrorMessage == "Process exited before cleanup.");
        string summary = request.DryRun
            ? $"DTE host cleanup dry-run found {matchedCount} candidate process(es)."
            : $"DTE host cleanup killed {killedCount} of {matchedCount} candidate process(es).";

        return new CleanupDteHostProcessesResult(
            succeeded,
            request.DryRun,
            matchedCount,
            killedCount,
            items,
            summary);
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
            string projectFilePath = ResolveCppProjectPaths(session, request.ProjectName).ProjectFilePath;
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

    public VisualStudioCppProjectInfo CreateVisualStudioCppProject(
        TwinCatEngineeringSession session,
        CreateVisualStudioCppProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        bool preferFallback = request.AllowTemplateFallback &&
                              (request.CandidateTemplatePaths is null || request.CandidateTemplatePaths.Count == 0);
        string solutionDirectory = preferFallback && !string.IsNullOrWhiteSpace(session.CurrentSolutionDirectory)
            ? session.CurrentSolutionDirectory!
            : ResolveSolutionDirectory(session);
        string projectDirectory = ResolveRequestedProjectDirectory(solutionDirectory, request.ProjectName, request.ProjectDirectory);
        string projectFilePath = Path.Combine(projectDirectory, request.ProjectName + ".vcxproj");
        Directory.CreateDirectory(projectDirectory);

        if (File.Exists(projectFilePath) && (!preferFallback || FindProjectByName(session.Dte, request.ProjectName) is not null))
        {
            return new VisualStudioCppProjectInfo(projectFilePath, ReadOrCreateProjectGuid(projectFilePath), projectDirectory);
        }

        string? templatePath = preferFallback ? null : ResolveVisualStudioCppTemplatePath(session, request);
        if (!preferFallback && !string.IsNullOrWhiteSpace(templatePath))
        {
            RetryComCall(
                () => session.Dte.Solution.AddFromTemplate(templatePath, projectDirectory, request.ProjectName, false),
                120,
                500);
            WaitUntil(
                () => File.Exists(projectFilePath),
                TimeSpan.FromMinutes(2),
                $"Visual Studio C++ project file was not created in time: {projectFilePath}");
        }
        else if (request.AllowTemplateFallback)
        {
            CreateFallbackVisualStudioCppProjectFiles(projectFilePath, request.ProjectName, request.PlatformToolset);
            RetryComCall(() => session.Dte.Solution.AddFromFile(projectFilePath, false), 60, 500);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unable to resolve a Visual Studio C++ template for TemplateKind='{request.TemplateKind}'. " +
                "Pass candidate template paths or set AllowTemplateFallback=true explicitly.");
        }

        if (!string.IsNullOrWhiteSpace(request.PlatformToolset))
        {
            SetCppProjectProperty(
                session,
                new SetCppProjectPropertyRequest(
                    request.ProjectName,
                    "PlatformToolset",
                    request.PlatformToolset!,
                    PropertyGroupLabel: "Configuration"));
        }

        SaveAll(session);
        string projectGuid = ReadOrCreateProjectGuid(projectFilePath);
        WaitUntil(
            () => FindProjectByName(session.Dte, request.ProjectName) is not null,
            TimeSpan.FromMinutes(1),
            $"Visual Studio solution model did not expose project '{request.ProjectName}'.");

        return new VisualStudioCppProjectInfo(projectFilePath, projectGuid, projectDirectory);
    }

    public ScopeProjectInfo CreateScopeProject(
        TwinCatEngineeringSession session,
        CreateScopeProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectName))
        {
            throw new InvalidOperationException("ProjectName must not be empty.");
        }

        string solutionDirectory = ResolveSolutionDirectory(session);
        string solutionPath = RetryComCall(() => session.Dte.Solution.FullName, 10, 300);
        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            throw new InvalidOperationException("The loaded solution does not have a saved .sln path.");
        }

        string projectDirectory = ResolveRequestedProjectDirectory(solutionDirectory, request.ProjectName, request.ProjectDirectory);
        string projectFilePath = Path.Combine(projectDirectory, request.ProjectName + ".tcmproj");
        string? configurationFileName = !string.IsNullOrWhiteSpace(request.ConfigurationFileName)
            ? NormalizeProjectRelativePath(request.ConfigurationFileName!)
            : request.CreateEmptyConfiguration
                ? request.ProjectName + ".tcscopex"
                : null;
        string? configurationFilePath = configurationFileName is not null
            ? ResolveProjectContainedPath(projectDirectory, configurationFileName)
            : null;

        Directory.CreateDirectory(projectDirectory);
        EnsureScopeProjectFile(projectFilePath, request.ProjectName, configurationFileName);

        if (request.CreateEmptyConfiguration && configurationFilePath is not null && !File.Exists(configurationFilePath))
        {
            WriteEmptyScopeConfiguration(configurationFilePath, request.ProjectName);
        }

        bool addedToSolution = FindProjectByFullName(session.Dte, Path.GetFullPath(projectFilePath)) is not null;
        bool usedSolutionFileFallback = false;
        if (!addedToSolution)
        {
            if (request.AllowSolutionFileFallback)
            {
                SaveAll(session);
                string projectGuid = ReadOrCreateProjectGuid(projectFilePath);
                UpsertSlnProjectEntry(solutionPath, TwinCatScopeProjectTypeGuid, request.ProjectName, projectFilePath, projectGuid);
                usedSolutionFileFallback = true;
                addedToSolution = true;
            }
            else
            {
                RetryComCall(() => session.Dte.Solution.AddFromFile(projectFilePath, false), 60, 500);
                WaitUntil(
                    () => FindProjectByFullName(session.Dte, Path.GetFullPath(projectFilePath)) is not null,
                    TimeSpan.FromMinutes(1),
                    $"Visual Studio solution model did not expose Scope project '{request.ProjectName}'.");
                addedToSolution = true;
                SaveAll(session);
            }
        }
        else
        {
            SaveAll(session);
        }

        string finalProjectGuid = ReadOrCreateProjectGuid(projectFilePath);
        if (usedSolutionFileFallback)
        {
            UpsertSlnProjectEntry(solutionPath, TwinCatScopeProjectTypeGuid, request.ProjectName, projectFilePath, finalProjectGuid);
        }

        return new ScopeProjectInfo(
            projectFilePath,
            finalProjectGuid,
            projectDirectory,
            configurationFilePath,
            addedToSolution,
            usedSolutionFileFallback);
    }

    public SolutionProjectDependencyResult EnsureSolutionProjectDependency(
        TwinCatEngineeringSession session,
        EnsureSolutionProjectDependencyRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        string solutionPath = RetryComCall(() => session.Dte.Solution.FullName, 10, 300);
        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            throw new InvalidOperationException("The loaded solution does not have a saved .sln path.");
        }

        SaveAll(session);
        SlnProjectEntry project = FindSlnProjectEntry(solutionPath, request.ProjectName);
        SlnProjectEntry dependency = FindSlnProjectEntry(solutionPath, request.DependsOnProjectName);
        UpsertSlnProjectDependency(solutionPath, project.Guid, dependency.Guid);

        return new SolutionProjectDependencyResult(project.Guid, dependency.Guid);
    }

    public TwinCatNodeInfo CreateIoDevice(TwinCatEngineeringSession session, CreateIoDeviceRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredText(request.Name, nameof(request.Name));
        ValidateRequiredText(request.ParentTreeItemPath, nameof(request.ParentTreeItemPath));

        ITcSysManager sysManager = RequireSysManager(session);
        string candidatePath = JoinTreePath(request.ParentTreeItemPath, request.Name);
        ITcSmTreeItem? existing = request.AllowExisting
            ? TryGetTreeItem(sysManager, candidatePath)
            : null;
        if (existing is not null)
        {
            try
            {
                ApplyDisabledState(existing, request.Disabled);
                SaveAll(session);
                return CreateNodeInfo(existing, candidatePath, UsedFallback: false);
            }
            finally
            {
                ReleaseComObjectIfNeeded(existing);
            }
        }

        ITcSmTreeItem? parent = null;
        ITcSmTreeItem? created = null;
        try
        {
            parent = GetTreeItem(sysManager, request.ParentTreeItemPath);
            created = RetryComCall(() => parent.CreateChild(
                request.Name,
                request.SubType,
                request.Before ?? string.Empty,
                request.VInfo ?? string.Empty),
                30,
                500);
            ApplyDisabledState(created, request.Disabled);
            SaveAll(session);
            ThreadingThread.Sleep(Math.Max(0, request.PostCreateDelayMs));
            return CreateNodeInfo(created, candidatePath, UsedFallback: false);
        }
        finally
        {
            ReleaseComObjectIfNeeded(created);
            ReleaseComObjectIfNeeded(parent);
        }
    }

    public TwinCatNodeInfo CreateEthercatBox(TwinCatEngineeringSession session, CreateEthercatBoxRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredText(request.Name, nameof(request.Name));
        ValidateRequiredText(request.ParentTreeItemPath, nameof(request.ParentTreeItemPath));

        string? vInfo = string.IsNullOrWhiteSpace(request.VInfo)
            ? request.ProductRevision
            : request.VInfo;
        CreateIoDeviceRequest createRequest = new(
            request.Name,
            request.SubType,
            request.ParentTreeItemPath,
            request.Before,
            vInfo,
            request.Disabled,
            request.AllowExisting,
            request.PostCreateDelayMs);
        return CreateIoDevice(session, createRequest);
    }

    public EngineeringCommandResult GenerateIoMappings(TwinCatEngineeringSession session, GenerateIoMappingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteSysManagerCommand(
            session,
            request.SuppressUi,
            request.TimeoutMs,
            "TwinCAT GenerateMappings",
            "GenerateMappings",
            commands => commands.GenerateMappings(),
            commands => commands.GenerateMappings(),
            request.AllowDteCommandFallback,
            "TwinCAT.生成映射",
            "TwinCAT.GenerateMappings",
            "TcXaeShell.TwinCAT.GenerateMappings");
    }

    public EngineeringCommandResult SearchIoDevices(TwinCatEngineeringSession session, SearchIoDevicesRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteSysManagerCommand(
            session,
            request.SuppressUi,
            request.TimeoutMs,
            "TwinCAT SearchDevices",
            "SearchDevices",
            commands => commands.SearchDevices(),
            commands => commands.SearchDevices());
    }

    public EngineeringCommandResult ReloadIoDevices(TwinCatEngineeringSession session, ReloadIoDevicesRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        return ExecuteSysManagerCommand(
            session,
            request.SuppressUi,
            request.TimeoutMs,
            "TwinCAT ReloadDevices",
            "ReloadDevices",
            commands => commands.ReloadDevices(),
            commands => commands.ReloadDevices());
    }

    public ApplyIoTreePlanResult ApplyIoTreePlan(TwinCatEngineeringSession session, ApplyIoTreePlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        List<TwinCatNodeInfo> nodes = [];
        int deviceCount = 0;
        foreach (CreateIoDeviceRequest device in request.Devices ?? Array.Empty<CreateIoDeviceRequest>())
        {
            nodes.Add(CreateIoDevice(session, device));
            deviceCount++;
        }

        int boxCount = 0;
        foreach (CreateEthercatBoxRequest box in request.Boxes ?? Array.Empty<CreateEthercatBoxRequest>())
        {
            nodes.Add(CreateEthercatBox(session, box));
            boxCount++;
        }

        SaveAll(session);
        return new ApplyIoTreePlanResult(
            true,
            deviceCount,
            boxCount,
            nodes,
            $"Applied IO tree plan: {deviceCount} device(s), {boxCount} box(es).");
    }

    private EngineeringCommandResult ExecuteSysManagerCommand(
        TwinCatEngineeringSession session,
        bool suppressUi,
        int timeoutMs,
        string operationName,
        string commandName,
        Action<ITcSmCommands> invokeCommands,
        Action<ITcSmCommands2> invokeCommands2,
        bool allowDteCommandFallback = false,
        params string[] dteCommandFallbacks)
    {
        ITcSysManager sysManager = RequireSysManager(session);
        if (suppressUi)
        {
            SuppressVisualStudioUi(session);
        }

        List<string> attempted = [];
        string command = RunOnStaThreadWithTimeout(
            () =>
            {
                attempted.Add("ITcSmCommands." + commandName);
                if (sysManager is ITcSmCommands smCommands)
                {
                    RetryComCall(() => invokeCommands(smCommands), 10, 500);
                    return "ITcSmCommands." + commandName;
                }

                attempted.Add("ITcSmCommands2." + commandName);
                if (sysManager is ITcSmCommands2 commands2)
                {
                    RetryComCall(() => invokeCommands2(commands2), 10, 500);
                    return "ITcSmCommands2." + commandName;
                }

                if (allowDteCommandFallback)
                {
                    foreach (string dteCommand in dteCommandFallbacks)
                    {
                        attempted.Add(dteCommand);
                        try
                        {
                            RetryComCall(() => session.Dte.ExecuteCommand(dteCommand), 5, 500);
                            return dteCommand;
                        }
                        catch
                        {
                        }
                    }
                }

                throw new InvalidOperationException(
                    $"The current ITcSysManager object does not expose ITcSmCommands.{commandName}; menu-command fallback is intentionally disabled for unattended runs.");
            },
            timeoutMs > 0 ? timeoutMs : 120000,
            operationName);

        SaveAll(session);
        return new EngineeringCommandResult(true, command, attempted);
    }

    public CppProjectItemResult CreateCppProjectItem(
        TwinCatEngineeringSession session,
        CreateCppProjectItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        return CreateCppProjectItem(paths, request, session);
    }

    public CppProjectItemResult CreateCppProjectItem(
        string twinCatProjectDirectory,
        CreateCppProjectItemRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twinCatProjectDirectory);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(twinCatProjectDirectory, request.ProjectName);
        return CreateCppProjectItem(paths, request, session: null);
    }

    private CppProjectItemResult CreateCppProjectItem(
        CppProjectPaths paths,
        CreateCppProjectItemRequest request,
        TwinCatEngineeringSession? session)
    {
        string relativePath = NormalizeProjectRelativePath(request.RelativePath);
        string filePath = ResolveProjectContainedPath(paths.ProjectDirectory, relativePath);
        CppProjectItemType itemType = ResolveCppProjectItemType(relativePath, request.ItemType);

        bool fileExists = File.Exists(filePath);
        bool registered = VcxprojHasItem(paths.ProjectFilePath, relativePath, itemType);
        if (request.ConflictPolicy == ProjectItemConflictPolicy.FailIfExists && (fileExists || registered))
        {
            throw new InvalidOperationException(
                $"C++ project item already exists or is already registered: Project='{request.ProjectName}', RelativePath='{relativePath}'.");
        }

        if (request.CreatePhysicalFile && !fileExists)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            using FileStream stream = File.Create(filePath);
        }

        bool addedToProject = false;
        if (request.AddToProject)
        {
            if (!request.AllowMsBuildFallback && session is null)
            {
                throw new InvalidOperationException(
                    "C++ project item creation without MSBuild fallback requires an active DTE session.");
            }

            if (!request.AllowMsBuildFallback)
            {
                Project project = FindProjectByName(session!.Dte, request.ProjectName)
                    ?? throw new InvalidOperationException($"DTE solution model does not contain C++ project '{request.ProjectName}'.");
                RetryComCall(() => project.ProjectItems.AddFromFile(filePath), 20, 300);
            }

            UpsertCppProjectItemRegistration(paths.ProjectFilePath, relativePath, itemType, request.ConflictPolicy);
            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                UpsertCppProjectItemFilter(paths.FiltersFilePath, relativePath, itemType, request.Filter!);
            }

            addedToProject = VcxprojHasItem(paths.ProjectFilePath, relativePath, itemType);
        }

        return new CppProjectItemResult(paths.ProjectFilePath, filePath, itemType, request.Filter, addedToProject);
    }

    public CppProjectItemContentResult WriteCppProjectItemContent(
        TwinCatEngineeringSession session,
        WriteCppProjectItemContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        return WriteCppProjectItemContent(paths, request);
    }

    public CppProjectItemContentResult WriteCppProjectItemContent(
        string twinCatProjectDirectory,
        WriteCppProjectItemContentRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twinCatProjectDirectory);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(twinCatProjectDirectory, request.ProjectName);
        return WriteCppProjectItemContent(paths, request);
    }

    private static CppProjectItemContentResult WriteCppProjectItemContent(
        CppProjectPaths paths,
        WriteCppProjectItemContentRequest request)
    {
        string relativePath = NormalizeProjectRelativePath(request.RelativePath);
        string filePath = ResolveProjectContainedPath(paths.ProjectDirectory, relativePath);
        if (request.RequireProjectRegistration &&
            !VcxprojHasAnyItem(paths.ProjectFilePath, relativePath))
        {
            throw new InvalidOperationException(
                $"C++ project item '{relativePath}' is not registered in '{paths.ProjectFilePath}'.");
        }

        WriteProjectItemContentToFile(request, filePath);
        byte[] bytes = File.ReadAllBytes(filePath);
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new CppProjectItemContentResult(filePath, sha256, bytes.LongLength);
    }

    public RemoveCppProjectItemResult RemoveCppProjectItem(
        TwinCatEngineeringSession session,
        RemoveCppProjectItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        return RemoveCppProjectItem(paths, request);
    }

    public RemoveCppProjectItemResult RemoveCppProjectItem(
        string twinCatProjectDirectory,
        RemoveCppProjectItemRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twinCatProjectDirectory);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(twinCatProjectDirectory, request.ProjectName);
        return RemoveCppProjectItem(paths, request);
    }

    private static RemoveCppProjectItemResult RemoveCppProjectItem(
        CppProjectPaths paths,
        RemoveCppProjectItemRequest request)
    {
        string relativePath = NormalizeProjectRelativePath(request.RelativePath);
        CppProjectItemType itemType = ResolveCppProjectItemType(relativePath, request.ItemType);
        string filePath = ResolveProjectContainedPath(paths.ProjectDirectory, relativePath);

        bool removedFromProject = RemoveCppProjectItemRegistration(paths.ProjectFilePath, relativePath, itemType);
        bool removedFromFilters = true;
        if (request.RemoveFilterEntry && File.Exists(paths.FiltersFilePath))
        {
            removedFromFilters = RemoveCppProjectItemFilter(paths.FiltersFilePath, relativePath, itemType);
        }

        if (!removedFromProject && !removedFromFilters && !File.Exists(filePath) && !request.IgnoreMissing)
        {
            throw new InvalidOperationException(
                $"C++ project item '{relativePath}' was not found in project '{request.ProjectName}'.");
        }

        bool deletedFile = false;
        if (request.DeletePhysicalFile && File.Exists(filePath))
        {
            File.Delete(filePath);
            deletedFile = true;
        }

        return new RemoveCppProjectItemResult(removedFromProject, deletedFile);
    }

    public CppProjectPropertyResult SetCppProjectProperty(
        TwinCatEngineeringSession session,
        SetCppProjectPropertyRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        return SetCppProjectProperty(paths, request);
    }

    public CppProjectPropertyResult SetCppProjectProperty(
        string twinCatProjectDirectory,
        SetCppProjectPropertyRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twinCatProjectDirectory);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(twinCatProjectDirectory, request.ProjectName);
        return SetCppProjectProperty(paths, request);
    }

    private static CppProjectPropertyResult SetCppProjectProperty(
        CppProjectPaths paths,
        SetCppProjectPropertyRequest request)
    {
        UpsertCppProjectProperty(
            paths.ProjectFilePath,
            request.PropertyName,
            request.Value,
            request.Condition,
            request.PropertyGroupLabel);
        return new CppProjectPropertyResult(paths.ProjectFilePath, request.PropertyName, request.Condition);
    }

    public CppItemDefinitionPropertyResult SetCppItemDefinitionProperty(
        TwinCatEngineeringSession session,
        SetCppItemDefinitionPropertyRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        return SetCppItemDefinitionProperty(paths, request);
    }

    public CppItemDefinitionPropertyResult SetCppItemDefinitionProperty(
        string twinCatProjectDirectory,
        SetCppItemDefinitionPropertyRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twinCatProjectDirectory);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(twinCatProjectDirectory, request.ProjectName);
        return SetCppItemDefinitionProperty(paths, request);
    }

    private static CppItemDefinitionPropertyResult SetCppItemDefinitionProperty(
        CppProjectPaths paths,
        SetCppItemDefinitionPropertyRequest request)
    {
        UpsertCppItemDefinitionProperty(
            paths.ProjectFilePath,
            request.ToolName,
            request.PropertyName,
            request.Value,
            request.Condition);
        return new CppItemDefinitionPropertyResult(paths.ProjectFilePath, request.ToolName, request.PropertyName, request.Condition);
    }

    public CppProjectItemMetadataResult SetCppProjectItemMetadata(
        TwinCatEngineeringSession session,
        SetCppProjectItemMetadataRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        return SetCppProjectItemMetadata(paths, request);
    }

    public CppProjectItemMetadataResult SetCppProjectItemMetadata(
        string twinCatProjectDirectory,
        SetCppProjectItemMetadataRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(twinCatProjectDirectory);
        ArgumentNullException.ThrowIfNull(request);

        CppProjectPaths paths = ResolveCppProjectPaths(twinCatProjectDirectory, request.ProjectName);
        return SetCppProjectItemMetadata(paths, request);
    }

    private static CppProjectItemMetadataResult SetCppProjectItemMetadata(
        CppProjectPaths paths,
        SetCppProjectItemMetadataRequest request)
    {
        string relativePath = NormalizeProjectRelativePath(request.RelativePath);
        CppProjectItemType itemType = ResolveCppProjectItemType(relativePath, request.ItemType);
        UpsertCppProjectItemMetadata(
            paths.ProjectFilePath,
            relativePath,
            itemType,
            request.MetadataName,
            request.Value,
            request.Condition);
        return new CppProjectItemMetadataResult(paths.ProjectFilePath, relativePath, request.MetadataName, request.Condition);
    }

    public PublishModulesResult PublishModules(TwinCatEngineeringSession session, PublishModulesRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (request.RunTmcCodeGeneratorFirst)
        {
            StartTmcCodeGenerator(
                session,
                new StartTmcCodeGeneratorRequest(
                    request.ProjectName,
                    PostStartDelayMs: 0,
                    WaitForUpdatedTmcTimeoutMs: request.WaitForUpdatedTmcTimeoutMs));
        }

        ITcSysManager sysManager = RequireSysManager(session);
        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        string tmcPath = Path.Combine(paths.ProjectDirectory, request.ProjectName + ".tmc");
        DateTime previousWrite = File.Exists(tmcPath) ? File.GetLastWriteTimeUtc(tmcPath) : DateTime.MinValue;
        string? previousHash = File.Exists(tmcPath) ? ComputeFileSha256(tmcPath) : null;

        ITcSmTreeItem? projectItem = null;
        try
        {
            projectItem = GetTreeItem(sysManager, $"TIXC^{request.ProjectName}");
            XDocument document = XDocument.Parse(RetryComCall(() => projectItem.ProduceXml(true)));
            XElement publish = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "PublishModules")
                ?? throw new InvalidOperationException($"PublishModules method is not exposed for C++ project '{request.ProjectName}'.");
            SetOrCreateChildElementValue(publish, "Active", "true");
            RetryComCall(() => projectItem.ConsumeXml(document.ToString(SaveOptions.DisableFormatting)), 20, 500);
            SaveAll(session);
        }
        finally
        {
            ReleaseComObjectIfNeeded(projectItem);
        }

        ThreadingThread.Sleep(Math.Max(0, request.PostPublishDelayMs));
        bool updated = WaitForTmcUpdate(tmcPath, previousWrite, previousHash, request.WaitForUpdatedTmcTimeoutMs);
        bool hasReadableTmc = TryReadTmc(tmcPath, out _);
        return new PublishModulesResult(hasReadableTmc, File.Exists(tmcPath) ? tmcPath : null, updated);
    }

    public StartTmcCodeGeneratorResult StartTmcCodeGenerator(TwinCatEngineeringSession session, StartTmcCodeGeneratorRequest request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        ITcSysManager sysManager = RequireSysManager(session);
        CppProjectPaths paths = ResolveCppProjectPaths(session, request.ProjectName);
        string tmcPath = Path.Combine(paths.ProjectDirectory, request.ProjectName + ".tmc");
        DateTime previousWrite = File.Exists(tmcPath) ? File.GetLastWriteTimeUtc(tmcPath) : DateTime.MinValue;
        string? previousHash = File.Exists(tmcPath) ? ComputeFileSha256(tmcPath) : null;

        ITcSmTreeItem? projectItem = null;
        try
        {
            projectItem = GetTreeItem(sysManager, $"TIXC^{request.ProjectName}");
            XDocument document = XDocument.Parse(RetryComCall(() => projectItem.ProduceXml(true)));
            XElement method = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "StartTmcCodeGenerator")
                ?? throw new InvalidOperationException($"StartTmcCodeGenerator method is not exposed for C++ project '{request.ProjectName}'.");
            SetOrCreateChildElementValue(method, "Active", "true");
            RetryComCall(() => projectItem.ConsumeXml(document.ToString(SaveOptions.DisableFormatting)), 20, 500);
            SaveAll(session);
        }
        finally
        {
            ReleaseComObjectIfNeeded(projectItem);
        }

        ThreadingThread.Sleep(Math.Max(0, request.PostStartDelayMs));
        bool updated = WaitForTmcUpdate(tmcPath, previousWrite, previousHash, request.WaitForUpdatedTmcTimeoutMs);
        bool hasReadableTmc = TryReadTmc(tmcPath, out _);
        return new StartTmcCodeGeneratorResult(hasReadableTmc, File.Exists(tmcPath) ? tmcPath : null, updated);
    }

    public VerifyTmcDataAreasResult VerifyTmcDataAreas(VerifyTmcDataAreasRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string tmcPath = Path.GetFullPath(request.ProjectTmcPath);
        if (!File.Exists(tmcPath))
        {
            return new VerifyTmcDataAreasResult(
                false,
                tmcPath,
                request.Modules.Count,
                0,
                "Project TMC file does not exist.",
                [$"Project TMC file does not exist: {tmcPath}"]);
        }

        XDocument document = XDocument.Load(tmcPath);
        Dictionary<string, TmcModuleShape> actualModules = ReadTmcModuleShapes(document);
        List<string> errors = [];
        int matchedModules = 0;

        foreach (TmcModuleExpectation expectedModule in request.Modules)
        {
            if (string.IsNullOrWhiteSpace(expectedModule.ModuleName))
            {
                errors.Add("Expected module name must not be empty.");
                continue;
            }

            if (!actualModules.TryGetValue(expectedModule.ModuleName, out TmcModuleShape? actualModule))
            {
                errors.Add($"Missing module '{expectedModule.ModuleName}'.");
                continue;
            }

            matchedModules++;
            ValidateExpectedDataAreas(expectedModule, actualModule, errors);
        }

        if (request.FailOnUnexpectedModule)
        {
            HashSet<string> expectedNames = request.Modules
                .Select(module => module.ModuleName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string actualName in actualModules.Keys.Where(name => !expectedNames.Contains(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Unexpected module '{actualName}'.");
            }
        }

        bool succeeded = errors.Count == 0;
        string summary = succeeded
            ? $"TMC data areas matched {matchedModules}/{request.Modules.Count} expected module(s)."
            : $"TMC data area verification failed with {errors.Count} error(s).";
        return new VerifyTmcDataAreasResult(
            succeeded,
            tmcPath,
            request.Modules.Count,
            matchedModules,
            summary,
            errors);
    }

    public ApplyTmcModuleModelResult ApplyTmcModuleModel(ApplyTmcModuleModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredText(request.ProjectTmcPath, nameof(request.ProjectTmcPath));
        ValidateRequiredText(request.ProjectName, nameof(request.ProjectName));
        if (request.Modules is null || request.Modules.Count == 0)
        {
            throw new InvalidOperationException("ApplyTmcModuleModel requires at least one module.");
        }

        string tmcPath = Path.GetFullPath(request.ProjectTmcPath);
        if (!File.Exists(tmcPath))
        {
            throw new FileNotFoundException("Project TMC file was not found.", tmcPath);
        }

        foreach (TmcModuleModel module in request.Modules)
        {
            ValidateRequiredText(module.Name, nameof(module.Name));
            _ = NormalizeGuidText(module.Guid);
        }

        TmcGeneratedModel generatedModel = ReadGeneratedTmcModel(request.GeneratedServicesHeaderPath, request.GeneratedHeaderPaths);
        XDocument document = XDocument.Load(tmcPath, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidOperationException("Project TMC root element is missing.");

        EnsureVendorElement(root);
        if (request.ReplaceDataTypesFromGeneratedHeader && generatedModel.DataTypes.Count > 0)
        {
            ReplaceTopLevelSection(root, "DataTypes", new XElement("DataTypes", generatedModel.DataTypes.Select(item => new XElement(item))));
        }

        EnsureCppGroupElement(root);
        XElement modulesElement = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Modules")
            ?? new XElement("Modules");
        if (modulesElement.Parent is null)
        {
            root.Add(modulesElement);
        }

        HashSet<string> requestedModuleNames = request.Modules
            .Select(module => module.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.RemoveUnexpectedModules)
        {
            foreach (XElement module in modulesElement.Elements().Where(element =>
                         element.Name.LocalName == "Module" &&
                         !requestedModuleNames.Contains(GetChildElementValue(element, "Name") ?? element.Attribute("ClassName")?.Value ?? string.Empty)).ToList())
            {
                module.Remove();
            }
        }

        foreach (TmcModuleModel module in request.Modules)
        {
            XElement moduleElement = CreateTmcModuleElement(module, request.ProjectName, generatedModel.TypeGuids);
            XElement? existingModule = modulesElement.Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "Module" &&
                    string.Equals(
                        GetChildElementValue(element, "Name") ?? element.Attribute("ClassName")?.Value,
                        module.Name,
                        StringComparison.OrdinalIgnoreCase));

            if (existingModule is null)
            {
                modulesElement.Add(moduleElement);
            }
            else
            {
                existingModule.ReplaceWith(moduleElement);
            }
        }

        string libraryName = string.IsNullOrWhiteSpace(request.LibraryName) ? request.ProjectName : request.LibraryName;
        ReplaceTopLevelSection(
            root,
            "Library",
            new XElement(
                "Library",
                new XElement("Name", libraryName),
                new XElement("Version", string.IsNullOrWhiteSpace(request.LibraryVersion) ? "0.0.0.1" : request.LibraryVersion)));

        document.Save(tmcPath);
        return new ApplyTmcModuleModelResult(
            true,
            tmcPath,
            request.Modules.Count,
            $"Applied {request.Modules.Count} TMC module model(s) to {tmcPath}.");
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
                if (TryNormalizeCppInstanceNameInTsproj(
                        projectPath,
                        request.ProjectName,
                        request.InstanceBaseName,
                        displayName,
                        objectId,
                        out string persistedName,
                        out string? persistedObjectId))
                {
                    displayName = persistedName;
                    objectId = string.IsNullOrWhiteSpace(persistedObjectId) ? objectId : persistedObjectId;
                }
                else
                {
                    return CreateOfflineModuleInstanceFallback(session, request, moduleGuid);
                }

                _ = TryNormalizeCppInstanceNamesInTsproj(projectPath, request.ProjectName);
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
        SuppressVisualStudioUi(session);

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

        TryNormalizeCurrentTwinCatProjectInstanceNames(session);
    }

    public BuildResult BuildCurrentSolution(TwinCatEngineeringSession session, BuildSolutionRequest request)
    {
        if (request.BuildEngine == BuildSolutionEngine.CommandLine)
        {
            string solutionPath = RetryComCall(() => session.Dte.Solution.FullName);
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                throw new InvalidOperationException("No solution is currently loaded.");
            }

            try
            {
                SaveAll(session);
            }
            catch
            {
                // Command-line build can continue from the saved project files if DTE is already unstable.
            }

            return BuildSolutionFromCommandLineCore(solutionPath, request);
        }

        if (request.BuildEngine == BuildSolutionEngine.MsBuildProjects)
        {
            string solutionPath = RetryComCall(() => session.Dte.Solution.FullName);
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                throw new InvalidOperationException("No solution is currently loaded.");
            }

            try
            {
                SaveAll(session);
            }
            catch
            {
                // MSBuild project sequence uses the on-disk project files if DTE is already unstable.
            }

            return BuildSolutionFromMsBuildProjects(solutionPath, request);
        }

        if (session.Dte.Solution is null)
        {
            throw new InvalidOperationException("No solution is currently loaded.");
        }

        SuppressVisualStudioUi(session);
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
        return new BuildResult(succeeded, lastBuildInfo, outputText, BuildEngine: "dte");
    }

    public BuildResult BuildSolutionFromCommandLine(string solutionPath, BuildSolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            throw new InvalidOperationException("A solution path is required for command-line build.");
        }

        string fullSolutionPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullSolutionPath))
        {
            throw new FileNotFoundException("Solution file was not found for command-line build.", fullSolutionPath);
        }

        return BuildSolutionFromCommandLineCore(fullSolutionPath, request);
    }

    public BuildResult BuildSolutionFromMsBuildProjects(string solutionPath, BuildSolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            throw new InvalidOperationException("A solution path is required for MSBuild project sequence build.");
        }

        string fullSolutionPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(fullSolutionPath))
        {
            throw new FileNotFoundException("Solution file was not found for MSBuild project sequence build.", fullSolutionPath);
        }

        return BuildSolutionFromMsBuildProjectsCore(fullSolutionPath, request);
    }

    private static BuildResult BuildSolutionFromCommandLineCore(string solutionPath, BuildSolutionRequest request)
    {
        string devenvPath = ResolveDevenvComPath(request.DevenvPath);
        string configurationPlatform = $"{request.Configuration}|{request.Platform}";
        string logFilePath = string.IsNullOrWhiteSpace(request.LogFilePath)
            ? Path.Combine(Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory, "_json_plan_evidence", "devenv-build.log")
            : Path.GetFullPath(request.LogFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

        DiagnosticsProcessStartInfo startInfo = new()
        {
            FileName = devenvPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(solutionPath);
        startInfo.ArgumentList.Add("/Build");
        startInfo.ArgumentList.Add(configurationPlatform);
        startInfo.ArgumentList.Add("/Out");
        startInfo.ArgumentList.Add(logFilePath);

        StringBuilder output = new();
        using DiagnosticsProcess process = new() { StartInfo = startInfo };
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        bool finished = process.WaitForExit(request.TimeoutMs > 0 ? request.TimeoutMs : 300000);
        if (!finished)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"devenv.com build did not finish within {request.TimeoutMs} ms.");
        }

        output.Append(stdout.GetAwaiter().GetResult());
        string error = stderr.GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(error))
        {
            if (output.Length > 0)
            {
                output.AppendLine();
            }

            output.Append(error);
        }

        string? logText = File.Exists(logFilePath) ? File.ReadAllText(logFilePath) : null;
        if (!string.IsNullOrWhiteSpace(logText))
        {
            if (output.Length > 0)
            {
                output.AppendLine();
            }

            output.AppendLine("--- devenv /Out log ---");
            output.Append(logText);
        }

        string commandLine = BuildCommandLine(devenvPath, startInfo.ArgumentList);
        return new BuildResult(
            process.ExitCode == 0,
            process.ExitCode,
            output.ToString().Trim(),
            BuildEngine: "command-line",
            ExitCode: process.ExitCode,
            CommandLine: commandLine,
            LogFilePath: logFilePath);
    }

    private static BuildResult BuildSolutionFromMsBuildProjectsCore(string solutionPath, BuildSolutionRequest request)
    {
        string msBuildPath = ResolveMsBuildPath(request.MsBuildPath);
        string solutionDirectory = EnsureTrailingDirectorySeparator(Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory);
        IReadOnlyList<string> projectPaths = ResolveMsBuildProjectSequence(solutionDirectory, request.ProjectPaths);
        string logFilePath = string.IsNullOrWhiteSpace(request.LogFilePath)
            ? Path.Combine(solutionDirectory, "_json_plan_evidence", "msbuild-projects.log")
            : Path.GetFullPath(request.LogFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

        StringBuilder combinedOutput = new();
        int exitCode = 0;
        foreach (string projectPath in projectPaths)
        {
            DiagnosticsProcessStartInfo startInfo = new()
            {
                FileName = msBuildPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add($"/p:Configuration={request.Configuration}");
            startInfo.ArgumentList.Add($"/p:Platform={request.Platform}");
            startInfo.ArgumentList.Add($"/p:SolutionDir={solutionDirectory}");
            startInfo.ArgumentList.Add("/m:1");
            startInfo.ArgumentList.Add("/v:minimal");
            startInfo.ArgumentList.Add("/nr:false");

            using DiagnosticsProcess process = new() { StartInfo = startInfo };
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            bool finished = process.WaitForExit(request.TimeoutMs > 0 ? request.TimeoutMs : 300000);
            if (!finished)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                throw new TimeoutException($"MSBuild did not finish within {request.TimeoutMs} ms for {projectPath}.");
            }

            string commandLine = BuildCommandLine(msBuildPath, startInfo.ArgumentList);
            combinedOutput.AppendLine(">>> " + commandLine);
            combinedOutput.Append(stdout.GetAwaiter().GetResult());
            string error = stderr.GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(error))
            {
                if (combinedOutput.Length > 0)
                {
                    combinedOutput.AppendLine();
                }

                combinedOutput.Append(error);
            }

            exitCode = process.ExitCode;
            if (exitCode != 0)
            {
                break;
            }
        }

        string outputText = combinedOutput.ToString().Trim();
        File.WriteAllText(logFilePath, outputText);
        return new BuildResult(
            exitCode == 0,
            exitCode,
            outputText,
            BuildEngine: "msbuild-projects",
            ExitCode: exitCode,
            CommandLine: $"{QuoteCommandArgument(msBuildPath)} <project sequence: {string.Join(";", projectPaths.Select(Path.GetFileName))}>",
            LogFilePath: logFilePath);
    }

    private static IReadOnlyList<string> ResolveMsBuildProjectSequence(string solutionDirectory, IReadOnlyList<string>? requestedProjectPaths)
    {
        IReadOnlyList<string> candidates = requestedProjectPaths is { Count: > 0 }
            ? requestedProjectPaths
            : ["OptcncTwinCAT\\Ruckig\\Ruckig.vcxproj", "OptcncTwinCAT\\Tinyxml2\\Tinyxml2.vcxproj", "OptcncTwinCAT\\MotionControl\\MotionControl.vcxproj"];

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(solutionDirectory, path)))
            .ToArray();
    }

    private static string ResolveMsBuildPath(string? requestedPath)
    {
        foreach (string candidate in EnumerateMsBuildPathCandidates(requestedPath))
        {
            string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException("MSBuild.exe was not found. Pass --msbuild-path or install Visual Studio Build Tools.");
    }

    private static IEnumerable<string> EnumerateMsBuildPathCandidates(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            yield return requestedPath;
        }

        string? vsInstallDir = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (!string.IsNullOrWhiteSpace(vsInstallDir))
        {
            yield return Path.Combine(vsInstallDir, "MSBuild", "Current", "Bin", "MSBuild.exe");
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            string vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            string? installationPath = TryReadLatestVisualStudioInstallationPath(vswherePath);
            if (!string.IsNullOrWhiteSpace(installationPath))
            {
                yield return Path.Combine(installationPath, "MSBuild", "Current", "Bin", "MSBuild.exe");
            }
        }
    }

    private static string ResolveDevenvComPath(string? requestedPath)
    {
        foreach (string candidate in EnumerateDevenvComPathCandidates(requestedPath))
        {
            string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException("devenv.com was not found. Pass --devenv-path or install Visual Studio with TwinCAT XAE support.");
    }

    private static IEnumerable<string> EnumerateDevenvComPathCandidates(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            yield return requestedPath;
        }

        string? vsInstallDir = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (!string.IsNullOrWhiteSpace(vsInstallDir))
        {
            yield return Path.Combine(vsInstallDir, "Common7", "IDE", "devenv.com");
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            string vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            string? installationPath = TryReadLatestVisualStudioInstallationPath(vswherePath);
            if (!string.IsNullOrWhiteSpace(installationPath))
            {
                yield return Path.Combine(installationPath, "Common7", "IDE", "devenv.com");
            }
        }

        Type? dteType = Type.GetTypeFromProgID("VisualStudio.DTE.17.0", throwOnError: false);
        if (dteType is not null)
        {
            string? devenvExe = ResolveDevenvPath(dteType);
            if (!string.IsNullOrWhiteSpace(devenvExe))
            {
                yield return Path.ChangeExtension(devenvExe, ".com");
            }
        }
    }

    private static string? TryReadLatestVisualStudioInstallationPath(string vswherePath, int? majorVersion = null)
    {
        if (!File.Exists(vswherePath))
        {
            return null;
        }

        try
        {
            DiagnosticsProcessStartInfo startInfo = new()
            {
                FileName = vswherePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-latest");
            if (majorVersion == 17)
            {
                startInfo.ArgumentList.Add("-version");
                startInfo.ArgumentList.Add("[17.0,18.0)");
            }
            else if (majorVersion == 16)
            {
                startInfo.ArgumentList.Add("-version");
                startInfo.ArgumentList.Add("[16.0,17.0)");
            }

            startInfo.ArgumentList.Add("-property");
            startInfo.ArgumentList.Add("installationPath");

            using DiagnosticsProcess process = new() { StartInfo = startInfo };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildCommandLine(string executablePath, IEnumerable<string> arguments)
    {
        return string.Join(
            " ",
            new[] { QuoteCommandArgument(executablePath) }.Concat(arguments.Select(QuoteCommandArgument)));
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private static string QuoteCommandArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"', StringComparison.Ordinal)
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;
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
        if (request.SuppressUi)
        {
            SuppressVisualStudioUi(session);
        }

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
        int timeoutMs = request.ActivationTimeoutMs > 0 ? request.ActivationTimeoutMs : 120000;
        activationCommand = RunOnStaThreadWithTimeout(
            () =>
            {
                string? commandUsed = null;
                try
                {
                    const string sysManagerActivation = "ITcSysManager.ActivateConfiguration";
                    attempted.Add(sysManagerActivation);
                    RetryComCall(() => sysManager.ActivateConfiguration());
                    commandUsed = sysManagerActivation;
                }
                catch
                {
                    if (!request.AllowDteCommandFallback)
                    {
                        throw;
                    }

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
                            commandUsed = command;
                            break;
                        }
                        catch
                        {
                        }
                    }

                    if (string.IsNullOrWhiteSpace(commandUsed))
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

                return commandUsed!;
            },
            timeoutMs,
            "TwinCAT activation");

        if (request.SaveConfigurationArchive &&
            !string.IsNullOrWhiteSpace(archivePath) &&
            !File.Exists(archivePath))
        {
            WriteFallbackActivationEvidenceArchive(archivePath, solutionPath, activationCommand, attempted, archiveSaveError);
        }

        return new ActivationResult(true, archivePath, activationCommand, attempted);
    }

    private static T RunOnStaThreadWithTimeout<T>(Func<T> action, int timeoutMs, string operationName)
    {
        T? result = default;
        Exception? exception = null;
        ThreadingThread thread = new(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(timeoutMs))
        {
            throw new TimeoutException($"{operationName} did not finish within {timeoutMs} ms.");
        }

        if (exception is not null)
        {
            throw exception;
        }

        return result!;
    }

    private static void SuppressVisualStudioUi(TwinCatEngineeringSession session)
    {
        try
        {
            RetryComCall(() => session.Dte.SuppressUI = true, 3, 200);
        }
        catch
        {
            // SuppressUI is best-effort; the caller should still avoid UI command fallbacks when unattended.
        }
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
        SuppressVisualStudioUi(session);

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

    private static string ResolveSolutionDirectory(TwinCatEngineeringSession session)
    {
        string? solutionPath = RetryComCall(() => session.Dte.Solution.FullName, 10, 300);
        if (!string.IsNullOrWhiteSpace(solutionPath) && File.Exists(solutionPath))
        {
            return Path.GetDirectoryName(solutionPath)
                ?? throw new InvalidOperationException($"Unable to resolve solution directory from '{solutionPath}'.");
        }

        return session.CurrentSolutionDirectory
            ?? throw new InvalidOperationException("Current solution directory is not available.");
    }

    private static string ResolveRequestedProjectDirectory(string solutionDirectory, string projectName, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidOperationException("ProjectName must not be empty.");
        }

        string rawDirectory = string.IsNullOrWhiteSpace(projectDirectory)
            ? Path.Combine(solutionDirectory, projectName)
            : projectDirectory!;
        return Path.GetFullPath(rawDirectory);
    }

    private static string? ResolveVisualStudioCppTemplatePath(
        TwinCatEngineeringSession session,
        CreateVisualStudioCppProjectRequest request)
    {
        foreach (string candidate in request.CandidateTemplatePaths ?? Array.Empty<string>())
        {
            string fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        if (!request.TemplateKind.Equals("ConsoleApplication", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] templateNames =
        {
            "ConsoleApplication.zip",
            "Console App.zip",
            "Windows Console Application.zip",
            "EmptyProject.zip"
        };

        foreach (string templateName in templateNames)
        {
            try
            {
                dynamic solution = session.Dte.Solution;
                string resolved = RetryComCall(() => (string)solution.GetProjectTemplate(templateName, "VC"), 3, 200);
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                {
                    return resolved;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static void CreateFallbackVisualStudioCppProjectFiles(string projectFilePath, string projectName, string? platformToolset)
    {
        string projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Unable to resolve project directory for '{projectFilePath}'.");
        Directory.CreateDirectory(projectDirectory);

        string projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        string toolset = string.IsNullOrWhiteSpace(platformToolset) ? "v143" : platformToolset!;
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        XDocument project = new(
            new XElement(
                ns + "Project",
                new XAttribute("DefaultTargets", "Build"),
                new XAttribute("ToolsVersion", "17.0"),
                new XElement(
                    ns + "ItemGroup",
                    new XAttribute("Label", "ProjectConfigurations"),
                    CreateProjectConfiguration(ns, "Debug", "x64"),
                    CreateProjectConfiguration(ns, "Release", "x64")),
                new XElement(
                    ns + "PropertyGroup",
                    new XAttribute("Label", "Globals"),
                    new XElement(ns + "VCProjectVersion", "17.0"),
                    new XElement(ns + "Keyword", "Win32Proj"),
                    new XElement(ns + "ProjectGuid", projectGuid),
                    new XElement(ns + "RootNamespace", MakeSafeIdentifier(projectName))),
                new XElement(ns + "Import", new XAttribute("Project", "$(VCTargetsPath)\\Microsoft.Cpp.Default.props")),
                CreateConfigurationGroup(ns, "Debug", "x64", "Application", toolset, useDebugLibraries: true),
                CreateConfigurationGroup(ns, "Release", "x64", "Application", toolset, useDebugLibraries: false),
                new XElement(ns + "Import", new XAttribute("Project", "$(VCTargetsPath)\\Microsoft.Cpp.props")),
                new XElement(ns + "ItemDefinitionGroup", new XAttribute("Condition", "'$(Configuration)|$(Platform)'=='Debug|x64'")),
                new XElement(ns + "ItemDefinitionGroup", new XAttribute("Condition", "'$(Configuration)|$(Platform)'=='Release|x64'")),
                new XElement(ns + "Import", new XAttribute("Project", "$(VCTargetsPath)\\Microsoft.Cpp.targets"))));

        project.Save(projectFilePath);

        XDocument filters = new(
            new XElement(
                ns + "Project",
                new XAttribute("ToolsVersion", "4.0"),
                new XElement(
                    ns + "ItemGroup",
                    CreateFilter(ns, "Source Files", "cpp;c;cxx;def;odl;idl;hpj;bat;asm;asmx"),
                    CreateFilter(ns, "Header Files", "h;hh;hpp;hxx;hm;inl;inc;xsd"),
                    CreateFilter(ns, "Resource Files", "rc;ico;cur;bmp;dlg;rc2;rct;bin;rgs;gif;jpg;jpeg;jpe;resx;tiff;tif;png;wav"))));
        filters.Save(projectFilePath + ".filters");
    }

    private static XElement CreateProjectConfiguration(XNamespace ns, string configuration, string platform) =>
        new(
            ns + "ProjectConfiguration",
            new XAttribute("Include", $"{configuration}|{platform}"),
            new XElement(ns + "Configuration", configuration),
            new XElement(ns + "Platform", platform));

    private static XElement CreateConfigurationGroup(
        XNamespace ns,
        string configuration,
        string platform,
        string configurationType,
        string platformToolset,
        bool useDebugLibraries) =>
        new(
            ns + "PropertyGroup",
            new XAttribute("Condition", $"'$(Configuration)|$(Platform)'=='{configuration}|{platform}'"),
            new XAttribute("Label", "Configuration"),
            new XElement(ns + "ConfigurationType", configurationType),
            new XElement(ns + "UseDebugLibraries", useDebugLibraries ? "true" : "false"),
            new XElement(ns + "PlatformToolset", platformToolset),
            new XElement(ns + "CharacterSet", "Unicode"));

    private static XElement CreateFilter(XNamespace ns, string name, string extensions) =>
        new(
            ns + "Filter",
            new XAttribute("Include", name),
            new XElement(ns + "UniqueIdentifier", Guid.NewGuid().ToString("B").ToUpperInvariant()),
            new XElement(ns + "Extensions", extensions));

    private static void EnsureScopeProjectFile(string projectFilePath, string projectName, string? configurationFileName)
    {
        if (!File.Exists(projectFilePath))
        {
            WriteScopeProjectFile(projectFilePath, projectName, configurationFileName);
            return;
        }

        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidOperationException($"Scope project '{projectFilePath}' is empty.");
        XNamespace ns = root.Name.Namespace;
        bool changed = false;

        XElement propertyGroup = root.Elements(ns + "PropertyGroup")
            .FirstOrDefault(group => group.Elements(ns + "ProjectGuid").Any())
            ?? root.Elements(ns + "PropertyGroup")
                .FirstOrDefault(group => string.Equals(group.Attribute("Label")?.Value, "Globals", StringComparison.OrdinalIgnoreCase))
            ?? root.Elements(ns + "PropertyGroup").FirstOrDefault()
            ?? new XElement(ns + "PropertyGroup", new XAttribute("Label", "Globals"));
        if (propertyGroup.Parent is null)
        {
            root.AddFirst(propertyGroup);
            changed = true;
        }

        changed |= EnsureMsBuildChildValue(propertyGroup, ns, "ProjectGuid", Guid.NewGuid().ToString("B").ToUpperInvariant());
        changed |= EnsureMsBuildChildValue(propertyGroup, ns, "AssemblyName", projectName);
        changed |= EnsureMsBuildChildValue(propertyGroup, ns, "Name", projectName);
        changed |= EnsureMsBuildChildValue(propertyGroup, ns, "RootNamespace", MakeSafeIdentifier(projectName));

        if (!string.IsNullOrWhiteSpace(configurationFileName) &&
            !root.Descendants(ns + "Content").Any(content =>
                string.Equals(content.Attribute("Include")?.Value, configurationFileName, StringComparison.OrdinalIgnoreCase)))
        {
            root.Add(
                new XElement(
                    ns + "ItemGroup",
                    new XElement(
                        ns + "Content",
                        new XAttribute("Include", configurationFileName!),
                        new XElement(ns + "SubType", "Content"))));
            changed = true;
        }

        if (changed)
        {
            document.Save(projectFilePath, SaveOptions.DisableFormatting);
        }
    }

    private static bool EnsureMsBuildChildValue(XElement parent, XNamespace ns, string childName, string value)
    {
        XElement? child = parent.Elements(ns + childName).FirstOrDefault();
        if (child is null)
        {
            parent.Add(new XElement(ns + childName, value));
            return true;
        }

        if (string.IsNullOrWhiteSpace(child.Value))
        {
            child.Value = value;
            return true;
        }

        return false;
    }

    private static void WriteScopeProjectFile(string projectFilePath, string projectName, string? configurationFileName)
    {
        string projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Unable to resolve project directory for '{projectFilePath}'.");
        Directory.CreateDirectory(projectDirectory);

        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        string projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        XElement root = new(
            ns + "Project",
            new XAttribute("ToolsVersion", "4.0"),
            new XAttribute("DefaultTargets", "Build"),
            new XElement(
                ns + "PropertyGroup",
                new XAttribute("Label", "Globals"),
                new XElement(ns + "ProjectGuid", projectGuid),
                new XElement(ns + "AssemblyName", projectName),
                new XElement(ns + "Name", projectName),
                new XElement(ns + "RootNamespace", MakeSafeIdentifier(projectName))));

        if (!string.IsNullOrWhiteSpace(configurationFileName))
        {
            root.Add(
                new XElement(
                    ns + "ItemGroup",
                    new XElement(
                        ns + "Content",
                        new XAttribute("Include", configurationFileName!),
                        new XElement(ns + "SubType", "Content"))));
        }

        new XDocument(root).Save(projectFilePath);
    }

    private static void WriteEmptyScopeConfiguration(string configurationFilePath, string projectName)
    {
        string directory = Path.GetDirectoryName(configurationFilePath)
            ?? throw new InvalidOperationException($"Unable to resolve Scope configuration directory for '{configurationFilePath}'.");
        Directory.CreateDirectory(directory);

        XDocument document = new(
            new XElement(
                "ScopeProject",
                new XAttribute("AssemblyName", "TwinCAT.Measurement.Scope.API.Model"),
                new XElement("AutoRestartRecord", "false"),
                new XElement("AutoSaveMode", "None"),
                new XElement("DisplayColor", "Black"),
                new XElement("Guid", Guid.NewGuid().ToString("D")),
                new XElement("MainServer", "127.0.0.1.1.1"),
                new XElement("Name", projectName),
                new XElement("RecordTime", "6000000000"),
                new XElement("SortPriority", "100"),
                new XElement("StopMode", "AutoStop"),
                new XElement("SubMember")));
        document.Save(configurationFilePath);
    }

    private static string ReadOrCreateProjectGuid(string projectFilePath)
    {
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        XElement? existingGuid = document.Root?
            .Descendants(ns + "ProjectGuid")
            .FirstOrDefault(element => !string.IsNullOrWhiteSpace(element.Value));
        if (existingGuid is not null)
        {
            return NormalizeGuidText(existingGuid.Value);
        }

        XElement globals = document.Root?.Elements(ns + "PropertyGroup")
            .FirstOrDefault(group => string.Equals(group.Attribute("Label")?.Value, "Globals", StringComparison.OrdinalIgnoreCase))
            ?? document.Root?.Elements(ns + "PropertyGroup").FirstOrDefault()
            ?? new XElement(ns + "PropertyGroup", new XAttribute("Label", "Globals"));
        if (globals.Parent is null)
        {
            document.Root?.AddFirst(globals);
        }

        XElement? guidElement = globals.Elements(ns + "ProjectGuid").FirstOrDefault();
        if (guidElement is null || string.IsNullOrWhiteSpace(guidElement.Value))
        {
            guidElement = new XElement(ns + "ProjectGuid", Guid.NewGuid().ToString("B").ToUpperInvariant());
            globals.Add(guidElement);
            document.Save(projectFilePath, SaveOptions.DisableFormatting);
        }

        return NormalizeGuidText(guidElement.Value);
    }

    private static Project? FindProjectByName(DTE dte, string projectName)
    {
        Projects projects = RetryComCall(() => dte.Solution.Projects);
        for (int index = 1; index <= projects.Count; index++)
        {
            Project? project = FindProjectByNameRecursive(projects.Item(index), projectName);
            if (project is not null)
            {
                return project;
            }
        }

        return null;
    }

    private static Project? FindProjectByNameRecursive(Project? project, string projectName)
    {
        if (project is null)
        {
            return null;
        }

        try
        {
            if (string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase))
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
                Project? match = FindProjectByNameRecursive(item.SubProject, projectName);
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

    private static SlnProjectEntry FindSlnProjectEntry(string solutionPath, string projectName)
    {
        string text = File.ReadAllText(solutionPath);
        Regex regex = new(
            @"Project\(""\{(?<type>[^""]+)\}""\)\s*=\s*""(?<name>[^""]+)""\s*,\s*""(?<path>[^""]+)""\s*,\s*""(?<guid>\{[^""]+\})""",
            RegexOptions.IgnoreCase);
        foreach (Match match in regex.Matches(text))
        {
            if (string.Equals(match.Groups["name"].Value, projectName, StringComparison.OrdinalIgnoreCase))
            {
                return new SlnProjectEntry(
                    match.Groups["name"].Value,
                    NormalizeGuidText(match.Groups["guid"].Value));
            }
        }

        throw new InvalidOperationException($"Project '{projectName}' was not found in solution '{solutionPath}'.");
    }

    private static void UpsertSlnProjectDependency(string solutionPath, string projectGuid, string dependsOnProjectGuid)
    {
        string text = File.ReadAllText(solutionPath);
        string normalizedProjectGuid = NormalizeGuidText(projectGuid);
        string normalizedDependencyGuid = NormalizeGuidText(dependsOnProjectGuid);
        string projectHeaderPattern = @"Project\(""\{[^""]+\}""\)\s*=\s*""[^""]+""\s*,\s*""[^""]+""\s*,\s*""" + Regex.Escape(normalizedProjectGuid) + @"""";
        Match header = Regex.Match(text, projectHeaderPattern, RegexOptions.IgnoreCase);
        if (!header.Success)
        {
            throw new InvalidOperationException($"Project GUID '{normalizedProjectGuid}' was not found in '{solutionPath}'.");
        }

        int projectEnd = text.IndexOf("EndProject", header.Index, StringComparison.Ordinal);
        if (projectEnd < 0)
        {
            throw new InvalidOperationException($"Project section for '{normalizedProjectGuid}' is malformed.");
        }

        string block = text[header.Index..projectEnd];
        if (block.Contains(normalizedDependencyGuid, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string dependencyLine = "\t\t" + normalizedDependencyGuid + " = " + normalizedDependencyGuid + Environment.NewLine;
        string sectionStart = "\tProjectSection(ProjectDependencies) = postProject";
        string sectionEnd = "\tEndProjectSection";
        string replacement;
        if (block.Contains("ProjectSection(ProjectDependencies)", StringComparison.OrdinalIgnoreCase))
        {
            int sectionEndIndex = block.IndexOf(sectionEnd, StringComparison.Ordinal);
            replacement = block.Insert(sectionEndIndex, dependencyLine);
        }
        else
        {
            replacement = block + sectionStart + Environment.NewLine + dependencyLine + sectionEnd + Environment.NewLine;
        }

        text = text.Remove(header.Index, block.Length).Insert(header.Index, replacement);
        File.WriteAllText(solutionPath, text, Encoding.UTF8);
    }

    private static void UpsertSlnProjectEntry(
        string solutionPath,
        string projectTypeGuid,
        string projectName,
        string projectFilePath,
        string projectGuid)
    {
        string text = File.ReadAllText(solutionPath);
        string normalizedTypeGuid = NormalizeGuidText(projectTypeGuid);
        string normalizedProjectGuid = NormalizeGuidText(projectGuid);
        if (text.Contains(normalizedProjectGuid, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string solutionDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException($"Unable to resolve solution directory for '{solutionPath}'.");
        string relativePath = Path.GetRelativePath(solutionDirectory, projectFilePath).Replace('/', '\\');
        string safeProjectName = projectName.Replace("\"", string.Empty, StringComparison.Ordinal);
        string safeRelativePath = relativePath.Replace("\"", string.Empty, StringComparison.Ordinal);
        string block =
            $"Project(\"{normalizedTypeGuid}\") = \"{safeProjectName}\", \"{safeRelativePath}\", \"{normalizedProjectGuid}\"{Environment.NewLine}" +
            $"EndProject{Environment.NewLine}";

        Regex projectRegex = new(
            @"Project\(""\{(?<type>[^""]+)\}""\)\s*=\s*""(?<name>[^""]+)""\s*,\s*""(?<path>[^""]+)""\s*,\s*""(?<guid>\{[^""]+\})""(?<body>.*?)EndProject\r?\n?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Match? existing = projectRegex.Matches(text)
            .Cast<Match>()
            .FirstOrDefault(match =>
                string.Equals(match.Groups["name"].Value, safeProjectName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(match.Groups["path"].Value.Replace('/', '\\'), safeRelativePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            string updatedExisting = text.Remove(existing.Index, existing.Length).Insert(existing.Index, block);
            File.WriteAllText(solutionPath, updatedExisting, Encoding.UTF8);
            return;
        }

        int globalIndex = text.IndexOf("Global", StringComparison.OrdinalIgnoreCase);
        string updated = globalIndex >= 0
            ? text.Insert(globalIndex, block)
            : text.TrimEnd() + Environment.NewLine + block;
        File.WriteAllText(solutionPath, updated, Encoding.UTF8);
    }

    private CppProjectPaths ResolveCppProjectPaths(TwinCatEngineeringSession session, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidOperationException("ProjectName must not be empty.");
        }

        Project? dteProject = FindProjectByName(session.Dte, projectName);
        string? fullName = dteProject is null ? null : SafeGetProjectFullName(dteProject);
        string projectFilePath = ResolveCppProjectFilePath(session, projectName, fullName);
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException($"C++ .vcxproj file was not found for project '{projectName}'.", projectFilePath);
        }

        string projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Unable to resolve project directory from '{projectFilePath}'.");
        return new CppProjectPaths(projectName, projectDirectory, projectFilePath, projectFilePath + ".filters");
    }

    private static CppProjectPaths ResolveCppProjectPaths(string twinCatProjectDirectory, string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidOperationException("ProjectName must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(twinCatProjectDirectory))
        {
            throw new InvalidOperationException("TwinCAT project directory must not be empty.");
        }

        string projectFilePath = ResolveCppProjectFilePath(twinCatProjectDirectory, projectName);
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException($"C++ .vcxproj file was not found for project '{projectName}'.", projectFilePath);
        }

        string projectDirectory = Path.GetDirectoryName(projectFilePath)
            ?? throw new InvalidOperationException($"Unable to resolve project directory from '{projectFilePath}'.");
        return new CppProjectPaths(projectName, projectDirectory, projectFilePath, projectFilePath + ".filters");
    }

    private static string ResolveCppProjectFilePath(TwinCatEngineeringSession session, string projectName, string? dteProjectFullName)
    {
        if (!string.IsNullOrWhiteSpace(dteProjectFullName) &&
            dteProjectFullName.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(dteProjectFullName))
        {
            return Path.GetFullPath(dteProjectFullName);
        }

        List<string> candidates = [];
        void AddCandidate(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            candidates.Add(Path.Combine(directory!, projectName, projectName + ".vcxproj"));
        }

        AddCandidate(session.CurrentSolutionDirectory);
        try
        {
            string solutionDirectory = ResolveSolutionDirectory(session);
            AddCandidate(solutionDirectory);
            if (Directory.Exists(solutionDirectory))
            {
                candidates.AddRange(Directory.GetFiles(solutionDirectory, projectName + ".vcxproj", SearchOption.AllDirectories));
            }
        }
        catch
        {
        }

        string[] existing = candidates
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (existing.Length == 1)
        {
            return existing[0];
        }

        string[] exactParentMatches = existing
            .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), projectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactParentMatches.Length == 1)
        {
            return exactParentMatches[0];
        }

        if (existing.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple C++ .vcxproj files named '{projectName}.vcxproj' were found. " +
                "Use a unique project name or expose the project through the DTE solution model. " +
                string.Join("; ", existing));
        }

        return Path.GetFullPath(Path.Combine(ResolveSolutionDirectory(session), projectName, projectName + ".vcxproj"));
    }

    private static string ResolveCppProjectFilePath(string twinCatProjectDirectory, string projectName)
    {
        string root = Path.GetFullPath(twinCatProjectDirectory);
        List<string> candidates =
        [
            Path.Combine(root, projectName, projectName + ".vcxproj")
        ];

        if (Directory.Exists(root))
        {
            candidates.AddRange(Directory.GetFiles(root, projectName + ".vcxproj", SearchOption.AllDirectories));
        }

        string[] existing = candidates
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (existing.Length == 1)
        {
            return existing[0];
        }

        string[] exactParentMatches = existing
            .Where(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), projectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactParentMatches.Length == 1)
        {
            return exactParentMatches[0];
        }

        if (existing.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple C++ .vcxproj files named '{projectName}.vcxproj' were found under '{root}'. " +
                string.Join("; ", existing));
        }

        return Path.GetFullPath(Path.Combine(root, projectName, projectName + ".vcxproj"));
    }

    private static string NormalizeProjectRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("RelativePath must not be empty.");
        }

        string normalized = relativePath.Replace('/', '\\').Trim();
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException($"RelativePath must not be rooted. Actual='{relativePath}'.");
        }

        string[] segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == "." || segment == ".."))
        {
            throw new InvalidOperationException($"RelativePath must not contain '.' or '..'. Actual='{relativePath}'.");
        }

        return string.Join('\\', segments);
    }

    private static string ResolveProjectContainedPath(string projectDirectory, string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(projectDirectory, relativePath));
        string root = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Resolved path escapes the project directory: {fullPath}");
        }

        return fullPath;
    }

    private static CppProjectItemType ResolveCppProjectItemType(string relativePath, CppProjectItemType requested)
    {
        if (requested != CppProjectItemType.Infer)
        {
            return requested;
        }

        string extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return extension switch
        {
            ".cpp" or ".c" or ".cxx" => CppProjectItemType.ClCompile,
            ".h" or ".hpp" or ".hh" or ".hxx" => CppProjectItemType.ClInclude,
            ".rc" => CppProjectItemType.ResourceCompile,
            _ => CppProjectItemType.None
        };
    }

    private static string GetMsBuildItemName(CppProjectItemType itemType) =>
        itemType == CppProjectItemType.Infer
            ? throw new InvalidOperationException("CppProjectItemType.Infer must be resolved before writing MSBuild XML.")
            : itemType.ToString();

    private static bool VcxprojHasItem(string projectFilePath, string relativePath, CppProjectItemType itemType)
    {
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        string itemName = GetMsBuildItemName(itemType);
        return document.Descendants(ns + itemName).Any(element => IncludeMatches(element, relativePath));
    }

    private static bool VcxprojHasAnyItem(string projectFilePath, string relativePath)
    {
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        return document.Descendants().Any(element =>
            IsCppProjectItemElement(element) &&
            IncludeMatches(element, relativePath));
    }

    private static bool IsCppProjectItemElement(XElement element) =>
        element.Name.LocalName is "ClCompile" or "ClInclude" or "ResourceCompile" or "None";

    private static bool IncludeMatches(XElement element, string relativePath) =>
        string.Equals(
            NormalizeMsBuildRelativePath(element.Attribute("Include")?.Value),
            NormalizeMsBuildRelativePath(relativePath),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMsBuildRelativePath(string? value) =>
        (value ?? string.Empty).Replace('/', '\\').Trim();

    private static void UpsertCppProjectItemRegistration(
        string projectFilePath,
        string relativePath,
        CppProjectItemType itemType,
        ProjectItemConflictPolicy conflictPolicy)
    {
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        string itemName = GetMsBuildItemName(itemType);

        if (conflictPolicy == ProjectItemConflictPolicy.ReplaceProjectRegistration)
        {
            foreach (XElement existing in document.Descendants(ns + itemName)
                         .Where(element => IncludeMatches(element, relativePath))
                         .ToList())
            {
                existing.Remove();
            }
        }
        else if (document.Descendants(ns + itemName).Any(element => IncludeMatches(element, relativePath)))
        {
            return;
        }

        XElement group = document.Root?.Elements(ns + "ItemGroup")
            .FirstOrDefault(itemGroup => itemGroup.Elements(ns + itemName).Any())
            ?? new XElement(ns + "ItemGroup");
        if (group.Parent is null)
        {
            document.Root?.Add(group);
        }

        group.Add(new XElement(ns + itemName, new XAttribute("Include", relativePath)));
        document.Save(projectFilePath, SaveOptions.DisableFormatting);
    }

    private static bool RemoveCppProjectItemRegistration(
        string projectFilePath,
        string relativePath,
        CppProjectItemType itemType)
    {
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        string itemName = GetMsBuildItemName(itemType);
        List<XElement> matches = document.Descendants(ns + itemName)
            .Where(element => IncludeMatches(element, relativePath))
            .ToList();
        foreach (XElement match in matches)
        {
            match.Remove();
        }

        if (matches.Count > 0)
        {
            document.Save(projectFilePath, SaveOptions.DisableFormatting);
        }

        return matches.Count > 0;
    }

    private static void UpsertCppProjectItemFilter(
        string filtersFilePath,
        string relativePath,
        CppProjectItemType itemType,
        string filter)
    {
        XDocument document = LoadOrCreateFiltersDocument(filtersFilePath);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        EnsureFilterExists(document, ns, filter);

        string itemName = GetMsBuildItemName(itemType);
        XElement item = document.Descendants(ns + itemName)
            .FirstOrDefault(element => IncludeMatches(element, relativePath))
            ?? new XElement(ns + itemName, new XAttribute("Include", relativePath));
        if (item.Parent is null)
        {
            XElement group = document.Root?.Elements(ns + "ItemGroup")
                .FirstOrDefault(itemGroup => itemGroup.Elements(ns + itemName).Any())
                ?? new XElement(ns + "ItemGroup");
            if (group.Parent is null)
            {
                document.Root?.Add(group);
            }

            group.Add(item);
        }

        SetOrCreateChildElementValue(item, "Filter", filter);
        document.Save(filtersFilePath, SaveOptions.DisableFormatting);
    }

    private static bool RemoveCppProjectItemFilter(
        string filtersFilePath,
        string relativePath,
        CppProjectItemType itemType)
    {
        XDocument document = XDocument.Load(filtersFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        string itemName = GetMsBuildItemName(itemType);
        List<XElement> matches = document.Descendants(ns + itemName)
            .Where(element => IncludeMatches(element, relativePath))
            .ToList();
        foreach (XElement match in matches)
        {
            match.Remove();
        }

        if (matches.Count > 0)
        {
            document.Save(filtersFilePath, SaveOptions.DisableFormatting);
        }

        return matches.Count > 0;
    }

    private static XDocument LoadOrCreateFiltersDocument(string filtersFilePath)
    {
        if (File.Exists(filtersFilePath))
        {
            return XDocument.Load(filtersFilePath, LoadOptions.PreserveWhitespace);
        }

        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        XDocument document = new(new XElement(ns + "Project", new XAttribute("ToolsVersion", "4.0")));
        Directory.CreateDirectory(Path.GetDirectoryName(filtersFilePath)!);
        document.Save(filtersFilePath);
        return document;
    }

    private static void EnsureFilterExists(XDocument document, XNamespace ns, string filter)
    {
        bool exists = document.Descendants(ns + "Filter")
            .Any(element => string.Equals(element.Attribute("Include")?.Value, filter, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return;
        }

        XElement group = document.Root?.Elements(ns + "ItemGroup")
            .FirstOrDefault(itemGroup => itemGroup.Elements(ns + "Filter").Any())
            ?? new XElement(ns + "ItemGroup");
        if (group.Parent is null)
        {
            document.Root?.AddFirst(group);
        }

        group.Add(
            new XElement(
                ns + "Filter",
                new XAttribute("Include", filter),
                new XElement(ns + "UniqueIdentifier", Guid.NewGuid().ToString("B").ToUpperInvariant())));
    }

    private static void WriteProjectItemContentToFile(WriteCppProjectItemContentRequest request, string filePath)
    {
        if (!File.Exists(filePath) && request.WritePolicy == ProjectItemWritePolicy.FailIfMissing)
        {
            throw new FileNotFoundException("Project item file was not found.", filePath);
        }

        if (File.Exists(filePath) &&
            request.WritePolicy == ProjectItemWritePolicy.FailIfNonEmpty &&
            new FileInfo(filePath).Length > 0)
        {
            throw new InvalidOperationException($"Project item file is not empty: {filePath}");
        }

        bool hasContentText = request.ContentText is not null;
        bool hasContentFile = !string.IsNullOrWhiteSpace(request.ContentFile);
        if (hasContentText == hasContentFile)
        {
            throw new InvalidOperationException("Pass exactly one of ContentText or ContentFile.");
        }

        string content = hasContentFile
            ? File.ReadAllText(Path.GetFullPath(request.ContentFile!), ResolveTextEncoding(request.Encoding, detectBom: true))
            : request.ContentText!;
        content = NormalizeNewLine(content, request.NewLine);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content, ResolveTextEncoding(request.Encoding, detectBom: false));
    }

    private static Encoding ResolveTextEncoding(string encodingName, bool detectBom)
    {
        string normalized = string.IsNullOrWhiteSpace(encodingName) ? "utf-8" : encodingName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "utf-8" or "utf8" => detectBom ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true) : new UTF8Encoding(false),
            "utf-8-bom" or "utf8-bom" => new UTF8Encoding(true),
            "ascii" => Encoding.ASCII,
            _ => throw new InvalidOperationException($"Unsupported encoding '{encodingName}'. Use utf-8, utf-8-bom, or ascii.")
        };
    }

    private static string NormalizeNewLine(string content, string newLine)
    {
        string normalized = string.IsNullOrWhiteSpace(newLine) ? "preserve" : newLine.Trim().ToLowerInvariant();
        return normalized switch
        {
            "preserve" => content,
            "crlf" => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal),
            "lf" => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal),
            _ => throw new InvalidOperationException($"Unsupported newline mode '{newLine}'. Use preserve, crlf, or lf.")
        };
    }

    private static void UpsertCppProjectProperty(
        string projectFilePath,
        string propertyName,
        string value,
        string? condition,
        string? propertyGroupLabel)
    {
        ValidateXmlName(propertyName, nameof(propertyName));
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        XElement group = document.Root?.Elements(ns + "PropertyGroup")
            .FirstOrDefault(candidate =>
                AttributeEquals(candidate, "Condition", condition) &&
                AttributeEquals(candidate, "Label", propertyGroupLabel))
            ?? new XElement(ns + "PropertyGroup");
        if (group.Parent is null)
        {
            if (!string.IsNullOrWhiteSpace(condition))
            {
                group.SetAttributeValue("Condition", condition);
            }

            if (!string.IsNullOrWhiteSpace(propertyGroupLabel))
            {
                group.SetAttributeValue("Label", propertyGroupLabel);
            }

            document.Root?.Add(group);
        }

        SetOrCreateChildElementValue(group, propertyName, value);
        document.Save(projectFilePath, SaveOptions.DisableFormatting);
    }

    private static void UpsertCppItemDefinitionProperty(
        string projectFilePath,
        string toolName,
        string propertyName,
        string value,
        string? condition)
    {
        ValidateXmlName(toolName, nameof(toolName));
        ValidateXmlName(propertyName, nameof(propertyName));
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        XElement group = document.Root?.Elements(ns + "ItemDefinitionGroup")
            .FirstOrDefault(candidate => AttributeEquals(candidate, "Condition", condition))
            ?? new XElement(ns + "ItemDefinitionGroup");
        if (group.Parent is null)
        {
            if (!string.IsNullOrWhiteSpace(condition))
            {
                group.SetAttributeValue("Condition", condition);
            }

            InsertCppItemDefinitionGroup(document, group, ns);
        }
        else
        {
            EnsureCppItemDefinitionGroupBeforeProjectItems(document, group, ns);
        }

        XElement tool = group.Elements(ns + toolName).FirstOrDefault() ?? new XElement(ns + toolName);
        if (tool.Parent is null)
        {
            group.Add(tool);
        }

        SetOrCreateChildElementValue(tool, propertyName, value);
        document.Save(projectFilePath, SaveOptions.DisableFormatting);
    }

    private static void InsertCppItemDefinitionGroup(XDocument document, XElement group, XNamespace ns)
    {
        XElement? firstProjectItemsGroup = FindFirstCppProjectItemsGroup(document, ns);
        if (firstProjectItemsGroup is not null)
        {
            firstProjectItemsGroup.AddBeforeSelf(group);
            return;
        }

        XElement? targetsImport = FindCppTargetsImport(document, ns);
        if (targetsImport is not null)
        {
            targetsImport.AddBeforeSelf(group);
            return;
        }

        document.Root?.Add(group);
    }

    private static void EnsureCppItemDefinitionGroupBeforeProjectItems(XDocument document, XElement group, XNamespace ns)
    {
        XElement? firstProjectItemsGroup = FindFirstCppProjectItemsGroup(document, ns);
        if (firstProjectItemsGroup is not null && group.IsAfter(firstProjectItemsGroup))
        {
            group.Remove();
            firstProjectItemsGroup.AddBeforeSelf(group);
            return;
        }

        XElement? targetsImport = FindCppTargetsImport(document, ns);
        if (targetsImport is not null && group.IsAfter(targetsImport))
        {
            group.Remove();
            targetsImport.AddBeforeSelf(group);
        }
    }

    private static XElement? FindFirstCppProjectItemsGroup(XDocument document, XNamespace ns)
    {
        return document.Root?.Elements(ns + "ItemGroup")
            .FirstOrDefault(itemGroup => !itemGroup.Elements(ns + "ProjectConfiguration").Any());
    }

    private static XElement? FindCppTargetsImport(XDocument document, XNamespace ns)
    {
        return document.Root?.Elements(ns + "Import")
            .FirstOrDefault(element =>
            {
                string? project = (string?)element.Attribute("Project");
                return project?.Contains("Microsoft.Cpp.targets", StringComparison.OrdinalIgnoreCase) == true;
            });
    }

    private static void UpsertCppProjectItemMetadata(
        string projectFilePath,
        string relativePath,
        CppProjectItemType itemType,
        string metadataName,
        string value,
        string? condition)
    {
        ValidateXmlName(metadataName, nameof(metadataName));
        XDocument document = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        string itemName = GetMsBuildItemName(itemType);
        XElement item = document.Descendants(ns + itemName)
            .FirstOrDefault(element =>
                IncludeMatches(element, relativePath) &&
                AttributeEquals(element, "Condition", condition))
            ?? document.Descendants(ns + itemName).FirstOrDefault(element => IncludeMatches(element, relativePath))
            ?? throw new InvalidOperationException($"Project item '{relativePath}' with item type '{itemName}' was not found in '{projectFilePath}'.");

        if (!string.IsNullOrWhiteSpace(condition))
        {
            item.SetAttributeValue("Condition", condition);
        }

        SetOrCreateChildElementValue(item, metadataName, value);
        document.Save(projectFilePath, SaveOptions.DisableFormatting);
    }

    private static bool AttributeEquals(XElement element, string attributeName, string? expected)
    {
        string? actual = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(expected))
        {
            return string.IsNullOrWhiteSpace(actual);
        }

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateXmlName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{parameterName} must not be empty.");
        }

        try
        {
            _ = XName.Get(value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{parameterName} is not a valid XML element name: '{value}'.", ex);
        }
    }

    private static bool WaitForTmcUpdate(string tmcPath, DateTime previousWrite, string? previousHash, int timeoutMs)
    {
        int waited = 0;
        int effectiveTimeout = Math.Max(0, timeoutMs);
        while (waited <= effectiveTimeout)
        {
            if (File.Exists(tmcPath))
            {
                DateTime current = File.GetLastWriteTimeUtc(tmcPath);
                if (previousWrite == DateTime.MinValue || current > previousWrite)
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(previousHash) &&
                    !string.Equals(previousHash, ComputeFileSha256(tmcPath), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            ThreadingThread.Sleep(500);
            waited += 500;
        }

        return false;
    }

    private static bool TryReadTmc(string tmcPath, out XDocument? document)
    {
        document = null;
        if (!File.Exists(tmcPath))
        {
            return false;
        }

        try
        {
            document = XDocument.Load(tmcPath);
            return document.Root is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string NormalizeGuidText(string value)
    {
        if (!Guid.TryParse(value, out Guid guid))
        {
            throw new InvalidOperationException($"Invalid GUID text: '{value}'.");
        }

        return guid.ToString("B").ToUpperInvariant();
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

    private static (
        DTE Dte,
        bool AttachedToExisting,
        IReadOnlyCollection<int> TargetProcessIds,
        IReadOnlyDictionary<string, int> AutoDismissedDialogs) CreateOrAttachVisualStudioDte(
        Type dteType,
        string progId,
        int launchTimeoutMs,
        bool attachToExisting,
        bool enableDialogAutoDismiss,
        int dialogPollIntervalMs,
        string? rootSuffix,
        string? dteHostPath,
        bool preferDteHostLaunch)
    {
        int timeoutMs = launchTimeoutMs > 0 ? launchTimeoutMs : 60000;
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        HashSet<int> existingDteHostProcessIds = CaptureDteHostProcessIds();
        DateTime launchWindowStart = DateTime.Now;
        Exception? lastException = null;
        bool succeeded = false;
        DteFallbackLaunchInfo? fallbackLaunchInfo = null;
        TwinCatEngineeringSession.TwinCatDialogAutoDismissScope? launchDialogScope = enableDialogAutoDismiss
            ? TwinCatEngineeringSession.TwinCatDialogAutoDismissScope.Start(Array.Empty<int>(), dialogPollIntervalMs)
            : null;

        try
        {
            if (attachToExisting)
            {
                try
                {
                    if (TryGetActiveVisualStudioDte(progId) is DTE activeDte)
                    {
                        succeeded = true;
                        return (activeDte, true, Array.Empty<int>(), SnapshotAutoDismissedDialogs(launchDialogScope));
                    }
                }
                catch (COMException ex)
                {
                    lastException = ex;
                }
            }

            if (preferDteHostLaunch && RemainingLaunchMilliseconds(deadline) > 0)
            {
                fallbackLaunchInfo = TryStartDteHostForDte(progId, dteType, rootSuffix, dteHostPath);
                if (fallbackLaunchInfo is not null)
                {
                    while (RemainingLaunchMilliseconds(deadline) > 0)
                    {
                        launchDialogScope?.AddTargetProcessIds(DetectNewDteHostProcessIds(existingDteHostProcessIds));
                        try
                        {
                            if (TryGetActiveVisualStudioDte(progId, existingDteHostProcessIds) is DTE activeDteAfterExplicitStart)
                            {
                                succeeded = true;
                                return (activeDteAfterExplicitStart, false, DetectNewDteHostProcessIds(existingDteHostProcessIds), SnapshotAutoDismissedDialogs(launchDialogScope));
                            }
                        }
                        catch (COMException ex)
                        {
                            lastException = ex;
                        }

                        if (fallbackLaunchInfo is not null && HasFallbackHostExitedWithActivityLogErrors(fallbackLaunchInfo))
                        {
                            break;
                        }

                        ThreadingThread.Sleep(Math.Min(1000, Math.Max(0, RemainingLaunchMilliseconds(deadline))));
                    }
                }
            }

            if (!preferDteHostLaunch && RemainingLaunchMilliseconds(deadline) > 0)
            {
                try
                {
                    launchDialogScope?.AddTargetProcessIds(DetectNewDteHostProcessIds(existingDteHostProcessIds));
                    int activationProbeMs = Math.Min(
                        RemainingLaunchMilliseconds(deadline),
                        Math.Clamp(timeoutMs / 2, 10000, 25000));
                    DTE created = CreateDteWithTimeout(
                        dteType,
                        activationProbeMs,
                        launchDialogScope,
                        existingDteHostProcessIds);
                    launchDialogScope?.AddTargetProcessIds(DetectNewDteHostProcessIds(existingDteHostProcessIds));
                    if (attachToExisting)
                    {
                        succeeded = true;
                        return (created, false, DetectNewDteHostProcessIds(existingDteHostProcessIds), SnapshotAutoDismissedDialogs(launchDialogScope));
                    }

                    if (TryGetDteProcessId(created) is int createdProcessId &&
                        !existingDteHostProcessIds.Contains(createdProcessId))
                    {
                        succeeded = true;
                        return (created, false, DetectNewDteHostProcessIds(existingDteHostProcessIds), SnapshotAutoDismissedDialogs(launchDialogScope));
                    }

                    int rotProbeMs = Math.Min(RemainingLaunchMilliseconds(deadline), 5000);
                    DateTime rotProbeDeadline = DateTime.UtcNow.AddMilliseconds(rotProbeMs);
                    while (RemainingLaunchMilliseconds(rotProbeDeadline) > 0)
                    {
                        if (TryGetActiveVisualStudioDte(progId, existingDteHostProcessIds) is DTE createdFromRot)
                        {
                            succeeded = true;
                            return (createdFromRot, false, DetectNewDteHostProcessIds(existingDteHostProcessIds), SnapshotAutoDismissedDialogs(launchDialogScope));
                        }

                        ThreadingThread.Sleep(500);
                    }

                    lastException = new InvalidOperationException(
                        "DTE COM activation returned a pre-existing Visual Studio host while AttachToExisting=false.");
                }
                catch (Exception ex) when (IsRetryableLaunchFailure(ex))
                {
                    lastException = ex;
                }
            }

            fallbackLaunchInfo ??= TryStartDteHostForDte(progId, dteType, rootSuffix, dteHostPath);
            while (RemainingLaunchMilliseconds(deadline) > 0)
            {
                launchDialogScope?.AddTargetProcessIds(DetectNewDteHostProcessIds(existingDteHostProcessIds));
                try
                {
                    if (TryGetActiveVisualStudioDte(progId, existingDteHostProcessIds) is DTE activeDteAfterStart)
                    {
                        succeeded = true;
                        return (activeDteAfterStart, false, DetectNewDteHostProcessIds(existingDteHostProcessIds), SnapshotAutoDismissedDialogs(launchDialogScope));
                    }
                }
                catch (COMException ex)
                {
                    lastException = ex;
                }

                if (fallbackLaunchInfo is not null && HasFallbackHostExitedWithActivityLogErrors(fallbackLaunchInfo))
                {
                    break;
                }

                ThreadingThread.Sleep(Math.Min(1000, Math.Max(0, RemainingLaunchMilliseconds(deadline))));
            }

            if (attachToExisting)
            {
                try
                {
                    if (TryGetActiveVisualStudioDte(progId) is DTE activeDte)
                    {
                        succeeded = true;
                        return (activeDte, true, Array.Empty<int>(), SnapshotAutoDismissedDialogs(launchDialogScope));
                    }
                }
                catch (COMException ex)
                {
                    lastException = ex;
                }
            }

            throw new InvalidOperationException(
                BuildDteLaunchFailureMessage(
                    progId,
                    lastException,
                    existingDteHostProcessIds,
                    launchWindowStart,
                    SnapshotAutoDismissedDialogs(launchDialogScope),
                    fallbackLaunchInfo),
                lastException);
        }
        finally
        {
            launchDialogScope?.Dispose();
            if (!succeeded)
            {
                CleanupFailedLaunchDteHostProcesses(existingDteHostProcessIds, launchWindowStart);
            }
        }
    }

    private static bool IsRetryableLaunchFailure(COMException ex) =>
        ex.HResult == ComServerExecFailure ||
        ex.HResult == RpcServerUnavailable ||
        ex.HResult == RpcCallRejected ||
        ex.HResult == RpcRetryLater ||
        ex.HResult == RpcCallFailed;

    private static bool IsRetryableLaunchFailure(Exception ex) =>
        ex is TimeoutException ||
        ex is COMException comException && IsRetryableLaunchFailure(comException);

    private static IReadOnlyDictionary<string, int> SnapshotAutoDismissedDialogs(
        TwinCatEngineeringSession.TwinCatDialogAutoDismissScope? scope) =>
        scope?.Snapshot() ?? EmptyAutoDismissedDialogs;

    private static IReadOnlyDictionary<string, int> MergeAutoDismissedDialogs(
        IReadOnlyDictionary<string, int>? first,
        IReadOnlyDictionary<string, int>? second)
    {
        if ((first is null || first.Count == 0) && (second is null || second.Count == 0))
        {
            return EmptyAutoDismissedDialogs;
        }

        Dictionary<string, int> merged = new(StringComparer.OrdinalIgnoreCase);
        AddAutoDismissedDialogs(merged, first);
        AddAutoDismissedDialogs(merged, second);
        return merged;
    }

    private static void AddAutoDismissedDialogs(
        Dictionary<string, int> target,
        IReadOnlyDictionary<string, int>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach ((string key, int count) in source)
        {
            if (string.IsNullOrWhiteSpace(key) || count <= 0)
            {
                continue;
            }

            target[key] = target.TryGetValue(key, out int existing) ? existing + count : count;
        }
    }

    private static string BuildDteLaunchFailureMessage(
        string progId,
        Exception? lastException,
        HashSet<int> existingProcessIds,
        DateTime launchWindowStart,
        IReadOnlyDictionary<string, int>? autoDismissedDialogs,
        DteFallbackLaunchInfo? fallbackLaunchInfo)
    {
        StringBuilder builder = new($"Unable to launch or attach to Visual Studio DTE '{progId}'.");
        if (lastException is not null)
        {
            builder.Append(" Last error: ")
                .Append(lastException.GetType().Name)
                .Append(": ")
                .Append(lastException.Message);
        }

        string processSnapshot = DescribeDteHostProcesses(existingProcessIds, launchWindowStart);
        if (!string.IsNullOrWhiteSpace(processSnapshot))
        {
            builder.Append(" DTE host snapshot: ").Append(processSnapshot);
        }

        if (autoDismissedDialogs is { Count: > 0 })
        {
            builder.Append(" Auto-dismissed launch dialogs: ")
                .Append(string.Join("; ", autoDismissedDialogs.Select(item => $"{item.Key} x{item.Value}")));
        }

        if (fallbackLaunchInfo is not null)
        {
            builder.Append(" Fallback launch: host=")
                .Append(fallbackLaunchInfo.HostPath);
            if (fallbackLaunchInfo.ProcessId.HasValue)
            {
                builder.Append(" pid=").Append(fallbackLaunchInfo.ProcessId.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(fallbackLaunchInfo.Arguments))
            {
                builder.Append(" args=").Append(fallbackLaunchInfo.Arguments);
            }

            builder.Append(" startSucceeded=")
                .Append(fallbackLaunchInfo.StartSucceeded ? "true" : "false");
            if (!string.IsNullOrWhiteSpace(fallbackLaunchInfo.StartError))
            {
                builder.Append(" startError=").Append(fallbackLaunchInfo.StartError);
            }
        }

        AppendActivityLogSummary(builder, fallbackLaunchInfo?.ActivityLogPath);

        return builder.ToString();
    }

    private static DTE CreateDteWithTimeout(
        Type dteType,
        int launchTimeoutMs,
        TwinCatEngineeringSession.TwinCatDialogAutoDismissScope? launchDialogScope,
        HashSet<int> existingDteHostProcessIds)
    {
        int timeoutMs = launchTimeoutMs > 0 ? launchTimeoutMs : 60000;
        DTE? created = null;
        Exception? exception = null;
        ThreadingThread thread = new(() =>
        {
            try
            {
                created = (DTE)Activator.CreateInstance(dteType)!;
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (RemainingLaunchMilliseconds(deadline) > 0)
        {
            int waitMs = Math.Min(500, RemainingLaunchMilliseconds(deadline));
            if (thread.Join(waitMs))
            {
                break;
            }

            launchDialogScope?.AddTargetProcessIds(DetectNewDteHostProcessIds(existingDteHostProcessIds));
        }

        if (thread.IsAlive)
        {
            throw new TimeoutException($"Visual Studio DTE COM activation did not complete within {timeoutMs} ms.");
        }

        if (exception is not null)
        {
            throw exception;
        }

        return created ?? throw new InvalidOperationException("Visual Studio DTE COM activation returned null.");
    }

    private static int RemainingLaunchMilliseconds(DateTime deadline)
    {
        double remaining = (deadline - DateTime.UtcNow).TotalMilliseconds;
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
    }

    private static DteFallbackLaunchInfo? TryStartDteHostForDte(
        string progId,
        Type dteType,
        string? rootSuffix,
        string? requestedHostPath)
    {
        string? hostPath = ResolveDteHostPath(progId, dteType, requestedHostPath);
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
        {
            return null;
        }

        string? activityLogPath = BuildFallbackActivityLogPath(progId);
        try
        {
            string arguments = string.IsNullOrWhiteSpace(activityLogPath)
                ? "/Embedding /NoSplash"
                : $"/Embedding /NoSplash /Log \"{activityLogPath}\"";
            if (!string.IsNullOrWhiteSpace(rootSuffix))
            {
                arguments += $" /RootSuffix \"{rootSuffix.Trim()}\"";
            }

            DiagnosticsProcess? process = DiagnosticsProcess.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return new DteFallbackLaunchInfo(
                hostPath,
                arguments,
                activityLogPath,
                StartSucceeded: true,
                StartError: null,
                ProcessId: process?.Id);
        }
        catch (Exception ex)
        {
            // Explicit host launch is a fallback after COM activation failed; keep the original COM error.
            return new DteFallbackLaunchInfo(
                hostPath,
                string.Empty,
                activityLogPath,
                StartSucceeded: false,
                StartError: ex.Message,
                ProcessId: null);
        }
    }

    private static bool HasFallbackHostExitedWithActivityLogErrors(DteFallbackLaunchInfo fallbackLaunchInfo)
    {
        if (fallbackLaunchInfo.ProcessId is not int processId)
        {
            return false;
        }

        try
        {
            using DiagnosticsProcess process = DiagnosticsProcess.GetProcessById(processId);
            if (!process.HasExited)
            {
                return false;
            }
        }
        catch
        {
            // If the host process has already disappeared, use the ActivityLog as the authoritative failure detail.
        }

        return !string.IsNullOrWhiteSpace(fallbackLaunchInfo.ActivityLogPath)
            && File.Exists(fallbackLaunchInfo.ActivityLogPath)
            && ReadActivityLogErrorSummaries(fallbackLaunchInfo.ActivityLogPath, maxErrors: 1).Count > 0;
    }

    private static string? BuildFallbackActivityLogPath(string progId)
    {
        try
        {
            string safeProgId = Regex.Replace(progId, @"[^A-Za-z0-9_.-]+", "_");
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "dte-launch-logs");
            Directory.CreateDirectory(directory);
            return Path.Combine(
                directory,
                $"{safeProgId}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-ActivityLog.xml");
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveDteHostPath(string progId, Type dteType, string? requestedHostPath = null)
    {
        foreach (string candidate in EnumerateDteHostPathCandidates(progId, dteType, requestedHostPath))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
            }
            catch
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private static string? ResolveDevenvPath(Type dteType) =>
        ResolveDteHostPath("VisualStudio.DTE.17.0", dteType);

    private static IEnumerable<string> EnumerateDteHostPathCandidates(
        string progId,
        Type dteType,
        string? requestedHostPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedHostPath))
        {
            yield return requestedHostPath;
        }

        string? comLocalServer = TryReadComLocalServerPath(dteType);
        if (!string.IsNullOrWhiteSpace(comLocalServer))
        {
            yield return comLocalServer;
        }

        if (IsTcXaeShellProgId(progId))
        {
            foreach (string candidate in EnumerateTcXaeShellPathCandidates())
            {
                yield return candidate;
            }

            yield break;
        }

        foreach (string candidate in EnumerateVisualStudioDteHostPathCandidates(progId))
        {
            yield return candidate;
        }
    }

    private static string? TryReadComLocalServerPath(Type dteType)
    {
        try
        {
            string clsidKey = $@"CLSID\{{{dteType.GUID}}}\LocalServer32";
            using RegistryKey? key = Registry.ClassesRoot.OpenSubKey(clsidKey);
            if (key?.GetValue(null) is string localServer)
            {
                return ParseExecutablePath(localServer);
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool IsTcXaeShellProgId(string progId) =>
        progId.StartsWith("TcXaeShell.", StringComparison.OrdinalIgnoreCase)
        || progId.Equals("TcXaeShell", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateTcXaeShellPathCandidates()
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Beckhoff", "TcXaeShell", "Common7", "IDE", "TcXaeShell.exe");
            yield return Path.Combine(programFilesX86, "Beckhoff", "TwinCAT", "3.1", "Components", "Base", "TcXaeShell", "TcXaeShell.exe");
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Beckhoff", "TcXaeShell", "Common7", "IDE", "TcXaeShell.exe");
            yield return Path.Combine(programFiles, "Beckhoff", "TwinCAT", "3.1", "Components", "Base", "TcXaeShell", "TcXaeShell.exe");
        }

        yield return Path.Combine("C:\\", "TwinCAT", "3.1", "Components", "Base", "TcXaeShell", "TcXaeShell.exe");
    }

    private static IEnumerable<string> EnumerateVisualStudioDteHostPathCandidates(string progId)
    {
        int? majorVersion = TryGetVisualStudioMajorVersion(progId);
        string? vsInstallDir = Environment.GetEnvironmentVariable("VSINSTALLDIR");
        if (!string.IsNullOrWhiteSpace(vsInstallDir))
        {
            yield return Path.Combine(vsInstallDir, "Common7", "IDE", "devenv.exe");
        }

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            string vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            string? installationPath = TryReadLatestVisualStudioInstallationPath(vswherePath, majorVersion);
            if (!string.IsNullOrWhiteSpace(installationPath))
            {
                yield return Path.Combine(installationPath, "Common7", "IDE", "devenv.exe");
            }

            foreach (string productYear in EnumerateVisualStudioProductYears(majorVersion))
            {
                yield return Path.Combine(programFilesX86, "Microsoft Visual Studio", productYear, "Enterprise", "Common7", "IDE", "devenv.exe");
                yield return Path.Combine(programFilesX86, "Microsoft Visual Studio", productYear, "Professional", "Common7", "IDE", "devenv.exe");
                yield return Path.Combine(programFilesX86, "Microsoft Visual Studio", productYear, "Community", "Common7", "IDE", "devenv.exe");
                yield return Path.Combine(programFilesX86, "Microsoft Visual Studio", productYear, "BuildTools", "Common7", "IDE", "devenv.exe");
            }
        }
    }

    private static int? TryGetVisualStudioMajorVersion(string progId)
    {
        Match match = Regex.Match(progId, @"^VisualStudio\.DTE\.(\d+)\.0$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
            ? version
            : null;
    }

    private static IEnumerable<string> EnumerateVisualStudioProductYears(int? majorVersion)
    {
        if (majorVersion == 17)
        {
            yield return "2022";
            yield break;
        }

        if (majorVersion == 16)
        {
            yield return "2019";
            yield break;
        }

        yield return "2022";
        yield return "2019";
    }

    private static string? ParseExecutablePath(string commandLine)
    {
        string trimmed = commandLine.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] == '"')
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 1 ? trimmed[1..closingQuote] : null;
        }

        int exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? trimmed[..(exeIndex + 4)] : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

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

    private static DTE? TryGetActiveVisualStudioDte(string progId)
    {
        if (TryGetActiveComObject(progId) is DTE activeDte &&
            IsResponsiveDte(activeDte))
        {
            return activeDte;
        }

        foreach (object candidate in EnumerateRunningVisualStudioDteObjects(progId))
        {
            if (candidate is DTE dte &&
                IsResponsiveDte(dte))
            {
                return dte;
            }
        }

        return null;
    }

    private static DTE? TryGetActiveVisualStudioDte(string progId, HashSet<int> excludedProcessIds)
    {
        foreach (object candidate in EnumerateRunningVisualStudioDteObjects(progId))
        {
            if (candidate is not DTE dte ||
                !IsResponsiveDte(dte) ||
                TryGetDteProcessId(dte) is not int processId ||
                excludedProcessIds.Contains(processId))
            {
                continue;
            }

            return dte;
        }

        return null;
    }

    private static bool IsResponsiveDte(DTE dte)
    {
        try
        {
            _ = RetryComCall(() => dte.Version, 3, 200);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int? TryGetDteProcessId(DTE dte)
    {
        try
        {
            IntPtr hwnd = new(RetryComCall(() => dte.MainWindow.HWnd, 3, 200));
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out int processId);
            return processId > 0 ? processId : null;
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<int> CaptureDteHostProcessIds()
    {
        HashSet<int> processIds = [];
        foreach (string processName in DteHostProcessNames)
        {
            foreach (DiagnosticsProcess process in DiagnosticsProcess.GetProcessesByName(processName))
            {
                using (process)
                {
                    processIds.Add(process.Id);
                }
            }
        }

        return processIds;
    }

    private static void CleanupFailedLaunchDteHostProcesses(HashSet<int> existingProcessIds, DateTime launchWindowStart)
    {
        foreach (string processName in DteHostProcessNames)
        {
            foreach (DiagnosticsProcess process in DiagnosticsProcess.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (existingProcessIds.Contains(process.Id) ||
                            process.StartTime < launchWindowStart.AddSeconds(-2))
                        {
                            continue;
                        }

                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                    catch
                    {
                        // Best-effort cleanup of failed unattended DTE launches only.
                    }
                }
            }
        }
    }

    private static IReadOnlyCollection<int> DetectNewDteHostProcessIds(HashSet<int> existingProcessIds)
    {
        HashSet<int> current = CaptureDteHostProcessIds();
        current.ExceptWith(existingProcessIds);
        return current.ToArray();
    }

    private static string DescribeDteHostProcesses(HashSet<int> existingProcessIds, DateTime launchWindowStart)
    {
        List<string> items = [];
        int total = 0;
        int headless = 0;
        foreach (string processName in DteHostProcessNames)
        {
            foreach (DiagnosticsProcess process in DiagnosticsProcess.GetProcessesByName(processName))
            {
                using (process)
                {
                    total++;
                    int processId;
                    try
                    {
                        processId = process.Id;
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    string? title = TryReadProcessString(() => process.MainWindowTitle);
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        headless++;
                    }

                    DateTimeOffset? startTime = TryReadProcessTime(process);
                    string origin = existingProcessIds.Contains(processId)
                        ? "pre-existing"
                        : startTime.HasValue && startTime.Value.LocalDateTime >= launchWindowStart.AddSeconds(-2)
                            ? "new"
                            : "unknown";
                    if (items.Count < 8)
                    {
                        string titleText = string.IsNullOrWhiteSpace(title) ? "(headless)" : title.Trim();
                        string windows = DescribeVisibleWindowsForProcess(processId);
                        items.Add($"{processName}:{processId}:{origin}:title={titleText}:windows={windows}");
                    }
                }
            }
        }

        return total == 0
            ? "none"
            : $"total={total}, headless={headless}, samples=[{string.Join("; ", items)}]";
    }

    private static string DescribeVisibleWindowsForProcess(int processId)
    {
        List<string> windows = [];
        try
        {
            EnumWindows((hwnd, _) =>
            {
                if (windows.Count >= 3)
                {
                    return false;
                }

                try
                {
                    if (!IsWindowVisible(hwnd))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(hwnd, out int hwndProcessId);
                    if (hwndProcessId != processId)
                    {
                        return true;
                    }

                    string className = GetClassNameText(hwnd);
                    string title = GetWindowTextString(hwnd);
                    string owner = GetWindow(hwnd, GwOwner) == IntPtr.Zero ? "ownerless" : "owned";
                    string buttons = DescribeChildButtons(hwnd);
                    windows.Add($"{className}:{owner}:title={NormalizeDiagnosticText(title)}:buttons={buttons}");
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        return windows.Count == 0 ? "none" : string.Join("|", windows);
    }

    private static string DescribeChildButtons(IntPtr hwnd)
    {
        List<string> buttons = [];
        try
        {
            EnumChildWindows(hwnd, (child, _) =>
            {
                if (buttons.Count >= 5)
                {
                    return false;
                }

                try
                {
                    if (string.Equals(GetClassNameText(child), "Button", StringComparison.OrdinalIgnoreCase))
                    {
                        buttons.Add(NormalizeDiagnosticText(GetWindowTextString(child)));
                    }
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        return buttons.Count == 0 ? "none" : string.Join(",", buttons);
    }

    private static string NormalizeDiagnosticText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(empty)";
        }

        string normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }

    private static void AppendActivityLogSummary(StringBuilder builder, string? activityLogPath)
    {
        if (string.IsNullOrWhiteSpace(activityLogPath))
        {
            return;
        }

        builder.Append(" Fallback activity log: ").Append(activityLogPath);
        if (!File.Exists(activityLogPath))
        {
            builder.Append(" (not written)");
            return;
        }

        try
        {
            IReadOnlyList<string> errors = ReadActivityLogErrorSummaries(activityLogPath, maxErrors: 3);
            if (errors.Count > 0)
            {
                builder.Append(" ActivityLog errors: ").Append(string.Join(" | ", errors));
            }
        }
        catch (Exception ex)
        {
            builder.Append(" (unable to parse: ").Append(ex.Message).Append(')');
        }
    }

    private static IReadOnlyList<string> ReadActivityLogErrorSummaries(string activityLogPath, int maxErrors)
    {
        XDocument document = XDocument.Load(activityLogPath);
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
            .Select(element => new
            {
                Type = GetActivityLogChildValue(element, "type"),
                Source = GetActivityLogChildValue(element, "source"),
                Description = GetActivityLogChildValue(element, "description"),
                HResult = GetActivityLogChildValue(element, "hr")
            })
            .Where(entry => entry.Type.Contains("error", StringComparison.OrdinalIgnoreCase))
            .Select(entry => string.IsNullOrWhiteSpace(entry.HResult)
                ? $"{entry.Source}: {NormalizeDiagnosticText(entry.Description)}"
                : $"{entry.Source}: {NormalizeDiagnosticText(entry.Description)} ({NormalizeDiagnosticText(entry.HResult)})")
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(maxErrors)
            .ToList();
    }

    private static string GetActivityLogChildValue(XElement entry, string localName) =>
        entry.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?? string.Empty;

    private static string? TryReadProcessString(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryReadProcessTime(DiagnosticsProcess process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch
        {
            return null;
        }
    }

    private static readonly string[] DteHostProcessNames = ["devenv", "TcXaeShell"];

    private static IReadOnlyList<object> EnumerateRunningVisualStudioDteObjects(string progId)
    {
        List<object> result = [];
        IRunningObjectTable? rot = null;
        IEnumMoniker? enumMoniker = null;
        IBindCtx? bindCtx = null;
        try
        {
            GetRunningObjectTable(0, out rot);
            rot.EnumRunning(out enumMoniker);
            CreateBindCtx(0, out bindCtx);

            IMoniker[] monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                string? displayName = null;
                try
                {
                    monikers[0].GetDisplayName(bindCtx, null, out displayName);
                }
                catch (COMException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(displayName) ||
                    !displayName.Contains(progId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    rot.GetObject(monikers[0], out object runningObject);
                    result.Add(runningObject);
                }
                catch (COMException)
                {
                }
            }
        }
        finally
        {
            if (bindCtx is not null)
            {
                Marshal.ReleaseComObject(bindCtx);
            }

            if (enumMoniker is not null)
            {
                Marshal.ReleaseComObject(enumMoniker);
            }

            if (rot is not null)
            {
                Marshal.ReleaseComObject(rot);
            }
        }

        return result;
    }

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object activeObject);

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void GetRunningObjectTable(int reserved, out IRunningObjectTable runningObjectTable);

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CreateBindCtx(int reserved, out IBindCtx bindCtx);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void CLSIDFromProgIDEx(string progId, out Guid clsid);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    private const int GwOwner = 4;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    private static string GetWindowTextString(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassNameText(IntPtr hwnd)
    {
        StringBuilder builder = new(256);
        _ = GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

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

    private static string JoinTreePath(string parentTreeItemPath, string childName) =>
        parentTreeItemPath.TrimEnd('^') + "^" + childName;

    private static TwinCatNodeInfo CreateNodeInfo(
        ITcSmTreeItem item,
        string fallbackPath,
        bool UsedFallback = false) =>
        new(
            GetTreePath(item, fallbackPath),
            GetTreeItemField(item, "ItemName")
                ?? GetTreeItemField(item, "Name")
                ?? fallbackPath.Split('^', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                ?? fallbackPath,
            GetTreeItemField(item, "ObjectId"),
            UsedFallback: UsedFallback);

    private static void ApplyDisabledState(ITcSmTreeItem item, bool? disabled)
    {
        if (!disabled.HasValue)
        {
            return;
        }

        RetryComCall(() =>
        {
            item.Disabled = disabled.Value
                ? DISABLED_STATE.SMDS_DISABLED
                : DISABLED_STATE.SMDS_NOT_DISABLED;
        }, 5, 200);
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

    private static void EnsureVendorElement(XElement root)
    {
        XElement vendor = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Vendor")
            ?? new XElement("Vendor");
        if (vendor.Parent is null)
        {
            root.AddFirst(vendor);
        }

        XElement name = vendor.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")
            ?? new XElement("Name");
        if (name.Parent is null)
        {
            vendor.Add(name);
        }

        if (string.IsNullOrWhiteSpace(name.Value))
        {
            name.Value = "C++ Module Vendor";
        }
    }

    private static void EnsureCppGroupElement(XElement root)
    {
        XElement groups = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Groups")
            ?? new XElement("Groups");
        if (groups.Parent is null)
        {
            XElement? modules = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Modules");
            if (modules is not null)
            {
                modules.AddBeforeSelf(groups);
            }
            else
            {
                root.Add(groups);
            }
        }

        XElement group = groups.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Group" &&
            string.Equals(GetChildElementValue(element, "Name"), "C++", StringComparison.OrdinalIgnoreCase))
            ?? new XElement("Group");
        if (group.Parent is null)
        {
            groups.Add(group);
        }

        group.SetAttributeValue("SortOrder", "701");
        SetOrCreateChildElementValue(group, "Name", "C++");
        SetOrCreateChildElementValue(group, "DisplayName", "C++ Modules");
    }

    private static void ReplaceTopLevelSection(XElement root, string localName, XElement replacement)
    {
        XElement normalized = CloneWithoutNamespace(replacement);
        XElement? existing = root.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
        if (existing is null)
        {
            root.Add(normalized);
        }
        else
        {
            existing.ReplaceWith(normalized);
        }
    }

    private static XElement CreateTmcModuleElement(
        TmcModuleModel model,
        string projectName,
        IReadOnlyDictionary<string, string> typeGuids)
    {
        string moduleGuid = NormalizeGuidText(model.Guid);
        XElement module = new(
            "Module",
            new XAttribute("ClassName", model.Name),
            new XAttribute("GUID", moduleGuid),
            new XAttribute("Group", "C++"),
            new XElement("Name", model.Name),
            new XElement("CLSID", new XAttribute("ClassFactory", projectName), moduleGuid),
            new XElement(
                "Licenses",
                new XElement(
                    "License",
                    new XElement("LicenseId", TcCppLicenseId),
                    new XElement("Comment", "TC3 C++"))),
            new XElement("InitSequence", "PSO"),
            new XElement("Contexts", new XElement("Context", new XElement("Id", "1"))),
            CreateModeledInterfaces(model, typeGuids),
            CreateModeledParameters(model.Parameters, typeGuids),
            CreateModeledDataAreas(model.DataAreas, typeGuids),
            CreateModeledPointers("InterfacePointers", "InterfacePointer", model.InterfacePointers, typeGuids),
            CreateModeledPointers("DataPointers", "DataPointer", model.DataPointers, typeGuids),
            new XElement("Deployment"));

        if (model.EventClasses is not null && model.EventClasses.Count > 0)
        {
            XElement eventClasses = new("EventClasses");
            foreach (TmcTypeReference eventClass in model.EventClasses)
            {
                TmcTypeInfo type = ResolveTmcType(eventClass.TypeName, eventClass.TypeGuid, typeGuids);
                eventClasses.Add(
                    new XElement(
                        "EventClass",
                        new XElement("Type", new XAttribute("GUID", type.Guid), type.Name)));
            }

            module.Add(eventClasses);
        }

        return module;
    }

    private static XElement CreateModeledInterfaces(TmcModuleModel model, IReadOnlyDictionary<string, string> typeGuids)
    {
        List<TmcInterfaceModel> interfaces =
        [
            new("ITComObject", DisableCodeGeneration: true),
            new("ITcCyclic"),
            new("ITcADI", DisableCodeGeneration: true),
            new("ITcWatchSource", DisableCodeGeneration: true)
        ];

        if (model.Interfaces is not null)
        {
            foreach (TmcInterfaceModel item in model.Interfaces)
            {
                if (!interfaces.Any(existing => string.Equals(existing.TypeName, item.TypeName, StringComparison.OrdinalIgnoreCase)))
                {
                    interfaces.Add(item);
                }
            }
        }

        XElement container = new("Interfaces");
        foreach (TmcInterfaceModel item in interfaces)
        {
            TmcTypeInfo type = ResolveTmcType(item.TypeName, item.TypeGuid, typeGuids);
            XElement element = new("Interface");
            if (item.DisableCodeGeneration)
            {
                element.SetAttributeValue("DisableCodeGeneration", "true");
            }

            element.Add(new XElement("Type", new XAttribute("GUID", type.Guid), type.Name));
            if (item.ContextId.HasValue)
            {
                element.Add(new XElement("ContextId", item.ContextId.Value.ToString(CultureInfo.InvariantCulture)));
            }

            container.Add(element);
        }

        return container;
    }

    private static XElement CreateModeledParameters(
        IReadOnlyList<TmcParameterModel>? parameters,
        IReadOnlyDictionary<string, string> typeGuids)
    {
        XElement container = new("Parameters");
        foreach (TmcParameterModel item in parameters ?? Array.Empty<TmcParameterModel>())
        {
            ValidateRequiredText(item.Name, nameof(item.Name));
            ValidateRequiredText(item.PtcId, nameof(item.PtcId));
            XElement parameter = new("Parameter");
            if (item.HideParameter)
            {
                parameter.SetAttributeValue("HideParameter", "true");
            }

            if (item.CreateSymbol)
            {
                parameter.SetAttributeValue("CreateSymbol", "true");
            }

            if (item.ShowSubItems)
            {
                parameter.SetAttributeValue("ShowSubItems", "true");
            }

            parameter.Add(new XElement("Name", item.Name));
            if (!string.IsNullOrWhiteSpace(item.Comment))
            {
                parameter.Add(new XElement("Comment", item.Comment));
            }

            if (item.BitSize.HasValue)
            {
                parameter.Add(new XElement("BitSize", item.BitSize.Value.ToString(CultureInfo.InvariantCulture)));
            }

            if (!string.IsNullOrWhiteSpace(item.TypeName))
            {
                TmcTypeInfo type = ResolveTmcType(item.TypeName, item.TypeGuid, typeGuids);
                parameter.Add(new XElement("BaseType", new XAttribute("GUID", type.Guid), type.Name));
            }

            foreach (TmcSubItemModel subItem in item.SubItems ?? Array.Empty<TmcSubItemModel>())
            {
                parameter.Add(CreateModeledSubItem(subItem, typeGuids));
            }

            parameter.Add(new XElement("PTCID", item.PtcId));
            parameter.Add(new XElement("ContextId", item.ContextId.ToString(CultureInfo.InvariantCulture)));
            container.Add(parameter);
        }

        return container;
    }

    private static XElement CreateModeledDataAreas(
        IReadOnlyList<TmcDataAreaModel>? dataAreas,
        IReadOnlyDictionary<string, string> typeGuids)
    {
        XElement container = new("DataAreas");
        foreach (TmcDataAreaModel item in dataAreas ?? Array.Empty<TmcDataAreaModel>())
        {
            ValidateRequiredText(item.Name, nameof(item.Name));
            ValidateRequiredText(item.AreaType, nameof(item.AreaType));
            XElement dataArea = new(
                "DataArea",
                new XElement("AreaNo", new XAttribute("AreaType", item.AreaType), item.AreaNo.ToString(CultureInfo.InvariantCulture)),
                new XElement("Name", item.Name),
                new XElement("ContextId", item.ContextId.ToString(CultureInfo.InvariantCulture)));

            if (item.ByteSize.HasValue)
            {
                dataArea.Add(new XElement("ByteSize", item.ByteSize.Value.ToString(CultureInfo.InvariantCulture)));
            }

            foreach (TmcSymbolModel symbol in item.Symbols ?? Array.Empty<TmcSymbolModel>())
            {
                dataArea.Add(CreateModeledSymbol(symbol, typeGuids));
            }

            container.Add(dataArea);
        }

        return container;
    }

    private static XElement CreateModeledSymbol(TmcSymbolModel symbol, IReadOnlyDictionary<string, string> typeGuids)
    {
        ValidateRequiredText(symbol.Name, nameof(symbol.Name));
        ValidateRequiredText(symbol.TypeName, nameof(symbol.TypeName));
        TmcTypeInfo type = ResolveTmcType(symbol.TypeName, symbol.TypeGuid, typeGuids);
        XElement element = new("Symbol");
        if (symbol.CreateSymbol)
        {
            element.SetAttributeValue("CreateSymbol", "true");
        }

        element.Add(new XElement("Name", symbol.Name));
        if (symbol.BitSize.HasValue)
        {
            element.Add(new XElement("BitSize", symbol.BitSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        element.Add(new XElement("BaseType", new XAttribute("GUID", type.Guid), type.Name));
        if (symbol.ArrayElements.HasValue)
        {
            element.Add(CreateArrayInfo(symbol.ArrayElements.Value));
        }

        if (symbol.Properties is not null && symbol.Properties.Count > 0)
        {
            element.Add(CreateProperties(symbol.Properties));
        }

        if (symbol.BitOffset.HasValue)
        {
            element.Add(new XElement("BitOffs", symbol.BitOffset.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return element;
    }

    private static XElement CreateModeledSubItem(TmcSubItemModel subItem, IReadOnlyDictionary<string, string> typeGuids)
    {
        ValidateRequiredText(subItem.Name, nameof(subItem.Name));
        ValidateRequiredText(subItem.TypeName, nameof(subItem.TypeName));
        TmcTypeInfo type = ResolveTmcType(subItem.TypeName, subItem.TypeGuid, typeGuids);
        XElement element = new(
            "SubItem",
            new XElement("Name", subItem.Name),
            new XElement("Type", new XAttribute("GUID", type.Guid), type.Name));

        if (subItem.ArrayElements.HasValue)
        {
            element.Add(CreateArrayInfo(subItem.ArrayElements.Value));
        }

        if (subItem.BitSize.HasValue)
        {
            element.Add(new XElement("BitSize", subItem.BitSize.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (subItem.Properties is not null && subItem.Properties.Count > 0)
        {
            element.Add(CreateProperties(subItem.Properties));
        }

        if (subItem.BitOffset.HasValue)
        {
            element.Add(new XElement("BitOffs", subItem.BitOffset.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return element;
    }

    private static XElement CreateModeledPointers(
        string containerName,
        string pointerElementName,
        IReadOnlyList<TmcPointerModel>? pointers,
        IReadOnlyDictionary<string, string> typeGuids)
    {
        XElement container = new(containerName);
        foreach (TmcPointerModel item in pointers ?? Array.Empty<TmcPointerModel>())
        {
            ValidateRequiredText(item.Name, nameof(item.Name));
            ValidateRequiredText(item.PtcId, nameof(item.PtcId));
            ValidateRequiredText(item.TypeName, nameof(item.TypeName));
            TmcTypeInfo type = ResolveTmcType(item.TypeName, item.TypeGuid, typeGuids);
            XElement pointer = new(
                pointerElementName,
                new XElement("PTCID", item.PtcId),
                new XElement("Name", item.Name),
                new XElement("Type", new XAttribute("GUID", type.Guid), type.Name));

            if (item.ArrayElements.HasValue)
            {
                pointer.Add(CreateArrayInfo(item.ArrayElements.Value));
            }

            if (item.ContextId.HasValue)
            {
                pointer.Add(new XElement("ContextId", item.ContextId.Value.ToString(CultureInfo.InvariantCulture)));
            }

            container.Add(pointer);
        }

        return container;
    }

    private static XElement CreateArrayInfo(int elements)
    {
        if (elements <= 0)
        {
            throw new InvalidOperationException("ArrayElements must be greater than zero.");
        }

        return new XElement(
            "ArrayInfo",
            new XElement("LBound", "0"),
            new XElement("Elements", elements.ToString(CultureInfo.InvariantCulture)));
    }

    private static XElement CreateProperties(IReadOnlyList<TmcPropertyModel> properties) =>
        new(
            "Properties",
            properties.Select(property =>
                new XElement(
                    "Property",
                    new XElement("Name", property.Name),
                    new XElement("Value", property.Value))));

    private static TmcGeneratedModel ReadGeneratedTmcModel(
        string? servicesHeaderPath,
        IReadOnlyList<string>? generatedHeaderPaths)
    {
        Dictionary<string, string> typeGuids = BuiltInTmcTypes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Guid,
            StringComparer.OrdinalIgnoreCase);
        List<XElement> dataTypes = [];

        string[] headerPaths = EnumerateGeneratedHeaderPaths(servicesHeaderPath, generatedHeaderPaths).ToArray();
        if (headerPaths.Length == 0)
        {
            return new TmcGeneratedModel(typeGuids, dataTypes);
        }

        List<string> headerTexts = [];
        foreach (string headerPath in headerPaths)
        {
            string resolvedPath = Path.GetFullPath(headerPath);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException("Generated header was not found.", resolvedPath);
            }

            string headerText = File.ReadAllText(resolvedPath);
            headerTexts.Add(headerText);
            foreach ((string TypeName, string GuidText) item in ReadCppGuidConstants(headerText))
            {
                typeGuids[item.TypeName] = item.GuidText;
            }

            foreach ((string TypeName, string GuidText) item in ReadTcTypeGuardGuids(headerText))
            {
                typeGuids.TryAdd(item.TypeName, item.GuidText);
            }
        }

        foreach (string headerText in headerTexts)
        {
            dataTypes.AddRange(ReadTmcDataTypesFromGeneratedHeader(headerText, typeGuids));
        }

        return new TmcGeneratedModel(typeGuids, dataTypes);
    }

    private static IEnumerable<string> EnumerateGeneratedHeaderPaths(
        string? servicesHeaderPath,
        IReadOnlyList<string>? generatedHeaderPaths)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(servicesHeaderPath) && seen.Add(servicesHeaderPath))
        {
            yield return servicesHeaderPath;
        }

        foreach (string headerPath in generatedHeaderPaths ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(headerPath) && seen.Add(headerPath))
            {
                yield return headerPath;
            }
        }
    }

    private static IEnumerable<(string TypeName, string GuidText)> ReadCppGuidConstants(string headerText)
    {
        Regex guidPattern = new(
            @"const\s+GUID\s+GUID_(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{0x(?<d1>[0-9A-Fa-f]{8}),\s*0x(?<d2>[0-9A-Fa-f]{4}),\s*0x(?<d3>[0-9A-Fa-f]{4}),\s*\{(?<bytes>[^}]*)\}\s*\}\s*;",
            RegexOptions.Compiled);

        foreach (Match match in guidPattern.Matches(headerText))
        {
            string[] bytes = match.Groups["bytes"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value)
                .ToArray();
            if (bytes.Length != 8)
            {
                continue;
            }

            string guidText = "{" +
                match.Groups["d1"].Value + "-" +
                match.Groups["d2"].Value + "-" +
                match.Groups["d3"].Value + "-" +
                bytes[0] + bytes[1] + "-" +
                string.Concat(bytes.Skip(2)) +
                "}";
            if (TryNormalizeGuid(guidText, out string normalized))
            {
                yield return (match.Groups["name"].Value, normalized);
            }
        }
    }

    private static IEnumerable<(string TypeName, string GuidText)> ReadTcTypeGuardGuids(string headerText)
    {
        foreach (Match match in Regex.Matches(
                     headerText,
                     @"^[ \t]*#define[ \t]+_TC_TYPE_(?<guid>[0-9A-Fa-f_]{36})_INCLUDED_[^\r\n]*\r?\n[ \t]*struct\s+__declspec\(novtable\)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:",
                     RegexOptions.Multiline))
        {
            string guidText = "{" + match.Groups["guid"].Value.Replace('_', '-') + "}";
            if (TryNormalizeGuid(guidText, out string normalized))
            {
                yield return (match.Groups["name"].Value, normalized);
            }
        }
    }

    private static IReadOnlyList<XElement> ReadTmcDataTypesFromGeneratedHeader(
        string headerText,
        IReadOnlyDictionary<string, string> typeGuids)
    {
        string? dataTypeRegion = ExtractAutoGeneratedRegion(headerText, "DataTypes");
        if (string.IsNullOrWhiteSpace(dataTypeRegion))
        {
            return Array.Empty<XElement>();
        }

        Dictionary<string, ParsedCppType> parsedTypes = new(StringComparer.OrdinalIgnoreCase);
        foreach (ParsedCppType parsed in ParseCppDataTypes(dataTypeRegion))
        {
            parsedTypes[parsed.Name] = parsed;
        }

        List<XElement> elements = [];
        foreach (ParsedCppType parsed in parsedTypes.Values)
        {
            if (!typeGuids.TryGetValue(parsed.Name, out string? guid))
            {
                continue;
            }

            XElement dataType = new(
                "DataType",
                new XElement("Name", new XAttribute("GUID", guid), parsed.Name));

            if (parsed.Kind == CppTypeKind.Enum || parsed.Kind == CppTypeKind.Alias)
            {
                TmcTypeInfo baseType = ResolveTmcType(parsed.BaseTypeName ?? "INT", null, typeGuids);
                int aliasBitSize = checked((baseType.BitSize ?? 0) * (parsed.ArrayElements ?? 1));
                dataType.Add(new XElement("BitSize", aliasBitSize.ToString(CultureInfo.InvariantCulture)));
                dataType.Add(new XElement("BaseType", new XAttribute("GUID", baseType.Guid), baseType.Name));
                if (parsed.ArrayElements.HasValue)
                {
                    dataType.Add(CreateArrayInfo(parsed.ArrayElements.Value));
                }

                foreach ((string Name, string Value) enumValue in parsed.EnumValues)
                {
                    dataType.Add(
                        new XElement(
                            "EnumInfo",
                            new XElement("Text", new XCData(enumValue.Name)),
                            new XElement("Enum", enumValue.Value)));
                }
            }
            else
            {
                int bitOffset = 0;
                foreach (ParsedCppField field in parsed.Fields)
                {
                    if (IsPaddingField(field))
                    {
                        bitOffset += ResolveCppFieldBitSize(field, parsedTypes, typeGuids);
                        continue;
                    }

                    TmcTypeInfo fieldType = ResolveTmcType(field.TypeName, null, typeGuids);
                    int bitSize = ResolveCppFieldBitSize(field, parsedTypes, typeGuids);
                    XElement subItem = new(
                        "SubItem",
                        new XElement("Name", field.Name),
                        new XElement("Type", new XAttribute("GUID", fieldType.Guid), fieldType.Name));
                    if (field.ArrayElements.HasValue)
                    {
                        subItem.Add(CreateArrayInfo(field.ArrayElements.Value));
                    }

                    subItem.Add(new XElement("BitSize", bitSize.ToString(CultureInfo.InvariantCulture)));
                    subItem.Add(new XElement("BitOffs", bitOffset.ToString(CultureInfo.InvariantCulture)));
                    dataType.Add(subItem);
                    bitOffset += bitSize;
                }

                dataType.AddFirst(new XElement("BitSize", bitOffset.ToString(CultureInfo.InvariantCulture)));
            }

            elements.Add(dataType);
        }

        return elements;
    }

    private static IEnumerable<ParsedCppType> ParseCppDataTypes(string dataTypeRegion)
    {
        foreach (Match match in Regex.Matches(
                     dataTypeRegion,
                     @"enum\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<base>[A-Za-z_][A-Za-z0-9_]*)\s*\{(?<body>.*?)\};",
                     RegexOptions.Singleline))
        {
            List<(string Name, string Value)> values = [];
            foreach (string rawLine in match.Groups["body"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string line = StripCppLineComment(rawLine).Trim().TrimEnd(',');
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    values.Add((parts[0], parts[1]));
                }
            }

            yield return new ParsedCppType(match.Groups["name"].Value, CppTypeKind.Enum, match.Groups["base"].Value, [], values);
        }

        foreach (Match match in Regex.Matches(
                     dataTypeRegion,
                     @"typedef\s+(?<base>[A-Za-z_][A-Za-z0-9_]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\[(?<count>\d+)\])?\s*;",
                     RegexOptions.Singleline))
        {
            int? arrayElements = int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount)
                ? parsedCount
                : null;
            yield return new ParsedCppType(match.Groups["name"].Value, CppTypeKind.Alias, match.Groups["base"].Value, [], [], arrayElements);
        }

        foreach (Match match in Regex.Matches(
                     dataTypeRegion,
                     @"typedef\s+struct\s+_[A-Za-z_][A-Za-z0-9_]*\s*\{(?<body>.*?)\}\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*\*P[A-Za-z_][A-Za-z0-9_]*\s*;",
                     RegexOptions.Singleline))
        {
            yield return new ParsedCppType(match.Groups["name"].Value, CppTypeKind.Struct, null, ParseCppFields(match.Groups["body"].Value), []);
        }
    }

    private static IReadOnlyList<ParsedCppField> ParseCppFields(string body)
    {
        List<ParsedCppField> fields = [];
        foreach (string rawLine in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string line = StripCppLineComment(rawLine).Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("enum :", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("}", StringComparison.Ordinal))
            {
                string? enumFieldName = line.TrimStart('}', ' ', '\t').Trim().TrimEnd(';');
                if (!string.IsNullOrWhiteSpace(enumFieldName))
                {
                    fields.Add(new ParsedCppField("INT", enumFieldName, null, null));
                }

                continue;
            }

            if (!line.EndsWith(';'))
            {
                continue;
            }

            Match bitField = Regex.Match(line, @"^(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<bits>\d+)\s*;");
            if (bitField.Success)
            {
                fields.Add(new ParsedCppField(
                    NormalizeCppTypeName(bitField.Groups["type"].Value),
                    bitField.Groups["name"].Value,
                    null,
                    int.Parse(bitField.Groups["bits"].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            Match field = Regex.Match(line, @"^(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\[(?<count>\d+)\])?\s*;");
            if (!field.Success)
            {
                continue;
            }

            int? arrayElements = int.TryParse(field.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount)
                ? parsedCount
                : null;
            fields.Add(new ParsedCppField(
                NormalizeCppTypeName(field.Groups["type"].Value),
                field.Groups["name"].Value,
                arrayElements,
                null));
        }

        return fields;
    }

    private static string? ExtractAutoGeneratedRegion(string text, string id)
    {
        string startMarker = "///<AutoGeneratedContent id=\"" + id + "\">";
        int start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += startMarker.Length;
        int end = text.IndexOf("///</AutoGeneratedContent>", start, StringComparison.OrdinalIgnoreCase);
        return end > start ? text[start..end] : null;
    }

    private static string StripCppLineComment(string line)
    {
        int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        return commentIndex < 0 ? line : line[..commentIndex];
    }

    private static string NormalizeCppTypeName(string rawType)
    {
        string normalized = rawType.Trim();
        normalized = normalized.Replace("unsigned char", "USINT", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("char", "SINT", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("bool", "BOOL", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("double", "LREAL", StringComparison.OrdinalIgnoreCase);
        return normalized.Trim();
    }

    private static bool IsPaddingField(ParsedCppField field) =>
        field.Name.StartsWith("reserved", StringComparison.OrdinalIgnoreCase);

    private static int ResolveCppFieldBitSize(
        ParsedCppField field,
        IReadOnlyDictionary<string, ParsedCppType> parsedTypes,
        IReadOnlyDictionary<string, string> typeGuids)
    {
        int baseSize = field.BitFieldSize ??
            ResolveCppTypeBitSize(field.TypeName, parsedTypes, typeGuids);
        return checked(baseSize * (field.ArrayElements ?? 1));
    }

    private static int ResolveCppTypeBitSize(
        string typeName,
        IReadOnlyDictionary<string, ParsedCppType> parsedTypes,
        IReadOnlyDictionary<string, string> typeGuids)
    {
        TmcTypeInfo type = ResolveTmcType(typeName, null, typeGuids, throwOnMissing: false);
        if (type.BitSize.HasValue)
        {
            return type.BitSize.Value;
        }

        if (parsedTypes.TryGetValue(typeName, out ParsedCppType? parsedType))
        {
            if (parsedType.Kind == CppTypeKind.Alias && !string.IsNullOrWhiteSpace(parsedType.BaseTypeName))
            {
                int baseSize = ResolveCppTypeBitSize(parsedType.BaseTypeName, parsedTypes, typeGuids);
                return checked(baseSize * (parsedType.ArrayElements ?? 1));
            }

            if (parsedType.Kind == CppTypeKind.Enum)
            {
                return 16;
            }

            return parsedType.Fields.Sum(field => ResolveCppFieldBitSize(field, parsedTypes, typeGuids));
        }

        throw new InvalidOperationException($"Unable to resolve bit size for generated C++ type '{typeName}'.");
    }

    private static TmcTypeInfo ResolveTmcType(
        string typeName,
        string? explicitGuid,
        IReadOnlyDictionary<string, string> typeGuids,
        bool throwOnMissing = true)
    {
        ValidateRequiredText(typeName, nameof(typeName));
        if (!string.IsNullOrWhiteSpace(explicitGuid))
        {
            return new TmcTypeInfo(typeName, NormalizeGuidText(explicitGuid), BuiltInTmcTypes.TryGetValue(typeName, out TmcTypeInfo? builtIn) ? builtIn.BitSize : null);
        }

        if (BuiltInTmcTypes.TryGetValue(typeName, out TmcTypeInfo? builtInType))
        {
            return builtInType;
        }

        if (typeGuids.TryGetValue(typeName, out string? guid))
        {
            return new TmcTypeInfo(typeName, NormalizeGuidText(guid), null);
        }

        if (throwOnMissing)
        {
            throw new InvalidOperationException($"Type GUID was not found for '{typeName}'. Provide TypeGuid or a GeneratedServicesHeaderPath containing GUID_{typeName}.");
        }

        return new TmcTypeInfo(typeName, string.Empty, null);
    }

    private static XElement CloneWithoutNamespace(XElement source)
    {
        XElement clone = new(source.Name.LocalName);
        foreach (XAttribute attribute in source.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                clone.SetAttributeValue(attribute.Name, attribute.Value);
            }
        }

        foreach (XNode node in source.Nodes())
        {
            clone.Add(node is XElement child ? CloneWithoutNamespace(child) : node);
        }

        return clone;
    }

    private static void ValidateRequiredText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{fieldName} must not be empty.");
        }
    }

    private static Dictionary<string, TmcModuleShape> ReadTmcModuleShapes(XDocument document)
    {
        Dictionary<string, TmcModuleShape> modules = new(StringComparer.OrdinalIgnoreCase);
        XElement? modulesElement = document.Root?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Modules");
        if (modulesElement is null)
        {
            return modules;
        }

        foreach (XElement module in modulesElement.Elements().Where(element => element.Name.LocalName == "Module"))
        {
            string? moduleName = module.Attribute("ClassName")?.Value ?? GetChildElementValue(module, "Name");
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                continue;
            }

            Dictionary<string, TmcDataAreaShape> dataAreas = new(StringComparer.OrdinalIgnoreCase);
            XElement? dataAreasElement = module.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "DataAreas");
            if (dataAreasElement is not null)
            {
                foreach (XElement dataArea in dataAreasElement.Elements().Where(element => element.Name.LocalName == "DataArea"))
                {
                    string? dataAreaName = GetChildElementValue(dataArea, "Name");
                    if (string.IsNullOrWhiteSpace(dataAreaName))
                    {
                        continue;
                    }

                    XElement? areaNo = dataArea.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "AreaNo");
                    string? areaType = areaNo?.Attribute("AreaType")?.Value;
                    List<TmcSymbolShape> symbols = dataArea.Elements()
                        .Where(element => element.Name.LocalName == "Symbol")
                        .Select(symbol => new TmcSymbolShape(
                            GetChildElementValue(symbol, "Name") ?? string.Empty,
                            GetChildElementValue(symbol, "BaseType")))
                        .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name))
                        .ToList();

                    dataAreas[dataAreaName] = new TmcDataAreaShape(dataAreaName, areaType, symbols);
                }
            }

            modules[moduleName] = new TmcModuleShape(moduleName, dataAreas);
        }

        return modules;
    }

    private static void ValidateExpectedDataAreas(
        TmcModuleExpectation expectedModule,
        TmcModuleShape actualModule,
        ICollection<string> errors)
    {
        foreach (TmcDataAreaExpectation expectedArea in expectedModule.DataAreas)
        {
            if (string.IsNullOrWhiteSpace(expectedArea.Name))
            {
                errors.Add($"Module '{expectedModule.ModuleName}' has an expected data area with an empty name.");
                continue;
            }

            if (!actualModule.DataAreas.TryGetValue(expectedArea.Name, out TmcDataAreaShape? actualArea))
            {
                errors.Add($"Module '{expectedModule.ModuleName}' is missing data area '{expectedArea.Name}'.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(expectedArea.AreaType) &&
                !string.Equals(actualArea.AreaType, expectedArea.AreaType, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Module '{expectedModule.ModuleName}' data area '{expectedArea.Name}' AreaType mismatch. Expected '{expectedArea.AreaType}', actual '{actualArea.AreaType ?? string.Empty}'.");
            }

            IReadOnlyList<TmcSymbolExpectation> expectedSymbols = expectedArea.Symbols ?? Array.Empty<TmcSymbolExpectation>();
            if (actualArea.Symbols.Count != expectedSymbols.Count)
            {
                errors.Add(
                    $"Module '{expectedModule.ModuleName}' data area '{expectedArea.Name}' symbol count mismatch. Expected {expectedSymbols.Count}, actual {actualArea.Symbols.Count}.");
            }

            foreach (TmcSymbolExpectation expectedSymbol in expectedSymbols)
            {
                TmcSymbolShape? actualSymbol = actualArea.Symbols
                    .FirstOrDefault(symbol => string.Equals(symbol.Name, expectedSymbol.Name, StringComparison.OrdinalIgnoreCase));
                if (actualSymbol is null)
                {
                    errors.Add($"Module '{expectedModule.ModuleName}' data area '{expectedArea.Name}' is missing symbol '{expectedSymbol.Name}'.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(expectedSymbol.TypeName) &&
                    !string.Equals(actualSymbol.TypeName, expectedSymbol.TypeName, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Module '{expectedModule.ModuleName}' data area '{expectedArea.Name}' symbol '{expectedSymbol.Name}' type mismatch. Expected '{expectedSymbol.TypeName}', actual '{actualSymbol.TypeName ?? string.Empty}'.");
                }
            }
        }
    }

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

        string displayName = request.InstanceBaseName;

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
            (string.Equals(GetChildElementValue(element, "Name"), displayName, StringComparison.OrdinalIgnoreCase) ||
             IsXaeSuffixedDisplayName(GetChildElementValue(element, "Name"), displayName)));
        if (existing is not null)
        {
            SetOrCreateChildElementValue(existing, "Name", displayName);
            document.Save(projectPath);
            string existingObjectId = existing.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName is "OTCID" or "Id")?.Value ?? "#x02010010";
            return new TwinCatNodeInfo(
                $"TIXC^{request.ProjectName}^{displayName}",
                displayName,
                existingObjectId,
                request.ProjectTmcPath,
                UsedFallback: true);
        }

        HashSet<string> usedObjectIds = cppProject.Elements()
            .Where(element => element.Name.LocalName == "Instance")
            .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName is "OTCID" or "Id")?.Value)
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

    private static bool TryNormalizeCppInstanceNameInTsproj(
        string projectPath,
        string cppProjectName,
        string requestedName,
        string displayName,
        string? objectId,
        out string persistedName,
        out string? persistedObjectId)
    {
        persistedName = requestedName;
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
            string.Equals(GetChildElementValue(element, "Name"), requestedName, StringComparison.OrdinalIgnoreCase));
        if (instance is null)
        {
            instance = cppProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                InstanceObjectIdEquals(element, objectId));
        }

        if (instance is null)
        {
            instance = cppProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                IsXaeSuffixedDisplayName(GetChildElementValue(element, "Name"), requestedName));
        }

        if (instance is null)
        {
            instance = cppProject.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "Instance" &&
                string.Equals(GetChildElementValue(element, "Name"), displayName, StringComparison.OrdinalIgnoreCase));
        }

        if (instance is null)
        {
            return false;
        }

        SetOrCreateChildElementValue(instance, "Name", requestedName);
        persistedName = requestedName;
        persistedObjectId =
            instance.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName is "OTCID" or "Id")?.Value ??
            objectId;
        document.Save(projectPath);
        return true;
    }

    private static void TryNormalizeCurrentTwinCatProjectInstanceNames(TwinCatEngineeringSession session)
    {
        string? projectPath = ResolveTwinCatProjectPathForOfflineMutation(session);
        if (!string.IsNullOrWhiteSpace(projectPath) && File.Exists(projectPath))
        {
            _ = TryNormalizeCppInstanceNamesInTsproj(projectPath, cppProjectName: null);
        }
    }

    private static bool TryNormalizeCppInstanceNamesInTsproj(string projectPath, string? cppProjectName)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch
        {
            return false;
        }

        XElement[] cppProjects = document.Descendants().Where(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Cpp" &&
            (string.IsNullOrWhiteSpace(cppProjectName) ||
             string.Equals(element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value, cppProjectName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetChildElementValue(element, "Name"), cppProjectName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (cppProjects.Length == 0)
        {
            return false;
        }

        bool changed = false;
        foreach (XElement instance in cppProjects
                     .SelectMany(project => project.Elements())
                     .Where(element => element.Name.LocalName == "Instance"))
        {
            string? name = GetChildElementValue(instance, "Name");
            string? normalized = NormalizeXaeSuffixedDisplayName(instance, name);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !string.Equals(name, normalized, StringComparison.Ordinal))
            {
                SetOrCreateChildElementValue(instance, "Name", normalized);
                changed = true;
            }
        }

        if (!changed)
        {
            return true;
        }

        document.Save(projectPath);
        return true;
    }

    private static bool InstanceObjectIdEquals(XElement instance, string? objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return false;
        }

        return instance.Attributes().Any(attribute =>
            attribute.Name.LocalName is "OTCID" or "Id" &&
            string.Equals(attribute.Value, objectId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsXaeSuffixedDisplayName(string? candidate, string requestedName)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return candidate.StartsWith(requestedName + " (", StringComparison.OrdinalIgnoreCase) &&
               candidate.EndsWith(')');
    }

    private static string? NormalizeXaeSuffixedDisplayName(XElement instance, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        int suffixStart = candidate.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffixStart <= 0 || !candidate.EndsWith(')'))
        {
            return candidate;
        }

        string suffix = candidate[(suffixStart + 2)..^1];
        string? tmcName = instance.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "TmcDesc")?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Name")?
            .Value;
        if (string.IsNullOrWhiteSpace(tmcName) ||
            !string.Equals(suffix, tmcName, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        return candidate[..suffixStart];
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

    private sealed record DteFallbackLaunchInfo(
        string HostPath,
        string Arguments,
        string? ActivityLogPath,
        bool StartSucceeded,
        string? StartError,
        int? ProcessId);

    private sealed record CppProjectPaths(
        string ProjectName,
        string ProjectDirectory,
        string ProjectFilePath,
        string FiltersFilePath);

    private sealed record TmcModuleShape(
        string Name,
        IReadOnlyDictionary<string, TmcDataAreaShape> DataAreas);

    private sealed record TmcDataAreaShape(
        string Name,
        string? AreaType,
        IReadOnlyList<TmcSymbolShape> Symbols);

    private sealed record TmcSymbolShape(
        string Name,
        string? TypeName);

    private sealed record TmcTypeInfo(
        string Name,
        string Guid,
        int? BitSize = null);

    private sealed record TmcGeneratedModel(
        IReadOnlyDictionary<string, string> TypeGuids,
        IReadOnlyList<XElement> DataTypes);

    private enum CppTypeKind
    {
        Struct,
        Enum,
        Alias
    }

    private sealed record ParsedCppType(
        string Name,
        CppTypeKind Kind,
        string? BaseTypeName,
        IReadOnlyList<ParsedCppField> Fields,
        IReadOnlyList<(string Name, string Value)> EnumValues,
        int? ArrayElements = null);

    private sealed record ParsedCppField(
        string TypeName,
        string Name,
        int? ArrayElements,
        int? BitFieldSize);

    private sealed record SlnProjectEntry(
        string Name,
        string Guid);
}
