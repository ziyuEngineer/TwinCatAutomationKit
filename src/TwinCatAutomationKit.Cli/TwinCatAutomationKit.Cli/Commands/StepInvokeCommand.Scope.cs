using System.Text.Json;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class StepInvokeCommand
{
    private static StepExecutionOutcome ExecuteScopeEnsureConfiguration(IReadOnlyDictionary<string, string> options)
    {
        EnsureScopeConfigurationRequest request = ParseEnsureScopeConfigurationRequest(options);
        TwinCatScopeConfigurationService service = new();
        ScopeConfigurationResult result = service.EnsureConfiguration(request);
        return StepExecutionOutcome.Success(
            result.Summary,
            CreateOutputs(
                ("configurationFilePath", result.ConfigurationFilePath),
                ("scopeName", result.ScopeName),
                ("adsChannelCount", result.AdsChannelCount.ToString()),
                ("chartChannelCount", result.ChartChannelCount.ToString())));
    }

    private static StepExecutionOutcome ExecuteScopeAssertConfigurationShape(IReadOnlyDictionary<string, string> options)
    {
        AssertScopeConfigurationShapeRequest request = ParseAssertScopeConfigurationShapeRequest(options);
        TwinCatScopeConfigurationService service = new();
        ScopeConfigurationShapeResult result = service.AssertConfigurationShape(request);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("configurationFilePath", result.ConfigurationFilePath),
            ("succeeded", result.Succeeded ? "true" : "false"),
            ("scopeName", result.ScopeName),
            ("chartName", result.ChartName),
            ("adsChannelCount", result.AdsChannelCount.ToString()),
            ("chartChannelCount", result.ChartChannelCount.ToString()),
            ("errorsText", string.Join("; ", result.Errors)),
            ("shapeJson", JsonSerializer.Serialize(result, JsonOptions)));

        return result.Succeeded
            ? StepExecutionOutcome.Success(result.Summary, outputs)
            : new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                result.Summary,
                outputs,
                Array.Empty<EvidenceArtifact>());
    }

    private static EnsureScopeConfigurationRequest ParseEnsureScopeConfigurationRequest(IReadOnlyDictionary<string, string> options)
    {
        EnsureScopeConfigurationRequest? request = HasJsonPayload(options)
            ? ReadJsonPayload<EnsureScopeConfigurationRequest>(options)
            : null;

        string configurationFilePath = CliOptionParser.GetOption(options, "configuration-file-path", "scope-file-path", "file-path")
            ?? request?.ConfigurationFilePath
            ?? throw new InvalidOperationException("scope.ensure-configuration requires --configuration-file-path.");
        return (request ?? new EnsureScopeConfigurationRequest(configurationFilePath)) with
        {
            ConfigurationFilePath = configurationFilePath,
            ScopeName = CliOptionParser.GetOption(options, "scope-name", "name") ?? request?.ScopeName ?? "Scope Project",
            MainServer = CliOptionParser.GetOption(options, "main-server") ?? request?.MainServer ?? "127.0.0.1.1.1",
            RecordTime = GetLongOption(options, "record-time", request?.RecordTime ?? 6000000000),
            StopMode = CliOptionParser.GetOption(options, "stop-mode") ?? request?.StopMode ?? "AutoStop",
            ChartName = CliOptionParser.GetOption(options, "chart-name") ?? request?.ChartName ?? "YT Chart",
            ReplaceChannels = CliOptionParser.GetBoolOption(options, "replace-channels", request?.ReplaceChannels ?? false)
        };
    }

    private static AssertScopeConfigurationShapeRequest ParseAssertScopeConfigurationShapeRequest(IReadOnlyDictionary<string, string> options)
    {
        AssertScopeConfigurationShapeRequest? request = HasJsonPayload(options)
            ? ReadJsonPayload<AssertScopeConfigurationShapeRequest>(options)
            : null;

        string configurationFilePath = CliOptionParser.GetOption(options, "configuration-file-path", "scope-file-path", "file-path")
            ?? request?.ConfigurationFilePath
            ?? throw new InvalidOperationException("scope.assert-configuration-shape requires --configuration-file-path.");
        return (request ?? new AssertScopeConfigurationShapeRequest(configurationFilePath)) with
        {
            ConfigurationFilePath = configurationFilePath,
            ExpectedScopeName = CliOptionParser.GetOption(options, "expected-scope-name", "scope-name", "name") ?? request?.ExpectedScopeName,
            ExpectedChartName = CliOptionParser.GetOption(options, "expected-chart-name", "chart-name") ?? request?.ExpectedChartName,
            ExpectedAdsChannelCount = GetNullableIntOption(options, "expected-ads-channel-count", request?.ExpectedAdsChannelCount),
            ExpectedChartChannelCount = GetNullableIntOption(options, "expected-chart-channel-count", request?.ExpectedChartChannelCount)
        };
    }

    private static long GetLongOption(IReadOnlyDictionary<string, string> options, string key, long fallback)
    {
        string? raw = CliOptionParser.GetOption(options, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!long.TryParse(raw, out long value))
        {
            throw new InvalidOperationException($"--{key} must be an integer. Actual='{raw}'.");
        }

        return value;
    }

    private static int? GetNullableIntOption(IReadOnlyDictionary<string, string> options, string key, int? fallback)
    {
        string? raw = CliOptionParser.GetOption(options, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, out int value))
        {
            throw new InvalidOperationException($"--{key} must be an integer. Actual='{raw}'.");
        }

        return value;
    }
}
