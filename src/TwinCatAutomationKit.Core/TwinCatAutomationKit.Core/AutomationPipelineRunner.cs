using System.Text;
using System.Text.Json;
using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.Core;

public sealed class DelegateAutomationStep : IAutomationStep
{
    private readonly Func<AutomationContext, CancellationToken, Task<StepExecutionOutcome>> _executor;

    public DelegateAutomationStep(
        string stepId,
        string displayName,
        StepContract contract,
        Func<AutomationContext, CancellationToken, Task<StepExecutionOutcome>> executor)
    {
        StepId = stepId;
        DisplayName = displayName;
        Contract = contract;
        _executor = executor;
    }

    public string StepId { get; }

    public string DisplayName { get; }

    public StepContract Contract { get; }

    public Task<StepExecutionOutcome> ExecuteAsync(AutomationContext context, CancellationToken cancellationToken) =>
        _executor(context, cancellationToken);
}

public sealed record AutomationPipelineOptions(bool StopOnFailure = true);

public sealed class AutomationPipelineRunner
{
    public async Task<AutomationRunSummary> RunAsync(
        AutomationContext context,
        IEnumerable<IAutomationStep> steps,
        AutomationPipelineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(steps);

        options ??= new AutomationPipelineOptions();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        List<StepExecutionRecord> records = new();

        foreach (IAutomationStep step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset stepStartedAt = DateTimeOffset.UtcNow;
            StepExecutionOutcome outcome = await step.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            DateTimeOffset stepFinishedAt = DateTimeOffset.UtcNow;

            StepExecutionRecord record = new(
                step.StepId,
                step.DisplayName,
                step.Contract,
                outcome.Status,
                outcome.Summary,
                stepStartedAt,
                stepFinishedAt,
                outcome.Outputs,
                outcome.Evidence);

            records.Add(record);

            if (outcome.Status == StepExecutionStatus.Failed && options.StopOnFailure)
            {
                break;
            }
        }

        DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
        AutomationRunSummary summary = new(
            context.RunName,
            context.EvidenceRoot,
            startedAt,
            finishedAt,
            records.All(record => record.Status != StepExecutionStatus.Failed),
            records);

        WriteArtifacts(context, summary);
        return summary;
    }

    private static void WriteArtifacts(AutomationContext context, AutomationRunSummary summary)
    {
        JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = true
        };

        string jsonPath = context.GetEvidencePath("run-summary.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(summary, jsonOptions), Encoding.UTF8);

        StringBuilder csv = new();
        csv.AppendLine("StepId,DisplayName,Kind,Status,Summary,StartedAtUtc,FinishedAtUtc");
        foreach (StepExecutionRecord record in summary.Steps)
        {
            csv.AppendLine(string.Join(",",
                EscapeCsv(record.StepId),
                EscapeCsv(record.DisplayName),
                EscapeCsv(record.Contract.Kind),
                EscapeCsv(record.Status.ToString()),
                EscapeCsv(record.Summary),
                EscapeCsv(record.StartedAt.ToString("O")),
                EscapeCsv(record.FinishedAt.ToString("O"))));
        }

        File.WriteAllText(context.GetEvidencePath("step-results.csv"), csv.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
