using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class StepInvokeCommand
{
    private static StepExecutionOutcome ExecuteSigningSetLicense(IReadOnlyDictionary<string, string> options)
    {
        string projectFilePath = ResolveCppProjectFilePath(options);
        string licenseName = CliOptionParser.GetOption(options, "license-name", "certificate-name", "name") ?? "optcnc";
        string? password = ReadSensitiveOption(options);
        bool enableSigning = CliOptionParser.GetBoolOption(options, "enable-signing", true);

        TwinCatSigningService signing = new();
        TwinCatSigningLicenseResult result = signing.SetLicense(
            new SetTwinCatSigningLicenseRequest(projectFilePath, licenseName, password, enableSigning));

        return StepExecutionOutcome.Success(
            $"TwinCAT signing license '{result.LicenseName}' written to C++ project.",
            CreateOutputs(
                ("projectFilePath", result.ProjectFilePath),
                ("licenseName", result.LicenseName),
                ("enableSigning", result.EnableSigning ? "true" : "false"),
                ("passwordWritten", result.PasswordWritten ? "true" : "false")),
            [new EvidenceArtifact("twincat-cpp-project", result.ProjectFilePath, "vcxproj")]);
    }

    private static StepExecutionOutcome ExecuteSigningSignTwinCatBinary(IReadOnlyDictionary<string, string> options)
    {
        string certificatePath = Path.GetFullPath(CliOptionParser.RequireOption(options, "certificate-path", "cert-path"));
        IReadOnlyList<string> targetPaths = ResolveSigningTargetPaths(options);
        string? password = ReadSensitiveOption(options);
        bool quiet = CliOptionParser.GetBoolOption(options, "quiet", true);
        string? toolPath = CliOptionParser.GetOption(options, "tool-path");

        TwinCatSigningService signing = new();
        TwinCatSignToolResult result = signing.Sign(
            new SignTwinCatBinaryRequest(certificatePath, targetPaths, password, quiet, toolPath));

        if (!result.Succeeded)
        {
            return StepExecutionOutcome.Failed(
                $"TwinCAT signing failed. ExitCode={result.ExitCode}. Output={result.Output}");
        }

        return StepExecutionOutcome.Success(
            $"Signed {result.TargetPaths.Count} TwinCAT binary file(s).",
            CreateOutputs(
                ("toolPath", result.ToolPath),
                ("certificatePath", certificatePath),
                ("targetPaths", string.Join(";", result.TargetPaths)),
                ("exitCode", result.ExitCode.ToString()),
                ("commandLine", result.RedactedCommandLine)),
            result.TargetPaths
                .Select(path => new EvidenceArtifact("signed-twincat-binary", path, Path.GetExtension(path).TrimStart('.')))
                .ToList());
    }

    private static StepExecutionOutcome ExecuteSigningVerifyTwinCatBinary(IReadOnlyDictionary<string, string> options)
    {
        IReadOnlyList<string> targetPaths = ResolveSigningTargetPaths(options);
        bool quiet = CliOptionParser.GetBoolOption(options, "quiet", true);
        string? toolPath = CliOptionParser.GetOption(options, "tool-path");
        bool allowTestModeWarning = CliOptionParser.GetBoolOption(options, "allow-test-mode-warning", false);

        TwinCatSigningService signing = new();
        TwinCatSignToolResult result = signing.Verify(
            new VerifyTwinCatBinarySignatureRequest(
                targetPaths,
                quiet,
                toolPath,
                AllowTestModeWarning: allowTestModeWarning));

        if (!result.Succeeded)
        {
            return StepExecutionOutcome.Failed(
                $"TwinCAT signature verification failed. ExitCode={result.ExitCode}. Output={result.Output}");
        }

        bool acceptedTestModeWarning = allowTestModeWarning && result.ExitCode != 0;
        return StepExecutionOutcome.Success(
            acceptedTestModeWarning
                ? $"Verified {result.TargetPaths.Count} signed TwinCAT binary file(s); accepted TcSignTool test-mode certificate warning."
                : $"Verified {result.TargetPaths.Count} signed TwinCAT binary file(s).",
            CreateOutputs(
                ("toolPath", result.ToolPath),
                ("targetPaths", string.Join(";", result.TargetPaths)),
                ("exitCode", result.ExitCode.ToString()),
                ("acceptedTestModeWarning", acceptedTestModeWarning ? "true" : "false"),
                ("commandLine", result.RedactedCommandLine)),
            result.TargetPaths
                .Select(path => new EvidenceArtifact("verified-twincat-binary", path, Path.GetExtension(path).TrimStart('.')))
                .ToList());
    }

    private static StepExecutionOutcome ExecuteSigningGrantCertificate(IReadOnlyDictionary<string, string> options)
    {
        string certificatePath = Path.GetFullPath(CliOptionParser.RequireOption(options, "certificate-path", "cert-path"));
        string? password = ReadSensitiveOption(options);
        bool removeGrant = CliOptionParser.GetBoolOption(options, "remove-grant", false);
        bool quiet = CliOptionParser.GetBoolOption(options, "quiet", true);
        string? toolPath = CliOptionParser.GetOption(options, "tool-path");

        TwinCatSigningService signing = new();
        TwinCatSignToolResult result = signing.GrantCertificate(
            new GrantTwinCatSigningCertificateRequest(certificatePath, password, removeGrant, quiet, toolPath));

        if (!result.Succeeded)
        {
            return StepExecutionOutcome.Failed(
                $"TwinCAT certificate grant failed. ExitCode={result.ExitCode}. Output={result.Output}");
        }

        string action = removeGrant ? "removed" : "granted";
        return StepExecutionOutcome.Success(
            $"TwinCAT signing certificate grant {action}.",
            CreateOutputs(
                ("toolPath", result.ToolPath),
                ("certificatePath", certificatePath),
                ("removeGrant", removeGrant ? "true" : "false"),
                ("exitCode", result.ExitCode.ToString()),
                ("commandLine", result.RedactedCommandLine)),
            [new EvidenceArtifact("twincat-signing-certificate", certificatePath, Path.GetExtension(certificatePath).TrimStart('.'))]);
    }

    internal static IReadOnlyList<string> ResolveSigningTargetPaths(IReadOnlyDictionary<string, string> options)
    {
        string? rawTargets = CliOptionParser.GetOption(options, "target-paths", "file-paths")
            ?? CliOptionParser.GetOption(options, "target-path", "file-path");

        if (!string.IsNullOrWhiteSpace(rawTargets))
        {
            return rawTargets
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .ToList();
        }

        string? projectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path");
        string? cppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name");
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(cppProjectName))
        {
            throw new InvalidOperationException(
                "Signing steps require --target-paths/--file-paths or both --project-path and --cpp-project-name.");
        }

        string configuration = CliOptionParser.GetOption(options, "configuration") ?? "Release";
        string platform = CliOptionParser.GetOption(options, "platform") ?? "TwinCAT OS (x64)";
        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        IReadOnlyList<string> candidates = EnumerateSigningTargetPathCandidates(
                projectDirectory,
                cppProjectName,
                platform,
                configuration)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? existing = candidates.FirstOrDefault(File.Exists);
        if (existing is not null)
        {
            return [existing];
        }

        throw new FileNotFoundException(
            "TwinCAT binary was not found. Checked: " + string.Join("; ", candidates));
    }

    private static IEnumerable<string> EnumerateSigningTargetPathCandidates(
        string projectDirectory,
        string cppProjectName,
        string platform,
        string configuration)
    {
        string productDirectory = Path.Combine(
            projectDirectory,
            cppProjectName,
            "_products",
            platform,
            configuration);

        yield return Path.Combine(productDirectory, cppProjectName + ".tmx");
        yield return Path.Combine(productDirectory, cppProjectName, cppProjectName + ".tmx");

        if (Directory.Exists(productDirectory))
        {
            foreach (string match in Directory.EnumerateFiles(
                         productDirectory,
                         cppProjectName + ".tmx",
                         SearchOption.AllDirectories))
            {
                yield return match;
            }
        }
    }

    private static string ResolveCppProjectFilePath(IReadOnlyDictionary<string, string> options)
    {
        string? explicitPath = CliOptionParser.GetOption(options, "cpp-project-file-path", "vcxproj-path");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        string? projectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path");
        string? cppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name");
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(cppProjectName))
        {
            throw new InvalidOperationException(
                "signing.set-license requires --cpp-project-file-path/--vcxproj-path or both --project-path and --cpp-project-name.");
        }

        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        return Path.Combine(projectDirectory, cppProjectName, cppProjectName + ".vcxproj");
    }

    private static string? ReadSensitiveOption(IReadOnlyDictionary<string, string> options)
    {
        string? password = CliOptionParser.GetOption(options, "password", "certificate-password", "license-password");
        string? passwordFile = CliOptionParser.GetOption(options, "password-file", "certificate-password-file", "license-password-file");
        string? passwordEnvVar = CliOptionParser.GetOption(options, "password-env-var", "certificate-password-env-var", "license-password-env-var");

        int sourceCount = new[] { password, passwordFile, passwordEnvVar }
            .Count(value => !string.IsNullOrWhiteSpace(value));
        if (sourceCount > 1)
        {
            throw new InvalidOperationException(
                "Pass only one password source: --password, --password-file, or --password-env-var.");
        }

        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            string resolved = Path.GetFullPath(passwordFile);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Password file does not exist: {resolved}", resolved);
            }

            return File.ReadAllText(resolved).TrimEnd('\r', '\n');
        }

        if (!string.IsNullOrWhiteSpace(passwordEnvVar))
        {
            string? value = Environment.GetEnvironmentVariable(passwordEnvVar);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Environment variable '{passwordEnvVar}' is empty or not set.");
            }

            return value;
        }

        return password;
    }

    private static string? DescribeSensitiveOptionSource(IReadOnlyDictionary<string, string> options)
    {
        if (!string.IsNullOrWhiteSpace(CliOptionParser.GetOption(options, "password-file", "certificate-password-file", "license-password-file")))
        {
            return "password-file";
        }

        if (!string.IsNullOrWhiteSpace(CliOptionParser.GetOption(options, "password-env-var", "certificate-password-env-var", "license-password-env-var")))
        {
            return "password-env-var";
        }

        if (!string.IsNullOrWhiteSpace(CliOptionParser.GetOption(options, "password", "certificate-password", "license-password")))
        {
            return "inline-redacted";
        }

        return null;
    }
}
