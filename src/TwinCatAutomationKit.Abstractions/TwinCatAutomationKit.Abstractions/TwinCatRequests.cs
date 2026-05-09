namespace TwinCatAutomationKit.Abstractions;

public enum AdsReadDataType
{
    Boolean,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Double,
    String
}

public sealed record LaunchVisualStudioRequest(
    string ProgId = "VisualStudio.DTE.17.0",
    int StartupDelayMs = 5000,
    bool Visible = true);

public sealed record CreateTwinCatSolutionRequest(
    string SolutionDirectory,
    string SolutionName,
    string ProjectName);

public sealed record OpenTwinCatSolutionRequest(
    string SolutionPath,
    string ProjectPath);

public sealed record CreateCppProjectRequest(
    string ProjectName,
    string WizardId = "TcVersionedDriverWizard");

public sealed record CreateVisualStudioCppProjectRequest(
    string ProjectName,
    string? ProjectDirectory = null,
    string TemplateKind = "ConsoleApplication",
    IReadOnlyList<string>? CandidateTemplatePaths = null,
    string? PlatformToolset = null,
    bool AllowTemplateFallback = false);

public sealed record EnsureSolutionProjectDependencyRequest(
    string ProjectName,
    string DependsOnProjectName);

public enum CppProjectItemType
{
    Infer,
    ClCompile,
    ClInclude,
    ResourceCompile,
    None
}

public enum ProjectItemConflictPolicy
{
    FailIfExists,
    KeepExisting,
    ReplaceProjectRegistration
}

public enum ProjectItemWritePolicy
{
    FailIfMissing,
    FailIfNonEmpty,
    Overwrite
}

public sealed record CreateCppProjectItemRequest(
    string ProjectName,
    string RelativePath,
    CppProjectItemType ItemType = CppProjectItemType.Infer,
    string? Filter = null,
    bool AddToProject = true,
    bool CreatePhysicalFile = true,
    ProjectItemConflictPolicy ConflictPolicy = ProjectItemConflictPolicy.FailIfExists,
    bool AllowMsBuildFallback = true);

public sealed record WriteCppProjectItemContentRequest(
    string ProjectName,
    string RelativePath,
    string? ContentText = null,
    string? ContentFile = null,
    string Encoding = "utf-8",
    string NewLine = "preserve",
    ProjectItemWritePolicy WritePolicy = ProjectItemWritePolicy.Overwrite,
    bool RequireProjectRegistration = false);

public sealed record RemoveCppProjectItemRequest(
    string ProjectName,
    string RelativePath,
    CppProjectItemType ItemType = CppProjectItemType.Infer,
    bool DeletePhysicalFile = true,
    bool RemoveFilterEntry = true,
    bool IgnoreMissing = false);

public sealed record SetCppProjectPropertyRequest(
    string ProjectName,
    string PropertyName,
    string Value,
    string? Condition = null,
    string? PropertyGroupLabel = null);

public sealed record SetCppItemDefinitionPropertyRequest(
    string ProjectName,
    string ToolName,
    string PropertyName,
    string Value,
    string? Condition = null);

public sealed record SetCppProjectItemMetadataRequest(
    string ProjectName,
    string RelativePath,
    CppProjectItemType ItemType,
    string MetadataName,
    string Value,
    string? Condition = null);

public sealed record ProbeCppProjectModuleArtifactsRequest(
    string SolutionDirectory,
    string ProjectName);

public sealed record CppProjectModuleArtifactsResult(
    string ProjectDirectory,
    string ProjectFilePath,
    string ProjectTmcPath,
    bool ProjectDirectoryExists,
    bool ProjectFileExists,
    bool ProjectTmcExists,
    bool HasClassFactoryClassMapEntry,
    bool HasModuleEntryInTmc,
    bool HasModuleSourceInProjectFile,
    bool HasUsableModuleGuid,
    string? DetectedModuleName,
    string? DetectedModuleGuid,
    bool HasDefaultModuleSkeleton,
    string DiagnosticSummary);

