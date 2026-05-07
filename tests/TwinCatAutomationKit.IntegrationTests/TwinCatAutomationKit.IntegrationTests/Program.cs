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
        if (args.Length > 0 &&
            (string.Equals(args[0], "probe-list", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(args[0], "probe-run", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(args[0], "probe-run-all", StringComparison.OrdinalIgnoreCase)))
        {
            return StepProbeRunner.Run(args);
        }

        IntegrationTestConfig config = IntegrationTestConfig.Load();

        IReadOnlyList<IntegrationTestCase> tests =
        [
            .. OrderedTwinCatScenarioTests.All,
        ];

        Console.WriteLine($"Running {tests.Count} integration tests on this machine.");
        Console.WriteLine($"  VS ProgId:     {config.VisualStudioProgId}");
        Console.WriteLine($"  Work root:     {config.WorkRootBase}");
        Console.WriteLine($"  Startup delay: {config.StartupDelayMs} ms");
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

        return failed == 0 ? 0 : 1;
    }
}
