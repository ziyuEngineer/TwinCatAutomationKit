using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static class AdsReadCommand
{
    public static int Run(string[] args)
    {
        Dictionary<string, string> options = CliOptionParser.Parse(args);
        if (!options.TryGetValue("net-id", out string? netId) ||
            !options.TryGetValue("port", out string? portText) ||
            !options.TryGetValue("symbol", out string? symbol) ||
            !options.TryGetValue("type", out string? typeText) ||
            !int.TryParse(portText, out int port) ||
            !Enum.TryParse(typeText, ignoreCase: true, out AdsReadDataType dataType))
        {
            Console.Error.WriteLine("ads-read requires --net-id=, --port=, --symbol=, and --type=.");
            return 2;
        }

        AdsValidationService ads = new();
        AdsReadResult result = ads.Read(new AdsReadRequest(netId, port, symbol, dataType));
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.ErrorMessage);
            return 3;
        }

        Console.WriteLine(result.Value);
        return 0;
    }
}
