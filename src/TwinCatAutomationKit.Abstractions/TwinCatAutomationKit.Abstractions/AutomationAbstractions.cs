using System.Collections.ObjectModel;

namespace TwinCatAutomationKit.Abstractions;

public enum StepExecutionStatus
{
    Succeeded,
    Indirect,
    Failed,
    Skipped
}

public sealed record EvidenceArtifact(string Name, string Path, string Kind);

public sealed record StepParameterContract(
    string Name,
    string Type,
    bool Required,
    string Description,
    string? Example = null);

public sealed record StepOutputContract(
    string Name,
    string Type,
    string Description);

public sealed record StepContract(
    string Kind,
    string MethodName,
    string Category,
    string Summary,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<StepParameterContract> Inputs,
    IReadOnlyList<StepOutputContract> Outputs,
    IReadOnlyList<string> VerificationNotes);

public sealed record StepExecutionOutcome(
    StepExecutionStatus Status,
    string Summary,
    IReadOnlyDictionary<string, string?> Outputs,
    IReadOnlyList<EvidenceArtifact> Evidence)
{
    public static StepExecutionOutcome Success(
        string summary,
        IReadOnlyDictionary<string, string?>? outputs = null,
        IReadOnlyList<EvidenceArtifact>? evidence = null) =>
        new(
            StepExecutionStatus.Succeeded,
            summary,
            outputs ?? EmptyOutputs,
            evidence ?? EmptyEvidence);

    public static StepExecutionOutcome Indirect(
        string summary,
        IReadOnlyDictionary<string, string?>? outputs = null,
        IReadOnlyList<EvidenceArtifact>? evidence = null) =>
        new(
            StepExecutionStatus.Indirect,
            summary,
            outputs ?? EmptyOutputs,
            evidence ?? EmptyEvidence);

    public static StepExecutionOutcome Failed(string summary) =>
        new(
            StepExecutionStatus.Failed,
            summary,
            EmptyOutputs,
            EmptyEvidence);

    public static StepExecutionOutcome Skipped(string summary) =>
        new(
            StepExecutionStatus.Skipped,
            summary,
            EmptyOutputs,
            EmptyEvidence);

    private static readonly IReadOnlyDictionary<string, string?> EmptyOutputs =
        new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

    private static readonly IReadOnlyList<EvidenceArtifact> EmptyEvidence = Array.Empty<EvidenceArtifact>();
}

public sealed record StepExecutionRecord(
    string StepId,
    string DisplayName,
    StepContract Contract,
    StepExecutionStatus Status,
    string Summary,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyDictionary<string, string?> Outputs,
    IReadOnlyList<EvidenceArtifact> Evidence);

public sealed record AutomationRunSummary(
    string RunName,
    string EvidenceRoot,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Succeeded,
    IReadOnlyList<StepExecutionRecord> Steps);

public sealed class AutomationState
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public void Set<T>(string key, T value) => _values[key] = value;

    public T GetRequired<T>(string key)
    {
        if (!_values.TryGetValue(key, out object? raw))
        {
            throw new KeyNotFoundException($"State key '{key}' was not found.");
        }

        if (raw is T typed)
        {
            return typed;
        }

        throw new InvalidCastException(
            $"State key '{key}' contains '{raw?.GetType().FullName ?? "<null>"}', not '{typeof(T).FullName}'.");
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_values.TryGetValue(key, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public IReadOnlyDictionary<string, object?> Snapshot() =>
        new ReadOnlyDictionary<string, object?>(_values);
}

public sealed class AutomationContext
{
    public AutomationContext(string runName, string evidenceRoot, bool dryRun = false, AutomationState? state = null)
    {
        RunName = runName ?? throw new ArgumentNullException(nameof(runName));
        EvidenceRoot = Path.GetFullPath(evidenceRoot ?? throw new ArgumentNullException(nameof(evidenceRoot)));
        DryRun = dryRun;
        State = state ?? new AutomationState();
        Directory.CreateDirectory(EvidenceRoot);
    }

    public string RunName { get; }

    public string EvidenceRoot { get; }

    public bool DryRun { get; }

    public AutomationState State { get; }

    public string GetEvidencePath(string relativePath)
    {
        string fullPath = Path.Combine(EvidenceRoot, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }
}

public interface IAutomationStep
{
    string StepId { get; }

    string DisplayName { get; }

    StepContract Contract { get; }

    Task<StepExecutionOutcome> ExecuteAsync(AutomationContext context, CancellationToken cancellationToken);
}
