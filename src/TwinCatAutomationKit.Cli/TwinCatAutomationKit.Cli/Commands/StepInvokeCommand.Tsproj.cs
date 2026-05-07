using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class StepInvokeCommand
{
    private static StepExecutionOutcome ExecuteTsprojEnsureTask(IReadOnlyDictionary<string, string> options)
    {
        string taskName = CliOptionParser.RequireOption(options, "task-name");
        int priority = CliOptionParser.GetIntOption(options, "priority", 20);
        int cycleTimeNs = CliOptionParser.GetIntOption(options, "cycle-time-ns", 10_000_000);
        int amsPort = CliOptionParser.GetIntOption(options, "ams-port", 301);
        bool? ioAtBegin = TryGetNullableBool(options, "io-at-begin");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureTaskDefinition(projectPath, new EnsureTaskDefinitionRequest(taskName, priority, cycleTimeNs, amsPort, ioAtBegin));
                return StepExecutionOutcome.Success($"Task definition {taskName} updated in tsproj.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearTaskLayout(IReadOnlyDictionary<string, string> options)
    {
        string taskName = CliOptionParser.RequireOption(options, "task-name");
        bool removeVars = CliOptionParser.GetBoolOption(options, "remove-vars", true);
        bool removeImage = CliOptionParser.GetBoolOption(options, "remove-image", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearTaskLayout(projectPath, new ClearTaskLayoutRequest(taskName, removeVars, removeImage));
                return StepExecutionOutcome.Success($"Task layout {taskName} cleared.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureTaskVarsGroup(IReadOnlyDictionary<string, string> options)
    {
        string taskName = CliOptionParser.RequireOption(options, "task-name");
        string groupName = CliOptionParser.RequireOption(options, "group-name");
        int varGrpType = CliOptionParser.GetIntOption(options, "var-grp-type", 1);
        int insertType = CliOptionParser.GetIntOption(options, "insert-type", 1);
        string baseVarName = CliOptionParser.GetOption(options, "base-var-name") ?? "Var ";
        string typeName = CliOptionParser.RequireOption(options, "type-name");
        int count = CliOptionParser.GetIntOption(options, "count", 1);
        int bitStride = CliOptionParser.GetIntOption(options, "bit-stride", 32);
        int externalAddressStride = CliOptionParser.GetIntOption(options, "external-address-stride", 4);
        int? firstExternalAddress = TryGetNullableInt(options, "first-external-address");
        int startIndex = CliOptionParser.GetIntOption(options, "start-index", 1);
        bool replaceExisting = CliOptionParser.GetBoolOption(options, "replace-existing-group", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureTaskVarsGroup(
                    projectPath,
                    new EnsureTaskVarsGroupRequest(
                        taskName,
                        groupName,
                        varGrpType,
                        insertType,
                        baseVarName,
                        typeName,
                        count,
                        bitStride,
                        externalAddressStride,
                        firstExternalAddress,
                        startIndex,
                        replaceExisting));
                return StepExecutionOutcome.Success($"Task vars group {taskName}/{groupName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureTaskImage(IReadOnlyDictionary<string, string> options)
    {
        string taskName = CliOptionParser.RequireOption(options, "task-name");
        int imageId = CliOptionParser.GetIntOption(options, "image-id", 1);
        int addressType = CliOptionParser.GetIntOption(options, "address-type", 1);
        int imageType = CliOptionParser.GetIntOption(options, "image-type", 1);
        int sizeIn = CliOptionParser.GetIntOption(options, "size-in", 40);
        int sizeOut = CliOptionParser.GetIntOption(options, "size-out", 10);
        string imageName = CliOptionParser.GetOption(options, "image-name") ?? "Image";
        bool? ioAtBegin = TryGetNullableBool(options, "io-at-begin");
        bool replaceExistingImage = CliOptionParser.GetBoolOption(options, "replace-existing-image", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureTaskImage(
                    projectPath,
                    new EnsureTaskImageRequest(taskName, imageId, addressType, imageType, sizeIn, sizeOut, imageName, ioAtBegin, replaceExistingImage));
                return StepExecutionOutcome.Success($"Task image {taskName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureCppInstance(IReadOnlyDictionary<string, string> options)
    {
        string cppProjectName = CliOptionParser.RequireOption(options, "cpp-project-name");
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        string objectId = CliOptionParser.RequireOption(options, "object-id");
        int contextId = CliOptionParser.GetIntOption(options, "context-id", 1);
        string contextName = CliOptionParser.GetOption(options, "context-name") ?? "FallbackCtx";
        int priority = CliOptionParser.GetIntOption(options, "priority", 0);
        int cycleTimeNs = CliOptionParser.GetIntOption(options, "cycle-time-ns", 0);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureCppInstance(
                    projectPath,
                    new EnsureCppInstanceRequest(cppProjectName, instanceName, objectId, contextId, contextName, priority, cycleTimeNs));
                return StepExecutionOutcome.Success($"C++ instance {instanceName} ensured.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsurePlcInstance(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsurePlcInstance(projectPath, new EnsurePlcInstanceRequest(plcProjectName, plcInstanceName));
                return StepExecutionOutcome.Success($"PLC instance {plcInstanceName} ensured.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojBindInstanceContext(IReadOnlyDictionary<string, string> options)
    {
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        string taskObjectId = CliOptionParser.RequireOption(options, "task-object-id");
        int priority = CliOptionParser.GetIntOption(options, "priority", 20);
        int cycleTimeNs = CliOptionParser.GetIntOption(options, "cycle-time-ns", 10_000_000);
        int contextId = CliOptionParser.GetIntOption(options, "context-id", 1);
        string? contextName = CliOptionParser.GetOption(options, "context-name");
        bool includeCyclicCaller = CliOptionParser.GetBoolOption(options, "include-cyclic-caller", true);
        bool removeCyclicCallerWhenExcluded = CliOptionParser.GetBoolOption(options, "remove-cyclic-caller-when-excluded", false);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.BindInstanceContext(
                    projectPath,
                    new BindInstanceContextRequest(
                        instanceName,
                        taskObjectId,
                        priority,
                        cycleTimeNs,
                        contextId,
                        contextName,
                        includeCyclicCaller,
                        removeCyclicCallerWhenExcluded));
                return StepExecutionOutcome.Success($"Instance context {instanceName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojBindInstanceTask(IReadOnlyDictionary<string, string> options)
    {
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        string taskObjectId = CliOptionParser.RequireOption(options, "task-object-id");
        int priority = CliOptionParser.GetIntOption(options, "priority", 20);
        int cycleTimeNs = CliOptionParser.GetIntOption(options, "cycle-time-ns", 10_000_000);
        bool includeCyclicCaller = CliOptionParser.GetBoolOption(options, "include-cyclic-caller", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.BindInstanceToTask(projectPath, new BindInstanceToTaskRequest(instanceName, taskObjectId, priority, cycleTimeNs, includeCyclicCaller));
                return StepExecutionOutcome.Success($"Instance {instanceName} bound to task {taskObjectId}.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojBindPlcInstanceTask(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");
        string plcTaskName = CliOptionParser.RequireOption(options, "plc-task-name", "task-name");
        string taskObjectId = CliOptionParser.RequireOption(options, "task-object-id");
        int priority = CliOptionParser.GetIntOption(options, "priority", 20);
        int cycleTimeNs = CliOptionParser.GetIntOption(options, "cycle-time-ns", 10_000_000);
        int contextId = CliOptionParser.GetIntOption(options, "context-id", 0);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.BindPlcInstanceToTask(
                    projectPath,
                    new BindPlcInstanceToTaskRequest(plcProjectName, plcInstanceName, plcTaskName, taskObjectId, priority, cycleTimeNs, contextId));
                return StepExecutionOutcome.Success($"PLC instance {plcInstanceName} bound to task {plcTaskName}.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojSetTaskAffinity(IReadOnlyDictionary<string, string> options)
    {
        string taskName = CliOptionParser.RequireOption(options, "task-name");
        string affinity = CliOptionParser.RequireOption(options, "affinity");
        bool enableAdtTasks = CliOptionParser.GetBoolOption(options, "enable-adt-tasks", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.SetTaskAffinity(projectPath, new SetTaskAffinityRequest(taskName, affinity, enableAdtTasks));
                return StepExecutionOutcome.Success($"Task affinity {taskName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojSetPlcProjectProperties(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string? projectFilePath = CliOptionParser.GetOption(options, "project-file-path");
        string? tmcFilePath = CliOptionParser.GetOption(options, "tmc-file-path");
        bool? reloadTmc = TryGetNullableBool(options, "reload-tmc");
        int? amsPort = TryGetNullableInt(options, "ams-port");
        string? fileArchiveSettings = CliOptionParser.GetOption(options, "file-archive-settings");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.SetPlcProjectProperties(
                    projectPath,
                    new SetPlcProjectPropertiesRequest(
                        plcProjectName,
                        projectFilePath,
                        tmcFilePath,
                        reloadTmc,
                        amsPort,
                        fileArchiveSettings));
                return StepExecutionOutcome.Success($"PLC project properties {plcProjectName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojSetPlcInstanceMetadata(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");
        string? tcSmClass = CliOptionParser.GetOption(options, "tc-sm-class");
        string? tmcPath = CliOptionParser.GetOption(options, "tmc-path");
        string? keepUnrestoredLinks = CliOptionParser.GetOption(options, "keep-unrestored-links");
        string? clsid = CliOptionParser.GetOption(options, "clsid");
        string? classFactory = CliOptionParser.GetOption(options, "class-factory");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.SetPlcInstanceMetadata(
                    projectPath,
                    new SetPlcInstanceMetadataRequest(
                        plcProjectName,
                        plcInstanceName,
                        tcSmClass,
                        tmcPath,
                        keepUnrestoredLinks,
                        clsid,
                        classFactory));
                return StepExecutionOutcome.Success($"PLC instance metadata {plcInstanceName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearPlcInstanceVars(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearPlcInstanceVars(projectPath, new ClearPlcInstanceVarsRequest(plcProjectName, plcInstanceName));
                return StepExecutionOutcome.Success($"PLC instance vars {plcInstanceName} cleared.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsurePlcInstanceVarsGroup(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");
        string groupName = CliOptionParser.RequireOption(options, "group-name");
        int varGrpType = CliOptionParser.GetIntOption(options, "var-grp-type", 1);
        int insertType = CliOptionParser.GetIntOption(options, "insert-type", 1);
        int? areaNo = TryGetNullableInt(options, "area-no");
        bool replaceExistingGroup = CliOptionParser.GetBoolOption(options, "replace-existing-group", true);
        IReadOnlyList<PlcInstanceVarItem>? variables = ParsePlcInstanceVars(options);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsurePlcInstanceVarsGroup(
                    projectPath,
                    new EnsurePlcInstanceVarsGroupRequest(
                        plcProjectName,
                        plcInstanceName,
                        groupName,
                        varGrpType,
                        insertType,
                        areaNo,
                        variables,
                        replaceExistingGroup));
                return StepExecutionOutcome.Success($"PLC vars group {plcInstanceName}/{groupName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearPlcInitSymbols(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");
        bool removeContainerWhenEmpty = CliOptionParser.GetBoolOption(options, "remove-container-when-empty", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearPlcInitSymbols(
                    projectPath,
                    new ClearPlcInitSymbolsRequest(plcProjectName, plcInstanceName, removeContainerWhenEmpty));
                return StepExecutionOutcome.Success($"PLC init symbols {plcInstanceName} cleared.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearPlcTaskPouOids(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");
        bool removeContainerWhenEmpty = CliOptionParser.GetBoolOption(options, "remove-container-when-empty", false);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearPlcTaskPouOids(
                    projectPath,
                    new ClearPlcTaskPouOidsRequest(plcProjectName, plcInstanceName, removeContainerWhenEmpty));
                return StepExecutionOutcome.Success($"PLC TaskPouOids {plcInstanceName} cleared.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearMappings(IReadOnlyDictionary<string, string> options)
    {
        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearMappings(projectPath, new ClearMappingsRequest());
                return StepExecutionOutcome.Success("Mappings cleared from tsproj.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearUnrestoredVarLinks(IReadOnlyDictionary<string, string> options)
    {
        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearUnrestoredVarLinks(projectPath, new ClearUnrestoredVarLinksRequest());
                return StepExecutionOutcome.Success("UnrestoredVarLinks cleared from tsproj.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojReplaceMappingsSection(IReadOnlyDictionary<string, string> options)
    {
        string mappingsXml = ReadTextPayload(options, "mappings-xml", "xml", "xml-file");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ReplaceMappingsSection(projectPath, new ReplaceMappingsSectionRequest(mappingsXml));
                return StepExecutionOutcome.Success("Mappings section replaced in tsproj.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojReplaceProjectIoSection(IReadOnlyDictionary<string, string> options)
    {
        string ioXml = ReadTextPayload(options, "io-xml", "xml", "xml-file");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ReplaceProjectIoSection(projectPath, new ReplaceProjectIoSectionRequest(ioXml));
                return StepExecutionOutcome.Success("Project Io section replaced in tsproj.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojReplaceDataTypesSection(IReadOnlyDictionary<string, string> options)
    {
        string dataTypesXml = ReadTextPayload(options, "data-types-xml", "xml", "xml-file");
        bool insertBeforeProject = CliOptionParser.GetBoolOption(options, "insert-before-project", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ReplaceDataTypesSection(projectPath, new ReplaceDataTypesSectionRequest(dataTypesXml, insertBeforeProject));
                return StepExecutionOutcome.Success("DataTypes section replaced in tsproj.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojReplaceSystemSettingsSection(IReadOnlyDictionary<string, string> options)
    {
        string settingsXml = ReadTextPayload(options, "settings-xml", "xml", "xml-file");
        bool insertBeforeTasks = CliOptionParser.GetBoolOption(options, "insert-before-tasks", true);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ReplaceSystemSettingsSection(projectPath, new ReplaceSystemSettingsSectionRequest(settingsXml, insertBeforeTasks));
                return StepExecutionOutcome.Success("System Settings section replaced in tsproj.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojApplyInstanceParameterPlan(IReadOnlyDictionary<string, string> options)
    {
        ApplyInstanceParameterPlanRequest request = ReadPlanRequest<InstanceParameterMutation, ApplyInstanceParameterPlanRequest>(
            options,
            items => new ApplyInstanceParameterPlanRequest(items),
            "json",
            "json-file");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ApplyInstanceParameterPlan(projectPath, request);
                return StepExecutionOutcome.Success($"Instance parameter plan applied with {request.Items.Count} item(s).", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearInstanceParameterValues(IReadOnlyDictionary<string, string> options)
    {
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        bool removeContainerWhenEmpty = CliOptionParser.GetBoolOption(options, "remove-container-when-empty", false);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearInstanceParameterValues(projectPath, new ClearInstanceParameterValuesRequest(instanceName, removeContainerWhenEmpty));
                return StepExecutionOutcome.Success($"ParameterValues cleared for {instanceName}.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojClearInstanceDataPointerValues(IReadOnlyDictionary<string, string> options)
    {
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        bool removeContainerWhenEmpty = CliOptionParser.GetBoolOption(options, "remove-container-when-empty", false);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ClearInstanceDataPointerValues(projectPath, new ClearInstanceDataPointerValuesRequest(instanceName, removeContainerWhenEmpty));
                return StepExecutionOutcome.Success($"DataPointerValues cleared for {instanceName}.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojApplyInstanceInterfacePointerPlan(IReadOnlyDictionary<string, string> options)
    {
        ApplyInstanceInterfacePointerPlanRequest request = ReadPlanRequest<InstanceInterfacePointerMutation, ApplyInstanceInterfacePointerPlanRequest>(
            options,
            items => new ApplyInstanceInterfacePointerPlanRequest(items),
            "json",
            "json-file");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ApplyInstanceInterfacePointerPlan(projectPath, request);
                return StepExecutionOutcome.Success($"Instance interface pointer plan applied with {request.Items.Count} item(s).", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojApplyInstanceDataPointerPlan(IReadOnlyDictionary<string, string> options)
    {
        ApplyInstanceDataPointerPlanRequest request = ReadPlanRequest<InstanceDataPointerMutation, ApplyInstanceDataPointerPlanRequest>(
            options,
            items => new ApplyInstanceDataPointerPlanRequest(items),
            "json",
            "json-file");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ApplyInstanceDataPointerPlan(projectPath, request);
                return StepExecutionOutcome.Success($"Instance data pointer plan applied with {request.Items.Count} item(s).", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureTaskPouOid(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");
        int priority = CliOptionParser.GetIntOption(options, "priority", 20);
        string? objectId = CliOptionParser.GetOption(options, "object-id");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureTaskPouOid(projectPath, new EnsureTaskPouOidRequest(plcProjectName, plcInstanceName, priority, objectId));
                return StepExecutionOutcome.Success($"PLC TaskPouOid {plcInstanceName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureInitSymbol(IReadOnlyDictionary<string, string> options)
    {
        string plcProjectName = CliOptionParser.RequireOption(options, "plc-project-name");
        string plcInstanceName = CliOptionParser.RequireOption(options, "plc-instance-name", "instance-name");
        string symbolName = CliOptionParser.RequireOption(options, "symbol-name");
        string objectId = CliOptionParser.RequireOption(options, "object-id");
        string typeName = CliOptionParser.GetOption(options, "type-name") ?? "OTCID";
        string typeGuid = CliOptionParser.GetOption(options, "type-guid") ?? "{18071995-0000-0000-0000-00000000000F}";
        string areaNo = CliOptionParser.GetOption(options, "area-no") ?? "#x00000003";

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureInitSymbol(
                    projectPath,
                    new EnsureInitSymbolRequest(plcProjectName, plcInstanceName, symbolName, objectId, typeName, typeGuid, areaNo));
                return StepExecutionOutcome.Success($"PLC init symbol {symbolName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureIoTaskImage(IReadOnlyDictionary<string, string> options)
    {
        string taskName = CliOptionParser.RequireOption(options, "task-name");
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        int imageId = CliOptionParser.GetIntOption(options, "image-id", 1);
        int sizeIn = CliOptionParser.GetIntOption(options, "size-in", 40);
        int sizeOut = CliOptionParser.GetIntOption(options, "size-out", 10);
        string pointerName = CliOptionParser.GetOption(options, "pointer-name") ?? "IoTaskImage";
        bool ensureDefaultTaskVariables = CliOptionParser.GetBoolOption(options, "ensure-default-task-variables", true);
        int inputRealCount = CliOptionParser.GetIntOption(options, "input-real-count", 10);
        int outputByteCount = CliOptionParser.GetIntOption(options, "output-byte-count", 10);
        bool? ioAtBegin = TryGetNullableBool(options, "io-at-begin");
        string? imageObjectId = CliOptionParser.GetOption(options, "image-object-id");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureIoTaskImage(
                    projectPath,
                    new EnsureIoTaskImageRequest(
                        taskName,
                        instanceName,
                        imageId,
                        sizeIn,
                        sizeOut,
                        pointerName,
                        ensureDefaultTaskVariables,
                        inputRealCount,
                        outputByteCount,
                        ioAtBegin,
                        imageObjectId));
                return StepExecutionOutcome.Success($"IO task image {taskName}/{instanceName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureParameter(IReadOnlyDictionary<string, string> options)
    {
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        string parameterName = CliOptionParser.RequireOption(options, "parameter-name");
        string? valueText = CliOptionParser.GetOption(options, "value-text");
        string? enumText = CliOptionParser.GetOption(options, "enum-text");
        string? stringText = CliOptionParser.GetOption(options, "string-text");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureParameterValue(projectPath, new EnsureParameterValueRequest(instanceName, parameterName, valueText, enumText, stringText));
                return StepExecutionOutcome.Success($"Parameter {parameterName} on {instanceName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureInterfacePointer(IReadOnlyDictionary<string, string> options)
    {
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        string pointerName = CliOptionParser.RequireOption(options, "pointer-name");
        string objectId = CliOptionParser.RequireOption(options, "object-id");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureInterfacePointerValue(projectPath, new EnsureInterfacePointerValueRequest(instanceName, pointerName, objectId));
                return StepExecutionOutcome.Success($"Interface pointer {pointerName} on {instanceName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureDataPointer(IReadOnlyDictionary<string, string> options)
    {
        string instanceName = CliOptionParser.RequireOption(options, "instance-name");
        string pointerName = CliOptionParser.RequireOption(options, "pointer-name");
        string objectId = CliOptionParser.RequireOption(options, "object-id");
        int areaNo = CliOptionParser.GetIntOption(options, "area-no", 0);
        int byteOffset = CliOptionParser.GetIntOption(options, "byte-offset", 0);
        int byteSize = CliOptionParser.GetIntOption(options, "byte-size", 0);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureDataPointerValue(projectPath, new EnsureDataPointerValueRequest(instanceName, pointerName, objectId, areaNo, byteOffset, byteSize));
                return StepExecutionOutcome.Success($"Data pointer {pointerName} on {instanceName} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojEnsureMappingLink(IReadOnlyDictionary<string, string> options)
    {
        string ownerAName = CliOptionParser.RequireOption(options, "owner-a-name");
        string ownerBName = CliOptionParser.RequireOption(options, "owner-b-name");
        string varA = CliOptionParser.RequireOption(options, "var-a");
        string varB = CliOptionParser.RequireOption(options, "var-b");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.EnsureMappingLink(projectPath, new EnsureMappingLinkRequest(ownerAName, ownerBName, varA, varB));
                return StepExecutionOutcome.Success($"Mapping link {ownerAName}:{varA} -> {ownerBName}:{varB} updated.", CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojUpsertElement(IReadOnlyDictionary<string, string> options)
    {
        TsprojElementUpsertRequest request = ReadJsonPayload<TsprojElementUpsertRequest>(options);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.UpsertElement(projectPath, request);
                return StepExecutionOutcome.Success(
                    $"Generic element {request.ElementName} upserted.",
                    CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojUpsertFragment(IReadOnlyDictionary<string, string> options)
    {
        TsprojFragmentUpsertRequest request = ReadJsonPayload<TsprojFragmentUpsertRequest>(options);

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.UpsertFragment(projectPath, request);
                return StepExecutionOutcome.Success(
                    $"Generic fragment upserted under {string.Join("/", request.ParentPath.Select(item => item.ElementName))}.",
                    CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojApplyMutationPlan(IReadOnlyDictionary<string, string> options)
    {
        ApplyTsprojMutationPlanRequest request = ReadJsonPayload<ApplyTsprojMutationPlanRequest>(options);
        int elementCount = request.ElementUpserts?.Count ?? 0;
        int fragmentCount = request.FragmentUpserts?.Count ?? 0;

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.ApplyMutationPlan(projectPath, request);
                return StepExecutionOutcome.Success(
                    $"Generic mutation plan applied with {elementCount} element upsert(s) and {fragmentCount} fragment upsert(s).",
                    CreateOutputs(("projectPath", projectPath)));
            });
    }

    private static StepExecutionOutcome ExecuteTsprojMergeFragment(IReadOnlyDictionary<string, string> options)
    {
        string parentElementName = CliOptionParser.RequireOption(options, "parent-element-name");
        string fragmentXml = ReadTextPayload(options, "fragment-xml", "xml", "xml-file");
        string? matchElementName = CliOptionParser.GetOption(options, "match-element-name");
        string? matchNameValue = CliOptionParser.GetOption(options, "match-name-value");
        bool replaceExisting = CliOptionParser.GetBoolOption(options, "replace-existing", true);
        string fragmentSource = CliOptionParser.RequireOption(options, "fragment-source");
        string targetParentPath = CliOptionParser.RequireOption(options, "target-parent-path");
        string fieldMeaning = CliOptionParser.RequireOption(options, "field-meaning");
        string verificationEvidence = CliOptionParser.RequireOption(options, "verification-evidence");

        return RunTsprojOperation(
            options,
            (tsproj, projectPath) =>
            {
                tsproj.MergeNamedElementFragment(
                    projectPath,
                    new MergeNamedElementFragmentRequest(
                        parentElementName,
                        fragmentXml,
                        matchElementName,
                        matchNameValue,
                        replaceExisting,
                        fragmentSource,
                        targetParentPath,
                        fieldMeaning,
                        verificationEvidence));
                return StepExecutionOutcome.Success($"Fragment merged into {parentElementName}.", CreateOutputs(("projectPath", projectPath)));
            });
    }

}
