using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatSigningService
{
    public TwinCatSignToolResult Sign(SignTwinCatBinaryRequest request)
    {
        string toolPath = ResolveToolPath(request.ToolPath);
        string certificatePath = ResolveExistingFile(request.CertificatePath, "TwinCAT signing certificate");
        IReadOnlyList<string> targetPaths = ResolveExistingFiles(request.TargetPaths, "TwinCAT binary");

        List<string> arguments = ["sign", "/f", certificatePath];
        string? standardInput = null;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            arguments.Add("/g");
            standardInput = request.Password;
        }

        if (request.Quiet)
        {
            arguments.Add("/q");
        }

        arguments.AddRange(targetPaths);

        return RunTool(toolPath, arguments, targetPaths, standardInput);
    }

    public TwinCatSignToolResult Verify(VerifyTwinCatBinarySignatureRequest request)
    {
        string toolPath = ResolveToolPath(request.ToolPath);
        IReadOnlyList<string> targetPaths = ResolveExistingFiles(request.TargetPaths, "TwinCAT binary");

        List<string> arguments = ["verify"];
        if (request.Quiet && !request.AllowTestModeWarning)
        {
            arguments.Add("/q");
        }

        arguments.AddRange(targetPaths);

        TwinCatSignToolResult result = RunTool(toolPath, arguments, targetPaths, standardInput: null);
        if (!result.Succeeded && IsAcceptedTestModeWarning(request, result))
        {
            return result with { Succeeded = true };
        }

        return result;
    }

    public TwinCatSignToolResult GrantCertificate(GrantTwinCatSigningCertificateRequest request)
    {
        string toolPath = ResolveToolPath(request.ToolPath);
        string certificatePath = ResolveExistingFile(request.CertificatePath, "TwinCAT signing certificate");

        List<string> arguments = ["grant", "/f", certificatePath];
        string? standardInput = null;
        if (request.RemoveGrant)
        {
            arguments.Add("/r");
        }
        else if (!string.IsNullOrWhiteSpace(request.Password))
        {
            arguments.Add("/g");
            standardInput = request.Password;
        }

        if (request.Quiet)
        {
            arguments.Add("/q");
        }

        return RunTool(toolPath, arguments, [certificatePath], standardInput);
    }

    public TwinCatSigningLicenseResult SetLicense(SetTwinCatSigningLicenseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseName))
        {
            throw new InvalidOperationException("TwinCAT signing license name must not be empty.");
        }

        string projectFilePath = ResolveExistingFile(request.ProjectFilePath, "TwinCAT C++ project file");
        XDocument document = XDocument.Load(projectFilePath);
        XElement root = document.Root
            ?? throw new InvalidOperationException($"TwinCAT C++ project XML root is missing: {projectFilePath}");
        XNamespace ns = root.GetDefaultNamespace();

        XElement tcSignGroup = root.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "PropertyGroup" &&
                string.Equals((string?)element.Attribute("Label"), "TcSign", StringComparison.OrdinalIgnoreCase))
            ?? new XElement(ns + "PropertyGroup", new XAttribute("Label", "TcSign"));
        if (tcSignGroup.Parent is null)
        {
            XElement? defaultPropsImport = root.Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "Import" &&
                    string.Equals((string?)element.Attribute("Project"), "$(VCTargetsPath)\\Microsoft.Cpp.Default.props", StringComparison.OrdinalIgnoreCase));
            if (defaultPropsImport is not null)
            {
                defaultPropsImport.AddBeforeSelf(tcSignGroup);
            }
            else
            {
                root.Add(tcSignGroup);
            }
        }

        foreach (XElement duplicate in document.Descendants()
                     .Where(element =>
                         element.Parent is not null &&
                         !ReferenceEquals(element.Parent, tcSignGroup) &&
                         IsTwinCatSigningProperty(element))
                     .ToList())
        {
            duplicate.Remove();
        }

        SetOrCreateElementValue(tcSignGroup, ns + "TcSignTwinCat", request.EnableSigning ? "true" : "false");
        SetOrCreateElementValue(tcSignGroup, ns + "TcSignTwinCatCertName", request.LicenseName);
        if (request.Password is not null)
        {
            SetOrCreateElementValue(tcSignGroup, ns + "TcSignTwinCatCertPW", request.Password);
        }
        else
        {
            foreach (XElement password in tcSignGroup.Elements()
                         .Where(element => element.Name.LocalName == "TcSignTwinCatCertPW")
                         .ToList())
            {
                password.Remove();
            }
        }

        document.Save(projectFilePath);
        return new TwinCatSigningLicenseResult(
            projectFilePath,
            request.LicenseName,
            request.EnableSigning,
            request.Password is not null);
    }

    public static string ResolveToolPath(string? requestedToolPath = null)
    {
        foreach (string candidate in EnumerateToolPathCandidates(requestedToolPath))
        {
            string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException(
            "TcSignTool.exe was not found. Pass --tool-path or install the TwinCAT SDK.",
            TwinCatPathDefaults.DefaultTcSignToolPath);
    }

    private static TwinCatSignToolResult RunTool(
        string toolPath,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> targetPaths,
        string? standardInput)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = toolPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        StringBuilder output = new();

        process.Start();
        if (standardInput is not null)
        {
            process.StandardInput.WriteLine(standardInput);
            process.StandardInput.Close();
        }

        output.Append(process.StandardOutput.ReadToEnd());
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(error))
        {
            if (output.Length > 0)
            {
                output.AppendLine();
            }

            output.Append(error);
        }

        return new TwinCatSignToolResult(
            process.ExitCode == 0,
            process.ExitCode,
            toolPath,
            BuildRedactedCommandLine(toolPath, arguments),
            targetPaths,
            output.ToString().Trim());
    }

    private static IEnumerable<string> EnumerateToolPathCandidates(string? requestedToolPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedToolPath))
        {
            yield return requestedToolPath;
        }

        string? twinCatSdk = Environment.GetEnvironmentVariable("TwinCatSdk");
        if (!string.IsNullOrWhiteSpace(twinCatSdk))
        {
            yield return Path.Combine(twinCatSdk, "Bin", "TcSignTool.exe");
        }

        yield return TwinCatPathDefaults.DefaultTcSignToolPath;
    }

    private static string ResolveExistingFile(string path, string description)
    {
        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"{description} was not found: {fullPath}", fullPath);
        }

        return fullPath;
    }

    private static IReadOnlyList<string> ResolveExistingFiles(IReadOnlyList<string> paths, string description)
    {
        if (paths.Count == 0)
        {
            throw new InvalidOperationException($"At least one {description} path is required.");
        }

        return paths.Select(path => ResolveExistingFile(path, description)).ToList();
    }

    private static bool IsAcceptedTestModeWarning(
        VerifyTwinCatBinarySignatureRequest request,
        TwinCatSignToolResult result) =>
        request.AllowTestModeWarning &&
        result.ExitCode == 2 &&
        result.Output.Contains("has signature", StringComparison.OrdinalIgnoreCase) &&
        result.Output.Contains("not signed by Beckhoff", StringComparison.OrdinalIgnoreCase);

    private static bool IsTwinCatSigningProperty(XElement element) =>
        element.Name.LocalName is "TcSignTwinCat" or "TcSignTwinCatCertName" or "TcSignTwinCatCertPW";

    private static void SetOrCreateElementValue(XElement parent, XName name, string value)
    {
        List<XElement> children = parent.Elements()
            .Where(element => element.Name.LocalName == name.LocalName)
            .ToList();
        XElement? child = children.FirstOrDefault();
        if (child is null)
        {
            parent.Add(new XElement(name, value));
            return;
        }

        child.Value = value;
        foreach (XElement duplicate in children.Skip(1))
        {
            duplicate.Remove();
        }
    }

    private static string BuildRedactedCommandLine(string toolPath, IReadOnlyList<string> arguments) =>
        Quote(toolPath) + " " + string.Join(" ", arguments.Select(Quote));

    private static string Quote(string value) =>
        value.Any(char.IsWhiteSpace)
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
}
