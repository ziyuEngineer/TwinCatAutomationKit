using System.Text.Json;
using System.Text.RegularExpressions;
using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.Cli;

public static class JsonPlanCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Regex TokenPattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    public static int Run(string[] args)
    {
        Dictionary<string, string> options = CliOptionParser.Parse(args);
        string planPath = Path.GetFullPath(CliOptionParser.RequireOption(options, "file", "plan"));
        bool dryRun = CliOptionParser.GetBoolOption(options, "dry-run", false);
        bool stopOnFailure = CliOptionParser.GetBoolOption(options, "stop-on-failure", true);
        string? summaryPath = CliOptionParser.GetOption(options, "summary");

        try
        {
            JsonAutomationPlan plan = LoadPlan(planPath);
            JsonPlanExecutionResult result = Execute(plan, planPath, dryRun, stopOnFailure);

            if (!string.IsNullOrWhiteSpace(summaryPath))
            {
                string resolvedSummaryPath = Path.GetFullPath(summaryPath);
                Directory.CreateDirectory(Path.GetDirectoryName(resolvedSummaryPath)!);
                File.WriteAllText(resolvedSummaryPath, JsonSerializer.Serialize(result, JsonOptions));
                Console.WriteLine($"Summary written: {resolvedSummaryPath}");
            }

            return result.Succeeded ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"run-plan failed for {planPath}");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static JsonAutomationPlan LoadPlan(string planPath)
    {
        if (!File.Exists(planPath))
        {
            throw new InvalidOperationException($"Plan file does not exist: {planPath}");
        }

        JsonAutomationPlan? plan = JsonSerializer.Deserialize<JsonAutomationPlan>(
            File.ReadAllText(planPath),
            JsonOptions);

        if (plan is null)
        {
            throw new InvalidOperationException("Plan JSON was empty or invalid.");
        }

        if (plan.Steps.Count == 0)
        {
            throw new InvalidOperationException("Plan must contain at least one step.");
        }

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonAutomationStep step in plan.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                throw new InvalidOperationException("Each plan step must have a non-empty id.");
            }

            if (string.IsNullOrWhiteSpace(step.Kind))
            {
                throw new InvalidOperationException($"Plan step '{step.Id}' must have a non-empty kind.");
            }

            if (!StepInvocationCatalog.Supports(step.Kind))
            {
                throw new InvalidOperationException($"Plan step '{step.Id}' uses unsupported kind '{step.Kind}'.");
            }

            if (!ids.Add(step.Id))
            {
                throw new InvalidOperationException($"Duplicate plan step id: {step.Id}");
            }
        }

        return plan;
    }

    public static JsonPlanExecutionResult Execute(JsonAutomationPlan plan, string planPath, bool dryRun, bool stopOnFailure)
    {
        string planDirectory = Path.GetDirectoryName(Path.GetFullPath(planPath)) ?? Directory.GetCurrentDirectory();
        Dictionary<string, string> variables = BuildVariables(plan, planDirectory);
        Dictionary<string, JsonPlanStepResult> completedSteps = new(StringComparer.OrdinalIgnoreCase);
        List<JsonPlanStepResult> results = [];
        List<JsonPlanFile> pendingFiles = [.. plan.Files];

        Console.WriteLine($"Plan: {plan.Name ?? Path.GetFileNameWithoutExtension(planPath)}");
        Console.WriteLine($"File: {Path.GetFullPath(planPath)}");
        Console.WriteLine(dryRun ? "Mode: dry-run" : "Mode: execute");

        if (dryRun)
        {
            foreach (JsonPlanFile file in pendingFiles)
            {
                string path = ResolveString(file.Path, variables, completedSteps, allowUnresolvedStepReferences: true);
                Console.WriteLine($"[FILE] {path}");
            }
        }
        else
        {
            WriteReadyFiles(pendingFiles, variables, completedSteps);
        }

        foreach (JsonAutomationStep step in plan.Steps)
        {
            if (!dryRun)
            {
                WriteReadyFiles(pendingFiles, variables, completedSteps);
            }

            bool enabled = ResolveEnabled(step, variables, completedSteps, dryRun);
            Dictionary<string, string> stepOptions = MergeOptions(plan.Defaults, step.Options, variables, completedSteps, dryRun);
            Dictionary<string, string> redactedStepOptions = RedactSensitiveOptions(stepOptions);
            Console.WriteLine();
            Console.WriteLine($"[{results.Count + 1:00}] {step.Id}: {step.Kind}");

            if (!enabled)
            {
                JsonPlanStepResult skipped = JsonPlanStepResult.Skipped(step.Id, step.Kind, "Step disabled by plan.");
                results.Add(skipped);
                completedSteps[step.Id] = skipped;
                Console.WriteLine("Status: Skipped");
                continue;
            }

            if (dryRun)
            {
                Console.WriteLine("Options: " + JsonSerializer.Serialize(redactedStepOptions, JsonOptions));
                JsonPlanStepResult dry = JsonPlanStepResult.DryRun(step.Id, step.Kind, redactedStepOptions);
                results.Add(dry);
                completedSteps[step.Id] = dry;
                continue;
            }

            DateTimeOffset startedAt = DateTimeOffset.Now;
            try
            {
                StepExecutionOutcome outcome = StepInvokeCommand.ExecutePlanStep(step.Kind, stepOptions);
                JsonPlanStepResult result = JsonPlanStepResult.FromOutcome(step.Id, step.Kind, startedAt, DateTimeOffset.Now, redactedStepOptions, outcome);
                results.Add(result);
                completedSteps[step.Id] = result;
                Console.WriteLine($"Status: {outcome.Status}");
                Console.WriteLine($"Result: {outcome.Summary}");
                if (outcome.Outputs.Count > 0)
                {
                    StepInvokeCommand.WriteOutputsForConsole(outcome.Outputs);
                }

                if (outcome.Status == StepExecutionStatus.Failed && stopOnFailure)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                JsonPlanStepResult failed = JsonPlanStepResult.Failed(step.Id, step.Kind, startedAt, DateTimeOffset.Now, redactedStepOptions, ex.Message);
                results.Add(failed);
                completedSteps[step.Id] = failed;
                Console.WriteLine("Status: Failed");
                Console.WriteLine("Result: " + ex.Message);
                if (stopOnFailure)
                {
                    break;
                }
            }
        }

        if (!dryRun)
        {
            WriteReadyFiles(pendingFiles, variables, completedSteps);
            if (pendingFiles.Count > 0)
            {
                string paths = string.Join(", ", pendingFiles.Select(file => file.Path));
                throw new InvalidOperationException($"Some plan payload files still contain unresolved step output references: {paths}");
            }
        }

        bool succeeded = results.All(item =>
            item.Status is nameof(StepExecutionStatus.Succeeded) or nameof(StepExecutionStatus.Skipped) or "DryRun");

        Console.WriteLine();
        Console.WriteLine("Summary");
        Console.WriteLine($"  Steps:     {results.Count}");
        Console.WriteLine($"  Succeeded: {results.Count(item => item.Status == nameof(StepExecutionStatus.Succeeded))}");
        Console.WriteLine($"  Skipped:   {results.Count(item => item.Status == nameof(StepExecutionStatus.Skipped))}");
        Console.WriteLine($"  Failed:    {results.Count(item => item.Status == nameof(StepExecutionStatus.Failed))}");

        return new JsonPlanExecutionResult(plan.Name, dryRun, succeeded, results);
    }

    private static Dictionary<string, string> RedactSensitiveOptions(IReadOnlyDictionary<string, string> options)
    {
        Dictionary<string, string> redacted = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in options)
        {
            redacted[key] = IsSensitiveOptionKey(key) ? "<redacted>" : value;
        }

        return redacted;
    }

    private static bool IsSensitiveOptionKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase);

    private static void WriteReadyFiles(
        List<JsonPlanFile> pendingFiles,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, JsonPlanStepResult> completedSteps)
    {
        foreach (JsonPlanFile file in pendingFiles.ToArray())
        {
            string path = ResolveString(file.Path, variables, completedSteps, allowUnresolvedStepReferences: false);
            string content = ConvertJsonValueToString(file.Content, variables, completedSteps, allowUnresolvedStepReferences: true);
            if (ContainsUnresolvedStepReference(path) || ContainsUnresolvedStepReference(content))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, content);
            pendingFiles.Remove(file);
            Console.WriteLine($"[FILE] {path}");
        }
    }

    private static bool ContainsUnresolvedStepReference(string value) =>
        TokenPattern.Matches(value)
            .Any(match => match.Groups[1].Value.Trim().StartsWith("steps.", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> BuildVariables(JsonAutomationPlan plan, string planDirectory)
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["plan.dir"] = planDirectory,
            ["cwd"] = Directory.GetCurrentDirectory()
        };

        foreach ((string key, JsonElement value) in plan.Variables)
        {
            variables[key] = ConvertJsonValueToString(value, variables, new Dictionary<string, JsonPlanStepResult>(StringComparer.OrdinalIgnoreCase), allowUnresolvedStepReferences: false);
        }

        for (int i = 0; i < 8; i++)
        {
            bool changed = false;
            foreach (string key in variables.Keys.ToArray())
            {
                string resolved = ResolveString(variables[key], variables, new Dictionary<string, JsonPlanStepResult>(StringComparer.OrdinalIgnoreCase), allowUnresolvedStepReferences: false);
                if (!string.Equals(resolved, variables[key], StringComparison.Ordinal))
                {
                    variables[key] = resolved;
                    changed = true;
                }
            }

            if (!changed)
            {
                return variables;
            }
        }

        throw new InvalidOperationException("Variable interpolation did not converge. Check for circular ${...} references.");
    }

    private static bool ResolveEnabled(
        JsonAutomationStep step,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, JsonPlanStepResult> completedSteps,
        bool dryRun)
    {
        if (step.Enabled.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        string value = ConvertJsonValueToString(step.Enabled, variables, completedSteps, dryRun);
        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException($"Step '{step.Id}' has invalid enabled value '{value}'.");
    }

    private static Dictionary<string, string> MergeOptions(
        IReadOnlyDictionary<string, JsonElement> defaults,
        IReadOnlyDictionary<string, JsonElement> options,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, JsonPlanStepResult> completedSteps,
        bool dryRun)
    {
        Dictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, JsonElement value) in defaults)
        {
            merged[key] = ConvertJsonValueToString(value, variables, completedSteps, dryRun);
        }

        foreach ((string key, JsonElement value) in options)
        {
            merged[key] = ConvertJsonValueToString(value, variables, completedSteps, dryRun);
        }

        return merged;
    }

    private static string ConvertJsonValueToString(
        JsonElement value,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, JsonPlanStepResult> completedSteps,
        bool allowUnresolvedStepReferences)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => ResolveString(value.GetString() ?? string.Empty, variables, completedSteps, allowUnresolvedStepReferences),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Object or JsonValueKind.Array => ResolveString(value.GetRawText(), variables, completedSteps, allowUnresolvedStepReferences),
            _ => string.Empty
        };
    }

    private static string ResolveString(
        string value,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, JsonPlanStepResult> completedSteps,
        bool allowUnresolvedStepReferences)
    {
        return TokenPattern.Replace(value, match =>
        {
            string token = match.Groups[1].Value.Trim();
            if (variables.TryGetValue(token, out string? variableValue))
            {
                return variableValue;
            }

            const string stepPrefix = "steps.";
            if (token.StartsWith(stepPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 &&
                    parts[2].Equals("outputs", StringComparison.OrdinalIgnoreCase) &&
                    completedSteps.TryGetValue(parts[1], out JsonPlanStepResult? step) &&
                    step.Outputs.TryGetValue(parts[3], out string? outputValue))
                {
                    return outputValue ?? string.Empty;
                }

                if (allowUnresolvedStepReferences)
                {
                    return match.Value;
                }
            }

            throw new InvalidOperationException($"Unable to resolve token '{match.Value}'.");
        });
    }
}

