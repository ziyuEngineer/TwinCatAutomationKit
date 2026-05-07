using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinCatAutomationKit.IntegrationTests;

/// <summary>
/// Machine-specific configuration for integration tests.
/// Loaded from Config/integration-test-config.json at runtime.
/// Copy Config/integration-test-config.example.json, rename it, and fill in your values.
/// </summary>
internal sealed class IntegrationTestConfig
{
    /// <summary>Visual Studio DTE ProgId, e.g. "VisualStudio.DTE.17.0".</summary>
    public string VisualStudioProgId { get; init; } = "VisualStudio.DTE.17.0";

    /// <summary>Local AMS Net Id for the ADS client (only used when ADS tests are enabled).</summary>
    public string AmsNetId { get; init; } = "127.0.0.1.1.1";

    /// <summary>ADS port for the local PLC runtime (typically 851).</summary>
    public int AdsPort { get; init; } = 851;

    /// <summary>Milliseconds to wait after LaunchVisualStudio before issuing COM calls.</summary>
    public int StartupDelayMs { get; init; } = 8000;

    /// <summary>
    /// Reserved for future use. Not consumed by the current integration test suite.
    ///
    /// Background: the default CreateCppProjectRequest uses "TcVersionedDriverWizard", which
    /// has TriggerAddModule=true in its .vsz file. That flag auto-creates a module class inside
    /// the project without a separate CreateModule call, which is the correct TC4026 path.
    /// CreateModule (subtype=1 on a project item) is not supported in TC4026.
    ///
    /// If a future test ever needs to explicitly pass a module class wizard name elsewhere,
    /// set this to the .vsz filename (without extension) from:
    ///   %TWINCAT3DIR%Components\Base\CppTemplate\Class\
    /// </summary>
    public string CppModuleWizardId { get; init; } = "TcModuleCyclicCallerWizard";

    /// <summary>
    /// Root directory under which each test creates its isolated work folder.
    /// Must be a short path — TwinCAT has a ~220-character path length limit.
    /// Default: D:\t
    /// </summary>
    public string WorkRootBase { get; init; } = @"D:\t";

    /// <summary>
    /// Keep generated work directories after the runner exits. Defaults to false so
    /// repeated integration runs do not fill the short-path workspace.
    /// </summary>
    public bool PreserveArtifacts { get; init; }

    /// <summary>Required gate for local activation. Full coverage fails when disabled.</summary>
    public bool EnableActivation { get; init; } = true;

    /// <summary>Required gate for ADS scan/read/read-symbols against the activated runtime.</summary>
    public bool EnableAdsRead { get; init; } = true;

    /// <summary>Optional gate for local OEM certificate signing probes. Default coverage excludes these three certificate operations.</summary>
    public bool EnableSigning { get; init; }

    /// <summary>Optional gate for TcSignTool grant. Default coverage excludes grant/sign/verify without a real OEM signing credential.</summary>
    public bool EnableCertificateGrant { get; init; }

    /// <summary>Certificate path used by optional signing tests.</summary>
    public string? SigningCertificatePath { get; init; }

    /// <summary>Certificate password used by optional signing tests. Do not commit real shared secrets.</summary>
    public string? SigningCertificatePassword { get; init; }

    /// <summary>License name written into the C++ project by signing.set-license tests.</summary>
    public string SigningLicenseName { get; init; } = "integration-test";

    /// <summary>Optional explicit TcSignTool.exe path.</summary>
    public string? SigningToolPath { get; init; }

    /// <summary>Allow TcSignTool verify exit code 2 when it is only the local test-mode certificate warning.</summary>
    public bool AllowTestModeSigningWarning { get; init; } = true;

    /// <summary>Milliseconds to wait after activation before ADS runtime reads.</summary>
    public int RuntimeSettleDelayMs { get; init; } = 5000;

    /// <summary>ADS ports scanned by validation.ads-scan when ADS tests are enabled.</summary>
    public int[] AdsScanPorts { get; init; } = [100, 200, 300, 800, 851, 852, 10000];

    /// <summary>
    /// Symbols read by validation.ads-read-symbols when ADS tests are enabled.
    /// Format: SymbolPath:DataType, for example MAIN.bHeartbeat:Boolean.
    /// </summary>
    public string[] AdsReadSymbols { get; init; } =
    [
        "MAIN.nCycle:UInt32",
        "MAIN.bHeartbeat:Boolean",
        "MAIN.bPipelineOk:Boolean",
        "MAIN.nConfiguredParameter:UInt32",
        "MAIN.nConvertedParameter:UInt32",
        "MAIN.RuntimeTaskOidProbe:UInt32",
        "MAIN.AuxTaskOidProbe:UInt32",
        "MAIN.nParameterChecksum:UInt32",
        "MAIN.bParameterTransformOk:Boolean",
        "MAIN.nMismatchCount:UInt32"
    ];

    [JsonIgnore]
    public bool IsExplicitConfig { get; private set; }

    [JsonIgnore]
    public string? ConfigPath { get; private set; }

    private static IntegrationTestConfig? _cached;

    /// <summary>
    /// Loads the config from Config/integration-test-config.json next to the executable.
    /// Returns default values if the file is not present (allows the runner to reach the
    /// environment check before failing on missing config).
    /// </summary>
    public static IntegrationTestConfig Load()
    {
        if (_cached is not null)
            return _cached;

        string? path = FindSourceConfigPath();
        if (path is null)
            return _cached = new IntegrationTestConfig();

        string json = File.ReadAllText(path);
        _cached = JsonSerializer.Deserialize<IntegrationTestConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new IntegrationTestConfig();
        _cached.IsExplicitConfig = true;
        _cached.ConfigPath = path;

        return _cached;
    }

    private static string? FindSourceConfigPath()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string projectFile = Path.Combine(current.FullName, "TwinCatAutomationKit.IntegrationTests.csproj");
            string configPath = Path.Combine(current.FullName, "Config", "integration-test-config.json");
            if (File.Exists(projectFile))
            {
                return File.Exists(configPath) ? configPath : null;
            }

            current = current.Parent;
        }

        return null;
    }
}
