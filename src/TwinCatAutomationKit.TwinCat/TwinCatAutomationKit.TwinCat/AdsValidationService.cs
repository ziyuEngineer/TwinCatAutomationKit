using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using TwinCatAutomationKit.Abstractions;
using TwinCAT.Ads;

namespace TwinCatAutomationKit.TwinCat;

public sealed class AdsValidationService
{
    private static readonly JsonSerializerOptions EventMarkerJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public AdsPortScanResult ScanPorts(AdsPortScanRequest request)
    {
        List<AdsPortProbeResult> results = new();
        foreach (int port in request.Ports.Distinct().Order())
        {
            results.Add(ScanPort(request.NetId, port));
        }

        return new AdsPortScanResult(request.NetId, results);
    }

    public AssertAdsStateResult AssertStates(AssertAdsStateRequest request)
    {
        if (request.ExpectedPorts.Count == 0)
        {
            throw new ArgumentException("At least one ADS port state assertion must be requested.", nameof(request));
        }

        AdsPortScanResult scan = ScanPorts(new AdsPortScanRequest(
            request.NetId,
            request.ExpectedPorts.Select(port => port.Port).ToArray()));
        Dictionary<int, AdsPortProbeResult> probes = scan.Ports.ToDictionary(port => port.Port);

        List<AdsPortStateAssertion> assertions = [];
        foreach (ExpectedAdsPortState expected in request.ExpectedPorts)
        {
            if (!probes.TryGetValue(expected.Port, out AdsPortProbeResult? probe))
            {
                assertions.Add(new AdsPortStateAssertion(
                    expected.Port,
                    false,
                    expected.AdsState,
                    null,
                    expected.DeviceState,
                    null,
                    "Port was not scanned."));
                continue;
            }

            bool adsStateMatches = string.Equals(probe.AdsState, expected.AdsState, StringComparison.OrdinalIgnoreCase);
            bool deviceStateMatches = expected.DeviceState is null || probe.DeviceState == expected.DeviceState;
            bool succeeded = probe.Succeeded && adsStateMatches && deviceStateMatches;
            string? errorMessage = succeeded
                ? null
                : BuildAdsStateAssertionError(expected, probe, adsStateMatches, deviceStateMatches);

            assertions.Add(new AdsPortStateAssertion(
                expected.Port,
                succeeded,
                expected.AdsState,
                probe.AdsState,
                expected.DeviceState,
                probe.DeviceState,
                errorMessage));
        }

        return new AssertAdsStateResult(request.NetId, assertions);
    }

    public EventLogWindowMarker MarkEventLogWindow(MarkEventLogWindowRequest request)
    {
        ValidateRequiredText(request.LogName, nameof(request.LogName));
        ValidateRequiredText(request.ProviderName, nameof(request.ProviderName));

        int? lastIndex = ReadProviderEntries(request.LogName, request.ProviderName, null, 1)
            .OrderBy(entry => entry.Index)
            .LastOrDefault()
            ?.Index;
        EventLogWindowMarker marker = new(
            request.LogName,
            request.ProviderName,
            DateTimeOffset.Now,
            lastIndex,
            Guid.NewGuid().ToString("N"))
        {
            LastLogEntryIndex = ReadLastLogEntryIndex(request.LogName)
        };

        if (!string.IsNullOrWhiteSpace(request.MarkerFilePath))
        {
            string fullPath = Path.GetFullPath(request.MarkerFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(marker, EventMarkerJsonOptions));
        }

        return marker;
    }

