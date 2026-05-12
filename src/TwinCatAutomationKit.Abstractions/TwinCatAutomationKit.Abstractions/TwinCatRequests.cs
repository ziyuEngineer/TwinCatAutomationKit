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
    bool Visible = true,
    bool SuppressUi = true,
    int LaunchTimeoutMs = 60000,
    bool EnableDialogAutoDismiss = true,
    int DialogPollIntervalMs = 500,
    bool AttachToExisting = false,
    string? RootSuffix = null,
    string? DteHostPath = null,
    bool PreferDteHostLaunch = false);

public sealed record CleanupDteHostProcessesRequest(
    IReadOnlyList<string>? ProcessNames = null,
    IReadOnlyList<int>? ProcessIds = null,
    bool DryRun = true,
    bool IncludeWindowed = false,
    bool KillProcessTree = true);

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

public sealed record CreateScopeProjectRequest(
    string ProjectName,
    string? ProjectDirectory = null,
    string? ConfigurationFileName = null,
    bool CreateEmptyConfiguration = true,
    bool AllowSolutionFileFallback = true);

public sealed record CreateIoDeviceRequest(
    string Name,
    int SubType = 111,
    string ParentTreeItemPath = "TIID",
    string? Before = null,
    string? VInfo = null,
    bool? Disabled = null,
    bool AllowExisting = true,
    int PostCreateDelayMs = 500);

public sealed record CreateEthercatBoxRequest(
    string ParentTreeItemPath,
    string Name,
    int SubType = 9099,
    string? Before = null,
    string? ProductRevision = null,
    string? VInfo = null,
    bool? Disabled = null,
    bool AllowExisting = true,
    int PostCreateDelayMs = 500);

public sealed record GenerateIoMappingsRequest(
    bool SuppressUi = true,
    bool AllowDteCommandFallback = false,
    int TimeoutMs = 120000);

public sealed record SearchIoDevicesRequest(
    bool SuppressUi = true,
    int TimeoutMs = 120000);

public sealed record ReloadIoDevicesRequest(
    bool SuppressUi = true,
    int TimeoutMs = 120000);

public sealed record ApplyIoTreePlanRequest(
    IReadOnlyList<CreateIoDeviceRequest>? Devices = null,
    IReadOnlyList<CreateEthercatBoxRequest>? Boxes = null);

public sealed record EtherCatProductRevisionRequirement(
    string ProductRevision,
    string? ProductCode = null,
    string? RevisionNo = null);

public sealed record AssertEtherCatProductRevisionsRequest(
    IReadOnlyList<string>? ProductRevisions = null,
    IReadOnlyList<EtherCatProductRevisionRequirement>? Items = null,
    IReadOnlyList<string>? SearchDirectories = null,
    bool IncludeHiddenTypes = false);

public sealed record ScopeAdsChannelDefinition(
    string Name,
    string SymbolName,
    string AmsNetId,
    int TargetPort,
    string DataType = "UINT32",
    long IndexGroup = 0,
    long IndexOffset = 0,
    int VariableSize = 4,
    int BaseSampleTime = 10000,
    bool Enabled = true,
    string DisplayColor = "Black");

public sealed record ScopeChartChannelDefinition(
    string Name,
    string AcquisitionName,
    string DisplayColor = "-16744448",
    int SortPriority = 10,
    int LineWidth = 2);

public sealed record EnsureScopeConfigurationRequest(
    string ConfigurationFilePath,
    string ScopeName = "Scope Project",
    string MainServer = "127.0.0.1.1.1",
    long RecordTime = 6000000000,
    string StopMode = "AutoStop",
    string ChartName = "YT Chart",
    bool ReplaceChannels = false,
    IReadOnlyList<ScopeAdsChannelDefinition>? AdsChannels = null,
    IReadOnlyList<ScopeChartChannelDefinition>? ChartChannels = null);

public sealed record ScopeConfigurationChannelShape(
    string Name,
    string? SymbolName = null,
    string? AcquisitionName = null);

