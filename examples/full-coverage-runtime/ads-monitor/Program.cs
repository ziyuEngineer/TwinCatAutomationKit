using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

Dictionary<string, string> options = ParseArgs(args);
string netId = Get(options, "net-id", "local");
int port = int.Parse(Get(options, "port", "851"));
int intervalMs = int.Parse(Get(options, "interval-ms", "1000"));
int count = int.Parse(Get(options, "count", "0"));
IReadOnlyList<AdsReadSymbolRequest> symbols = ParseSymbols(Get(
    options,
    "symbols",
    "MAIN.nCycle:UInt32;MAIN.nSeed:UInt32;MAIN.nStage1:UInt32;MAIN.nStage1ChangeCount:UInt32;MAIN.nStage2Seed:UInt32;MAIN.nStage2:UInt32;MAIN.nStage2ChangeCount:UInt32;MAIN.bHeartbeat:Boolean;MAIN.bPipelineOk:Boolean;MAIN.nMismatchCount:UInt32"));

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

AdsValidationService ads = new();
Console.WriteLine($"ADS monitor netId={netId} port={port} intervalMs={intervalMs} count={(count <= 0 ? "until Ctrl+C" : count)}");
Console.WriteLine(string.Join("; ", symbols.Select(symbol => $"{symbol.SymbolPath}:{symbol.DataType}")));

int iteration = 0;
while (!cts.IsCancellationRequested && (count <= 0 || iteration < count))
{
    iteration++;
    AdsReadSymbolsResult result = ads.ReadSymbols(new AdsReadSymbolsRequest(netId, port, symbols, AutoReconnect: true));
    string values = string.Join(
        "; ",
        result.Symbols.Select(symbol =>
            symbol.Succeeded
                ? $"{symbol.SymbolPath}={symbol.Value}"
                : $"{symbol.SymbolPath}=<failed: {symbol.ErrorMessage}>"));

    Console.WriteLine($"{DateTimeOffset.Now:O} #{iteration} {values}");

    try
    {
        await Task.Delay(intervalMs, cts.Token);
    }
    catch (TaskCanceledException)
    {
        break;
    }
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    Dictionary<string, string> parsed = new(StringComparer.OrdinalIgnoreCase);
    foreach (string arg in args)
    {
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        int equalsIndex = arg.IndexOf('=');
        if (equalsIndex < 0)
        {
            parsed[arg[2..]] = "true";
            continue;
        }

        parsed[arg[2..equalsIndex]] = arg[(equalsIndex + 1)..];
    }

    return parsed;
}

static string Get(IReadOnlyDictionary<string, string> options, string name, string defaultValue) =>
    options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : defaultValue;

static IReadOnlyList<AdsReadSymbolRequest> ParseSymbols(string raw)
{
    List<AdsReadSymbolRequest> symbols = [];
    foreach (string entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        int separatorIndex = entry.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == entry.Length - 1)
        {
            throw new InvalidOperationException($"Symbol entry must use SymbolPath:DataType. Actual='{entry}'.");
        }

        string symbolPath = entry[..separatorIndex].Trim();
        string typeText = entry[(separatorIndex + 1)..].Trim();
        if (!Enum.TryParse(typeText, ignoreCase: true, out AdsReadDataType dataType))
        {
            throw new InvalidOperationException($"Unsupported ADS data type '{typeText}' for symbol '{symbolPath}'.");
        }

        symbols.Add(new AdsReadSymbolRequest(symbolPath, dataType));
    }

    if (symbols.Count == 0)
    {
        throw new InvalidOperationException("At least one symbol is required.");
    }

    return symbols;
}
