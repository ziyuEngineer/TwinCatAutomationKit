using System.Text.Json;
using System.Text.Json.Serialization;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

public sealed record GuidedBuildOptions(
    string OutputRoot,
    string SolutionName = "GuidedTwinCat",
    string ProjectName = "GuidedTwinCat",
    string CppProjectName = "CppBase",
    string PlcProjectName = "PlcBase",
    int CppInstanceCount = 3,
    int PlcInstanceCount = 3,
    bool Visible = false,
    int StartupDelayMs = 8000,
    bool BuildSolution = true,
    bool ActivateAfterBuild = false,
    bool AutoContinue = false);

public sealed record GuidedBuildPlanStep(
    string StepId,
    string Kind,
    string MethodName,
    string Title,
    string RequestPreviewJson);

public static class GuidedBuildPlan
{
    public static IReadOnlyList<GuidedBuildPlanStep> Create(GuidedBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuidedBuildCommand.CreatePlan(options);
    }
}

internal static partial class GuidedBuildCommand
{
    internal static IReadOnlyList<GuidedBuildPlanStep> CreatePlan(GuidedBuildOptions options) =>
        GuidedBuildDefinitions.Create(options)
            .Select(definition => definition.PlanStep)
            .ToArray();

    public static int ShowPlan(string[] args)
    {
        GuidedBuildOptions options = ParseOptions(args);
        IReadOnlyList<GuidedBuildPlanStep> plan = CreatePlan(options);

        Console.WriteLine("Guided Build Plan");
        Console.WriteLine($"OutputRoot: {Path.GetFullPath(options.OutputRoot)}");
        Console.WriteLine($"SolutionName: {options.SolutionName}");
        Console.WriteLine($"ProjectName: {options.ProjectName}");
        Console.WriteLine();

        for (int index = 0; index < plan.Count; index++)
        {
            GuidedBuildPlanStep step = plan[index];
            Console.WriteLine($"{index + 1}. {step.Title}");
            Console.WriteLine($"   Kind: {step.Kind}");
            Console.WriteLine($"   Interface: {step.MethodName}");
            Console.WriteLine($"   Inputs: {step.RequestPreviewJson}");
        }

        return 0;
    }

    public static int Run(string[] args)
    {
        GuidedBuildOptions options = ParseOptions(args);
        IReadOnlyList<GuidedBuildDefinition> definitions = GuidedBuildDefinitions.Create(options);
        GuidedBuildRuntimeState state = new(options, RepositoryRootLocator.FindRoot());

        try
        {
            EnsureOutputLocationIsReady(state);
            Directory.CreateDirectory(state.EvidenceRoot);
            SaveReport(state, succeeded: false, abortedByUser: false, failureMessage: null);

            for (int index = 0; index < definitions.Count; index++)
            {
                GuidedBuildDefinition definition = definitions[index];
                if (!options.AutoContinue && !state.AutoContinueRemainder)
                {
                    PromptAction action = PromptBeforeStep(index + 1, definitions.Count, definition.PlanStep.Title);
                    if (action == PromptAction.Quit)
                    {
                        Console.WriteLine("已停止，未继续后续步骤。");
                        SaveReport(state, succeeded: false, abortedByUser: true, failureMessage: null);
                        return 0;
                    }

                    if (action == PromptAction.RunAll)
                    {
                        state.AutoContinueRemainder = true;
                    }
                }

                object? runtimeRequest = definition.RuntimeRequestFactory(state);
                PrintStepHeader(index + 1, definitions.Count, definition.PlanStep, runtimeRequest);

                DateTimeOffset startedAt = DateTimeOffset.UtcNow;
                try
                {
                    StepExecutionOutcome outcome = definition.Execute(state);
                    DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
                    PrintStepResult(outcome, finishedAt - startedAt);
                    state.ReportSteps.Add(CreateReportStep(definition.PlanStep, runtimeRequest, outcome, startedAt, finishedAt));
                    SaveReport(state, succeeded: false, abortedByUser: false, failureMessage: null);
                }
                catch (Exception ex)
                {
                    DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
                    Console.Error.WriteLine($"步骤失败: {ex.Message}");
                    state.ReportSteps.Add(CreateFailureReportStep(definition.PlanStep, runtimeRequest, ex, startedAt, finishedAt));
                    SaveReport(state, succeeded: false, abortedByUser: false, failureMessage: ex.ToString());
                    return 2;
                }
            }

            SaveReport(state, succeeded: true, abortedByUser: false, failureMessage: null);
            Console.WriteLine();
            Console.WriteLine($"GUIDED_BUILD_OK solution={state.SolutionPath ?? state.ExpectedSolutionPath}");
            Console.WriteLine($"GUIDED_BUILD_PROJECT {state.ProjectPath ?? state.ExpectedProjectPath}");
            Console.WriteLine($"GUIDED_BUILD_REPORT {state.ReportPath}");
            return 0;
        }
        finally
        {
            CloseSessionIfNeeded(state, saveBeforeClose: true);
        }
    }