public sealed record AssertScopeConfigurationShapeRequest(
    string ConfigurationFilePath,
    int? ExpectedAdsChannelCount = null,
    int? ExpectedChartChannelCount = null,
    string? ExpectedScopeName = null,
    string? ExpectedChartName = null,
    IReadOnlyList<ScopeConfigurationChannelShape>? AdsChannels = null,
    IReadOnlyList<ScopeConfigurationChannelShape>? ChartChannels = null);

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
    int PostStartDelayMs = 500,
    int WaitForUpdatedTmcTimeoutMs = 30000);

public sealed record PublishModulesRequest(
    string ProjectName,
    int PostPublishDelayMs = 5000,
    int WaitForUpdatedTmcTimeoutMs = 30000,
    bool RunTmcCodeGeneratorFirst = false);

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

public enum BuildSolutionEngine
{
    Dte,
    CommandLine,
    MsBuildProjects
}

public sealed record BuildSolutionRequest(
    int TimeoutMs = 300000,
    BuildSolutionEngine BuildEngine = BuildSolutionEngine.Dte,
    string Configuration = "Release",
    string Platform = "TwinCAT OS (x64)",
    string? DevenvPath = null,
    string? LogFilePath = null,
    string? MsBuildPath = null,
    IReadOnlyList<string>? ProjectPaths = null);

public sealed record CloseVisualStudioRequest(
    bool SaveBeforeClose = true);

public sealed record ActivateConfigurationRequest(
    bool SaveConfigurationArchive = true,
    string? ConfigurationArchivePath = null,
    bool SuppressUi = true,
    bool AllowDteCommandFallback = false,
    int ActivationTimeoutMs = 120000);

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
    bool? IoAtBegin = null,
    int? TaskId = null);

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
    string? Affinity = null,
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

public sealed record SetCppInstanceMetadataRequest(
    string InstanceName,
    bool? Disabled = null,
    string? KeepUnrestoredLinks = null,
    string? ClassFactoryId = null,
    string? ObjectId = null);

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
    int ByteSize,
    int? ArrayIndex = null);

public sealed record ApplyInstanceDataPointerPlanRequest(
    IReadOnlyList<InstanceDataPointerMutation> Items);

public sealed record ExpectedDataPointerValueShape(
    string PointerName,
    int? DataRecordCount = null,
    IReadOnlyList<int>? ArrayIndexes = null);

public sealed record ExpectedMappingLinkShape(
    string OwnerAName,
    string OwnerBName,
    string VarA,
    string VarB);

public sealed record AssertDataPointerShapeRequest(
    string InstanceName,
    IReadOnlyList<ExpectedDataPointerValueShape>? DataPointers = null,
    int? ExpectedDataPointerRecordCount = null,
    IReadOnlyList<ExpectedMappingLinkShape>? MappingLinks = null,
    int? ExpectedDataPointerMappingLinkCount = null,
    int? ExpectedRootMappingLinkCount = null);

public sealed record DataPointerValueShape(
    string PointerName,
    int DataRecordCount,
    IReadOnlyList<int> ArrayIndexes);

public sealed record MappingLinkShape(
    string OwnerAName,
    string OwnerBName,
    string VarA,
    string VarB);

public sealed record AssertDataPointerShapeResult(
    bool Succeeded,
    string InstanceName,
    int DataPointerRecordCount,
    int DataPointerMappingLinkCount,
    int RootMappingLinkCount,
    IReadOnlyList<DataPointerValueShape> DataPointers,
    IReadOnlyList<MappingLinkShape> MappingLinks,
    IReadOnlyList<string> Errors,
    string Summary);

public sealed record ExpectedIoDeviceShape(
    int DeviceId,
    string? Name = null,
    int? BoxCount = null,
    string? InfoImageId = null,
    int? ImageCount = null,
    int? DirectBoxCount = null,
    int? DirectImageCount = null,
    IReadOnlyDictionary<string, int>? DirectChildElementCounts = null,
    IReadOnlyDictionary<string, int>? EtherCatChildElementCounts = null,
    IReadOnlyDictionary<string, int>? EthernetChildElementCounts = null);

public sealed record ExpectedIoBoxShape(
    int DeviceId,
    int BoxId,
    string? Name = null,
    int? PdoCount = null,
    string? ImageId = null,
    int? ParentBoxId = null,
    string? BoxFlags = null,
    int? PdoEntryCount = null,
    int? DirectChildBoxCount = null,
    int? TotalChildBoxCount = null,
    IReadOnlyDictionary<string, int>? DirectChildElementCounts = null,
    IReadOnlyDictionary<string, int>? EtherCatChildElementCounts = null);