public sealed record CreatePlcProjectRequest(
    string ProjectName,
    IReadOnlyList<string>? CandidateTemplatePaths = null,
    bool AllowOfflineFallback = true);

public sealed record CreateModuleRequest(
    string ProjectName,
    string ModuleName,
    string WizardId,
    bool AllowOfflineFallback = true);

public sealed record BootstrapCppModuleArtifactsRequest(
    string SolutionDirectory,
    string ProjectName,
    string ModuleName,
    string? ModuleGuid = null);

public sealed record BootstrapCppModuleArtifactsResult(
    string ProjectDirectory,
    string ModuleName,
    string ModuleClassName,
    string ModuleGuid,
    string ModuleHeaderPath,
    string ModuleSourcePath,
    string Summary);

public sealed record StartTmcCodeGeneratorRequest(
    string ProjectName,
    int PostStartDelayMs = 500);

public sealed record PublishModulesRequest(
    string ProjectName,
    int PostPublishDelayMs = 5000,
    int WaitForUpdatedTmcTimeoutMs = 30000);

public sealed record AddModuleInstanceRequest(
    string ProjectName,
    string ProjectTmcPath,
    string InstanceBaseName,
    string? ModuleClassName = null,
    bool AllowOfflineFallback = true);

public sealed record EnsureTaskRequest(
    string TaskName,
    int TaskSubtype,
    int Priority,
    int CycleTimeUs,
    int AmsPort,
    bool? IoAtBegin = null);

public sealed record BuildSolutionRequest(int TimeoutMs = 300000);

public sealed record CloseVisualStudioRequest(
    bool SaveBeforeClose = true);

public sealed record ActivateConfigurationRequest(
    bool SaveConfigurationArchive = true,
    string? ConfigurationArchivePath = null);

public sealed record SignTwinCatBinaryRequest(
    string CertificatePath,
    IReadOnlyList<string> TargetPaths,
    string? Password = null,
    bool Quiet = true,
    string? ToolPath = null);

public sealed record VerifyTwinCatBinarySignatureRequest(
    IReadOnlyList<string> TargetPaths,
    bool Quiet = true,
    string? ToolPath = null,
    bool AllowTestModeWarning = false);

public sealed record GrantTwinCatSigningCertificateRequest(
    string CertificatePath,
    string? Password = null,
    bool RemoveGrant = false,
    bool Quiet = true,
    string? ToolPath = null);

public sealed record SetTwinCatSigningLicenseRequest(
    string ProjectFilePath,
    string LicenseName = "optcnc",
    string? Password = null,
    bool EnableSigning = true);

public sealed record ExportTreeItemXmlRequest(
    string TreeItemPath,
    string DestinationPath,
    bool Recursive = false);

public sealed record EnsureTaskDefinitionRequest(
    string TaskName,
    int Priority,
    int CycleTimeNs,
    int AmsPort,
    bool? IoAtBegin = null);

public sealed record ClearTaskLayoutRequest(
    string TaskName,
    bool RemoveVars = true,
    bool RemoveImage = true);

public sealed record EnsureTaskVarsGroupRequest(
    string TaskName,
    string GroupName,
    int VarGrpType,
    int InsertType,
    string BaseVarName,
    string TypeName,
    int Count,
    int BitStride,
    int ExternalAddressStride,
    int? FirstExternalAddress = null,
    int StartIndex = 1,
    bool ReplaceExistingGroup = true);

public sealed record EnsureTaskImageRequest(
    string TaskName,
    int ImageId = 1,
    int AddressType = 1,
    int ImageType = 1,
    int SizeIn = 40,
    int SizeOut = 10,
    string ImageName = "Image",
    bool? IoAtBegin = null,
    bool ReplaceExistingImage = true);

