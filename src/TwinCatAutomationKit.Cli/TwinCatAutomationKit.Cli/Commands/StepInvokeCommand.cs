using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class StepInvokeCommand
{
    private static EngineeringSessionReuseScope? _engineeringSessionReuseScope;

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
        int commandTimeoutMs = CliOptionParser.GetIntOption(options, "command-timeout-ms", 0);

        try
        {
            StepExecutionOutcome outcome = ExecuteWithOptionalTimeout(kind, options, commandTimeoutMs);
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

    internal static StepExecutionOutcome ExecutePlanStep(
        string kind,
        IReadOnlyDictionary<string, string> options,
        int commandTimeoutMs = 0) =>
        ExecuteWithOptionalTimeout(kind, options, commandTimeoutMs);

    internal static IDisposable BeginEngineeringSessionReuse()
    {
        if (_engineeringSessionReuseScope is not null)
        {
            throw new InvalidOperationException("An engineering session reuse scope is already active.");
        }

        _engineeringSessionReuseScope = new EngineeringSessionReuseScope();
        return _engineeringSessionReuseScope;
    }

    internal static bool SupportsEngineeringSessionReuse(string kind)
    {
        string normalizedKind = kind.ToLowerInvariant();
        return normalizedKind is
            "engineering.launch-visual-studio" or
            "engineering.create-xae-solution" or
            "engineering.open-xae-solution" or
            "engineering.create-cpp-project" or
            "engineering.create-vs-cpp-project" or
            "engineering.create-scope-project" or
            "engineering.ensure-solution-project-dependency" or
            "engineering.create-io-device" or
            "engineering.create-ethercat-box" or
            "engineering.generate-io-mappings" or
            "engineering.search-io-devices" or
            "engineering.reload-io-devices" or
            "engineering.apply-io-tree-plan" or
            "engineering.create-plc-project" or
            "engineering.create-module" or
            "engineering.start-tmc-code-generator" or
            "engineering.publish-modules" or
            "engineering.add-module-instance" or
            "engineering.ensure-task" or
            "engineering.export-tree-item-xml" or
            "engineering.save-all" or
            "engineering.close-visual-studio" or
            "engineering.build-solution" or
            "engineering.activate-configuration";
    }

    internal static bool RequiresCommandTimeoutWhenReusingEngineeringSession(string kind)
    {
        string normalizedKind = kind.ToLowerInvariant();
        return normalizedKind is
            "engineering.launch-visual-studio" or
            "engineering.open-xae-solution" or
            "engineering.export-tree-item-xml" or
            "engineering.build-solution" or
            "engineering.activate-configuration";
    }

    internal static void CloseReusedEngineeringSessionIfOpen(bool saveBeforeClose = true) =>
        _engineeringSessionReuseScope?.Close(saveBeforeClose);

    internal static bool AbandonReusedEngineeringSessionIfOpen()
    {
        if (_engineeringSessionReuseScope is null)
        {
            return false;
        }

        _engineeringSessionReuseScope.Abandon();
        return true;
    }

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
            "engineering.cleanup-dte-host-processes" => ExecuteEngineeringCleanupDteHostProcesses(options),
            "engineering.create-xae-solution" => ExecuteEngineeringCreateXaeSolution(options),
            "engineering.open-xae-solution" => ExecuteEngineeringOpenXaeSolution(options),
            "engineering.create-cpp-project" => ExecuteEngineeringCreateCppProject(options),
            "engineering.create-vs-cpp-project" => ExecuteEngineeringCreateVsCppProject(options),
            "engineering.create-scope-project" => ExecuteEngineeringCreateScopeProject(options),
            "scope.ensure-configuration" => ExecuteScopeEnsureConfiguration(options),
            "scope.assert-configuration-shape" => ExecuteScopeAssertConfigurationShape(options),
            "engineering.ensure-solution-project-dependency" => ExecuteEngineeringEnsureSolutionProjectDependency(options),
            "engineering.create-io-device" => ExecuteEngineeringCreateIoDevice(options),
            "engineering.create-ethercat-box" => ExecuteEngineeringCreateEthercatBox(options),
            "engineering.generate-io-mappings" => ExecuteEngineeringGenerateIoMappings(options),
            "engineering.search-io-devices" => ExecuteEngineeringSearchIoDevices(options),
            "engineering.reload-io-devices" => ExecuteEngineeringReloadIoDevices(options),
            "engineering.apply-io-tree-plan" => ExecuteEngineeringApplyIoTreePlan(options),
            "ethercat.assert-product-revisions" => ExecuteEtherCatAssertProductRevisions(options),
            "engineering.create-plc-project" => ExecuteEngineeringCreatePlcProject(options),
            "engineering.create-module" => ExecuteEngineeringCreateModule(options),
            "engineering.start-tmc-code-generator" => ExecuteEngineeringStartTmcCodeGenerator(options),
            "engineering.verify-tmc-data-areas" => ExecuteEngineeringVerifyTmcDataAreas(options),
            "engineering.apply-tmc-module-model" => ExecuteEngineeringApplyTmcModuleModel(options),
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
            "tsproj.set-cpp-instance-metadata" => ExecuteTsprojSetCppInstanceMetadata(options),
            "tsproj.clear-plc-instance-vars" => ExecuteTsprojClearPlcInstanceVars(options),
            "tsproj.ensure-plc-instance-vars-group" => ExecuteTsprojEnsurePlcInstanceVarsGroup(options),
            "tsproj.clear-plc-init-symbols" => ExecuteTsprojClearPlcInitSymbols(options),
            "tsproj.clear-plc-task-pou-oids" => ExecuteTsprojClearPlcTaskPouOids(options),
            "tsproj.clear-mappings" => ExecuteTsprojClearMappings(options),
            "tsproj.clear-unrestored-var-links" => ExecuteTsprojClearUnrestoredVarLinks(options),
            "tsproj.replace-mappings-section" => ExecuteTsprojReplaceMappingsSection(options),
            "tsproj.replace-project-io-section" => ExecuteTsprojReplaceProjectIoSection(options),
            "tsproj.ensure-io-section" => ExecuteTsprojEnsureIoSection(options),
            "tsproj.ensure-io-device" => ExecuteTsprojEnsureIoDevice(options),
            "tsproj.ensure-ethercat-box" => ExecuteTsprojEnsureEthercatBox(options),
            "tsproj.ensure-io-pdo" => ExecuteTsprojEnsureIoPdo(options),
            "tsproj.ensure-io-box-image" => ExecuteTsprojEnsureIoBoxImage(options),
            "tsproj.ensure-mapping-info" => ExecuteTsprojEnsureMappingInfo(options),
            "tsproj.ensure-io-mapping-link" => ExecuteTsprojEnsureIoMappingLink(options),
            "tsproj.apply-io-topology-plan" => ExecuteTsprojApplyIoTopologyPlan(options),
            "tsproj.replace-data-types-section" => ExecuteTsprojReplaceDataTypesSection(options),
            "tsproj.replace-system-settings-section" => ExecuteTsprojReplaceSystemSettingsSection(options),
            "tsproj.ensure-system-settings" => ExecuteTsprojEnsureSystemSettings(options),
            "tsproj.clear-instance-parameter-values" => ExecuteTsprojClearInstanceParameterValues(options),
            "tsproj.clear-instance-data-pointer-values" => ExecuteTsprojClearInstanceDataPointerValues(options),
            "tsproj.apply-instance-parameter-plan" => ExecuteTsprojApplyInstanceParameterPlan(options),
            "tsproj.apply-instance-interface-pointer-plan" => ExecuteTsprojApplyInstanceInterfacePointerPlan(options),
            "tsproj.apply-instance-data-pointer-plan" => ExecuteTsprojApplyInstanceDataPointerPlan(options),
            "tsproj.assert-data-pointer-shape" => ExecuteTsprojAssertDataPointerShape(options),
            "tsproj.assert-io-topology-shape" => ExecuteTsprojAssertIoTopologyShape(options),
            "tsproj.assert-io-image-references" => ExecuteTsprojAssertIoImageReferences(options),
            "tsproj.describe-io-topology" => ExecuteTsprojDescribeIoTopology(options),
            "tsproj.compare-io-topology" => ExecuteTsprojCompareIoTopology(options),
            "tsproj.refresh-cpp-instance-tmc-desc" => ExecuteTsprojRefreshCppInstanceTmcDesc(options),
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
            "validation.assert-ads-state" => ExecuteValidationAssertAdsState(options),
            "validation.mark-event-log-window" => ExecuteValidationMarkEventLogWindow(options),
            "validation.assert-event-log-window" => ExecuteValidationAssertEventLogWindow(options),
            "validation.assert-process-crash-window" => ExecuteValidationAssertProcessCrashWindow(options),
            "validation.ads-read" => ExecuteValidationAdsRead(options),
            "validation.ads-read-symbols" => ExecuteValidationAdsReadSymbols(options),
            _ => throw new InvalidOperationException($"Unsupported invoke-step kind '{kind}'.")
        };

    private static StepExecutionOutcome ExecuteWithOptionalTimeout(
        string kind,
        IReadOnlyDictionary<string, string> options,
        int commandTimeoutMs)
    {
        if (commandTimeoutMs <= 0)
        {
            return Execute(kind, options);
        }

        DateTime timeoutWindowStart = DateTime.UtcNow;
        HashSet<int> existingDteHostProcessIds = CaptureDteHostProcessIds();
        StepExecutionOutcome? outcome = null;
        Exception? exception = null;
        using ManualResetEventSlim completed = new(false);
        Thread worker = new(() =>
        {
            try
            {
                outcome = Execute(kind, options);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "TwinCAT invoke-step timeout worker"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        if (!completed.Wait(commandTimeoutMs))
        {
            bool abandonedReusedSession = AbandonReusedEngineeringSessionIfOpen();
            int cleanedProcessCount = CleanupNewDteHostProcesses(existingDteHostProcessIds, timeoutWindowStart);
            throw new TimeoutException(
                $"invoke-step '{kind}' did not finish within {commandTimeoutMs} ms. " +
                "The background step worker was abandoned so unattended runs do not wait forever on Visual Studio/TwinCAT UI prompts. " +
                $"Reused engineering session abandoned: {abandonedReusedSession.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}. " +
                $"New Visual Studio/XAE host processes cleaned up: {cleanedProcessCount}.");
        }

        if (exception is not null)
        {
            throw exception;
        }

        return outcome ?? throw new InvalidOperationException($"invoke-step '{kind}' completed without an outcome.");
    }

    private static HashSet<int> CaptureDteHostProcessIds()
    {
        HashSet<int> result = new();
        foreach (string processName in DteHostProcessNames)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        result.Add(process.Id);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
        }

        return result;
    }

    private static int CleanupNewDteHostProcesses(HashSet<int> existingProcessIds, DateTime timeoutWindowStartUtc)
    {
        int cleaned = 0;
        foreach (string processName in DteHostProcessNames)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (existingProcessIds.Contains(process.Id))
                        {
                            continue;
                        }

                        DateTime startTimeUtc = process.StartTime.ToUniversalTime();
                        if (startTimeUtc < timeoutWindowStartUtc.AddSeconds(-5))
                        {
                            continue;
                        }

                        process.Kill(entireProcessTree: true);
                        cleaned++;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                    catch (NotSupportedException)
                    {
                    }
                }
            }
        }

        return cleaned;
    }

    private static void DisposeEngineeringSession(TwinCatEngineeringSession session)
    {
        bool attachedToExisting = session.AttachedToExisting;
        int[] targetProcessIds = session.TargetProcessIds.Distinct().ToArray();
        try
        {
            session.Dispose();
        }
        finally
        {
            TerminateOwnedDteHostProcesses(attachedToExisting, targetProcessIds);
        }
    }

    private static int TerminateOwnedDteHostProcesses(bool attachedToExisting, IReadOnlyCollection<int> targetProcessIds)
    {
        if (attachedToExisting || targetProcessIds.Count == 0)
        {
            return 0;
        }

        int cleaned = 0;
        foreach (int processId in targetProcessIds)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    continue;
                }

                if (!DteHostProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (process.WaitForExit(3000))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                cleaned++;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return cleaned;
    }

    private static readonly string[] DteHostProcessNames = ["devenv", "TcXaeShell"];

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

    private static StepExecutionOutcome ExecuteEtherCatAssertProductRevisions(IReadOnlyDictionary<string, string> options)
    {
        AssertEtherCatProductRevisionsRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<AssertEtherCatProductRevisionsRequest>(options)
            : new AssertEtherCatProductRevisionsRequest(
                ProductRevisions: ParseOptionalList(CliOptionParser.GetOption(options, "product-revisions", "product-revision")),
                SearchDirectories: ParseOptionalList(CliOptionParser.GetOption(options, "search-directories", "search-directory")),
                IncludeHiddenTypes: CliOptionParser.GetBoolOption(options, "include-hidden-types", false));

        TwinCatEtherCatDeviceDescriptionService service = new();
        AssertEtherCatProductRevisionsResult result = service.AssertProductRevisions(request);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("succeeded", result.Succeeded ? "true" : "false"),
            ("requestedCount", result.RequestedCount.ToString(CultureInfo.InvariantCulture)),
            ("matchedCount", result.MatchedCount.ToString(CultureInfo.InvariantCulture)),
            ("missingCount", result.MissingCount.ToString(CultureInfo.InvariantCulture)),
            ("scannedFileCount", result.ScannedFileCount.ToString(CultureInfo.InvariantCulture)),
            ("searchDirectories", string.Join(";", result.SearchDirectories)),
            ("assertionsJson", JsonSerializer.Serialize(result.Assertions, JsonOptions)));

        return result.Succeeded
            ? StepExecutionOutcome.Success(result.Summary, outputs)
            : new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                result.Summary,
                outputs,
                Array.Empty<EvidenceArtifact>());
    }

    private static StepExecutionOutcome ExecuteValidationAssertAdsState(IReadOnlyDictionary<string, string> options)
    {
        AssertAdsStateRequest request = ParseAssertAdsStateRequest(options);
        AdsValidationService ads = new();
        AssertAdsStateResult result = ads.AssertStates(request);
        string statesText = FormatAdsStateAssertions(result.Ports);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("netId", result.NetId),
            ("succeededCount", result.SucceededCount.ToString()),
            ("failedCount", result.FailedCount.ToString()),
            ("statesText", statesText),
            ("statesJson", JsonSerializer.Serialize(result.Ports, JsonOptions)));

        if (!result.Succeeded)
        {
            return new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                $"ADS state assertion failed: {statesText}",
                outputs,
                Array.Empty<EvidenceArtifact>());
        }

        return StepExecutionOutcome.Success(
            $"ADS state assertion succeeded: {statesText}",
            outputs);
    }

    private static StepExecutionOutcome ExecuteValidationMarkEventLogWindow(IReadOnlyDictionary<string, string> options)
    {
        MarkEventLogWindowRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<MarkEventLogWindowRequest>(options)
            : new MarkEventLogWindowRequest(
                LogName: CliOptionParser.GetOption(options, "log-name") ?? "Application",
                ProviderName: CliOptionParser.GetOption(options, "provider-name", "source") ?? "TcSysSrv",
                MarkerFilePath: CliOptionParser.GetOption(options, "marker-file", "marker-file-path"));

        AdsValidationService ads = new();
        EventLogWindowMarker marker = ads.MarkEventLogWindow(request);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("logName", marker.LogName),
            ("providerName", marker.ProviderName),
            ("markedAt", marker.MarkedAt.ToString("O")),
            ("lastEntryIndex", marker.LastEntryIndex?.ToString()),
            ("markerId", marker.MarkerId),
            ("markerJson", JsonSerializer.Serialize(marker, JsonOptions)));

        return StepExecutionOutcome.Success(
            $"Event log window marked for {marker.ProviderName} at {marker.MarkedAt:O}.",
            outputs);
    }

    private static StepExecutionOutcome ExecuteValidationAssertEventLogWindow(IReadOnlyDictionary<string, string> options)
    {
        AssertEventLogWindowRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<AssertEventLogWindowRequest>(options)
            : new AssertEventLogWindowRequest(
                MarkerFilePath: CliOptionParser.GetOption(options, "marker-file", "marker-file-path"),
                LogName: CliOptionParser.GetOption(options, "log-name") ?? "Application",
                ProviderName: CliOptionParser.GetOption(options, "provider-name", "source") ?? "TcSysSrv",
                LookbackSeconds: CliOptionParser.GetIntOption(options, "lookback-seconds", 300),
                FailOnErrorOrCritical: CliOptionParser.GetBoolOption(options, "fail-on-error-or-critical", true),
                FailOnConfigAdsState: CliOptionParser.GetBoolOption(options, "fail-on-config-ads-state", true),
                FailMessageContains: ParseOptionalList(CliOptionParser.GetOption(options, "fail-message-contains")),
                MaxEvents: CliOptionParser.GetIntOption(options, "max-events", 50));

        AdsValidationService ads = new();
        AssertEventLogWindowResult result = ads.AssertEventLogWindow(request);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("logName", result.LogName),
            ("providerName", result.ProviderName),
            ("windowStart", result.WindowStart.ToString("O")),
            ("observedEventCount", result.ObservedEventCount.ToString()),
            ("errorOrCriticalCount", result.ErrorOrCriticalCount.ToString()),
            ("configAdsStateCount", result.ConfigAdsStateCount.ToString()),
            ("errorsText", string.Join("; ", result.Errors)),
            ("eventsJson", JsonSerializer.Serialize(result.Events, JsonOptions)),
            ("assertionJson", JsonSerializer.Serialize(result, JsonOptions)));

        return result.Succeeded
            ? StepExecutionOutcome.Success(result.Summary, outputs)
            : new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                result.Summary,
                outputs,
                Array.Empty<EvidenceArtifact>());
    }

    private static StepExecutionOutcome ExecuteValidationAssertProcessCrashWindow(IReadOnlyDictionary<string, string> options)
    {
        AssertProcessCrashWindowRequest request = HasJsonPayload(options)
            ? ReadJsonPayload<AssertProcessCrashWindowRequest>(options)
            : new AssertProcessCrashWindowRequest(
                MarkerFilePath: CliOptionParser.GetOption(options, "marker-file", "marker-file-path"),
                LogName: CliOptionParser.GetOption(options, "log-name") ?? "Application",
                LookbackSeconds: CliOptionParser.GetIntOption(options, "lookback-seconds", 300),
                ProviderNames: ParseOptionalList(CliOptionParser.GetOption(options, "provider-names", "providers", "sources")),
                ProcessNames: ParseOptionalList(CliOptionParser.GetOption(options, "process-names", "processes")),
                ModuleNames: ParseOptionalList(CliOptionParser.GetOption(options, "module-names", "modules")),
                MessageContains: ParseOptionalList(CliOptionParser.GetOption(options, "message-contains", "fail-message-contains")),
                MaxEvents: CliOptionParser.GetIntOption(options, "max-events", 100));

        AdsValidationService ads = new();
        AssertProcessCrashWindowResult result = ads.AssertProcessCrashWindow(request);
        IReadOnlyDictionary<string, string?> outputs = CreateOutputs(
            ("logName", result.LogName),
            ("windowStart", result.WindowStart.ToString("O")),
            ("observedEventCount", result.ObservedEventCount.ToString()),
            ("matchingEventCount", result.MatchingEventCount.ToString()),
            ("errorsText", string.Join("; ", result.Errors)),
            ("matchingEventsJson", JsonSerializer.Serialize(result.MatchingEvents, JsonOptions)),
            ("assertionJson", JsonSerializer.Serialize(result, JsonOptions)));

        return result.Succeeded
            ? StepExecutionOutcome.Success(result.Summary, outputs)
            : new StepExecutionOutcome(
                StepExecutionStatus.Failed,
                result.Summary,
                outputs,
                Array.Empty<EvidenceArtifact>());
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
        if (_engineeringSessionReuseScope is not null)
        {
            TwinCatEngineeringSession reusedSession = _engineeringSessionReuseScope.EnsureWorkspaceOpen(options, workspace);
            return operation(_engineeringSessionReuseScope.Engineering, reusedSession, workspace);
        }

        string progId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0";
        bool visible = CliOptionParser.GetBoolOption(options, "visible", false);
        int startupDelayMs = CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000);
        bool suppressUi = CliOptionParser.GetBoolOption(options, "suppress-ui", true);
        int launchTimeoutMs = CliOptionParser.GetIntOption(options, "launch-timeout-ms", 60000);
        bool enableDialogAutoDismiss = CliOptionParser.GetBoolOption(options, "enable-dialog-auto-dismiss", true);
        int dialogPollIntervalMs = CliOptionParser.GetIntOption(options, "dialog-poll-interval-ms", 500);
        bool attachToExisting = CliOptionParser.GetBoolOption(options, "attach-to-existing", false);
        string? rootSuffix = CliOptionParser.GetOption(options, "root-suffix");
        string? dteHostPath = CliOptionParser.GetOption(options, "dte-host-path");
        bool preferDteHostLaunch = CliOptionParser.GetBoolOption(options, "prefer-dte-host-launch", false);
        TwinCatEngineeringService engineering = new();
        TwinCatEngineeringSession? session = null;
        try
        {
            session = engineering.LaunchVisualStudio(new LaunchVisualStudioRequest(
                ProgId: progId,
                Visible: visible,
                StartupDelayMs: startupDelayMs,
                SuppressUi: suppressUi,
                LaunchTimeoutMs: launchTimeoutMs,
                EnableDialogAutoDismiss: enableDialogAutoDismiss,
                DialogPollIntervalMs: dialogPollIntervalMs,
                AttachToExisting: attachToExisting,
                RootSuffix: rootSuffix,
                DteHostPath: dteHostPath,
                PreferDteHostLaunch: preferDteHostLaunch));
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
                    DisposeEngineeringSession(session);
                }
            }
        }
    }

    private static StepExecutionOutcome RunCppProjectFileOperation(
        IReadOnlyDictionary<string, string> options,
        Func<TwinCatEngineeringService, string, StepExecutionOutcome> operation)
    {
        CliWorkspacePaths workspace = ResolveWorkspace(options, requireSolutionPath: false);
        string twinCatProjectDirectory = Path.GetDirectoryName(workspace.ProjectPath)
            ?? throw new InvalidOperationException($"Cannot resolve TwinCAT project directory from {workspace.ProjectPath}.");
        TwinCatEngineeringService engineering = new();
        return operation(engineering, twinCatProjectDirectory);
    }

    private static LaunchVisualStudioRequest BuildLaunchVisualStudioRequest(IReadOnlyDictionary<string, string> options) =>
        new(
            ProgId: CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
            StartupDelayMs: CliOptionParser.GetIntOption(options, "startup-delay-ms", 8000),
            Visible: CliOptionParser.GetBoolOption(options, "visible", false),
            SuppressUi: CliOptionParser.GetBoolOption(options, "suppress-ui", true),
            LaunchTimeoutMs: CliOptionParser.GetIntOption(options, "launch-timeout-ms", 60000),
            EnableDialogAutoDismiss: CliOptionParser.GetBoolOption(options, "enable-dialog-auto-dismiss", true),
            DialogPollIntervalMs: CliOptionParser.GetIntOption(options, "dialog-poll-interval-ms", 500),
            AttachToExisting: CliOptionParser.GetBoolOption(options, "attach-to-existing", false),
            RootSuffix: CliOptionParser.GetOption(options, "root-suffix"),
            DteHostPath: CliOptionParser.GetOption(options, "dte-host-path"),
            PreferDteHostLaunch: CliOptionParser.GetBoolOption(options, "prefer-dte-host-launch", false));

    private sealed class EngineeringSessionReuseScope : IDisposable
    {
        private bool _disposed;
        private CliWorkspacePaths? _workspace;

        public TwinCatEngineeringService Engineering { get; } = new();

        public TwinCatEngineeringSession? Session { get; private set; }

        public TwinCatEngineeringSession EnsureLaunched(IReadOnlyDictionary<string, string> options)
        {
            ThrowIfDisposed();
            if (Session is not null)
            {
                return Session;
            }

            Session = Engineering.LaunchVisualStudio(BuildLaunchVisualStudioRequest(options));
            return Session;
        }

        public TwinCatEngineeringSession EnsureWorkspaceOpen(
            IReadOnlyDictionary<string, string> options,
            CliWorkspacePaths workspace)
        {
            TwinCatEngineeringSession session = EnsureLaunched(options);
            if (_workspace is not null &&
                string.Equals(_workspace.SolutionPath, workspace.SolutionPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_workspace.ProjectPath, workspace.ProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }

            if (string.IsNullOrWhiteSpace(workspace.SolutionPath))
            {
                throw new InvalidOperationException("Engineering session reuse requires a solution path before opening workspace steps.");
            }

            Engineering.OpenTwinCatSolution(session, new OpenTwinCatSolutionRequest(workspace.SolutionPath, workspace.ProjectPath));
            _workspace = workspace;
            return session;
        }

        public void RememberWorkspace(CliWorkspacePaths workspace) => _workspace = workspace;

        public void ClearWorkspace() => _workspace = null;

        public void Close(bool saveBeforeClose)
        {
            if (Session is null)
            {
                return;
            }

            TwinCatEngineeringSession session = Session;
            Session = null;
            _workspace = null;
            try
            {
                Engineering.CloseVisualStudio(session, saveBeforeClose);
            }
            finally
            {
                DisposeEngineeringSession(session);
            }
        }

        public void Abandon()
        {
            if (Session is null)
            {
                return;
            }

            TwinCatEngineeringSession session = Session;
            Session = null;
            _workspace = null;
            try
            {
                DisposeEngineeringSession(session);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Abandon();
            }
            catch
            {
            }
            finally
            {
                if (ReferenceEquals(_engineeringSessionReuseScope, this))
                {
                    _engineeringSessionReuseScope = null;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EngineeringSessionReuseScope));
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

    private static bool HasJsonPayload(IReadOnlyDictionary<string, string> options) =>
        !string.IsNullOrWhiteSpace(CliOptionParser.GetOption(options, "json-file")) ||
        !string.IsNullOrWhiteSpace(CliOptionParser.GetOption(options, "json"));

    private static IReadOnlyList<IoPdoEntry>? ParseInlineIoPdoEntries(IReadOnlyDictionary<string, string> options)
    {
        string? raw = CliOptionParser.GetOption(options, "entries");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        List<IoPdoEntry> entries = [];
        foreach (string entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split(':', StringSplitOptions.None);
            if (parts.Length < 1 || parts.Length > 4)
            {
                throw new InvalidOperationException("--entries must use 'Name[:Index[:Sub[:Type]]]' entries separated by ';'.");
            }

            entries.Add(new IoPdoEntry(
                EmptyToNull(parts.ElementAtOrDefault(0)),
                EmptyToNull(parts.ElementAtOrDefault(1)),
                EmptyToNull(parts.ElementAtOrDefault(2)),
                EmptyToNull(parts.ElementAtOrDefault(3))));
        }

        return entries;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

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

    private static AssertAdsStateRequest ParseAssertAdsStateRequest(IReadOnlyDictionary<string, string> options)
    {
        if (HasJsonPayload(options))
        {
            string json = ReadTextPayload(options, "json", "json", "json-file");
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                IReadOnlyList<ExpectedAdsPortState>? expectedPorts =
                    JsonSerializer.Deserialize<List<ExpectedAdsPortState>>(json, JsonInputOptions);
                if (expectedPorts is not null && expectedPorts.Count > 0)
                {
                    string payloadNetId = CliOptionParser.GetOption(options, "net-id") ?? "local";
                    return new AssertAdsStateRequest(payloadNetId, expectedPorts);
                }
            }

            AssertAdsStateRequest? request = JsonSerializer.Deserialize<AssertAdsStateRequest>(json, JsonInputOptions);
            if (request is not null)
            {
                return request;
            }
        }

        string netId = CliOptionParser.GetOption(options, "net-id") ?? "local";
        string? expectedInline = CliOptionParser.GetOption(options, "expected", "states", "expected-states");
        if (!string.IsNullOrWhiteSpace(expectedInline))
        {
            return new AssertAdsStateRequest(netId, ParseInlineExpectedAdsStates(expectedInline));
        }

        IReadOnlyList<int> ports = ParsePorts(CliOptionParser.GetOption(options, "ports"), Array.Empty<int>());
        if (ports.Count == 0)
        {
            throw new InvalidOperationException("validation.assert-ads-state requires --expected=10000=Run;200=Run or --ports=... with --ads-state=Run.");
        }

        string adsState = CliOptionParser.GetOption(options, "ads-state") ?? "Run";
        int deviceStateValue = CliOptionParser.GetIntOption(options, "device-state", fallback: int.MinValue);
        short? deviceState = deviceStateValue == int.MinValue ? null : checked((short)deviceStateValue);
        return new AssertAdsStateRequest(
            netId,
            ports.Select(port => new ExpectedAdsPortState(port, adsState, deviceState)).ToArray());
    }

    private static IReadOnlyList<ExpectedAdsPortState> ParseInlineExpectedAdsStates(string raw)
    {
        List<ExpectedAdsPortState> expected = [];
        foreach (string entry in raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split(new[] { '=', ':' }, 3, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts.Length > 3)
            {
                throw new InvalidOperationException("--expected entries must use Port=AdsState[:DeviceState], separated by semicolons or commas.");
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
            {
                throw new InvalidOperationException($"ADS port is invalid. Actual='{parts[0]}'.");
            }

            short? deviceState = null;
            if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                if (!short.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out short parsedDeviceState))
                {
                    throw new InvalidOperationException($"ADS device state is invalid for port {port}. Actual='{parts[2]}'.");
                }

                deviceState = parsedDeviceState;
            }

            expected.Add(new ExpectedAdsPortState(port, parts[1], deviceState));
        }

        if (expected.Count == 0)
        {
            throw new InvalidOperationException("--expected must include at least one ADS port state assertion.");
        }

        return expected;
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

    private static string FormatAdsStateAssertions(IReadOnlyList<AdsPortStateAssertion> ports) =>
        string.Join(
            "; ",
            ports.Select(port =>
                port.Succeeded
                    ? $"{port.Port}={port.ActualAdsState}"
                    : $"{port.Port}=<expected {port.ExpectedAdsState}, actual {port.ActualAdsState ?? "(unreachable)"}: {port.ErrorMessage}>"));

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
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms"),
                SuppressUi = CliOptionParser.GetOption(options, "suppress-ui"),
                LaunchTimeoutMs = CliOptionParser.GetOption(options, "launch-timeout-ms"),
                AttachToExisting = CliOptionParser.GetOption(options, "attach-to-existing"),
                RootSuffix = CliOptionParser.GetOption(options, "root-suffix"),
                DteHostPath = CliOptionParser.GetOption(options, "dte-host-path"),
                PreferDteHostLaunch = CliOptionParser.GetOption(options, "prefer-dte-host-launch")
            },
            "engineering.create-xae-solution" => new
            {
                SolutionDirectory = CliOptionParser.GetOption(options, "solution-directory", "output"),
                SolutionName = CliOptionParser.GetOption(options, "solution-name"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                ProgId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms"),
                SuppressUi = CliOptionParser.GetOption(options, "suppress-ui"),
                LaunchTimeoutMs = CliOptionParser.GetOption(options, "launch-timeout-ms"),
                AttachToExisting = CliOptionParser.GetOption(options, "attach-to-existing"),
                RootSuffix = CliOptionParser.GetOption(options, "root-suffix"),
                DteHostPath = CliOptionParser.GetOption(options, "dte-host-path"),
                PreferDteHostLaunch = CliOptionParser.GetOption(options, "prefer-dte-host-launch")
            },
            "engineering.open-xae-solution" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                ProgId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms"),
                SuppressUi = CliOptionParser.GetOption(options, "suppress-ui"),
                LaunchTimeoutMs = CliOptionParser.GetOption(options, "launch-timeout-ms"),
                AttachToExisting = CliOptionParser.GetOption(options, "attach-to-existing"),
                RootSuffix = CliOptionParser.GetOption(options, "root-suffix"),
                DteHostPath = CliOptionParser.GetOption(options, "dte-host-path"),
                PreferDteHostLaunch = CliOptionParser.GetOption(options, "prefer-dte-host-launch")
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
            "engineering.create-scope-project" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "scope-project-name", "project-name"),
                ProjectDirectory = CliOptionParser.GetOption(options, "project-directory"),
                ConfigurationFileName = CliOptionParser.GetOption(options, "configuration-file-name", "config-file-name"),
                CreateEmptyConfiguration = CliOptionParser.GetOption(options, "create-empty-configuration"),
                AllowSolutionFileFallback = CliOptionParser.GetOption(options, "allow-solution-file-fallback")
            },
            "scope.ensure-configuration" => new
            {
                ConfigurationFilePath = CliOptionParser.GetOption(options, "configuration-file-path", "scope-file-path", "file-path"),
                ScopeName = CliOptionParser.GetOption(options, "scope-name", "name"),
                MainServer = CliOptionParser.GetOption(options, "main-server"),
                RecordTime = CliOptionParser.GetOption(options, "record-time"),
                StopMode = CliOptionParser.GetOption(options, "stop-mode"),
                ChartName = CliOptionParser.GetOption(options, "chart-name"),
                ReplaceChannels = CliOptionParser.GetOption(options, "replace-channels"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "scope.assert-configuration-shape" => new
            {
                ConfigurationFilePath = CliOptionParser.GetOption(options, "configuration-file-path", "scope-file-path", "file-path"),
                ScopeName = CliOptionParser.GetOption(options, "expected-scope-name", "scope-name", "name"),
                ChartName = CliOptionParser.GetOption(options, "expected-chart-name", "chart-name"),
                ExpectedAdsChannelCount = CliOptionParser.GetOption(options, "expected-ads-channel-count", "ads-channel-count"),
                ExpectedChartChannelCount = CliOptionParser.GetOption(options, "expected-chart-channel-count", "chart-channel-count"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "engineering.ensure-solution-project-dependency" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "dependency-project-name", "project-name"),
                DependsOnProjectName = CliOptionParser.GetOption(options, "depends-on-project-name", "depends-on")
            },
            "engineering.create-io-device" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                Name = CliOptionParser.GetOption(options, "name", "device-name"),
                SubType = CliOptionParser.GetOption(options, "subtype"),
                ParentTreeItemPath = CliOptionParser.GetOption(options, "parent-tree-item-path", "parent-path") ?? "TIID",
                Before = CliOptionParser.GetOption(options, "before"),
                VInfo = CliOptionParser.GetOption(options, "vinfo", "info"),
                Disabled = CliOptionParser.GetOption(options, "disabled"),
                AllowExisting = CliOptionParser.GetOption(options, "allow-existing"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "engineering.cleanup-dte-host-processes" => new
            {
                ProcessNames = CliOptionParser.GetOption(options, "process-names", "process-name"),
                ProcessIds = CliOptionParser.GetOption(options, "process-ids", "process-id"),
                DryRun = CliOptionParser.GetOption(options, "dry-run"),
                IncludeWindowed = CliOptionParser.GetOption(options, "include-windowed"),
                KillProcessTree = CliOptionParser.GetOption(options, "kill-process-tree")
            },
            "engineering.create-ethercat-box" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ParentTreeItemPath = CliOptionParser.GetOption(options, "parent-tree-item-path", "parent-path"),
                Name = CliOptionParser.GetOption(options, "name", "box-name"),
                SubType = CliOptionParser.GetOption(options, "subtype"),
                Before = CliOptionParser.GetOption(options, "before"),
                ProductRevision = CliOptionParser.GetOption(options, "product-revision", "product"),
                VInfo = CliOptionParser.GetOption(options, "vinfo", "info"),
                Disabled = CliOptionParser.GetOption(options, "disabled"),
                AllowExisting = CliOptionParser.GetOption(options, "allow-existing"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "ethercat.assert-product-revisions" => new
            {
                ProductRevisions = CliOptionParser.GetOption(options, "product-revisions", "product-revision"),
                SearchDirectories = CliOptionParser.GetOption(options, "search-directories", "search-directory"),
                IncludeHiddenTypes = CliOptionParser.GetOption(options, "include-hidden-types"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "engineering.generate-io-mappings" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                SuppressUi = CliOptionParser.GetOption(options, "suppress-ui"),
                AllowDteCommandFallback = CliOptionParser.GetOption(options, "allow-dte-command-fallback"),
                TimeoutMs = CliOptionParser.GetOption(options, "timeout-ms"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
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
            "engineering.start-tmc-code-generator" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                PostStartDelayMs = CliOptionParser.GetOption(options, "post-start-delay-ms"),
                WaitForUpdatedTmcTimeoutMs = CliOptionParser.GetOption(options, "wait-for-updated-tmc-timeout-ms")
            },
            "engineering.verify-tmc-data-areas" => new
            {
                ProjectTmcPath = CliOptionParser.GetOption(options, "project-tmc-path", "tmc-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "engineering.apply-tmc-module-model" => new
            {
                ProjectTmcPath = CliOptionParser.GetOption(options, "project-tmc-path", "tmc-path"),
                ProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                GeneratedServicesHeaderPath = CliOptionParser.GetOption(options, "generated-services-header-path", "services-header-path"),
                GeneratedHeaderPaths = CliOptionParser.GetOption(options, "generated-header-paths", "generated-header-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "engineering.publish-modules" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                PostPublishDelayMs = CliOptionParser.GetOption(options, "post-publish-delay-ms"),
                WaitForUpdatedTmcTimeoutMs = CliOptionParser.GetOption(options, "wait-for-updated-tmc-timeout-ms"),
                RunTmcCodeGeneratorFirst = CliOptionParser.GetOption(options, "run-tmc-code-generator-first")
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
                ProgId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
                TimeoutMs = CliOptionParser.GetOption(options, "timeout-ms"),
                BuildEngine = CliOptionParser.GetOption(options, "build-engine", "engine"),
                Configuration = CliOptionParser.GetOption(options, "configuration"),
                Platform = CliOptionParser.GetOption(options, "platform"),
                DevenvPath = CliOptionParser.GetOption(options, "devenv-path"),
                MsBuildPath = CliOptionParser.GetOption(options, "msbuild-path"),
                ProjectPaths = CliOptionParser.GetOption(options, "project-paths", "build-project-paths"),
                LogFilePath = CliOptionParser.GetOption(options, "log-file-path", "log-file"),
                AttachToExisting = CliOptionParser.GetOption(options, "attach-to-existing"),
                RootSuffix = CliOptionParser.GetOption(options, "root-suffix"),
                DteHostPath = CliOptionParser.GetOption(options, "dte-host-path"),
                PreferDteHostLaunch = CliOptionParser.GetOption(options, "prefer-dte-host-launch")
            },
            "engineering.save-all" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                ProgId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms"),
                SuppressUi = CliOptionParser.GetOption(options, "suppress-ui"),
                LaunchTimeoutMs = CliOptionParser.GetOption(options, "launch-timeout-ms"),
                AttachToExisting = CliOptionParser.GetOption(options, "attach-to-existing"),
                RootSuffix = CliOptionParser.GetOption(options, "root-suffix"),
                DteHostPath = CliOptionParser.GetOption(options, "dte-host-path"),
                PreferDteHostLaunch = CliOptionParser.GetOption(options, "prefer-dte-host-launch")
            },
            "engineering.close-visual-studio" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                SaveBeforeClose = CliOptionParser.GetOption(options, "save-before-close"),
                ProgId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms"),
                SuppressUi = CliOptionParser.GetOption(options, "suppress-ui"),
                LaunchTimeoutMs = CliOptionParser.GetOption(options, "launch-timeout-ms"),
                AttachToExisting = CliOptionParser.GetOption(options, "attach-to-existing"),
                RootSuffix = CliOptionParser.GetOption(options, "root-suffix"),
                DteHostPath = CliOptionParser.GetOption(options, "dte-host-path"),
                PreferDteHostLaunch = CliOptionParser.GetOption(options, "prefer-dte-host-launch")
            },
            "tsproj.ensure-task" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                TaskName = CliOptionParser.GetOption(options, "task-name"),
                Priority = CliOptionParser.GetOption(options, "priority"),
                CycleTimeNs = CliOptionParser.GetOption(options, "cycle-time-ns"),
                AmsPort = CliOptionParser.GetOption(options, "ams-port"),
                IoAtBegin = CliOptionParser.GetOption(options, "io-at-begin"),
                TaskId = CliOptionParser.GetOption(options, "task-id")
            },
            "engineering.activate-configuration" => new
            {
                SolutionPath = CliOptionParser.GetOption(options, "solution-path"),
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ProjectName = CliOptionParser.GetOption(options, "project-name"),
                SaveConfigurationArchive = CliOptionParser.GetOption(options, "save-configuration-archive"),
                ConfigurationArchivePath = CliOptionParser.GetOption(options, "configuration-archive-path"),
                SuppressUi = CliOptionParser.GetOption(options, "suppress-ui"),
                AllowDteCommandFallback = CliOptionParser.GetOption(options, "allow-dte-command-fallback"),
                ActivationTimeoutMs = CliOptionParser.GetOption(options, "activation-timeout-ms"),
                ProgId = CliOptionParser.GetOption(options, "prog-id") ?? "VisualStudio.DTE.17.0",
                Visible = CliOptionParser.GetOption(options, "visible"),
                StartupDelayMs = CliOptionParser.GetOption(options, "startup-delay-ms"),
                LaunchTimeoutMs = CliOptionParser.GetOption(options, "launch-timeout-ms"),
                AttachToExisting = CliOptionParser.GetOption(options, "attach-to-existing"),
                RootSuffix = CliOptionParser.GetOption(options, "root-suffix"),
                DteHostPath = CliOptionParser.GetOption(options, "dte-host-path"),
                PreferDteHostLaunch = CliOptionParser.GetOption(options, "prefer-dte-host-launch")
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
            "tsproj.assert-data-pointer-shape" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.assert-io-topology-shape" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.assert-io-image-references" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "validation.assert-ads-state" => new
            {
                NetId = CliOptionParser.GetOption(options, "net-id") ?? "local",
                Expected = CliOptionParser.GetOption(options, "expected", "states", "expected-states")
                    ?? CliOptionParser.GetOption(options, "json-file")
                    ?? CliOptionParser.GetOption(options, "json"),
                Ports = CliOptionParser.GetOption(options, "ports"),
                AdsState = CliOptionParser.GetOption(options, "ads-state"),
                DeviceState = CliOptionParser.GetOption(options, "device-state")
            },
            "validation.mark-event-log-window" => new
            {
                LogName = CliOptionParser.GetOption(options, "log-name") ?? "Application",
                ProviderName = CliOptionParser.GetOption(options, "provider-name", "source") ?? "TcSysSrv",
                MarkerFilePath = CliOptionParser.GetOption(options, "marker-file", "marker-file-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "validation.assert-event-log-window" => new
            {
                LogName = CliOptionParser.GetOption(options, "log-name") ?? "Application",
                ProviderName = CliOptionParser.GetOption(options, "provider-name", "source") ?? "TcSysSrv",
                MarkerFilePath = CliOptionParser.GetOption(options, "marker-file", "marker-file-path"),
                LookbackSeconds = CliOptionParser.GetOption(options, "lookback-seconds"),
                FailOnErrorOrCritical = CliOptionParser.GetOption(options, "fail-on-error-or-critical"),
                FailOnConfigAdsState = CliOptionParser.GetOption(options, "fail-on-config-ads-state"),
                FailMessageContains = CliOptionParser.GetOption(options, "fail-message-contains"),
                MaxEvents = CliOptionParser.GetOption(options, "max-events"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "validation.assert-process-crash-window" => new
            {
                LogName = CliOptionParser.GetOption(options, "log-name") ?? "Application",
                MarkerFilePath = CliOptionParser.GetOption(options, "marker-file", "marker-file-path"),
                LookbackSeconds = CliOptionParser.GetOption(options, "lookback-seconds"),
                ProviderNames = CliOptionParser.GetOption(options, "provider-names", "providers", "sources"),
                ProcessNames = CliOptionParser.GetOption(options, "process-names", "processes"),
                ModuleNames = CliOptionParser.GetOption(options, "module-names", "modules"),
                MessageContains = CliOptionParser.GetOption(options, "message-contains", "fail-message-contains"),
                MaxEvents = CliOptionParser.GetOption(options, "max-events"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
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
            "tsproj.ensure-io-section" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path")
            },
            "tsproj.ensure-io-device" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                DeviceId = CliOptionParser.GetOption(options, "device-id"),
                Name = CliOptionParser.GetOption(options, "name"),
                DevType = CliOptionParser.GetOption(options, "dev-type"),
                Disabled = CliOptionParser.GetOption(options, "disabled"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.ensure-ethercat-box" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                DeviceId = CliOptionParser.GetOption(options, "device-id"),
                ParentBoxId = CliOptionParser.GetOption(options, "parent-box-id"),
                BoxId = CliOptionParser.GetOption(options, "box-id"),
                Name = CliOptionParser.GetOption(options, "name"),
                BoxType = CliOptionParser.GetOption(options, "box-type"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.ensure-io-pdo" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                DeviceId = CliOptionParser.GetOption(options, "device-id"),
                BoxId = CliOptionParser.GetOption(options, "box-id"),
                Name = CliOptionParser.GetOption(options, "name"),
                Index = CliOptionParser.GetOption(options, "index"),
                Entries = CliOptionParser.GetOption(options, "entries"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.ensure-io-box-image" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                DeviceId = CliOptionParser.GetOption(options, "device-id"),
                BoxId = CliOptionParser.GetOption(options, "box-id"),
                ImageId = CliOptionParser.GetOption(options, "image-id"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.ensure-mapping-info" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                Identifier = CliOptionParser.GetOption(options, "identifier"),
                Id = CliOptionParser.GetOption(options, "id"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.ensure-io-mapping-link" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                OwnerAName = CliOptionParser.GetOption(options, "owner-a-name"),
                OwnerBName = CliOptionParser.GetOption(options, "owner-b-name"),
                VarA = CliOptionParser.GetOption(options, "var-a"),
                VarB = CliOptionParser.GetOption(options, "var-b"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.apply-io-topology-plan" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.describe-io-topology" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                IncludeDevices = CliOptionParser.GetOption(options, "include-devices"),
                IncludeBoxes = CliOptionParser.GetOption(options, "include-boxes"),
                IncludePdos = CliOptionParser.GetOption(options, "include-pdos"),
                IncludeMappings = CliOptionParser.GetOption(options, "include-mappings"),
                IncludeAttributes = CliOptionParser.GetOption(options, "include-attributes"),
                MaxItemsPerCollection = CliOptionParser.GetOption(options, "max-items-per-collection"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
            },
            "tsproj.compare-io-topology" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                ReferenceProjectPath = CliOptionParser.GetOption(options, "reference-project-path", "reference-tsproj-path"),
                IncludeMappings = CliOptionParser.GetOption(options, "include-mappings"),
                IncludePdos = CliOptionParser.GetOption(options, "include-pdos"),
                IncludeAttributes = CliOptionParser.GetOption(options, "include-attributes"),
                MaxDifferences = CliOptionParser.GetOption(options, "max-differences"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
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
            "tsproj.ensure-system-settings" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CpuId = CliOptionParser.GetOption(options, "cpu-id"),
                IoIdleTaskPriority = CliOptionParser.GetOption(options, "io-idle-task-priority"),
                InsertBeforeTasks = CliOptionParser.GetOption(options, "insert-before-tasks"),
                MaxCpus = CliOptionParser.GetOption(options, "max-cpus"),
                NonWinCpus = CliOptionParser.GetOption(options, "non-win-cpus"),
                JsonFile = CliOptionParser.GetOption(options, "json-file"),
                InlineJson = CliOptionParser.GetOption(options, "json")
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
            "tsproj.set-cpp-instance-metadata" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                InstanceName = CliOptionParser.GetOption(options, "instance-name"),
                Disabled = CliOptionParser.GetOption(options, "disabled"),
                KeepUnrestoredLinks = CliOptionParser.GetOption(options, "keep-unrestored-links"),
                ClassFactoryId = CliOptionParser.GetOption(options, "class-factory-id"),
                ObjectId = CliOptionParser.GetOption(options, "object-id")
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
            "tsproj.refresh-cpp-instance-tmc-desc" => new
            {
                ProjectPath = CliOptionParser.GetOption(options, "project-path", "tsproj-path"),
                CppProjectName = CliOptionParser.GetOption(options, "cpp-project-name", "project-name"),
                ProjectTmcPath = CliOptionParser.GetOption(options, "project-tmc-path", "tmc-path"),
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

    private static IReadOnlyList<int>? ParseOptionalIntList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        List<int> values = [];
        foreach (string item in raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(item, out int value))
            {
                throw new InvalidOperationException($"Integer list contains an invalid value. Actual='{item}'.");
            }

            values.Add(value);
        }

        return values;
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