public sealed record AssertIoTopologyShapeRequest(
    int? ExpectedDeviceCount = null,
    int? ExpectedBoxCount = null,
    int? ExpectedImageCount = null,
    int? ExpectedPdoCount = null,
    int? ExpectedPdoEntryCount = null,
    int? ExpectedMappingInfoCount = null,
    int? ExpectedOwnerACount = null,
    int? ExpectedRootMappingLinkCount = null,
    IReadOnlyList<ExpectedIoDeviceShape>? Devices = null,
    IReadOnlyList<ExpectedIoBoxShape>? Boxes = null,
    IReadOnlyList<ExpectedMappingLinkShape>? MappingLinks = null);

public sealed record IoDeviceShape(
    int DeviceId,
    string Name,
    int BoxCount,
    string? InfoImageId,
    int ImageCount,
    int DirectBoxCount,
    int DirectImageCount,
    IReadOnlyDictionary<string, int> DirectChildElementCounts,
    IReadOnlyDictionary<string, int> EtherCatChildElementCounts,
    IReadOnlyDictionary<string, int> EthernetChildElementCounts);

public sealed record IoBoxShape(
    int DeviceId,
    int? ParentBoxId,
    int BoxId,
    string Name,
    int PdoCount,
    string? ImageId,
    string? BoxFlags,
    int PdoEntryCount,
    int DirectChildBoxCount,
    int TotalChildBoxCount,
    IReadOnlyDictionary<string, int> DirectChildElementCounts,
    IReadOnlyDictionary<string, int> EtherCatChildElementCounts);

public sealed record AssertIoTopologyShapeResult(
    bool Succeeded,
    int DeviceCount,
    int BoxCount,
    int ImageCount,
    int PdoCount,
    int PdoEntryCount,
    int MappingInfoCount,
    int OwnerACount,
    int RootMappingLinkCount,
    IReadOnlyList<IoDeviceShape> Devices,
    IReadOnlyList<IoBoxShape> Boxes,
    IReadOnlyList<MappingLinkShape> MappingLinks,
    IReadOnlyList<string> Errors,
    string Summary);

public sealed record AssertIoImageReferencesRequest(
    int? ExpectedRootImageDataCount = null,
    int? ExpectedDeviceImageCount = null,
    int? ExpectedImageReferenceCount = null,
    bool RequireDeviceImageForInfoImageId = true,
    bool RequireImageIdBacking = true,
    IReadOnlyList<string>? AllowedUnbackedImageIds = null);

public sealed record IoDeviceImageReferenceShape(
    int DeviceId,
    string Name,
    string? InfoImageId,
    int DirectImageCount,
    IReadOnlyList<string> DirectImageIds);

public sealed record IoImageIdReferenceShape(
    string OwnerKind,
    int? DeviceId,
    int? BoxId,
    string? OwnerName,
    string ImageId,
    string? BackingKind,
    bool Backed);

public sealed record AssertIoImageReferencesResult(
    bool Succeeded,
    int RootImageDataCount,
    int DeviceImageCount,
    int DeviceWithInfoImageCount,
    int DeviceInfoWithoutImageCount,
    int ImageReferenceCount,
    int BackedImageReferenceCount,
    int UnbackedImageReferenceCount,
    IReadOnlyList<string> RootImageDataIds,
    IReadOnlyList<IoDeviceImageReferenceShape> Devices,
    IReadOnlyList<IoImageIdReferenceShape> ImageReferences,
    IReadOnlyList<string> Errors,
    string Summary);

public sealed record DescribeIoTopologyRequest(
    bool IncludeDevices = true,
    bool IncludeBoxes = true,
    bool IncludePdos = true,
    bool IncludeMappings = true,
    bool IncludeAttributes = false,
    int MaxItemsPerCollection = 0);

public sealed record IoTopologyAttributeDescription(
    string Name,
    string Value);

