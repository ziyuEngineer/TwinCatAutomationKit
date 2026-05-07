namespace TwinCatAutomationKit.Cli;

public sealed record CliWorkspacePaths(
    string? SolutionPath,
    string ProjectPath);

public static class CliWorkspaceResolver
{
    public static CliWorkspacePaths Resolve(
        string? solutionPath,
        string? projectPath,
        string? projectName = null)
    {
        string? normalizedSolutionPath = string.IsNullOrWhiteSpace(solutionPath)
            ? null
            : Path.GetFullPath(solutionPath);
        string? normalizedProjectPath = string.IsNullOrWhiteSpace(projectPath)
            ? null
            : Path.GetFullPath(projectPath);

        if (!string.IsNullOrWhiteSpace(normalizedProjectPath))
        {
            if (!File.Exists(normalizedProjectPath))
            {
                throw new InvalidOperationException($"Project path does not exist: {normalizedProjectPath}");
            }

            return new CliWorkspacePaths(normalizedSolutionPath, normalizedProjectPath);
        }

        if (string.IsNullOrWhiteSpace(normalizedSolutionPath))
        {
            throw new InvalidOperationException("Workspace resolution requires --project-path/--tsproj-path or --solution-path.");
        }

        if (!File.Exists(normalizedSolutionPath))
        {
            throw new InvalidOperationException($"Solution path does not exist: {normalizedSolutionPath}");
        }

        string solutionDirectory = Path.GetDirectoryName(normalizedSolutionPath)
            ?? throw new InvalidOperationException($"Cannot resolve solution directory from {normalizedSolutionPath}.");
        string[] tsprojCandidates = Directory.GetFiles(solutionDirectory, "*.tsproj", SearchOption.AllDirectories);
        if (tsprojCandidates.Length == 0)
        {
            throw new InvalidOperationException($"No .tsproj files were found under solution directory {solutionDirectory}.");
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            string expectedFileName = projectName + ".tsproj";
            string[] matches = tsprojCandidates
                .Where(path => string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 1)
            {
                return new CliWorkspacePaths(normalizedSolutionPath, matches[0]);
            }

            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple .tsproj files named '{expectedFileName}' were found. Please pass --project-path explicitly.");
            }
        }

        if (tsprojCandidates.Length == 1)
        {
            return new CliWorkspacePaths(normalizedSolutionPath, tsprojCandidates[0]);
        }

        throw new InvalidOperationException(
            $"Multiple .tsproj files were found under {solutionDirectory}. Please pass --project-path or --project-name.");
    }
}
