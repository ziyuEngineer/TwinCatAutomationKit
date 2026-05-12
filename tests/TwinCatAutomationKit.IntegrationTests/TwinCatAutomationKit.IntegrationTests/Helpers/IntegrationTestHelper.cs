using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.IntegrationTests;

/// <summary>
/// Shared helpers for integration test setup and teardown.
///
/// Work directory convention:
///   D:\t\{8-char-guid}  ← must stay short to avoid TwinCAT's ~220-char path limit.
///
/// The main ordered scenario keeps one Visual Studio session and one short-path
/// project alive for later signing/activation/ADS tests, then Program.cs calls
/// OrderedTwinCatScenarioTests.CleanupCachedScenario at the end of the run.
/// </summary>
internal static class IntegrationTestHelper
{
    /// <summary>
    /// Creates and returns a short isolated work directory under config.WorkRootBase.
    /// The path is guaranteed to be ≤ 20 characters beyond the root to stay well within
    /// TwinCAT's path-length limit.
    /// </summary>
    public static string CreateWorkDir(IntegrationTestConfig config)
    {
        string dir = Path.Combine(config.WorkRootBase, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Builds a <see cref="LaunchVisualStudioRequest"/> from the loaded config.
    /// Starts VS hidden — integration tests don't need a visible window.
    /// </summary>
    public static LaunchVisualStudioRequest MakeLaunchRequest(IntegrationTestConfig config) =>
        new(
            config.VisualStudioProgId,
            Visible: false,
            StartupDelayMs: config.StartupDelayMs,
            SuppressUi: true,
            LaunchTimeoutMs: config.DteLaunchTimeoutMs,
            EnableDialogAutoDismiss: true,
            DialogPollIntervalMs: 500);

    /// <summary>
    /// Builds a <see cref="CreateTwinCatSolutionRequest"/> for a test solution
    /// inside the given work directory.
    /// </summary>
    public static CreateTwinCatSolutionRequest MakeSolutionRequest(
        string workDir,
        string solutionName = "TestSolution",
        string projectName = "TestProject") =>
        new(workDir, solutionName, projectName);

    /// <summary>
    /// Returns true if the given .tmc file exists AND contains at least one Module element
    /// with a usable GUID attribute. Tests that need AddModuleInstance should assert this
    /// after CreateCppProject because the engineering service now has a deterministic
    /// fallback that bootstraps module artifacts when the TwinCAT wizard leaves them empty.
    /// </summary>
    public static bool TmcHasAnyModule(string tmcFilePath)
    {
        if (!File.Exists(tmcFilePath))
            return false;

        try
        {
            string content = File.ReadAllText(tmcFilePath);
            return content.Contains("<Module", StringComparison.OrdinalIgnoreCase) &&
                   content.Contains("GUID", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely closes Visual Studio, suppressing any exception so cleanup always runs.
    /// </summary>
    public static void TryCloseVisualStudio(TwinCatEngineeringService engineering, TwinCatEngineeringSession? session)
    {
        if (session is null)
            return;

        try
        {
            engineering.CloseVisualStudio(session);
        }
        catch
        {
            // Best-effort close — a devenv.exe process may survive on the machine
            // but the test result is already recorded.
        }
    }

    /// <summary>
    /// Safely deletes a work directory, suppressing any exception so cleanup always runs.
    /// </summary>
    public static void TryDeleteWorkDir(string? workDir)
    {
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
            return;

        try
        {
            Directory.Delete(workDir, recursive: true);
        }
        catch
        {
            // Best-effort delete.
        }
    }
}