public sealed record IoTopologyDeviceDescription(
    int DeviceId,
    string Name,
    string? DevType,
    bool Disabled,
    string? DevFlags,
    string? AmsPort,
    string? AmsNetId,
    string? RemoteName,
    string? InfoImageId,
    int DirectBoxCount,
    int TotalBoxCount,
    int DirectImageCount,
    int TotalImageCount,
    IReadOnlyDictionary<string, int> DirectChildElementCounts,
    IReadOnlyDictionary<string, int> EtherCatChildElementCounts,
    IReadOnlyDictionary<string, int> EthernetChildElementCounts,
    IReadOnlyList<IoTopologyAttributeDescription> Attributes,
    IReadOnlyList<IoTopologyAttributeDescription> EtherCatAttributes,
    IReadOnlyList<IoTopologyAttributeDescription> EthernetAttributes);

public sealed record IoTopologyImageDescription(
    int DeviceId,
    string? ParentKind,
    int? ParentBoxId,
    string Id,
    string? AddrType,
    string? ImageType,
    string? ImageFlags,
    string? SizeIn,
    string? SizeOut,
    string Name,
    IReadOnlyList<IoTopologyAttributeDescription> Attributes);

public sealed record IoTopologyBoxDescription(
    int DeviceId,
    int? ParentBoxId,
    int BoxId,
    string Name,
    string? BoxType,
    bool Disabled,
    string? BoxFlags,
    string? ImageId,
    int DirectChildBoxCount,
    int TotalChildBoxCount,
    int PdoCount,
    int PdoEntryCount,
    IReadOnlyDictionary<string, int> DirectChildElementCounts,
    IReadOnlyDictionary<string, int> EtherCatChildElementCounts,
    IReadOnlyList<IoTopologyAttributeDescription> Attributes,
    IReadOnlyList<IoTopologyAttributeDescription> EtherCatAttributes);

public sealed record IoTopologyPdoEntryDescription(
    string? Name,
    string? Index,
    string? Sub,
    string? Type,
    string? BitLen,
    IReadOnlyList<IoTopologyAttributeDescription> Attributes);

public sealed record IoTopologyPdoDescription(
    int DeviceId,
    int BoxId,
    string BoxName,
    string Name,
    string Index,
    string? InOut,
    string? Flags,
    string? SyncMan,
    int EntryCount,
    IReadOnlyList<IoTopologyPdoEntryDescription> Entries,
    IReadOnlyList<IoTopologyAttributeDescription> Attributes);

public sealed record IoTopologyMappingInfoDescription(
    string Identifier,
    string Id,
    IReadOnlyList<IoTopologyAttributeDescription> Attributes);

public sealed record IoTopologyOwnerDescription(
    string OwnerAName,
    string? Prefix,
    string? Type,
    int OwnerBCount,
    int LinkCount);

public sealed record DescribeIoTopologyResult(
    bool Succeeded,
    string ProjectPath,
    int DeviceCount,
    int BoxCount,
    int ImageCount,
    int PdoCount,
    int PdoEntryCount,
    int MappingInfoCount,
    int OwnerACount,
    int RootMappingLinkCount,
    bool Truncated,
    IReadOnlyList<IoTopologyDeviceDescription> Devices,
    IReadOnlyList<IoTopologyBoxDescription> Boxes,
    IReadOnlyList<IoTopologyImageDescription> Images,
    IReadOnlyList<IoTopologyPdoDescription> Pdos,
    IReadOnlyList<IoTopologyMappingInfoDescription> MappingInfos,
    IReadOnlyList<IoTopologyOwnerDescription> Owners,
    IReadOnlyList<MappingLinkShape> MappingLinks,
    string Summary);

public sealed record CompareIoTopologyRequest(
    string ReferenceProjectPath,
    bool IncludeMappings = true,
    bool IncludePdos = true,
    bool IncludeAttributes = false,
    int MaxDifferences = 200);

public sealed record IoTopologyCountComparison(
    string Name,
    int Reference,
    int Candidate,
    bool Matches);

public sealed record IoTopologyDifference(
    string Kind,
    string Key,
    string Field,
    string? Reference,
    string? Candidate);

public sealed record CompareIoTopologyResult(
    bool Succeeded,
    string ReferenceProjectPath,
    string CandidateProjectPath,
    IReadOnlyList<IoTopologyCountComparison> Counts,
    IReadOnlyList<IoTopologyDifference> Differences,
    bool Truncated,
    string Summary);

