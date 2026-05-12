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

    /// <summary>Maximum wall-clock time for the default integration runner before it kills the child process tree.</summary>
    public int RunnerTimeoutMs { get; init; } = 600000;

    /// <summary>Maximum time to wait for Visual Studio DTE COM activation.</summary>
    public int DteLaunchTimeoutMs { get; init; } = 60000;

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
            return _cached = ApplyEnvironmentOverrides(new IntegrationTestConfig());

        string json = File.ReadAllText(path);
        IntegrationTestConfig loaded = JsonSerializer.Deserialize<IntegrationTestConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new IntegrationTestConfig();
        loaded.IsExplicitConfig = true;
        loaded.ConfigPath = path;
        _cached = ApplyEnvironmentOverrides(loaded);

        return _cached;
    }

    private static IntegrationTestConfig ApplyEnvironmentOverrides(IntegrationTestConfig config)
    {
        IntegrationTestConfig result = new()
        {
            VisualStudioProgId = GetEnvironmentText("TAK_INTEGRATION_VISUAL_STUDIO_PROG_ID") ?? config.VisualStudioProgId,
            AmsNetId = GetEnvironmentText("TAK_INTEGRATION_AMS_NET_ID") ?? config.AmsNetId,
            AdsPort = GetEnvironmentInt("TAK_INTEGRATION_ADS_PORT") ?? config.AdsPort,
            StartupDelayMs = GetEnvironmentInt("TAK_INTEGRATION_STARTUP_DELAY_MS") ?? config.StartupDelayMs,
            RunnerTimeoutMs = GetEnvironmentInt("TAK_INTEGRATION_RUNNER_TIMEOUT_MS") ?? config.RunnerTimeoutMs,
            DteLaunchTimeoutMs = GetEnvironmentInt("TAK_INTEGRATION_DTE_LAUNCH_TIMEOUT_MS") ?? config.DteLaunchTimeoutMs,
            CppModuleWizardId = GetEnvironmentText("TAK_INTEGRATION_CPP_MODULE_WIZARD_ID") ?? config.CppModuleWizardId,
            WorkRootBase = ResolveWritableWorkRootBase(GetEnvironmentText("TAK_INTEGRATION_WORK_ROOT_BASE") ?? config.WorkRootBase),
            PreserveArtifacts = GetEnvironmentBool("TAK_INTEGRATION_PRESERVE_ARTIFACTS") ?? config.PreserveArtifacts,
            EnableActivation = GetEnvironmentBool("TAK_INTEGRATION_ENABLE_ACTIVATION") ?? config.EnableActivation,
            EnableAdsRead = GetEnvironmentBool("TAK_INTEGRATION_ENABLE_ADS_READ") ?? config.EnableAdsRead,
            EnableSigning = GetEnvironmentBool("TAK_INTEGRATION_ENABLE_SIGNING") ?? config.EnableSigning,
            EnableCertificateGrant = GetEnvironmentBool("TAK_INTEGRATION_ENABLE_CERTIFICATE_GRANT") ?? config.EnableCertificateGrant,
            SigningCertificatePath = GetEnvironmentText("TAK_INTEGRATION_SIGNING_CERTIFICATE_PATH") ?? config.SigningCertificatePath,
            SigningCertificatePassword = GetEnvironmentText("TAK_INTEGRATION_SIGNING_CERTIFICATE_PASSWORD") ?? config.SigningCertificatePassword,
            SigningLicenseName = GetEnvironmentText("TAK_INTEGRATION_SIGNING_LICENSE_NAME") ?? config.SigningLicenseName,
            SigningToolPath = GetEnvironmentText("TAK_INTEGRATION_SIGNING_TOOL_PATH") ?? config.SigningToolPath,
            AllowTestModeSigningWarning = GetEnvironmentBool("TAK_INTEGRATION_ALLOW_TEST_MODE_SIGNING_WARNING") ?? config.AllowTestModeSigningWarning,
            RuntimeSettleDelayMs = GetEnvironmentInt("TAK_INTEGRATION_RUNTIME_SETTLE_DELAY_MS") ?? config.RuntimeSettleDelayMs,
            AdsScanPorts = config.AdsScanPorts,
            AdsReadSymbols = config.AdsReadSymbols
        };

        result.IsExplicitConfig = config.IsExplicitConfig;
        result.ConfigPath = config.ConfigPath;
        return result;
    }

    private static string ResolveWritableWorkRootBase(string configuredWorkRoot)
    {
        if (CanCreateDirectory(configuredWorkRoot))
        {
            return configuredWorkRoot;
        }

        string fallback = Path.Combine(FindRepositoryRoot() ?? AppContext.BaseDirectory, "t");
        return CanCreateDirectory(fallback)
            ? fallback
            : configuredWorkRoot;
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TwinCatAutomationKit.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool CanCreateDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string probeDirectory = Path.Combine(directory, ".tak-write-probe-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(probeDirectory);
            string probeFile = Path.Combine(probeDirectory, "probe.tmp");
            File.WriteAllText(probeFile, "probe");
            try
            {
                File.Delete(probeFile);
                Directory.Delete(probeDirectory);
            }
            catch
            {
                // Leftover probe artifacts are harmless; the important check is child-directory write access.
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetEnvironmentText(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? GetEnvironmentInt(string name)
    {
        string? value = GetEnvironmentText(name);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, out int parsed)
            ? parsed
            : throw new InvalidOperationException($"Environment variable {name} must be an integer.");
    }

    private static bool? GetEnvironmentBool(string name)
    {
        string? value = GetEnvironmentText(name);
        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException($"Environment variable {name} must be true or false.");
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
