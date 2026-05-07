using System.Globalization;
using TwinCatAutomationKit.Abstractions;
using TwinCAT.Ads;

namespace TwinCatAutomationKit.TwinCat;

public sealed class AdsValidationService
{
    public AdsPortScanResult ScanPorts(AdsPortScanRequest request)
    {
        List<AdsPortProbeResult> results = new();
        foreach (int port in request.Ports.Distinct().Order())
        {
            results.Add(ScanPort(request.NetId, port));
        }

        return new AdsPortScanResult(request.NetId, results);
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
}