public sealed record ClearMappingsRequest();

public sealed record ClearUnrestoredVarLinksRequest();

public sealed record ReplaceMappingsSectionRequest(
    string MappingsXml);

public sealed record ReplaceProjectIoSectionRequest(
    string IoXml);

public sealed record EnsureIoSectionRequest();

public sealed record EnsureIoSectionResult(
    bool Created,
    int DeviceCount,
    string ProjectPath);

public sealed record IoRawXmlFragment(
    string FragmentXml,
    string? MatchElementName = null,
    string? MatchNameValue = null,
    bool ReplaceExisting = true,
    string? FragmentSource = null,
    string? TargetParentPath = null,
    string? FieldMeaning = null,
    string? VerificationEvidence = null);

public sealed record IoAddressInfo(
    string? TcComObjectId = null,
    string? PnpDeviceDesc = null,
    string? PnpDeviceName = null,
    string? PnpDeviceData = null,
    string? RawXml = null,
    string? FragmentSource = null,
    string? TargetParentPath = null,
    string? FieldMeaning = null,
    string? VerificationEvidence = null);

public sealed record IoImageDefinition(
    int Id,
    int AddrType,
    int ImageType,
    string Name,
    int? SizeIn = null,
    int? SizeOut = null,
    string? ImageFlags = null);

public sealed record IoStructuredElement(
    string ElementName,
    string? Value = null,
    IReadOnlyList<TsprojXmlAttribute>? Attributes = null,
    IReadOnlyList<IoStructuredElement>? Children = null);

public sealed record EnsureIoDeviceRequest(
    int DeviceId,
    string Name,
    int DevType,
    bool? Disabled = null,
    string? DevFlags = null,
    int? AmsPort = null,
    string? AmsNetId = null,
    string? RemoteName = null,
    int? InfoImageId = null,
    IoAddressInfo? AddressInfo = null,
    IReadOnlyList<IoImageDefinition>? Images = null,
    IReadOnlyList<TsprojXmlAttribute>? EtherCatAttributes = null,
    IReadOnlyList<IoStructuredElement>? EtherCatElements = null,
    bool ReplaceEtherCatElements = true,
    IReadOnlyList<TsprojXmlAttribute>? EthernetAttributes = null,
    IReadOnlyList<IoStructuredElement>? EthernetElements = null,
    bool ReplaceEthernetElements = true,
    IReadOnlyList<IoRawXmlFragment>? ExtraFragments = null);

public sealed record EnsureEthercatBoxRequest(
    int DeviceId,
    int? ParentBoxId,
    int BoxId,
    string Name,
    int BoxType,
    bool? Disabled = null,
    string? BoxFlags = null,
    int? ImageId = null,
    IReadOnlyList<TsprojXmlAttribute>? EtherCatAttributes = null,
    IReadOnlyList<TsprojXmlChildValue>? EtherCatChildValues = null,
    IReadOnlyList<IoStructuredElement>? EtherCatElements = null,
    bool ReplaceEtherCatElements = true,
    IReadOnlyList<IoRawXmlFragment>? ExtraFragments = null);

public sealed record IoPdoEntry(
    string? Name = null,
    string? Index = null,
    string? Sub = null,
    string? Type = null,
    IReadOnlyList<TsprojXmlAttribute>? Attributes = null,
    IReadOnlyList<TsprojXmlChildValue>? ChildValues = null);

public sealed record EnsureIoPdoRequest(
    int DeviceId,
    int BoxId,
    string Name,
    string Index,
    string? InOut = null,
    string? Flags = null,
    int? SyncMan = null,
    IReadOnlyList<IoPdoEntry>? Entries = null,
    bool ReplaceExistingEntries = true,
    IReadOnlyList<IoRawXmlFragment>? ExtraFragments = null);

public sealed record EnsureIoBoxImageRequest(
    int DeviceId,
    int BoxId,
    int ImageId,
    IReadOnlyList<TsprojXmlChildValue>? MetadataValues = null,
    IReadOnlyList<IoRawXmlFragment>? MetadataFragments = null);

public sealed record EnsureMappingInfoRequest(
    string Identifier,
    string Id,
    IReadOnlyList<TsprojXmlAttribute>? Attributes = null);

