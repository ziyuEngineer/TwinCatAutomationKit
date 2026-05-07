using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.IntegrationTests;

/// <summary>
/// Detects whether TwinCAT 4026 and VS2022 are available on the current machine.
/// Used by Program.cs to fail early with a clear precondition message on machines
/// that are not set up for TwinCAT development.
/// </summary>
internal static class TwinCatEnvironmentCheck
{
    /// <summary>
    /// Returns true if both VS2022 DTE and the TwinCAT SysManager COM objects
    /// are registerable on this machine.
    /// </summary>
    public static bool IsAvailable(IntegrationTestConfig config)
    {
        return IsDteProgIdRegistered(config.VisualStudioProgId)
            && HasAnyDefaultTwinCatTemplate();
    }

    private static bool IsDteProgIdRegistered(string progId)
    {
        try
        {
            Type? t = Type.GetTypeFromProgID(progId, throwOnError: false);
            return t is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasAnyDefaultTwinCatTemplate()
    {
        return TwinCatPathDefaults.DefaultXaeTemplatePaths.Any(File.Exists);
    }
}