    private static GuidedBuildOptions ParseOptions(string[] args)
    {
        Dictionary<string, string> options = CliOptionParser.Parse(args);
        string repoRoot = RepositoryRootLocator.FindRoot();
        string solutionName = options.TryGetValue("solution-name", out string? rawSolutionName) && !string.IsNullOrWhiteSpace(rawSolutionName)
            ? rawSolutionName
            : "GuidedTwinCat";
        string outputRoot = options.TryGetValue("output", out string? rawOutputRoot) && !string.IsNullOrWhiteSpace(rawOutputRoot)
            ? Path.GetFullPath(rawOutputRoot)
            : Path.Combine(repoRoot, "guided_runs", solutionName);

        GuidedBuildOptions parsed = new(
            OutputRoot: outputRoot,
            SolutionName: solutionName,
            ProjectName: GetOption(options, "project-name", solutionName),
            CppProjectName: GetOption(options, "cpp-project-name", "CppBase"),
            PlcProjectName: GetOption(options, "plc-project-name", "PlcBase"),
            CppInstanceCount: GetIntOption(options, "cpp-instance-count", 3),
            PlcInstanceCount: GetIntOption(options, "plc-instance-count", 3),
            Visible: GetBoolOption(options, "visible", false),
            StartupDelayMs: GetIntOption(options, "startup-delay-ms", 8000),
            BuildSolution: GetBoolOption(options, "build", true),
            ActivateAfterBuild: GetBoolOption(options, "activate", false),
            AutoContinue: options.ContainsKey("auto"));

        ValidateOptions(parsed);
        return parsed;
    }

    private static void ValidateOptions(GuidedBuildOptions options)
    {
        if (options.CppInstanceCount <= 0)
        {
            throw new InvalidOperationException("--cpp-instance-count must be greater than zero.");
        }

        if (options.PlcInstanceCount <= 0)
        {
            throw new InvalidOperationException("--plc-instance-count must be greater than zero.");
        }

        if (options.StartupDelayMs < 0)
        {
            throw new InvalidOperationException("--startup-delay-ms must be zero or greater.");
        }

        if (options.ActivateAfterBuild && !options.BuildSolution)
        {
            throw new InvalidOperationException("--activate=true requires --build=true.");
        }
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, string key, string fallback) =>
        options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static int GetIntOption(IReadOnlyDictionary<string, string> options, string key, int fallback)
    {
        if (!options.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, out int value))
        {
            throw new InvalidOperationException($"--{key} must be an integer. Actual='{raw}'.");
        }