public sealed record EnsureIoMappingLinkRequest(
    string OwnerAName,
    string OwnerBName,
    string VarA,
    string VarB,
    string? OwnerAPrefix = null,
    string? OwnerAType = null,
    string? OwnerBPrefix = null,
    string? OwnerBType = null,
    IReadOnlyList<TsprojXmlAttribute>? LinkAttributes = null,
    bool ReplaceExistingAttributes = true);

public sealed record ApplyIoTopologyPlanRequest(
    IReadOnlyList<EnsureIoDeviceRequest>? Devices = null,
    IReadOnlyList<EnsureEthercatBoxRequest>? Boxes = null,
    IReadOnlyList<EnsureIoPdoRequest>? Pdos = null,
    IReadOnlyList<EnsureIoBoxImageRequest>? BoxImages = null,
    IReadOnlyList<EnsureMappingInfoRequest>? MappingInfos = null,
    IReadOnlyList<EnsureIoMappingLinkRequest>? Links = null,
    bool EnsureIoSection = true);

public sealed record ApplyIoTopologyPlanResult(
    bool Succeeded,
    string ProjectPath,
    int DeviceCount,
    int BoxCount,
    int PdoCount,
    int BoxImageCount,
    int MappingInfoCount,
    int LinkCount,
    string Summary);

public sealed record ReplaceDataTypesSectionRequest(
    string DataTypesXml,
    bool InsertBeforeProject = true);

public sealed record ReplaceSystemSettingsSectionRequest(
    string SettingsXml,
    bool InsertBeforeTasks = true);

public sealed record SystemCpuSetting(
    int? CpuId = null,
    IReadOnlyList<TsprojXmlAttribute>? Attributes = null);

public sealed record EnsureSystemSettingsRequest(
    int? CpuId = null,
    int? IoIdleTaskPriority = null,
    bool InsertBeforeTasks = true,
    int? MaxCpus = null,
    int? NonWinCpus = null,
    IReadOnlyList<SystemCpuSetting>? CpuEntries = null,
    bool ReplaceCpuEntries = false);

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
    int ByteSize,
    int? ArrayIndex = null);

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

public sealed record ExpectedAdsPortState(
    int Port,
    string AdsState = "Run",
    short? DeviceState = null);

public sealed record AssertAdsStateRequest(
    string NetId,
    IReadOnlyList<ExpectedAdsPortState> ExpectedPorts);

public sealed record MarkEventLogWindowRequest(
    string LogName = "Application",
    string ProviderName = "TcSysSrv",
    string? MarkerFilePath = null);

public sealed record EventLogWindowMarker(
    string LogName,
    string ProviderName,
    DateTimeOffset MarkedAt,
    int? LastEntryIndex,
    string MarkerId)
{
    public int? LastLogEntryIndex { get; init; }
}

public sealed record AssertEventLogWindowRequest(
    EventLogWindowMarker? Marker = null,
    string? MarkerFilePath = null,
    string LogName = "Application",
    string ProviderName = "TcSysSrv",
    int LookbackSeconds = 300,
    bool FailOnErrorOrCritical = true,
    bool FailOnConfigAdsState = true,
    IReadOnlyList<string>? FailMessageContains = null,
    int MaxEvents = 50);

public sealed record EventLogEntrySnapshot(
    int Index,
    DateTimeOffset TimeGenerated,
    string Source,
    string EntryType,
    long InstanceId,
    string Message);

public sealed record AssertEventLogWindowResult(
    bool Succeeded,
    string LogName,
    string ProviderName,
    DateTimeOffset WindowStart,
    int ObservedEventCount,
    int ErrorOrCriticalCount,
    int ConfigAdsStateCount,
    IReadOnlyList<EventLogEntrySnapshot> Events,
    IReadOnlyList<string> Errors,
    string Summary);

public sealed record AssertProcessCrashWindowRequest(
    EventLogWindowMarker? Marker = null,
    string? MarkerFilePath = null,
    string LogName = "Application",
    int LookbackSeconds = 300,
    IReadOnlyList<string>? ProviderNames = null,
    IReadOnlyList<string>? ProcessNames = null,
    IReadOnlyList<string>? ModuleNames = null,
    IReadOnlyList<string>? MessageContains = null,
    int MaxEvents = 100);

