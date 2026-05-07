namespace TwinCatAutomationKit.Cli;

internal static class RepositoryRootLocator
{
    public static string FindRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "TwinCatAutomationKit.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate the TwinCatAutomationKit root folder.");
    }
}