        return value;
    }

    private static bool GetBoolOption(IReadOnlyDictionary<string, string> options, string key, bool fallback)
    {
        if (!options.TryGetValue(key, out string? raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!bool.TryParse(raw, out bool value))
        {
            throw new InvalidOperationException($"--{key} must be true or false. Actual='{raw}'.");
        }

        return value;
    }

    private static void EnsureOutputLocationIsReady(GuidedBuildRuntimeState state)
    {
        Directory.CreateDirectory(state.OutputRoot);

        if (File.Exists(state.ExpectedSolutionPath))
        {
            throw new InvalidOperationException(
                $"输出目录已存在同名 solution。请换一个 --output 或 --solution-name。path={state.ExpectedSolutionPath}");
        }

        if (File.Exists(state.ExpectedProjectPath))
        {
            throw new InvalidOperationException(
                $"输出目录已存在同名 tsproj。请换一个 --output 或 --project-name。path={state.ExpectedProjectPath}");
        }

        if (Directory.Exists(state.ExpectedProjectDirectory) &&
            Directory.EnumerateFileSystemEntries(state.ExpectedProjectDirectory).Any())
        {
            throw new InvalidOperationException(
                $"目标项目目录已存在且非空。为避免覆盖，guided-build 不会自动清理。path={state.ExpectedProjectDirectory}");
        }
    }

    private static PromptAction PromptBeforeStep(int index, int total, string title)
    {
        Console.WriteLine();
        Console.Write($"[{index}/{total}] {title} | 回车执行，a=后续自动执行，q=退出: ");
        string? input = Console.ReadLine();
        if (string.Equals(input, "a", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.RunAll;
        }

        if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
        {
            return PromptAction.Quit;
        }

        return PromptAction.RunStep;
    }

    private static void PrintStepHeader(int index, int total, GuidedBuildPlanStep step, object? runtimeRequest)
    {
        StepContract contract = TwinCatStepCatalog.Require(step.Kind);
        Console.WriteLine();
        Console.WriteLine($"Step {index}/{total}: {step.Title}");
        Console.WriteLine($"  Kind: {step.Kind}");
        Console.WriteLine($"  Interface: {step.MethodName}");
        Console.WriteLine($"  Summary: {contract.Summary}");
        Console.WriteLine($"  Inputs: {SerializeJson(runtimeRequest) ?? step.RequestPreviewJson}");
        Console.WriteLine($"  DeclaredOutputs: {SerializeJson(contract.Outputs)}");
    }

    private static void PrintStepResult(StepExecutionOutcome outcome, TimeSpan elapsed)
    {
        Console.WriteLine($"  Status: {outcome.Status}");
        Console.WriteLine($"  Result: {outcome.Summary}");
        Console.WriteLine($"  DurationMs: {(int)elapsed.TotalMilliseconds}");
        if (outcome.Outputs.Count > 0)
        {
            Console.WriteLine($"  Outputs: {SerializeJson(outcome.Outputs)}");
        }

        if (outcome.Evidence.Count > 0)
        {
            Console.WriteLine($"  Evidence: {SerializeJson(outcome.Evidence)}");
        }
    }

    private static GuidedBuildReportStep CreateReportStep(
        GuidedBuildPlanStep step,
        object? request,
        StepExecutionOutcome outcome,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt) =>
        new()
        {
            StepId = step.StepId,
            Kind = step.Kind,
            MethodName = step.MethodName,
            Title = step.Title,
            RequestJson = SerializeJson(request) ?? step.RequestPreviewJson,
            Status = outcome.Status.ToString(),
            Summary = outcome.Summary,
            Outputs = new Dictionary<string, string?>(outcome.Outputs, StringComparer.OrdinalIgnoreCase),
            Evidence = outcome.Evidence.Select(item => item.Path).ToList(),
            StartedAtUtc = startedAt.UtcDateTime,
            FinishedAtUtc = finishedAt.UtcDateTime
        };

    private static GuidedBuildReportStep CreateFailureReportStep(
        GuidedBuildPlanStep step,
        object? request,
        Exception ex,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt) =>
        new()
        {
            StepId = step.StepId,
            Kind = step.Kind,
            MethodName = step.MethodName,
            Title = step.Title,
            RequestJson = SerializeJson(request) ?? step.RequestPreviewJson,
            Status = StepExecutionStatus.Failed.ToString(),
            Summary = ex.Message,
            Outputs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            Evidence = [],
            StartedAtUtc = startedAt.UtcDateTime,
            FinishedAtUtc = finishedAt.UtcDateTime
        };

    private static void SaveReport(
        GuidedBuildRuntimeState state,
        bool succeeded,
        bool abortedByUser,
        string? failureMessage)
    {
        GuidedBuildReport report = new()
        {
            Options = state.Options,
            OutputRoot = state.OutputRoot,
            EvidenceRoot = state.EvidenceRoot,
            SolutionPath = state.SolutionPath ?? state.ExpectedSolutionPath,
            ProjectPath = state.ProjectPath ?? state.ExpectedProjectPath,
            Succeeded = succeeded,
            AbortedByUser = abortedByUser,
            FailureMessage = failureMessage,
            Steps = state.ReportSteps.ToList()
        };

        string json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(state.ReportPath, json);
    }

    private static void CloseSessionIfNeeded(GuidedBuildRuntimeState state, bool saveBeforeClose)
    {
        if (state.Session is null)
        {
            return;
        }

        try
        {
            state.Engineering.CloseVisualStudio(state.Session, saveBeforeClose);
        }
        catch
        {
        }
        finally
        {
            try
            {
                state.Session.Dispose();
            }
            catch
            {
            }

            state.Session = null;
        }
    }

    private static string GetEvidencePath(GuidedBuildRuntimeState state, string fileName)
    {
        string path = Path.Combine(state.EvidenceRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string? SerializeJson(object? value)
    {
        if (value is null)
        {
            return "{}";
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private enum PromptAction
    {
        RunStep,
        RunAll,
        Quit
    }

    private sealed class GuidedBuildRuntimeState
    {
        public GuidedBuildRuntimeState(GuidedBuildOptions options, string repoRoot)
        {
            Options = options;
            RepoRoot = repoRoot;
            OutputRoot = Path.GetFullPath(options.OutputRoot);
            EvidenceRoot = Path.Combine(OutputRoot, "_guided_build_evidence");
            ExpectedSolutionPath = Path.Combine(OutputRoot, options.SolutionName + ".sln");
            ExpectedProjectDirectory = Path.Combine(OutputRoot, options.ProjectName);
            ExpectedProjectPath = Path.Combine(ExpectedProjectDirectory, options.ProjectName + ".tsproj");
            ReportPath = Path.Combine(EvidenceRoot, "guided-build-report.json");
            TaskMainObjectId = "#x02010010";
            TaskAuxObjectId = TwinCatTsprojMutationService.DeriveNextObjectId(TaskMainObjectId, 0x10);
        }

        public GuidedBuildOptions Options { get; }
        public string RepoRoot { get; }
        public string OutputRoot { get; }
        public string EvidenceRoot { get; }
        public string ExpectedSolutionPath { get; }
        public string ExpectedProjectDirectory { get; }
        public string ExpectedProjectPath { get; }
        public string ReportPath { get; }
        public TwinCatEngineeringService Engineering { get; } = new();
        public TwinCatTsprojMutationService Tsproj { get; } = new();
        public TwinCatEngineeringSession? Session { get; set; }
        public string? SolutionPath { get; set; }
        public string? ProjectPath { get; set; }
        public string? SolutionDirectory { get; set; }
        public string TaskMainObjectId { get; set; }
        public string TaskAuxObjectId { get; set; }
        public bool AutoContinueRemainder { get; set; }
        public List<GuidedBuildReportStep> ReportSteps { get; } = [];
    }

    private sealed class GuidedBuildReport
    {
        public GuidedBuildOptions? Options { get; set; }
        public string OutputRoot { get; set; } = string.Empty;
        public string EvidenceRoot { get; set; } = string.Empty;
        public string SolutionPath { get; set; } = string.Empty;
        public string ProjectPath { get; set; } = string.Empty;
        public bool Succeeded { get; set; }
        public bool AbortedByUser { get; set; }
        public string? FailureMessage { get; set; }
        public List<GuidedBuildReportStep> Steps { get; set; } = [];
    }

    private sealed class GuidedBuildReportStep
    {
        public string StepId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string RequestJson { get; set; } = "{}";
        public string Status { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public Dictionary<string, string?> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Evidence { get; set; } = [];
        public DateTime StartedAtUtc { get; set; }
        public DateTime FinishedAtUtc { get; set; }
    }

    private sealed record GuidedBuildDefinition(
        GuidedBuildPlanStep PlanStep,
        Func<GuidedBuildRuntimeState, object?> RuntimeRequestFactory,
        Func<GuidedBuildRuntimeState, StepExecutionOutcome> Execute);

    private static TwinCatEngineeringSession RequireSession(GuidedBuildRuntimeState state) =>
        state.Session ?? throw new InvalidOperationException("当前步骤需要一个活动中的 TwinCatEngineeringSession。");

    private static string RequireSolutionPath(GuidedBuildRuntimeState state) =>
        state.SolutionPath ?? state.ExpectedSolutionPath;

    private static string RequireProjectPath(GuidedBuildRuntimeState state) =>
        state.ProjectPath ?? state.ExpectedProjectPath;

    private static IReadOnlyDictionary<string, string?> CreateOutputs(params (string Name, string? Value)[] values)
    {
        Dictionary<string, string?> outputs = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string? value) in values)
        {
            outputs[name] = value;
        }

        return outputs;
    }
}