public sealed record BindInstanceContextRequest(
    string InstanceName,
    string TaskObjectId,
    int Priority,
    int CycleTimeNs,
    int ContextId = 1,
    string? ContextName = null,
    bool IncludeCyclicCaller = true,
    bool RemoveCyclicCallerWhenExcluded = false);

public sealed record BindInstanceToTaskRequest(
    string InstanceName,
    string TaskObjectId,
    int Priority,
    int CycleTimeNs,
    bool IncludeCyclicCaller = true);

public sealed record EnsureCppInstanceRequest(
    string CppProjectName,
    string InstanceName,
    string ObjectId,
    int ContextId = 1,
    string ContextName = "FallbackCtx",
    int Priority = 0,
    int CycleTimeNs = 0);

public sealed record EnsurePlcInstanceRequest(
    string PlcProjectName,
    string PlcInstanceName);

public sealed record BindPlcInstanceToTaskRequest(
    string PlcProjectName,
    string PlcInstanceName,
    string PlcTaskName,
    string TaskObjectId,
    int Priority,
    int CycleTimeNs,
    int ContextId = 0);

public sealed record SetTaskAffinityRequest(
    string TaskName,
    string Affinity,
    bool EnableAdtTasks = true);

public sealed record SetPlcProjectPropertiesRequest(
    string PlcProjectName,
    string? ProjectFilePath = null,
    string? TmcFilePath = null,
    bool? ReloadTmc = null,
    int? AmsPort = null,
    string? FileArchiveSettings = null);

public sealed record SetPlcInstanceMetadataRequest(
    string PlcProjectName,
    string PlcInstanceName,
    string? TcSmClass = null,
    string? TmcPath = null,
    string? KeepUnrestoredLinks = null,
    string? Clsid = null,
    string? ClassFactory = null);

public sealed record PlcInstanceVarItem(
    string Name,
    string Type,
    int? BitOffset = null,
    int? ExternalAddress = null);

public sealed record ClearPlcInstanceVarsRequest(
    string PlcProjectName,
    string PlcInstanceName);

public sealed record EnsurePlcInstanceVarsGroupRequest(
    string PlcProjectName,
    string PlcInstanceName,
    string GroupName,
    int VarGrpType,
    int InsertType = 1,
    int? AreaNo = null,
    IReadOnlyList<PlcInstanceVarItem>? Variables = null,
    bool ReplaceExistingGroup = true);

public sealed record ClearPlcInitSymbolsRequest(
    string PlcProjectName,
    string PlcInstanceName,
    bool RemoveContainerWhenEmpty = true);

public sealed record ClearPlcTaskPouOidsRequest(
    string PlcProjectName,
    string PlcInstanceName,
    bool RemoveContainerWhenEmpty = false);

public sealed record InstanceParameterMutation(
    string InstanceName,
    string ParameterName,
    string? ValueText = null,
    string? EnumText = null,
    string? StringText = null);

public sealed record ApplyInstanceParameterPlanRequest(
    IReadOnlyList<InstanceParameterMutation> Items);

public sealed record ClearInstanceParameterValuesRequest(
    string InstanceName,
    bool RemoveContainerWhenEmpty = false);

public sealed record ClearInstanceDataPointerValuesRequest(
    string InstanceName,
    bool RemoveContainerWhenEmpty = false);

public sealed record InstanceInterfacePointerMutation(
    string InstanceName,
    string PointerName,
    string ObjectId);

public sealed record ApplyInstanceInterfacePointerPlanRequest(
    IReadOnlyList<InstanceInterfacePointerMutation> Items);

public sealed record InstanceDataPointerMutation(
    string InstanceName,
    string PointerName,
    string ObjectId,
    int AreaNo,
    int ByteOffset,
    int ByteSize);

public sealed record ApplyInstanceDataPointerPlanRequest(
    IReadOnlyList<InstanceDataPointerMutation> Items);