    public AssertEventLogWindowResult AssertEventLogWindow(AssertEventLogWindowRequest request)
    {
        if (request.MaxEvents < 0)
        {
            throw new InvalidOperationException("AssertEventLogWindow MaxEvents must be greater than or equal to zero.");
        }

        if (request.LookbackSeconds < 0)
        {
            throw new InvalidOperationException("AssertEventLogWindow LookbackSeconds must be greater than or equal to zero.");
        }

        EventLogWindowMarker? marker = request.Marker;
        if (marker is null && !string.IsNullOrWhiteSpace(request.MarkerFilePath))
        {
            string fullPath = Path.GetFullPath(request.MarkerFilePath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Event log marker file does not exist: {fullPath}");
            }

            marker = JsonSerializer.Deserialize<EventLogWindowMarker>(File.ReadAllText(fullPath), EventMarkerJsonOptions)
                ?? throw new InvalidOperationException($"Event log marker file was empty or invalid: {fullPath}");
        }

        string logName = marker?.LogName ?? request.LogName;
        string providerName = marker?.ProviderName ?? request.ProviderName;
        ValidateRequiredText(logName, nameof(request.LogName));
        ValidateRequiredText(providerName, nameof(request.ProviderName));

        DateTimeOffset windowStart = marker?.MarkedAt ?? DateTimeOffset.Now.AddSeconds(-request.LookbackSeconds);
        int? afterEntryIndex = marker?.LastEntryIndex;
        List<EventLogEntrySnapshot> observed = ReadProviderEntries(logName, providerName, windowStart, request.MaxEvents == 0 ? int.MaxValue : request.MaxEvents)
            .Where(entry => !afterEntryIndex.HasValue || entry.Index > afterEntryIndex.Value)
            .OrderBy(entry => entry.Index)
            .ToList();

        List<EventLogEntrySnapshot> errorOrCritical = observed
            .Where(entry => IsErrorOrCritical(entry.EntryType))
            .ToList();
        List<EventLogEntrySnapshot> configAdsState = observed
            .Where(entry => entry.Message.Contains("AdsState: >15<", StringComparison.OrdinalIgnoreCase))
            .ToList();
        List<EventLogEntrySnapshot> messageMatches = observed
            .Where(entry => (request.FailMessageContains ?? Array.Empty<string>())
                .Any(pattern => !string.IsNullOrWhiteSpace(pattern) && entry.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        List<string> errors = [];
        if (request.FailOnErrorOrCritical && errorOrCritical.Count > 0)
        {
            errors.Add($"Found {errorOrCritical.Count} Error/Critical event(s).");
        }

        if (request.FailOnConfigAdsState && configAdsState.Count > 0)
        {
            errors.Add($"Found {configAdsState.Count} AdsState >15< Config event(s).");
        }

        if (messageMatches.Count > 0)
        {
            errors.Add($"Found {messageMatches.Count} event(s) matching FailMessageContains.");
        }

        bool succeeded = errors.Count == 0;
        string summary = succeeded
            ? $"Event log window matched: {observed.Count} {providerName} event(s), no forbidden events."
            : $"Event log window failed: {string.Join(" ", errors)}";

        return new AssertEventLogWindowResult(
            succeeded,
            logName,
            providerName,
            windowStart,
            observed.Count,
            errorOrCritical.Count,
            configAdsState.Count,
            observed,
            errors,
            summary);
    }

    public AssertProcessCrashWindowResult AssertProcessCrashWindow(AssertProcessCrashWindowRequest request)
    {
        if (request.MaxEvents < 0)
        {
            throw new InvalidOperationException("AssertProcessCrashWindow MaxEvents must be greater than or equal to zero.");
        }

        if (request.LookbackSeconds < 0)
        {
            throw new InvalidOperationException("AssertProcessCrashWindow LookbackSeconds must be greater than or equal to zero.");
        }

        EventLogWindowMarker? marker = request.Marker;
        if (marker is null && !string.IsNullOrWhiteSpace(request.MarkerFilePath))
        {
            string fullPath = Path.GetFullPath(request.MarkerFilePath);
            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"Event log marker file does not exist: {fullPath}");
            }

            marker = JsonSerializer.Deserialize<EventLogWindowMarker>(File.ReadAllText(fullPath), EventMarkerJsonOptions)
                ?? throw new InvalidOperationException($"Event log marker file was empty or invalid: {fullPath}");
        }

        string logName = marker?.LogName ?? request.LogName;
        ValidateRequiredText(logName, nameof(request.LogName));

        DateTimeOffset windowStart = marker?.MarkedAt ?? DateTimeOffset.Now.AddSeconds(-request.LookbackSeconds);
        int? afterEntryIndex = marker?.LastLogEntryIndex;
        List<string> providerNames = NormalizeList(request.ProviderNames) ??
            ["Application Error", ".NET Runtime", "Windows Error Reporting"];
        List<string> processNames = NormalizeList(request.ProcessNames) ??
            ["devenv.exe", "TcXaeShell.exe"];
        List<string> moduleNames = NormalizeList(request.ModuleNames) ??
            ["TwinCAT System Manager.x64.dll", "TwinCAT System Manager.dll"];
        List<string> messageContains = NormalizeList(request.MessageContains) ?? [];

        List<EventLogEntrySnapshot> observed = ReadLogEntries(logName, windowStart, request.MaxEvents == 0 ? int.MaxValue : request.MaxEvents)
            .Where(entry => !afterEntryIndex.HasValue || entry.Index > afterEntryIndex.Value)
            .OrderBy(entry => entry.Index)
            .ToList();

        List<EventLogEntrySnapshot> matching = observed
            .Where(entry => EventMatchesAny(entry, providerNames, processNames, moduleNames, messageContains))
            .ToList();

        List<string> errors = [];
        if (matching.Count > 0)
        {
            errors.Add($"Found {matching.Count} process crash event(s).");
        }

        bool succeeded = errors.Count == 0;
        string summary = succeeded
            ? $"Process crash window matched: {observed.Count} Application event(s), no matching crash events."
            : $"Process crash window failed: {string.Join(" ", errors)}";

        return new AssertProcessCrashWindowResult(
            succeeded,
            logName,
            windowStart,
            observed.Count,
            matching.Count,
            observed,
            matching,
            errors,
            summary);
    }