public sealed record AssertProcessCrashWindowResult(
    bool Succeeded,
    string LogName,
    DateTimeOffset WindowStart,
    int ObservedEventCount,
    int MatchingEventCount,
    IReadOnlyList<EventLogEntrySnapshot> Events,
    IReadOnlyList<EventLogEntrySnapshot> MatchingEvents,
    IReadOnlyList<string> Errors,
    string Summary);

public sealed record TwinCatProjectInfo(
    string SolutionPath,
    string ProjectPath,
    string SolutionDirectory);

public sealed record VisualStudioCppProjectInfo(
    string ProjectFilePath,
    string ProjectGuid,
    string ProjectDirectory);

public sealed record ScopeProjectInfo(
    string ProjectFilePath,
    string ProjectGuid,
    string ProjectDirectory,
    string? ConfigurationFilePath,
    bool AddedToSolution,
    bool UsedSolutionFileFallback);

public sealed record ScopeConfigurationResult(
    string ConfigurationFilePath,
    string ScopeName,
    int AdsChannelCount,
    int ChartChannelCount,
    string Summary);

public sealed record ScopeConfigurationShapeResult(
    bool Succeeded,
    string ConfigurationFilePath,
    string? ScopeName,
    string? ChartName,
    int AdsChannelCount,
    int ChartChannelCount,
    IReadOnlyList<string> Errors,
    string Summary);

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
    string? OutputText = null,
    string BuildEngine = "dte",
    int? ExitCode = null,
    string? CommandLine = null,
    string? LogFilePath = null);

public sealed record EngineeringCommandResult(
    bool Succeeded,
    string Command,
    IReadOnlyList<string> AttemptedCommands);

public sealed record ApplyIoTreePlanResult(
    bool Succeeded,
    int DeviceCount,
    int BoxCount,
    IReadOnlyList<TwinCatNodeInfo> Nodes,
    string Summary);

public sealed record DteHostProcessCleanupItem(
    int ProcessId,
    string ProcessName,
    string? MainWindowTitle = null,
    string? FilePath = null,
    DateTimeOffset? StartTime = null,
    bool Matched = false,
    bool Killed = false,
    string? ErrorMessage = null);

public sealed record CleanupDteHostProcessesResult(
    bool Succeeded,
    bool DryRun,
    int MatchedCount,
    int KilledCount,
    IReadOnlyList<DteHostProcessCleanupItem> Processes,
    string Summary);

public sealed record EtherCatProductRevisionAssertion(
    string ProductRevision,
    bool Succeeded,
    string? ProductCode = null,
    string? RevisionNo = null,
    string? TypeName = null,
    string? SourceFilePath = null,
    bool HiddenType = false,
    string? ErrorMessage = null);

public sealed record AssertEtherCatProductRevisionsResult(
    bool Succeeded,
    int RequestedCount,
    int MatchedCount,
    int MissingCount,
    int ScannedFileCount,
    IReadOnlyList<string> SearchDirectories,
    IReadOnlyList<EtherCatProductRevisionAssertion> Assertions,
    string Summary);

public sealed record PublishModulesResult(
    bool Succeeded,
    string? UpdatedTmcPath,
    bool Updated = false);

public sealed record StartTmcCodeGeneratorResult(
    bool Succeeded,
    string? UpdatedTmcPath,
    bool Updated = false);

public sealed record VerifyTmcDataAreasRequest(
    string ProjectTmcPath,
    IReadOnlyList<TmcModuleExpectation> Modules,
    bool FailOnUnexpectedModule = false);

public sealed record TmcModuleExpectation(
    string ModuleName,
    IReadOnlyList<TmcDataAreaExpectation> DataAreas);

public sealed record TmcDataAreaExpectation(
    string Name,
    string? AreaType = null,
    IReadOnlyList<TmcSymbolExpectation>? Symbols = null);

public sealed record TmcSymbolExpectation(
    string Name,
    string? TypeName = null);

public sealed record VerifyTmcDataAreasResult(
    bool Succeeded,
    string ProjectTmcPath,
    int ExpectedModuleCount,
    int MatchedModuleCount,
    string Summary,
    IReadOnlyList<string> Errors);