public sealed record ClearMappingsRequest();

public sealed record ClearUnrestoredVarLinksRequest();

public sealed record ReplaceMappingsSectionRequest(
    string MappingsXml);

public sealed record ReplaceProjectIoSectionRequest(
    string IoXml);

public sealed record ReplaceDataTypesSectionRequest(
    string DataTypesXml,
    bool InsertBeforeProject = true);

public sealed record ReplaceSystemSettingsSectionRequest(
    string SettingsXml,
    bool InsertBeforeTasks = true);

public sealed record EnsureParameterValueRequest(
    string InstanceName,
    string ParameterName,
    string? ValueText = null,
    string? EnumText = null,
    string? StringText = null);

public sealed record EnsureInterfacePointerValueRequest(
    string InstanceName,
    string PointerName,
    string ObjectId);

public sealed record EnsureDataPointerValueRequest(
    string InstanceName,
    string PointerName,
    string ObjectId,
    int AreaNo,
    int ByteOffset,
    int ByteSize);

public sealed record EnsureTaskPouOidRequest(
    string PlcProjectName,
    string PlcInstanceName,
    int Priority,
    string? ObjectId = null);

public sealed record EnsureInitSymbolRequest(
    string PlcProjectName,
    string PlcInstanceName,
    string SymbolName,
    string ObjectId,
    string TypeName = "OTCID",
    string TypeGuid = "{18071995-0000-0000-0000-00000000000F}",
    string AreaNo = "#x00000003");

public sealed record EnsureMappingLinkRequest(
    string OwnerAName,
    string OwnerBName,
    string VarA,
    string VarB);

public sealed record EnsureIoTaskImageRequest(
    string TaskName,
    string InstanceName,
    int ImageId = 1,
    int SizeIn = 40,
    int SizeOut = 10,
    string PointerName = "IoTaskImage",
    bool EnsureDefaultTaskVariables = true,
    int InputRealCount = 10,
    int OutputByteCount = 10,
    bool? IoAtBegin = true,
    string? ImageObjectId = null);

public sealed record MergeNamedElementFragmentRequest(
    string ParentElementName,
    string FragmentXml,
    string? MatchElementName = null,
    string? MatchNameValue = null,
    bool ReplaceExisting = true,
    string? FragmentSource = null,
    string? TargetParentPath = null,
    string? FieldMeaning = null,
    string? VerificationEvidence = null);

public enum TsprojMutationConflictPolicy
{
    ReplaceExisting,
    KeepExisting,
    FailOnConflict
}

public sealed record TsprojPathSegment(
    string ElementName,
    string? NameValue = null);

public sealed record TsprojXmlAttribute(
    string Name,
    string Value);

public sealed record TsprojXmlChildValue(
    string ElementName,
    string Value);

public sealed record TsprojElementUpsertRequest(
    IReadOnlyList<TsprojPathSegment> ParentPath,
    string ElementName,
    string? MatchNameValue = null,
    IReadOnlyList<TsprojXmlAttribute>? Attributes = null,
    IReadOnlyList<TsprojXmlChildValue>? ChildValues = null,
    TsprojMutationConflictPolicy ConflictPolicy = TsprojMutationConflictPolicy.ReplaceExisting);

public sealed record TsprojFragmentUpsertRequest(
    IReadOnlyList<TsprojPathSegment> ParentPath,
    string FragmentXml,
    string? MatchElementName = null,
    string? MatchNameValue = null,
    TsprojMutationConflictPolicy ConflictPolicy = TsprojMutationConflictPolicy.ReplaceExisting);

public sealed record ApplyTsprojMutationPlanRequest(
    IReadOnlyList<TsprojElementUpsertRequest>? ElementUpserts = null,
    IReadOnlyList<TsprojFragmentUpsertRequest>? FragmentUpserts = null);

public sealed record AdsReadRequest(
    string NetId,
    int Port,
    string SymbolPath,
    AdsReadDataType DataType,
    bool AutoReconnect = false);

