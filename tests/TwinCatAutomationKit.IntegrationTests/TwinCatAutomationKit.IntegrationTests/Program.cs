using System.Diagnostics;
using System.Reflection;
using System.Text;
using TwinCatAutomationKit.IntegrationTests;
using TwinCatAutomationKit.TwinCat;

/// <summary>
/// Integration test runner for TwinCatAutomationKit.
///
/// IMPORTANT: The [STAThread] attribute on Main is required because all DTE/COM calls
/// in TwinCatEngineeringService need a Single-Threaded Apartment. Removing it causes
/// random RPC_E_WRONG_THREAD errors at COM activation or on the first DTE call.
///
/// Real TwinCAT scenarios require these preconditions (missing conditions cause FAIL):
///   - Config/integration-test-config.json populated
///   - TwinCAT 4026 installed
///   - Configured DTE/XAE ProgId registered and launchable or already running
///
/// Run command:
///   dotnet run --project tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunChildWithWatchdog(["--run-tests-child"], "Integration test");
        }

        if (args.Length == 1 &&
            string.Equals(args[0], "--run-tests-child", StringComparison.OrdinalIgnoreCase))
        {
            return RunTests();
        }

        if (args.Length > 1 &&
            string.Equals(args[0], "--run-probe-child", StringComparison.OrdinalIgnoreCase))
        {
            return StepProbeRunner.Run(args.Skip(1).ToArray());
        }

        if (args.Length > 0 &&
            (string.Equals(args[0], "probe-list", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(args[0], "probe-run", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(args[0], "probe-run-all", StringComparison.OrdinalIgnoreCase)))
        {
            return RunChildWithWatchdog(["--run-probe-child", .. args], "Integration step probe");
        }

        if (TryNormalizeProbeAlias(args, out string[]? probeArgs, out string? probeError))
        {
            return RunChildWithWatchdog(["--run-probe-child", .. probeArgs!], "Integration step probe");
        }

        if (!string.IsNullOrWhiteSpace(probeError))
        {
            Console.Error.WriteLine(probeError);
            return 2;
        }

        Console.Error.WriteLine($"Unknown integration test argument '{args[0]}'.");
        Console.Error.WriteLine("Use no arguments for the full suite, 'probe-list', 'probe-run --kind=<step-kind>', or '--probe=<step-kind>'.");
        return 2;
    }

    private static int RunChildWithWatchdog(IReadOnlyList<string> childArgs, string operationName)
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve current integration test executable path.");
        string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (IsDotnetHost(executablePath) && !string.IsNullOrWhiteSpace(assemblyPath))
        {
            startInfo.ArgumentList.Add(assemblyPath);
        }

        foreach (string childArg in childArgs)
        {
            startInfo.ArgumentList.Add(childArg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {operationName} child process.");
        StringBuilder output = new();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            output.AppendLine(eventArgs.Data);
            Console.Out.WriteLine(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            output.AppendLine(eventArgs.Data);
            Console.Error.WriteLine(eventArgs.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(config.RunnerTimeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            Console.Error.WriteLine($"{operationName} child process exceeded {config.RunnerTimeoutMs} ms and was terminated.");
            return 124;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool IsDotnetHost(string executablePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(executablePath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeProbeAlias(string[] args, out string[]? probeArgs, out string? error)
    {
        probeArgs = null;
        error = null;
        string first = args[0];

        if (TryReadInlineProbeKind(first, "--probe=", out string? inlineKind) ||
            TryReadInlineProbeKind(first, "probe=", out inlineKind))
        {
            if (string.IsNullOrWhiteSpace(inlineKind))
            {
                error = "Probe alias requires a non-empty step kind.";
                return false;
            }

            probeArgs = ["probe-run", $"--kind={inlineKind}", .. args.Skip(1)];
            return true;
        }

        if (string.Equals(first, "--probe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(first, "probe", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("--", StringComparison.Ordinal))
            {
                error = "Probe alias requires a step kind, for example '--probe=tsproj.ensure-task'.";
                return false;
            }

            probeArgs = ["probe-run", $"--kind={args[1]}", .. args.Skip(2)];
            return true;
        }

        return false;
    }

    private static bool TryReadInlineProbeKind(string arg, string prefix, out string? kind)
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = arg[prefix.Length..];
            return true;
        }

        kind = null;
        return false;
    }

    private static int RunTests()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();

        IReadOnlyList<IntegrationTestCase> tests =
        [
            .. OrderedTwinCatScenarioTests.All,
        ];

        Console.WriteLine($"Running {tests.Count} integration tests on this machine.");
        Console.WriteLine($"  VS ProgId:     {config.VisualStudioProgId}");
        Console.WriteLine($"  Work root:     {config.WorkRootBase}");
        Console.WriteLine($"  Startup delay: {config.StartupDelayMs} ms");
        Console.WriteLine($"  DTE timeout:   {config.DteLaunchTimeoutMs} ms");
        Console.WriteLine($"  Runner timeout:{config.RunnerTimeoutMs} ms");
        Console.WriteLine($"  Config file:   {(config.IsExplicitConfig ? config.ConfigPath : "missing (real TwinCAT scenarios will FAIL)")}");
        Console.WriteLine($"  Activation:    {config.EnableActivation}");
        Console.WriteLine($"  ADS read:      {config.EnableAdsRead}");
        Console.WriteLine($"  Signing cert:  {(config.EnableSigning ? "enabled locally" : "excluded by default")}");
        Console.WriteLine();

        int failed = 0;
        try
        {
            foreach (IntegrationTestCase test in tests)
            {
                Console.Write($"RUN  {test.Name} ... ");
                try
                {
                    test.Body();
                    Console.WriteLine("PASS");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL");
                    Console.WriteLine($"     {ex.Message}");
                    if (ex.InnerException is not null)
                        Console.WriteLine($"     Inner: {ex.InnerException.Message}");
                }
            }
        }
        finally
        {
            OrderedTwinCatScenarioTests.CleanupCachedScenario();
        }

        Console.WriteLine();
        if (failed == 0)
            Console.WriteLine($"All {tests.Count} integration tests passed.");
        else
            Console.WriteLine($"{failed} of {tests.Count} integration tests FAILED.");

        int exitCode = failed == 0 ? 0 : 1;
        return exitCode;
    }
}