    public AdsReadResult Read(AdsReadRequest request)
    {
        AdsReadSymbolsResult batch = ReadSymbols(new AdsReadSymbolsRequest(
            request.NetId,
            request.Port,
            [new AdsReadSymbolRequest(request.SymbolPath, request.DataType)],
            request.AutoReconnect));

        AdsReadSymbolResult first = batch.Symbols[0];
        return new AdsReadResult(first.Succeeded, first.SymbolPath, first.Value, first.ErrorMessage);
    }

    public AdsReadSymbolsResult ReadSymbols(AdsReadSymbolsRequest request)
    {
        if (request.Symbols.Count == 0)
        {
            throw new ArgumentException("At least one ADS symbol must be requested.", nameof(request));
        }

        List<AdsReadSymbolResult> results = [];

        try
        {
            using TcAdsClient client = new();
            client.Synchronize = false;
            Connect(client, request.NetId, request.Port);

            foreach (AdsReadSymbolRequest symbol in request.Symbols)
            {
                try
                {
                    object value = client.ReadSymbolByName(symbol.SymbolPath, ResolveType(symbol.DataType), request.AutoReconnect);
                    results.Add(new AdsReadSymbolResult(
                        true,
                        symbol.SymbolPath,
                        symbol.DataType,
                        Convert.ToString(value, CultureInfo.InvariantCulture),
                        null));
                }
                catch (Exception ex)
                {
                    results.Add(new AdsReadSymbolResult(
                        false,
                        symbol.SymbolPath,
                        symbol.DataType,
                        null,
                        ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            foreach (AdsReadSymbolRequest symbol in request.Symbols)
            {
                results.Add(new AdsReadSymbolResult(
                    false,
                    symbol.SymbolPath,
                    symbol.DataType,
                    null,
                    ex.Message));
            }
        }

        return new AdsReadSymbolsResult(request.NetId, request.Port, results);
    }

    private static Type ResolveType(AdsReadDataType dataType) =>
        dataType switch
        {
            AdsReadDataType.Boolean => typeof(bool),
            AdsReadDataType.Int32 => typeof(int),
            AdsReadDataType.UInt32 => typeof(uint),
            AdsReadDataType.Int64 => typeof(long),
            AdsReadDataType.UInt64 => typeof(ulong),
            AdsReadDataType.Double => typeof(double),
            AdsReadDataType.String => typeof(string),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unsupported ADS data type.")
        };

    private static string BuildAdsStateAssertionError(
        ExpectedAdsPortState expected,
        AdsPortProbeResult probe,
        bool adsStateMatches,
        bool deviceStateMatches)
    {
        if (!probe.Succeeded)
        {
            return probe.ErrorMessage ?? "Port state could not be read.";
        }

        List<string> errors = [];
        if (!adsStateMatches)
        {
            errors.Add($"ADS state expected {expected.AdsState} but was {probe.AdsState ?? "(null)"}.");
        }

        if (!deviceStateMatches)
        {
            errors.Add($"Device state expected {expected.DeviceState} but was {probe.DeviceState?.ToString(CultureInfo.InvariantCulture) ?? "(null)"}.");
        }

        return string.Join(" ", errors);
    }

    private static AdsPortProbeResult ScanPort(string netId, int port)
    {
        try
        {
            using TcAdsClient client = new();
            client.Synchronize = false;
            Connect(client, netId, port);
            StateInfo state = client.ReadState();

            string? deviceName = null;
            string? deviceVersion = null;
            try
            {
                DeviceInfo deviceInfo = client.ReadDeviceInfo();
                deviceName = deviceInfo.Name;
                deviceVersion = deviceInfo.Version.ToString();
            }
            catch
            {
                // Device info is useful context, but a readable ADS state is enough to mark the port reachable.
            }

            return new AdsPortProbeResult(
                port,
                true,
                state.AdsState.ToString(),
                state.DeviceState,
                deviceName,
                deviceVersion,
                null);
        }
        catch (Exception ex)
        {
            return new AdsPortProbeResult(port, false, null, null, null, null, ex.Message);
        }
    }

    private static void Connect(TcAdsClient client, string netId, int port)
    {
        if (string.IsNullOrWhiteSpace(netId) ||
            string.Equals(netId, "local", StringComparison.OrdinalIgnoreCase))
        {
            client.Connect(port);
            return;
        }

        client.Connect(AmsNetId.Parse(netId), port);
    }

    private static List<EventLogEntrySnapshot> ReadProviderEntries(
        string logName,
        string providerName,
        DateTimeOffset? notBefore,
        int maxEvents)
    {
        List<EventLogEntrySnapshot> entries = [];
        if (!EventLog.Exists(logName))
        {
            throw new InvalidOperationException($"Windows event log '{logName}' does not exist.");
        }

        using EventLog log = new(logName);
        for (int i = log.Entries.Count - 1; i >= 0; i--)
        {
            EventLogEntry entry = log.Entries[i];
            if (!string.Equals(entry.Source, providerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTimeOffset generatedAt = new(entry.TimeGenerated);
            if (notBefore.HasValue && generatedAt < notBefore.Value)
            {
                break;
            }

            entries.Add(new EventLogEntrySnapshot(
                entry.Index,
                generatedAt,
                entry.Source,
                entry.EntryType.ToString(),
                entry.InstanceId,
                entry.Message ?? string.Empty));

            if (entries.Count >= maxEvents)
            {
                break;
            }
        }

        entries.Sort((left, right) => left.Index.CompareTo(right.Index));
        return entries;
    }

    private static List<EventLogEntrySnapshot> ReadLogEntries(
        string logName,
        DateTimeOffset? notBefore,
        int maxEvents)
    {
        List<EventLogEntrySnapshot> entries = [];
        if (!EventLog.Exists(logName))
        {
            throw new InvalidOperationException($"Windows event log '{logName}' does not exist.");
        }

        using EventLog log = new(logName);
        for (int i = log.Entries.Count - 1; i >= 0; i--)
        {
            EventLogEntry entry = log.Entries[i];
            DateTimeOffset generatedAt = new(entry.TimeGenerated);
            if (notBefore.HasValue && generatedAt < notBefore.Value)
            {
                break;
            }

            entries.Add(CreateSnapshot(entry, generatedAt));
            if (entries.Count >= maxEvents)
            {
                break;
            }
        }

        entries.Sort((left, right) => left.Index.CompareTo(right.Index));
        return entries;
    }

    private static int? ReadLastLogEntryIndex(string logName)
    {
        if (!EventLog.Exists(logName))
        {
            throw new InvalidOperationException($"Windows event log '{logName}' does not exist.");
        }

        using EventLog log = new(logName);
        return log.Entries.Count == 0
            ? null
            : log.Entries[^1].Index;
    }

    private static EventLogEntrySnapshot CreateSnapshot(EventLogEntry entry, DateTimeOffset generatedAt) =>
        new(
            entry.Index,
            generatedAt,
            entry.Source,
            entry.EntryType.ToString(),
            entry.InstanceId,
            entry.Message ?? string.Empty);

    private static bool EventMatchesAny(
        EventLogEntrySnapshot entry,
        IReadOnlyList<string> providerNames,
        IReadOnlyList<string> processNames,
        IReadOnlyList<string> moduleNames,
        IReadOnlyList<string> messageContains)
    {
        bool providerMatches = providerNames.Count == 0 || providerNames.Any(provider =>
            string.Equals(entry.Source, provider, StringComparison.OrdinalIgnoreCase));
        if (!providerMatches)
        {
            return false;
        }

        bool processMatches = processNames.Count > 0 && processNames.Any(name =>
            entry.Message.Contains(name, StringComparison.OrdinalIgnoreCase));
        bool moduleMatches = moduleNames.Count > 0 && moduleNames.Any(name =>
            entry.Message.Contains(name, StringComparison.OrdinalIgnoreCase));
        bool messageMatches = messageContains.Count > 0 && messageContains.Any(pattern =>
            entry.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase));

        return processMatches || moduleMatches || messageMatches;
    }

    private static List<string>? NormalizeList(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsErrorOrCritical(string entryType) =>
        string.Equals(entryType, "Error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entryType, "Critical", StringComparison.OrdinalIgnoreCase);

    private static void ValidateRequiredText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} must not be empty.");
        }
    }
}