public sealed record AdsReadSymbolRequest(
    string SymbolPath,
    AdsReadDataType DataType);

public sealed record AdsReadSymbolsRequest(
    string NetId,
    int Port,
    IReadOnlyList<AdsReadSymbolRequest> Symbols,
    bool AutoReconnect = false,
    bool ContinueOnError = false);

public sealed record AdsPortScanRequest(
    string NetId,
    IReadOnlyList<int> Ports);

public sealed record TwinCatProjectInfo(
    string SolutionPath,
    string ProjectPath,
    string SolutionDirectory);

public sealed record VisualStudioCppProjectInfo(
    string ProjectFilePath,
    string ProjectGuid,
    string ProjectDirectory);

public sealed record TwinCatNodeInfo(
    string TreeItemPath,
    string DisplayName,
    string? ObjectId = null,
    string? FilePath = null,
    bool UsedFallback = false);

public sealed record SolutionProjectDependencyResult(
    string ProjectGuid,
    string DependsOnProjectGuid);

public sealed record CppProjectItemResult(
    string ProjectFilePath,
    string FilePath,
    CppProjectItemType ItemType,
    string? Filter,
    bool AddedToProject);

public sealed record CppProjectItemContentResult(
    string FilePath,
    string Sha256,
    long BytesWritten);

public sealed record RemoveCppProjectItemResult(
    bool RemovedFromProject,
    bool DeletedFile);

public sealed record CppProjectPropertyResult(
    string ProjectFilePath,
    string PropertyName,
    string? Condition);

public sealed record CppItemDefinitionPropertyResult(
    string ProjectFilePath,
    string ToolName,
    string PropertyName,
    string? Condition);

public sealed record CppProjectItemMetadataResult(
    string ProjectFilePath,
    string RelativePath,
    string MetadataName,
    string? Condition);

public sealed record BuildResult(
    bool Succeeded,
    int LastBuildInfo,
    string? OutputText = null);

public sealed record PublishModulesResult(
    bool Succeeded,
    string? UpdatedTmcPath,
    bool Updated = false);

public sealed record ActivationResult(
    bool Succeeded,
    string? ConfigurationArchivePath,
    string? ActivationCommand,
    IReadOnlyList<string> AttemptedCommands);

public sealed record TwinCatSignToolResult(
    bool Succeeded,
    int ExitCode,
    string ToolPath,
    string RedactedCommandLine,
    IReadOnlyList<string> TargetPaths,
    string Output);

public sealed record TwinCatSigningLicenseResult(
    string ProjectFilePath,
    string LicenseName,
    bool EnableSigning,
    bool PasswordWritten);

public sealed record AdsReadResult(
    bool Succeeded,
    string SymbolPath,
    string? Value,
    string? ErrorMessage);

public sealed record AdsReadSymbolResult(
    bool Succeeded,
    string SymbolPath,
    AdsReadDataType DataType,
    string? Value,
    string? ErrorMessage);

public sealed record AdsReadSymbolsResult(
    string NetId,
    int Port,
    IReadOnlyList<AdsReadSymbolResult> Symbols)
{
    public bool Succeeded => Symbols.Count > 0 && Symbols.All(symbol => symbol.Succeeded);

    public int SucceededCount => Symbols.Count(symbol => symbol.Succeeded);

    public int FailedCount => Symbols.Count(symbol => !symbol.Succeeded);
}

public sealed record AdsPortProbeResult(
    int Port,
    bool Succeeded,
    string? AdsState,
    short? DeviceState,
    string? DeviceName,
    string? DeviceVersion,
    string? ErrorMessage);

public sealed record AdsPortScanResult(
    string NetId,
    IReadOnlyList<AdsPortProbeResult> Ports)
{
    public bool AnySucceeded => Ports.Any(port => port.Succeeded);
}