public sealed record JsonAutomationPlan(
    int SchemaVersion,
    string? Name,
    Dictionary<string, JsonElement> Variables,
    Dictionary<string, JsonElement> Defaults,
    List<JsonPlanFile> Files,
    List<JsonAutomationStep> Steps)
{
    public Dictionary<string, JsonElement> Variables { get; init; } = Variables ?? new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> Defaults { get; init; } = Defaults ?? new(StringComparer.OrdinalIgnoreCase);
    public List<JsonPlanFile> Files { get; init; } = Files ?? [];
    public List<JsonAutomationStep> Steps { get; init; } = Steps ?? [];
}

public sealed record JsonPlanFile(string Path, JsonElement Content);

public sealed record JsonAutomationStep(
    string Id,
    string Kind,
    Dictionary<string, JsonElement> Options,
    JsonElement Enabled)
{
    public Dictionary<string, JsonElement> Options { get; init; } = Options ?? new(StringComparer.OrdinalIgnoreCase);
}

public sealed record JsonPlanExecutionResult(
    string? Name,
    bool DryRun,
    bool Succeeded,
    IReadOnlyList<JsonPlanStepResult> Steps);

public sealed record JsonPlanStepResult(
    string Id,
    string Kind,
    string Status,
    string Summary,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlyDictionary<string, string?> Outputs,
    IReadOnlyList<EvidenceArtifact> Evidence)
{
    public static JsonPlanStepResult DryRun(string id, string kind, IReadOnlyDictionary<string, string> options) =>
        new(id, kind, "DryRun", "Step was resolved but not executed.", null, null, options, EmptyOutputs, []);

    public static JsonPlanStepResult Skipped(string id, string kind, string summary) =>
        new(id, kind, nameof(StepExecutionStatus.Skipped), summary, null, null, EmptyOptions, EmptyOutputs, []);

    public static JsonPlanStepResult Failed(
        string id,
        string kind,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        IReadOnlyDictionary<string, string> options,
        string summary) =>
        new(id, kind, nameof(StepExecutionStatus.Failed), summary, startedAt, finishedAt, options, EmptyOutputs, []);

    public static JsonPlanStepResult FromOutcome(
        string id,
        string kind,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        IReadOnlyDictionary<string, string> options,
        StepExecutionOutcome outcome) =>
        new(id, kind, outcome.Status.ToString(), outcome.Summary, startedAt, finishedAt, options, outcome.Outputs, outcome.Evidence);

    private static readonly IReadOnlyDictionary<string, string> EmptyOptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string?> EmptyOutputs =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
