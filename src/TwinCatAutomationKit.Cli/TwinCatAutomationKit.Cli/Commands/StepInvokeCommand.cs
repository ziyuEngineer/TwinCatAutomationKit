using System.Text.Json;
using System.Text.Json.Serialization;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class StepInvokeCommand
{
    public static int Run(string[] args)
    {
        Dictionary<string, string> options = CliOptionParser.Parse(args);
        string kind = CliOptionParser.RequireOption(options, "kind");
        if (!StepInvocationCatalog.Supports(kind))
        {
            Console.Error.WriteLine($"invoke-step does not support kind '{kind}' yet.");
            Console.WriteLine(CliHelpText.BuildInvokeStepHelp());
            return 2;
        }

        StepContract contract = TwinCatStepCatalog.Require(kind);

        try
        {
            StepExecutionOutcome outcome = Execute(kind, options);
            Console.WriteLine($"Kind: {contract.Kind}");
            Console.WriteLine($"Interface: {contract.MethodName}");
            Console.WriteLine($"Summary: {contract.Summary}");
            Console.WriteLine($"Inputs: {BuildRuntimeRequestJson(kind, options)}");
            Console.WriteLine($"Status: {outcome.Status}");
            Console.WriteLine($"Result: {outcome.Summary}");
            WriteOutputsForConsole(
                outcome.Outputs,
                raw: CliOptionParser.GetBoolOption(options, "raw-output", false));
            if (outcome.Evidence.Count > 0)
            {
                Console.WriteLine($"Evidence: {JsonSerializer.Serialize(outcome.Evidence, JsonOptions)}");
            }

            return outcome.Status == StepExecutionStatus.Failed ? 3 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"invoke-step failed for {kind}");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static StepExecutionOutcome ExecutePlanStep(string kind, IReadOnlyDictionary<string, string> options) =>
        Execute(kind, options);

    internal static void WriteOutputsForConsole(IReadOnlyDictionary<string, string?> outputs, bool raw = false)
    {
        if (outputs.Count == 0)
        {
            return;
        }

        if (raw)
        {
            Console.WriteLine($"Outputs: {JsonSerializer.Serialize(outputs, JsonOptions)}");
            return;
        }

        Console.WriteLine("Outputs:");
        foreach ((string key, string? value) in outputs)
        {
            if (TryWriteStructuredConsoleOutput(key, value))
            {
                continue;
            }

            Console.WriteLine($"  {key}: {FormatConsoleOutputValue(key, value)}");
        }
    }

    private static bool TryWriteStructuredConsoleOutput(string key, string? value)
    {
        if (!key.Equals("valuesText", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] items = value.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length <= 1)
        {
            return false;
        }

        Console.WriteLine($"  {key}:");
        foreach (string item in items)
        {
            int separator = item.IndexOf('=');
            if (separator <= 0)
            {
                Console.WriteLine($"    {item}");
                continue;
            }

            string name = item[..separator].Trim();
            string itemValue = item[(separator + 1)..].Trim();
            Console.WriteLine($"    {name} = {itemValue}");
        }

        return true;
    }

    private static string FormatConsoleOutputValue(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (key.EndsWith("Json", StringComparison.OrdinalIgnoreCase) && value.Length > 240)
        {
            return $"<json omitted from console; {value.Length} chars, use --raw-output=true or plan --summary for full value>";
        }

        string singleLine = value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        const int maxLength = 500;
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..maxLength] + $"... <truncated, {singleLine.Length} chars>";
    }

    private static StepExecutionOutcome Execute(string kind, IReadOnlyDictionary<string, string> options) =>
        kind.ToLowerInvariant() switch
        {
            "engineering.launch-visual-studio" => ExecuteEngineeringLaunchVisualStudio(options),
            "engineering.create-xae-solution" => ExecuteEngineeringCreateXaeSolution(options),
            "engineering.open-xae-solution" => ExecuteEngineeringOpenXaeSolution(options),
            "engineering.create-cpp-project" => ExecuteEngineeringCreateCppProject(options),
            "engineering.create-vs-cpp-project" => ExecuteEngineeringCreateVsCppProject(options),
            "engineering.ensure-solution-project-dependency" => ExecuteEngineeringEnsureSolutionProjectDependency(options),
            "engineering.create-plc-project" => ExecuteEngineeringCreatePlcProject(options),
            "engineering.create-module" => ExecuteEngineeringCreateModule(options),
            "engineering.publish-modules" => ExecuteEngineeringPublishModules(options),
            "engineering.add-module-instance" => ExecuteEngineeringAddModuleInstance(options),
            "engineering.ensure-task" => ExecuteEngineeringEnsureTask(options),
            "engineering.export-tree-item-xml" => ExecuteEngineeringExportTreeItemXml(options),
            "engineering.save-all" => ExecuteEngineeringSaveAll(options),
            "engineering.close-visual-studio" => ExecuteEngineeringCloseVisualStudio(options),
            "engineering.build-solution" => ExecuteEngineeringBuildSolution(options),
            "cpp.create-project-item" => ExecuteCppCreateProjectItem(options),
            "cpp.write-project-item-content" => ExecuteCppWriteProjectItemContent(options),
            "cpp.remove-project-item" => ExecuteCppRemoveProjectItem(options),
            "cpp.set-project-property" => ExecuteCppSetProjectProperty(options),
            "cpp.set-item-definition-property" => ExecuteCppSetItemDefinitionProperty(options),
            "cpp.set-project-item-metadata" => ExecuteCppSetProjectItemMetadata(options),
            "signing.grant-certificate" => ExecuteSigningGrantCertificate(options),
            "signing.set-license" => ExecuteSigningSetLicense(options),
            "signing.sign-twincat-binary" => ExecuteSigningSignTwinCatBinary(options),
            "signing.verify-twincat-binary" => ExecuteSigningVerifyTwinCatBinary(options),
            "engineering.activate-configuration" => ExecuteEngineeringActivateConfiguration(options),
            "tsproj.ensure-task" => ExecuteTsprojEnsureTask(options),
            "tsproj.clear-task-layout" => ExecuteTsprojClearTaskLayout(options),
            "tsproj.ensure-task-vars-group" => ExecuteTsprojEnsureTaskVarsGroup(options),
            "tsproj.ensure-task-image" => ExecuteTsprojEnsureTaskImage(options),
            "tsproj.ensure-cpp-instance" => ExecuteTsprojEnsureCppInstance(options),
            "tsproj.ensure-plc-instance" => ExecuteTsprojEnsurePlcInstance(options),
            "tsproj.bind-instance-context" => ExecuteTsprojBindInstanceContext(options),
            "tsproj.bind-instance-task" => ExecuteTsprojBindInstanceTask(options),
            "tsproj.bind-plc-instance-task" => ExecuteTsprojBindPlcInstanceTask(options),
            "tsproj.set-task-affinity" => ExecuteTsprojSetTaskAffinity(options),
            "tsproj.set-plc-project-properties" => ExecuteTsprojSetPlcProjectProperties(options),
            "tsproj.set-plc-instance-metadata" => ExecuteTsprojSetPlcInstanceMetadata(options),
            "tsproj.clear-plc-instance-vars" => ExecuteTsprojClearPlcInstanceVars(options),
            "tsproj.ensure-plc-instance-vars-group" => ExecuteTsprojEnsurePlcInstanceVarsGroup(options),
            "tsproj.clear-plc-init-symbols" => ExecuteTsprojClearPlcInitSymbols(options),
            "tsproj.clear-plc-task-pou-oids" => ExecuteTsprojClearPlcTaskPouOids(options),
            "tsproj.clear-mappings" => ExecuteTsprojClearMappings(options),
            "tsproj.clear-unrestored-var-links" => ExecuteTsprojClearUnrestoredVarLinks(options),
            "tsproj.replace-mappings-section" => ExecuteTsprojReplaceMappingsSection(options),
            "tsproj.replace-project-io-section" => ExecuteTsprojReplaceProjectIoSection(options),
            "tsproj.replace-data-types-section" => ExecuteTsprojReplaceDataTypesSection(options),
            "tsproj.replace-system-settings-section" => ExecuteTsprojReplaceSystemSettingsSection(options),
            "tsproj.clear-instance-parameter-values" => ExecuteTsprojClearInstanceParameterValues(options),
            "tsproj.clear-instance-data-pointer-values" => ExecuteTsprojClearInstanceDataPointerValues(options),
            "tsproj.apply-instance-parameter-plan" => ExecuteTsprojApplyInstanceParameterPlan(options),
            "tsproj.apply-instance-interface-pointer-plan" => ExecuteTsprojApplyInstanceInterfacePointerPlan(options),
            "tsproj.apply-instance-data-pointer-plan" => ExecuteTsprojApplyInstanceDataPointerPlan(options),
            "tsproj.ensure-io-task-image" => ExecuteTsprojEnsureIoTaskImage(options),
            "tsproj.ensure-task-pou-oid" => ExecuteTsprojEnsureTaskPouOid(options),
            "tsproj.ensure-init-symbol" => ExecuteTsprojEnsureInitSymbol(options),
            "tsproj.ensure-parameter" => ExecuteTsprojEnsureParameter(options),
            "tsproj.ensure-interface-pointer" => ExecuteTsprojEnsureInterfacePointer(options),
            "tsproj.ensure-data-pointer" => ExecuteTsprojEnsureDataPointer(options),
            "tsproj.ensure-mapping-link" => ExecuteTsprojEnsureMappingLink(options),
            "tsproj.upsert-element" => ExecuteTsprojUpsertElement(options),
            "tsproj.upsert-fragment" => ExecuteTsprojUpsertFragment(options),
            "tsproj.apply-mutation-plan" => ExecuteTsprojApplyMutationPlan(options),
            "tsproj.merge-fragment" => ExecuteTsprojMergeFragment(options),
            "validation.ads-scan" => ExecuteValidationAdsScan(options),
            "validation.ads-read" => ExecuteValidationAdsRead(options),
            "validation.ads-read-symbols" => ExecuteValidationAdsReadSymbols(options),
            _ => throw new InvalidOperationException($"Unsupported invoke-step kind '{kind}'.")
        };

    private static StepExecutionOutcome ExecuteValidationAdsScan(IReadOnlyDictionary<string, string> options)
    {
        string netId = CliOptionParser.GetOption(options, "net-id") ?? "local";
        IReadOnlyList<int> ports = ParsePorts(CliOptionParser.GetOption(options, "ports"), new[] { 100, 200, 300, 800, 851, 852, 10000 });
        AdsValidationService ads = new();
        AdsPortScanResult result = ads.ScanPorts(new AdsPortScanRequest(netId, ports));
        int succeeded = result.Ports.Count(port => port.Succeeded);
        int failed = result.Ports.Count - succeeded;
        string portsJson = JsonSerializer.Serialize(result.Ports, JsonOptions);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("netId", result.NetId),
            ("ports", string.Join(",", result.Ports.Select(port => port.Port.ToString()))),
            ("openPortCount", succeeded.ToString()),
            ("failedPortCount", failed.ToString()),
            ("portsJson", portsJson));

        if (!result.AnySucceeded)
        {
            return new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                $"ADS scan found no reachable ports for {netId}.",
                outputs,
                Array.Empty<EvidenceArtifact>());
        }

        return StepExecutionOutcome.Success(
            $"ADS scan found {succeeded} reachable port(s) for {netId}.",
            outputs);
    }

    private static StepExecutionOutcome ExecuteValidationAdsRead(IReadOnlyDictionary<string, string> options)
    {
        string netId = CliOptionParser.RequireOption(options, "net-id");
        int port = CliOptionParser.GetIntOption(options, "port", fallback: -1);
        if (port < 0)
        {
            throw new InvalidOperationException("Missing required option: --port");
        }

        string symbolPath = CliOptionParser.RequireOption(options, "symbol", "symbol-path");
        string typeText = CliOptionParser.RequireOption(options, "type");
        if (!Enum.TryParse(typeText, ignoreCase: true, out AdsReadDataType dataType))
        {
            throw new InvalidOperationException($"--type is invalid. Actual='{typeText}'.");
        }

        bool autoReconnect = CliOptionParser.GetBoolOption(options, "auto-reconnect", false);
        AdsValidationService ads = new();
        AdsReadResult result = ads.Read(new AdsReadRequest(netId, port, symbolPath, dataType, autoReconnect));
        if (!result.Succeeded)
        {
            return StepExecutionOutcome.Failed("ADS read failed: " + result.ErrorMessage);
        }

        return StepExecutionOutcome.Success(
            $"ADS read succeeded for {symbolPath}.",
            CreateOutputs(
                ("symbolPath", result.SymbolPath),
                ("value", result.Value),
                ("netId", netId),
                ("port", port.ToString())));
    }

    private static StepExecutionOutcome ExecuteValidationAdsReadSymbols(IReadOnlyDictionary<string, string> options)
    {
        string netId = CliOptionParser.RequireOption(options, "net-id");
        int port = CliOptionParser.GetIntOption(options, "port", fallback: -1);
        if (port < 0)
        {
            throw new InvalidOperationException("Missing required option: --port");
        }

        IReadOnlyList<AdsReadSymbolRequest> symbols = ParseAdsReadSymbols(options);
        bool autoReconnect = CliOptionParser.GetBoolOption(options, "auto-reconnect", false);
        bool continueOnError = CliOptionParser.GetBoolOption(options, "continue-on-error", false);

        AdsValidationService ads = new();
        AdsReadSymbolsResult result = ads.ReadSymbols(new AdsReadSymbolsRequest(
            netId,
            port,
            symbols,
            autoReconnect,
            continueOnError));

        string valuesText = FormatAdsReadValues(result.Symbols);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("netId", result.NetId),
            ("port", result.Port.ToString()),
            ("succeededCount", result.SucceededCount.ToString()),
            ("failedCount", result.FailedCount.ToString()),
            ("valuesText", valuesText),
            ("valuesJson", JsonSerializer.Serialize(result.Symbols, JsonOptions)));

        if (!result.Succeeded && !continueOnError)
        {
            return new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                $"ADS read symbols failed: {valuesText}",
                outputs,
                Array.Empty<EvidenceArtifact>());
        }

        return StepExecutionOutcome.Success(
            $"ADS read symbols completed: {valuesText}",
            outputs);
    }

    private static StepExecutionOutcome RunEngineeringOperation(
        IReadOnlyDictionary<string, string> options,
        Func<TwinCatEngineeringService, TwinCatEngineeringSession, CliWorkspacePaths, StepExecutionOutcome> operation)
    {
        CliWorkspacePaths workspace = ResolveWorkspace(options, requireSolutionPath: true);
        bool visible = CliOptionParser.GetBoolOption(options, "visible", false);
        int startupDelayMs = CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000);
        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(new LaunchVisualStudioRequest(Visible: visible, StartupDelayMs: startupDelayMs));
            engineering.OpenTwinCatSolution(session, new OpenTwinCatSolutionRequest(workspace.SolutionPath!, workspace.ProjectPath));
            StepExecutionOutcome outcome = operation(engineering, session, workspace);
            engineering.SaveAll(session);
            return outcome;
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    engineering.CloseVisualStudio(session, saveBeforeClose: true);
                }
                catch
                {
                }
                finally
                {
                    session.Dispose();
                }
            }
        }
    }

    private static StepExecutionOutcome RunTsprojOperation(
        IReadOnlyDictionary<string, string> options,
        Func<TwinCatTsprojMutationService, string, StepExecutionOutcome> operation)
    {
        CliWorkspacePaths workspace = ResolveWorkspace(options, requireSolutionPath: false);
        TwinCatTsprojMutationService tsproj = new();
        return operation(tsproj, workspace.ProjectPath);
    }

    private static CliWorkspacePaths ResolveWorkspace(
        IReadOnlyDictionary<string, string> options,
        bool requireSolutionPath)
    {
        string? solutionPath = CliOptionParser.GetOption(options, "solution-path");
        string? projectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path");
        string? projectName = CliOptionParser.GetOption(options, "project-name");
        if (requireSolutionPath && string.IsNullOrWhiteSpace(solutionPath))
        {
            throw new InvalidOperationException("This step requires --solution-path.");
        }

        return CliWorkspaceResolver.Resolve(solutionPath, projectPath, projectName);
    }

    private static IReadOnlyList<PlcInstanceVarItem>? ParsePlcInstanceVars(IReadOnlyDictionary<string, string> options)
    {
        string? raw = CliOptionParser.GetOption(options, "variables");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        List<PlcInstanceVarItem> items = [];
        foreach (string entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split(':', StringSplitOptions.None);
            if (parts.Length < 2 || parts.Length > 4)
            {
                throw new InvalidOperationException(
                    "--variables must use 'Name:Type[:BitOffset[:ExternalAddress]]' entries separated by ';'.");
            }

            int? bitOffset = null;
            int? externalAddress = null;
            if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                if (!int.TryParse(parts[2], out int parsedBitOffset))
                {
                    throw new InvalidOperationException($"Variable bit offset is invalid. Actual='{parts[2]}'.");
                }

                bitOffset = parsedBitOffset;
            }

            if (parts.Length == 4 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                if (!int.TryParse(parts[3], out int parsedExternalAddress))
                {
                    throw new InvalidOperationException($"Variable external address is invalid. Actual='{parts[3]}'.");
                }

                externalAddress = parsedExternalAddress;
            }

            items.Add(new PlcInstanceVarItem(parts[0], parts[1], bitOffset, externalAddress));
        }

        return items;
    }

    private static string ReadTextPayload(
        IReadOnlyDictionary<string, string> options,
        string primaryInlineKey,
        string secondaryInlineKey,
        string fileKey)
    {
        string? inline = CliOptionParser.GetOption(options, primaryInlineKey, secondaryInlineKey);
        string? filePath = CliOptionParser.GetOption(options, fileKey);

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            string resolved = Path.GetFullPath(filePath);
            if (!File.Exists(resolved))
            {
                throw new InvalidOperationException($"Payload file does not exist: {resolved}");
            }

            return File.ReadAllText(resolved);
        }

        if (!string.IsNullOrWhiteSpace(inline))
        {
            return inline;
        }

        throw new InvalidOperationException($"Missing payload. Pass --{fileKey}=... or --{primaryInlineKey}=...");
    }

    private static TRequest ReadPlanRequest<TItem, TRequest>(
        IReadOnlyDictionary<string, string> options,
        Func<IReadOnlyList<TItem>, TRequest> requestFactory,
        string inlineKey,
        string fileKey)
        where TRequest : class
    {
        string json = ReadTextPayload(options, inlineKey, inlineKey, fileKey);

        if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            IReadOnlyList<TItem>? directItems = JsonSerializer.Deserialize<List<TItem>>(json, JsonInputOptions);
            if (directItems is not null)
            {
                return requestFactory(directItems);
            }

            throw new InvalidOperationException("Unable to parse structured JSON array payload.");
        }

        TRequest? request = JsonSerializer.Deserialize<TRequest>(json, JsonInputOptions);
        if (request is not null)
        {
            return request;
        }

        IReadOnlyList<TItem>? items = JsonSerializer.Deserialize<List<TItem>>(json, JsonInputOptions);
        if (items is not null)
        {
            return requestFactory(items);
        }

        throw new InvalidOperationException("Unable to parse structured JSON payload.");
    }

    private static TRequest ReadJsonPayload<TRequest>(
        IReadOnlyDictionary<string, string> options,
        string inlineKey = "json",
        string fileKey = "json-file")
        where TRequest : class
    {
        string json = ReadTextPayload(options, inlineKey, inlineKey, fileKey);
        TRequest? request = JsonSerializer.Deserialize<TRequest>(json, JsonInputOptions);
        if (request is null)
        {
            throw new InvalidOperationException("Unable to parse structured JSON payload.");
        }

        return request;
    }

    private static IReadOnlyList<AdsReadSymbolRequest> ParseAdsReadSymbols(IReadOnlyDictionary<string, string> options)
    {
        string? inlineSymbols = CliOptionParser.GetOption(options, "symbols");
        if (!string.IsNullOrWhiteSpace(inlineSymbols))
        {
            return ParseInlineAdsReadSymbols(inlineSymbols);
        }

        if (!string.IsNullOrWhiteSpace(CliOptionParser.GetOption(options, "json-file")) ||
            !string.IsNullOrWhiteSpace(CliOptionParser.GetOption(options, "json")))
        {
            string json = ReadTextPayload(options, "json", "json", "json-file");
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                IReadOnlyList<AdsReadSymbolRequest>? directItems =
                    JsonSerializer.Deserialize<List<AdsReadSymbolRequest>>(json, JsonInputOptions);
                if (directItems is not null && directItems.Count > 0)
                {
                    return directItems;
                }
            }
            else
            {
                AdsReadSymbolsPayload? payload =
                    JsonSerializer.Deserialize<AdsReadSymbolsPayload>(json, JsonInputOptions);
                if (payload?.Symbols is not null && payload.Symbols.Count > 0)
                {
                    return payload.Symbols;
                }
            }
        }

        throw new InvalidOperationException("validation.ads-read-symbols requires --symbols=... or --json-file=...");
    }

    private static IReadOnlyList<AdsReadSymbolRequest> ParseInlineAdsReadSymbols(string raw)
    {
        List<AdsReadSymbolRequest> symbols = [];
        foreach (string entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = entry.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                throw new InvalidOperationException("--symbols entries must use SymbolPath:DataType and be separated by semicolons.");
            }

            string symbolPath = entry[..separatorIndex].Trim();
            string typeText = entry[(separatorIndex + 1)..].Trim();
            if (!Enum.TryParse(typeText, ignoreCase: true, out AdsReadDataType dataType))
            {
                throw new InvalidOperationException($"ADS data type is invalid for symbol '{symbolPath}'. Actual='{typeText}'.");
            }

            symbols.Add(new AdsReadSymbolRequest(symbolPath, dataType));
        }

        if (symbols.Count == 0)
        {
            throw new InvalidOperationException("--symbols must contain at least one SymbolPath:DataType entry.");
        }

        return symbols;
    }

    private static string FormatAdsReadValues(IReadOnlyList<AdsReadSymbolResult> symbols) =>
        string.Join(
            "; ",
            symbols.Select(symbol =>
                symbol.Succeeded
                    ? $"{symbol.SymbolPath}={symbol.Value}"
                    : $"{symbol.SymbolPath}=<failed: {symbol.ErrorMessage}>"));

    private static CliWorkspacePaths? ResolveOptionalWorkspace(IReadOnlyDictionary<string, string> options)
    {
        string? solutionPath = CliOptionParser.GetOption(options, "solution-path");
        string? projectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path");
        string? projectName = CliOptionParser.GetOption(options, "project-name");

        if (string.IsNullOrWhiteSpace(solutionPath) && string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        return CliWorkspaceResolver.Resolve(solutionPath, projectPath, projectName);
    }

    private static string BuildRuntimeRequestJson(string kind, IReadOnlyDictionary<string, string> options)
    {
        object request = kind.ToLowerInvariant() switch
        {
            "engineering.launch-visual-studio" => new
            {
                ProgId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms")
            },
            "engineering.create-xae-solution" => new
            {
                SolutionDirectory = CliOptionParser.GetOption(options, "solution-directory", "output"),
                SolutionName = CliOptionParser.GetOption(options, "solution-name"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms")
            },
            "engineering.open-xae-solution" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms")
            },
            "engineering.create-cpp-project" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                WizardId = CliOptionParser.GetOption(options, "wizard-id") ?? "TcVersionedDriverWizard"
            },
            "engineering.create-vs-cpp-project" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "vs-project-name", "cpp-project-name", "project-name"),
                ProjectDirectory = CliOptionParser.GetOption(options, "project-directory"),
                TemplateKind = CliOptionParser.GetOption(options, "template-kind") ?? "ConsoleApplication",
                CandidateTemplatePaths = CliOptionParser.GetOption(options, "candidate-template-paths", "candidate-template-path"),
                PlatformToolset = CliOptionParser.GetOption(options, "platform-toolset"),
                AllowTemplateFallback = CliOptionParser.GetOption(options, "allow-template-fallback")
            },
            "engineering.ensure-solution-project-dependency" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "dependency-project-name", "project-name"),
                DependsOnProjectName = CliOptionParser.GetOption(options, "depends-on-project-name", "depends-on")
            },
            "engineering.create-plc-project" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                PlcProjectName = CliOptionParser.GetOption(options, "plc-project-name", "project-name"),
                AllowOfflineFallback = CliOptionParser.GetOption(options, "allow-offline-fallback")
            },
            "engineering.create-module" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                ModuleName = CliOptionParser.GetOption(options, "module-name"),
                WizardId = CliOptionParser.GetOption(options, "wizard-id") ?? "TcModuleClassWizard",
            },
            "engineering.publish-modules" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                PostPublishDelayMs = CliOptionParser.GetOption(options, "post-publish-delay-ms"),
                WaitForUpdatedTmcTimeoutMs = CliOptionParser.GetOption(options, "wait-for-updated-tmc-timeout-ms")
            },
            "engineering.add-module-instance" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name"),
                ProjectTmcPath = CliOptionParser.GetOption(options, "project-tmc-path"),
                InstanceBaseName = CliOptionParser.GetOption(options, "instance-base-name"),
                ModuleClassName = CliOptionParser.GetOption(options, "module-class-name"),
            },
            "engineering.build-solution" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                TimeoutMs = CliOptionParser.GetOption(options, "timeout-ms")
            },
            "engineering.save-all" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms")
            },
            "engineering.close-visual-studio" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                SaveBeforeClose = CliOptionParser.GetOption(options, "save-before-close"),
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms")
            },
            "engineering.activate-configuration" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                SaveConfigurationArchive = CliOptionParser.GetOption(options, "save-configuration-archive"),
                ConfigurationArchivePath = CliOptionParser.GetOption(options, "configuration-archive-path"),
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms")
            },
            "cpp.create-project-item" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                RelativePath = CliOptionParser.GetOption(options, "relative-path", "path"),
                ItemType = CliOptionParser.GetOption(options, "item-type"),
                Filter = CliOptionParser.GetOption(options, "filter"),
                AddToProject = CliOptionParser.GetOption(options, "add-to-project"),
                CreatePhysicalFile = CliOptionParser.GetOption(options, "create-physical-file"),
                ConflictPolicy = CliOptionParser.GetOption(options, "conflict-policy"),
                AllowMsBuildFallback = CliOptionParser.GetOption(options, "allow-msbuild-fallback")
            },
            "cpp.write-project-item-content" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                RelativePath = CliOptionParser.GetOption(options, "relative-path", "path"),
                ContentText = CliOptionParser.GetOption(options, "content-text", "content") is null ? null : "<inline content>",
                ContentFile = CliOptionParser.GetOption(options, "content-file"),
                Encoding = CliOptionParser.GetOption(options, "encoding"),
                NewLine = CliOptionParser.GetOption(options, "new-line", "newline"),
                WritePolicy = CliOptionParser.GetOption(options, "write-policy"),
                RequireProjectRegistration = CliOptionParser.GetOption(options, "require-project-registration")
            },
            "cpp.remove-project-item" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                RelativePath = CliOptionParser.GetOption(options, "relative-path", "path"),
                ItemType = CliOptionParser.GetOption(options, "item-type"),
                DeletePhysicalFile = CliOptionParser.GetOption(options, "delete-physical-file"),
                RemoveFilterEntry = CliOptionParser.GetOption(options, "remove-filter-entry"),
                IgnoreMissing = CliOptionParser.GetOption(options, "ignore-missing")
            },
            "cpp.set-project-property" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                PropertyName = CliOptionParser.GetOption(options, "property-name"),
                Value = CliOptionParser.GetOption(options, "value"),
                Condition = CliOptionParser.GetOption(options, "condition"),
                PropertyGroupLabel = CliOptionParser.GetOption(options, "property-group-label")
            },
            "cpp.set-item-definition-property" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                ToolName = CliOptionParser.GetOption(options, "tool-name"),
                PropertyName = CliOptionParser.GetOption(options, "property-name"),
                Value = CliOptionParser.GetOption(options, "value"),
                Condition = CliOptionParser.GetOption(options, "condition")
            },
            "cpp.set-project-item-metadata" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                RelativePath = CliOptionParser.GetOption(options, "relative-path", "path"),
                ItemType = CliOptionParser.GetOption(options, "item-type"),
                MetadataName = CliOptionParser.GetOption(options, "metadata-name"),
                Value = CliOptionParser.GetOption(options, "value"),
                Condition = CliOptionParser.GetOption(options, "condition")
            },
            "signing.grant-certificate" => new
            {
                CertificatePath = CliOptionParser.GetOption(options, "certificate-path", "cert-path"),
                RemoveGrant = CliOptionParser.GetOption(options, "remove-grant"),
                Quiet = CliOptionParser.GetOption(options, "quiet"),
                ToolPath = CliOptionParser.GetOption(options, "tool-path"),
                PasswordSource = DescribeSensitiveOptionSource(options)
            },
            "signing.set-license" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                ProjectFilePath = CliOptionParser.GetOption(options, "cpp-project-file-path", "vcxproj-path"),
                LicenseName = CliOptionParser.GetOption(options, "license-name", "certificate-name", "name"),
                EnableSigning = CliOptionParser.GetOption(options, "enable-signing"),
                PasswordSource = DescribeSensitiveOptionSource(options)
            },
            "signing.sign-twincat-binary" => new
            {
                CertificatePath = CliOptionParser.GetOption(options, "certificate-path", "cert-path"),
                TargetPaths = CliOptionParser.GetOption(options, "target-paths", "file-paths")
                    ?? CliOptionParser.GetOption(options, "target-path", "file-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                Configuration = CliOptionParser.GetOption(options, "configuration"),
                Platform = CliOptionParser.GetOption(options, "platform"),
                Quiet = CliOptionParser.GetOption(options, "quiet"),
                ToolPath = CliOptionParser.GetOption(options, "tool-path"),
                PasswordSource = DescribeSensitiveOptionSource(options)
            },
            "signing.verify-twincat-binary" => new
            {
                TargetPaths = CliOptionParser.GetOption(options, "target-paths", "file-paths")
                    ?? CliOptionParser.GetOption(options, "target-path", "file-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                Configuration = CliOptionParser.GetOption(options, "configuration"),
                Platform = CliOptionParser.GetOption(options, "platform"),
                Quiet = CliOptionParser.GetOption(options, "quiet"),
                ToolPath = CliOptionParser.GetOption(options, "tool-path"),
                AllowTestModeWarning = CliOptionParser.GetOption(options, "allow-test-mode-warning")
            },
            "validation.ads-scan" => new
            {
                NetId = CliOptionParser.GetOption(options, "net-id") ?? "local",
                Ports = CliOptionParser.GetOption(options, "ports") ?? "100,200,300,800,851,852,10000"
            },
            "validation.ads-read" => new
            {
                NetId = CliOptionParser.GetOption(options, "net-id"),
                Port = CliOptionParser.GetOption(options, "port"),
                SymbolPath = CliOptionParser.GetOption(options, "symbol", "symbol-path"),
                Type = CliOptionParser.GetOption(options, "type"),
                AutoReconnect = CliOptionParser.GetOption(options, "auto-reconnect")
            },
            "validation.ads-read-symbols" => new
            {
                NetId = CliOptionParser.GetOption(options, "net-id"),
                Port = CliOptionParser.GetOption(options, "port"),
                Symbols = CliOptionParser.GetOption(options, "symbols")
                    ?? CliOptionParser.GetOption(options, "json-file")
                    ?? CliOptionParser.GetOption(options, "json"),
                AutoReconnect = CliOptionParser.GetOption(options, "auto-reconnect"),
                ContinueOnError = CliOptionParser.GetOption(options, "continue-on-error")
            },
            "tsproj.replace-mappings-section" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                XmlFile = CliOptionParser.GetOption(options, "xml-file"),
                InlineXml = CliOptionParser.GetOption(options, "mappings-xml", "xml")
            },
            "tsproj.replace-project-io-section" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                XmlFile = CliOptionParser.GetOption(options, "xml-file"),
                InlineXml = CliOptionParser.GetOption(options, "io-xml", "xml")
            },
            "tsproj.replace-data-types-section" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                XmlFile = CliOptionParser.GetOption(options, "xml-file"),
                InlineXml = CliOptionParser.GetOption(options, "data-types-xml", "xml"),
                InsertBeforeProject = CliOptionParser.GetOption(options, "insert-before-project")
            },
            "tsproj.replace-system-settings-section" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                XmlFile = CliOptionParser.GetOption(options, "xml-file"),
                InlineXml = CliOptionParser.GetOption(options, "settings-xml", "xml"),
                InsertBeforeTasks = CliOptionParser.GetOption(options, "insert-before-tasks")
            },
            "tsproj.clear-instance-parameter-values" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                InstanceName = CliOptionParser.GetOption(options, "instance-name"),
                RemoveContainerWhenEmpty = CliOptionParser.GetOption(options, "remove-container-when-empty")
            },
            "tsproj.clear-instance-data-pointer-values" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                InstanceName = CliOptionParser.GetOption(options, "instance-name"),
                RemoveContainerWhenEmpty = CliOptionParser.GetOption(options, "remove-container-when-empty")
            },
            "tsproj.clear-unrestored-var-links" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path")
            },
            "tsproj.apply-instance-parameter-plan" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.apply-instance-interface-pointer-plan" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.apply-instance-data-pointer-plan" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.upsert-element" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.upsert-fragment" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.apply-mutation-plan" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.merge-fragment" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ParentElementName = CliOptionParser.GetOption(options, "parent-element-name"),
                XmlFile = CliOptionParser.GetOption(options, "xml-file"),
                InlineXml = CliOptionParser.GetOption(options, "fragment-xml", "xml"),
                MatchElementName = CliOptionParser.GetOption(options, "match-element-name"),
                MatchNameValue = CliOptionParser.GetOption(options, "match-name-value"),
                ReplaceExisting = CliOptionParser.GetOption(options, "replace-existing"),
                FragmentSource = CliOptionParser.GetOption(options, "fragment-source"),
                TargetParentPath = CliOptionParser.GetOption(options, "target-parent-path"),
                FieldMeaning = CliOptionParser.GetOption(options, "field-meaning"),
                VerificationEvidence = CliOptionParser.GetOption(options, "verification-evidence")
            },
            _ => options
        };

        return JsonSerializer.Serialize(request, JsonOptions);
    }

    private static bool? TryGetNullableBool(IReadOnlyDictionary<string, string> options, string key)
    {
        string? raw = CliOptionParser.GetOption(options, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!bool.TryParse(raw, out bool value))
        {
            throw new InvalidOperationException($"--{key} must be true or false. Actual='{raw}'.");
        }

        return value;
    }

    private static int? TryGetNullableInt(IReadOnlyDictionary<string, string> options, string key)
    {
        string? raw = CliOptionParser.GetOption(options, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!int.TryParse(raw, out int value))
        {
            throw new InvalidOperationException($"--{key} must be an integer. Actual='{raw}'.");
        }

        return value;
    }

    private static TEnum ParseEnumOption<TEnum>(
        IReadOnlyDictionary<string, string> options,
        string key,
        TEnum fallback)
        where TEnum : struct, Enum
    {
        string? raw = CliOptionParser.GetOption(options, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (Enum.TryParse(raw, ignoreCase: true, out TEnum parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"--{key} is invalid. Actual='{raw}'. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }

    private static IReadOnlyList<string>? ParseOptionalList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<int> ParsePorts(string? raw, IReadOnlyList<int> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        List<int> ports = new();
        foreach (string item in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(item, out int port) || port <= 0)
            {
                throw new InvalidOperationException($"--ports contains an invalid port. Actual='{item}'.");
            }

            ports.Add(port);
        }

        if (ports.Count == 0)
        {
            throw new InvalidOperationException("--ports must include at least one ADS port.");
        }

        return ports;
    }

    private static IReadOnlyDictionary<string, string?> CreateOutputs(params (string Name, string? Value)[] values)
    {
        Dictionary<string, string?> outputs = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string? value) in values)
        {
            outputs[name] = value;
        }

        return outputs;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions JsonInputOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record AdsReadSymbolsPayload(IReadOnlyList<AdsReadSymbolRequest>? Symbols);
}