public sealed record ApplyTmcModuleModelRequest(
    string ProjectTmcPath,
    string ProjectName,
    IReadOnlyList<TmcModuleModel> Modules,
    string? GeneratedServicesHeaderPath = null,
    IReadOnlyList<string>? GeneratedHeaderPaths = null,
    string LibraryName = "",
    string LibraryVersion = "0.0.0.1",
    bool RemoveUnexpectedModules = false,
    bool ReplaceDataTypesFromGeneratedHeader = true);

public sealed record TmcModuleModel(
    string Name,
    string Guid,
    IReadOnlyList<TmcInterfaceModel>? Interfaces = null,
    IReadOnlyList<TmcParameterModel>? Parameters = null,
    IReadOnlyList<TmcDataAreaModel>? DataAreas = null,
    IReadOnlyList<TmcPointerModel>? InterfacePointers = null,
    IReadOnlyList<TmcPointerModel>? DataPointers = null,
    IReadOnlyList<TmcTypeReference>? EventClasses = null);

public sealed record TmcInterfaceModel(
    string TypeName,
    string? TypeGuid = null,
    int? ContextId = null,
    bool DisableCodeGeneration = false);

public sealed record TmcParameterModel(
    string Name,
    string PtcId,
    string? TypeName = null,
    string? TypeGuid = null,
    int? BitSize = null,
    IReadOnlyList<TmcSubItemModel>? SubItems = null,
    int ContextId = 1,
    bool HideParameter = false,
    bool CreateSymbol = false,
    bool ShowSubItems = false,
    string? Comment = null);

public sealed record TmcDataAreaModel(
    string Name,
    int AreaNo,
    string AreaType,
    int ContextId = 1,
    int? ByteSize = null,
    IReadOnlyList<TmcSymbolModel>? Symbols = null);

public sealed record TmcSymbolModel(
    string Name,
    string TypeName,
    string? TypeGuid = null,
    int? BitSize = null,
    int? BitOffset = null,
    int? ArrayElements = null,
    bool CreateSymbol = false,
    IReadOnlyList<TmcPropertyModel>? Properties = null);

public sealed record TmcSubItemModel(
    string Name,
    string TypeName,
    string? TypeGuid = null,
    int? BitSize = null,
    int? BitOffset = null,
    int? ArrayElements = null,
    IReadOnlyList<TmcPropertyModel>? Properties = null);

public sealed record TmcPointerModel(
    string Name,
    string PtcId,
    string TypeName,
    string? TypeGuid = null,
    int? ContextId = 1,
    int? ArrayElements = null);

public sealed record TmcTypeReference(
    string TypeName,
    string? TypeGuid = null);

public sealed record TmcPropertyModel(
    string Name,
    string Value);

public sealed record ApplyTmcModuleModelResult(
    bool Succeeded,
    string ProjectTmcPath,
    int ModuleCount,
    string Summary);

public sealed record RefreshCppInstanceTmcDescRequest(
    string CppProjectName,
    string ProjectTmcPath,
    IReadOnlyList<CppInstanceTmcDescRefreshItem> Instances,
    bool PreserveValueSections = true,
    bool PreserveContextValues = true,
    bool ImportDataTypesFromTmc = true,
    bool FailIfMissingModule = true);

public sealed record CppInstanceTmcDescRefreshItem(
    string InstanceName,
    string ModuleClassName,
    string? ClassFactoryId = null);

public sealed record RefreshCppInstanceTmcDescResult(
    bool Succeeded,
    string ProjectPath,
    int RefreshedCount,
    IReadOnlyList<string> Errors,
    string Summary);

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

public sealed record AdsPortStateAssertion(
    int Port,
    bool Succeeded,
    string ExpectedAdsState,
    string? ActualAdsState,
    short? ExpectedDeviceState,
    short? ActualDeviceState,
    string? ErrorMessage);

public sealed record AssertAdsStateResult(
    string NetId,
    IReadOnlyList<AdsPortStateAssertion> Ports)
{
    public bool Succeeded => Ports.Count > 0 && Ports.All(port => port.Succeeded);

    public int SucceededCount => Ports.Count(port => port.Succeeded);

    public int FailedCount => Ports.Count(port => !port.Succeeded);
}
