using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using TwinCatAutomationKit.Core;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.IntegrationTests;

internal static class OrderedTwinCatScenarioTests
{
    private const string SolutionName = "ItSolution";
    private const string ProjectName = "ItProject";
    private const string CppProjectName = "ItCpp";
    private const string PlcProjectName = "ItPlc";
    private const string AuxModuleName = "AuxModule";
    private const string RuntimeTaskName = "RuntimeTask";
    private const string AuxTaskName = "AuxTask";
    private const int RuntimeTaskPriority = 15;
    private const int AuxTaskPriority = 18;
    private const int RuntimeTaskCycleTimeUs = 10_000;
    private const int AuxTaskCycleTimeUs = 20_000;
    private const int RuntimeTaskCycleTimeNs = 10_000_000;
    private const int AuxTaskCycleTimeNs = 20_000_000;
    private const int RuntimeTaskAmsPort = 360;
    private const int AuxTaskAmsPort = 361;
    private const int PlcAmsPort = 851;
    private const uint RuntimeConfiguredParameter = 12_345;
    private const uint RuntimeConversionOffset = 17;
    private const uint RuntimeChecksumSalt = 99;
    private const string StaleTaskVarsGroupName = "StaleAuxVars";
    private const string StaleTaskImageName = "StaleAuxImage";
    private const string StalePlcVarsGroupName = "Stale Plc Vars";
    private const string StalePlcVarName = "MAIN.nStale";
    private const string StaleInitSymbolName = "MAIN.StaleOidProbe";
    private const string StaleParameterName = "Parameter.stale";
    private const string StalePointerName = "StalePointer";
    private const string StaleDataTypeName = "ST_StaleReplacedType";
    private const string StaleIoName = "StaleOwnedIo";
    private const string StaleFragmentName = "StaleFragment";
    private const string AtomicParameterName = "Parameter.atomicWrapper";
    private const string AtomicPointerName = "AtomicWrapperPointer";
    private const string AtomicMetadataName = "AtomicStepWrappersExecuteAgainstRealProject";
    private const string AtomicFragmentName = "DirectWrapperProbe";

    private static ScenarioState? cachedState;
    private static Exception? cachedScenarioFailure;
    private static readonly string[] ExcludedSigningStepKinds =
    [
        "signing.grant-certificate",
        "signing.sign-twincat-binary",
        "signing.verify-twincat-binary",
    ];

    private static readonly string[] ExcludedSigningInterfaces =
    [
        "TwinCatSigningService.GrantCertificate",
        "TwinCatSigningService.Sign",
        "TwinCatSigningService.Verify",
    ];

    public static IReadOnlyList<IntegrationTestCase> All =>
    [
        new("ordered-step-surface engineering + tsproj + reopen + build", EngineeringTsprojReopenAndBuild),
        new("signing set-license writes C++ project metadata without certificate", SigningSetLicenseWritesMetadata),
        new("required activation writes configuration archive", RequiredActivationWritesArchive),
        new("required ADS scan/read validates runtime symbols", RequiredAdsScanAndReadSymbols),
        new("real TwinCAT boundary cases fail safely", RealTwinCatBoundaryCases),
        new("atomic step wrappers execute against real TwinCAT project", AtomicStepWrappersExecuteAgainstRealProject),
        new("real scenario covers every open service interface", RealScenarioCoversEveryOpenServiceInterface),
    ];

    public static void CleanupCachedScenario()
    {
        ScenarioState? state = cachedState;
        cachedState = null;
        cachedScenarioFailure = null;
        if (state is null)
        {
            return;
        }

        IntegrationTestHelper.TryCloseVisualStudio(state.Engineering, state.Session);
        if (!state.Config.PreserveArtifacts)
        {
            IntegrationTestHelper.TryDeleteWorkDir(state.WorkDir);
            IntegrationTestHelper.TryDeleteWorkDir(state.RuntimeWorkDir);
        }
        else
        {
            Console.WriteLine($"     Preserved integration artifacts: {state.WorkDir}");
        }
    }

    private static void EngineeringTsprojReopenAndBuild()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        ScenarioState state = GetOrCreateBuiltScenario(config);

        IntegrationAssertEx.True(File.Exists(state.ProjectInfo.ProjectPath), $"Project file missing: {state.ProjectInfo.ProjectPath}");
        IntegrationAssertEx.True(File.Exists(state.ProjectInfo.SolutionPath), $"Solution file missing: {state.ProjectInfo.SolutionPath}");
        AssertTreeExport(Path.Combine(state.EvidenceDir, "cpp.after-file-mutation.xml"), "ItCpp", "TIXC^ItCpp", "C++ Project");
        AssertTreeExport(Path.Combine(state.EvidenceDir, "tasks.after-file-mutation.xml"), "任务", "TIRT", null);
        IntegrationAssertEx.True(state.BuildResult?.Succeeded == true, "Build result should be successful.");
        IntegrationAssertEx.Equal(0, state.BuildResult!.LastBuildInfo, "LastBuildInfo should be 0 after the ordered scenario build.");
        AssertRuntimeProjectBuildOutputs(state);
        AssertRealProjectMutationSemantics(state);
    }

    private static void SigningSetLicenseWritesMetadata()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        ScenarioState state = GetOrCreateBuiltScenario(config);
        TwinCatSigningService signing = new();
        string toolPath = TwinCatSigningService.ResolveToolPath(EmptyToNull(config.SigningToolPath));
        state.Cover("TwinCatSigningService.ResolveToolPath");
        IntegrationAssertEx.True(File.Exists(toolPath), $"TcSignTool should resolve to an existing file: {toolPath}");

        TwinCatSigningLicenseResult result = signing.SetLicense(new SetTwinCatSigningLicenseRequest(
            state.CppProjectFilePath,
            config.SigningLicenseName,
            Password: null,
            EnableSigning: false));
        state.Cover("TwinCatSigningService.SetLicense");
        state.CoverStep("signing.set-license");

        IntegrationAssertEx.Equal(state.CppProjectFilePath, result.ProjectFilePath, "signing.set-license should report the modified project path.");
        IntegrationAssertEx.False(result.PasswordWritten, "The non-certificate metadata test must not write a password.");

        AssertSigningMetadata(state.CppProjectFilePath, config.SigningLicenseName);
    }

    private static void RequiredSigningGrantSignAndVerify()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        IntegrationPrerequisites.RequireSigning(config);
        ScenarioState state = GetOrCreateBuiltScenario(config);

        string tmxPath = state.FindBuiltTwinCatBinary();
        TwinCatSigningService signing = new();

        if (config.EnableCertificateGrant)
        {
            TwinCatSignToolResult grant = signing.GrantCertificate(new GrantTwinCatSigningCertificateRequest(
                config.SigningCertificatePath!,
                Password: EmptyToNull(config.SigningCertificatePassword),
                Quiet: true,
                ToolPath: EmptyToNull(config.SigningToolPath)));
            state.Cover("TwinCatSigningService.GrantCertificate");
            state.CoverStep("signing.grant-certificate");
            IntegrationAssertEx.True(grant.Succeeded, $"signing.grant-certificate failed: {grant.Output}");
        }
        else
        {
            throw new InvalidOperationException("EnableCertificateGrant=false. Full integration coverage requires signing.grant-certificate.");
        }

        TwinCatSignToolResult sign = signing.Sign(new SignTwinCatBinaryRequest(
            config.SigningCertificatePath!,
            [tmxPath],
            Password: EmptyToNull(config.SigningCertificatePassword),
            Quiet: true,
            ToolPath: EmptyToNull(config.SigningToolPath)));
        state.Cover("TwinCatSigningService.Sign");
        state.CoverStep("signing.sign-twincat-binary");
        IntegrationAssertEx.True(sign.Succeeded, $"signing.sign-twincat-binary failed: {sign.Output}");
        if (!string.IsNullOrWhiteSpace(config.SigningCertificatePassword))
        {
            IntegrationAssertEx.False(sign.RedactedCommandLine.Contains(config.SigningCertificatePassword, StringComparison.Ordinal),
                "Signing command line should not expose the configured certificate password.");
        }

        TwinCatSignToolResult verify = signing.Verify(new VerifyTwinCatBinarySignatureRequest(
            [tmxPath],
            Quiet: true,
            ToolPath: EmptyToNull(config.SigningToolPath),
            AllowTestModeWarning: config.AllowTestModeSigningWarning));
        state.Cover("TwinCatSigningService.Verify");
        state.CoverStep("signing.verify-twincat-binary");
        IntegrationAssertEx.True(verify.Succeeded, $"signing.verify-twincat-binary failed: {verify.Output}");

        string toolPath = TwinCatSigningService.ResolveToolPath(EmptyToNull(config.SigningToolPath));
        state.Cover("TwinCatSigningService.ResolveToolPath");
        IntegrationAssertEx.True(File.Exists(toolPath), $"TcSignTool should resolve to an existing file: {toolPath}");
    }

    private static void RequiredActivationWritesArchive()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        IntegrationPrerequisites.RequireActivation(config);
        ScenarioState state = GetOrCreateBuiltScenario(config);

        EnsurePlcOnlyRuntimeSession(state);

        string archivePath = Path.Combine(state.RuntimeEvidenceDir!, "activated.tszip");
        ActivationResult activation = state.Engineering.ActivateConfiguration(
            state.RequireSession(),
            new ActivateConfigurationRequest(SaveConfigurationArchive: true, archivePath));
        state.ActivationResult = activation;
        state.Cover("TwinCatEngineeringService.ActivateConfiguration");
        state.CoverStep("engineering.activate-configuration");

        IntegrationAssertEx.True(activation.Succeeded, "engineering.activate-configuration should report success.");
        IntegrationAssertEx.True(File.Exists(archivePath), $"Activation archive was not written: {archivePath}");
        AssertActivationArchive(archivePath, activation);
    }

    private static void RequiredAdsScanAndReadSymbols()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        IntegrationPrerequisites.RequireAdsRead(config);
        ScenarioState state = GetOrCreateBuiltScenario(config);

        if (config.EnableActivation && state.ActivationResult is null)
        {
            RequiredActivationWritesArchive();
        }

        Thread.Sleep(config.RuntimeSettleDelayMs);

        AdsValidationService ads = new();
        AdsPortScanResult scan = ads.ScanPorts(new AdsPortScanRequest(config.AmsNetId, config.AdsScanPorts));
        state.Cover("AdsValidationService.ScanPorts");
        state.CoverStep("validation.ads-scan");
        IntegrationAssertEx.True(scan.AnySucceeded, "validation.ads-scan should find at least one reachable configured port.");
        AssertAdsScanHitRuntimePort(scan, config.AdsPort);

        IReadOnlyList<AdsReadSymbolRequest> symbols = BuildRequiredAdsSymbols(config);
        AdsReadSymbolsResult batch = ads.ReadSymbols(new AdsReadSymbolsRequest(
            config.AmsNetId,
            config.AdsPort,
            symbols,
            AutoReconnect: true,
            ContinueOnError: false));
        state.Cover("AdsValidationService.ReadSymbols");
        state.CoverStep("validation.ads-read-symbols");
        IntegrationAssertEx.True(batch.Succeeded, "validation.ads-read-symbols failed: " + FormatAdsFailures(batch));

        AdsReadSymbolRequest first = DeterministicSingleAdsReadSymbol();
        AdsReadResult single = ads.Read(new AdsReadRequest(
            config.AmsNetId,
            config.AdsPort,
            first.SymbolPath,
            first.DataType,
            AutoReconnect: true));
        state.Cover("AdsValidationService.Read");
        state.CoverStep("validation.ads-read");
        IntegrationAssertEx.True(single.Succeeded, $"validation.ads-read failed for {first.SymbolPath}: {single.ErrorMessage}");
        IntegrationAssertEx.Equal(RuntimeConfiguredParameter.ToString(CultureInfo.InvariantCulture), single.Value, "validation.ads-read should return the exact deterministic configured parameter.");

        AssertRuntimeSemanticSymbols(batch, state);
        _ = state;
    }

    private static void RealTwinCatBoundaryCases()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        IntegrationPrerequisites.RequireTwinCat(config);
        ScenarioState state = GetOrCreateBuiltScenario(config);
        TwinCatTsprojMutationService mutation = new();

        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureTaskVarsGroup(state.ProjectInfo.ProjectPath, new EnsureTaskVarsGroupRequest(RuntimeTaskName, "BadCount", 1, 1, "X", "UDINT", 0, 32, 4)),
            "Count",
            "tsproj.ensure-task-vars-group should reject Count=0 on a real TwinCAT project.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureTaskVarsGroup(state.ProjectInfo.ProjectPath, new EnsureTaskVarsGroupRequest(RuntimeTaskName, string.Empty, 1, 1, "X", "UDINT", 1, 32, 4)),
            "GroupName",
            "tsproj.ensure-task-vars-group should reject an empty group name before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureTaskImage(state.ProjectInfo.ProjectPath, new EnsureTaskImageRequest(RuntimeTaskName, ImageId: 0)),
            "Image Id",
            "tsproj.ensure-task-image should reject ImageId=0 before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureIoTaskImage(state.ProjectInfo.ProjectPath, new EnsureIoTaskImageRequest(RuntimeTaskName, state.PrimaryInstanceName, ImageId: 0)),
            "Image Id",
            "tsproj.ensure-io-task-image should reject ImageId=0 before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.SetTaskAffinity(state.ProjectInfo.ProjectPath, new SetTaskAffinityRequest("MissingTask", "#x1")),
            "MissingTask",
            "tsproj.set-task-affinity should reject a missing task on a real TwinCAT project.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.SetTaskAffinity(state.ProjectInfo.ProjectPath, new SetTaskAffinityRequest(RuntimeTaskName, string.Empty)),
            "Affinity",
            "tsproj.set-task-affinity should reject an empty affinity before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.BindInstanceToTask(state.ProjectInfo.ProjectPath, new BindInstanceToTaskRequest("MissingInstance", "#x02010010", 1, 1_000_000)),
            "MissingInstance",
            "tsproj.bind-instance-task should reject a missing instance on a real TwinCAT project.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.BindInstanceToTask(state.ProjectInfo.ProjectPath, new BindInstanceToTaskRequest(state.PrimaryInstanceName, "not-a-hex-object-id", 1, 1_000_000)),
            "not-a-hex-object-id",
            "tsproj.bind-instance-task should reject malformed ObjectId before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureParameterValue(state.ProjectInfo.ProjectPath, new EnsureParameterValueRequest("MissingInstance", "Parameter.bad", ValueText: "1")),
            "MissingInstance",
            "tsproj.ensure-parameter should reject a missing instance on a real TwinCAT project.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureParameterValue(state.ProjectInfo.ProjectPath, new EnsureParameterValueRequest(state.PrimaryInstanceName, string.Empty, ValueText: "1")),
            "ParameterName",
            "tsproj.ensure-parameter should reject an empty parameter name before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.ApplyInstanceParameterPlan(state.ProjectInfo.ProjectPath, new ApplyInstanceParameterPlanRequest(
            [
                new InstanceParameterMutation(state.PrimaryInstanceName, "Parameter.shouldNotPersist", ValueText: "1"),
                new InstanceParameterMutation("MissingInstance", "Parameter.bad", ValueText: "2"),
            ])),
            "MissingInstance",
            "tsproj.apply-instance-parameter-plan should be atomic on validation failure and not persist earlier batch items.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureDataPointerValue(state.ProjectInfo.ProjectPath, new EnsureDataPointerValueRequest(state.PrimaryInstanceName, "BadPointer", "#x01010020", AreaNo: -1, ByteOffset: 0, ByteSize: 4)),
            "AreaNo",
            "tsproj.ensure-data-pointer should reject an invalid AreaNo on a real TwinCAT project.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureDataPointerValue(state.ProjectInfo.ProjectPath, new EnsureDataPointerValueRequest(state.PrimaryInstanceName, "BadPointer", "#x01010020", AreaNo: 0, ByteOffset: 0, ByteSize: 0)),
            "ByteSize",
            "tsproj.ensure-data-pointer should reject ByteSize=0 before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.ApplyInstanceDataPointerPlan(state.ProjectInfo.ProjectPath, new ApplyInstanceDataPointerPlanRequest(
            [
                new InstanceDataPointerMutation(state.PrimaryInstanceName, "ShouldNotPersist", state.AuxInstanceObjectId, AreaNo: 1, ByteOffset: 0, ByteSize: 4),
                new InstanceDataPointerMutation(state.PrimaryInstanceName, "BadPointer", state.AuxInstanceObjectId, AreaNo: 1, ByteOffset: -1, ByteSize: 4),
            ])),
            "ByteOffset",
            "tsproj.apply-instance-data-pointer-plan should validate the whole batch before persisting any item.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.ApplyInstanceInterfacePointerPlan(state.ProjectInfo.ProjectPath, new ApplyInstanceInterfacePointerPlanRequest(
            [
                new InstanceInterfacePointerMutation(state.PrimaryInstanceName, "ShouldNotPersist", state.RuntimeTaskObjectId),
                new InstanceInterfacePointerMutation(state.PrimaryInstanceName, "BadPointer", string.Empty),
            ])),
            "ObjectId",
            "tsproj.apply-instance-interface-pointer-plan should validate the whole batch before persisting any item.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureMappingLink(state.ProjectInfo.ProjectPath, new EnsureMappingLinkRequest(string.Empty, "TIPC^Missing", "A", "B")),
            "OwnerAName",
            "tsproj.ensure-mapping-link should reject an empty mapping owner on a real TwinCAT project.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.EnsureMappingLink(state.ProjectInfo.ProjectPath, new EnsureMappingLinkRequest("TIXC^Missing", "TIPC^Missing", string.Empty, "B")),
            "VarA",
            "tsproj.ensure-mapping-link should reject an empty VarA before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.UpsertElement(state.ProjectInfo.ProjectPath, new TsprojElementUpsertRequest(
                [new TsprojPathSegment("Project"), new TsprojPathSegment(string.Empty)],
                "ShouldNotPersist")),
            "ElementName",
            "tsproj.upsert-element should reject an empty parent path segment before writing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.UpsertElement(state.ProjectInfo.ProjectPath, new TsprojElementUpsertRequest(
                [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
                "IntegrationMetadata",
                ChildValues: [new TsprojXmlChildValue("RuntimeTask", "ConflictingValue")],
                ConflictPolicy: TsprojMutationConflictPolicy.FailOnConflict)),
            "conflict",
            "tsproj.upsert-element should fail on configured conflict policy without changing existing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.UpsertFragment(state.ProjectInfo.ProjectPath, new TsprojFragmentUpsertRequest(
                [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
                "<IntegrationFragment><Name>RuntimeAdsProbe</Name><Symbols>ConflictingValue</Symbols></IntegrationFragment>",
                MatchElementName: "IntegrationFragment",
                MatchNameValue: "RuntimeAdsProbe",
                ConflictPolicy: TsprojMutationConflictPolicy.FailOnConflict)),
            "Fragment conflict",
            "tsproj.upsert-fragment should fail on configured conflict policy without changing existing XML.");
        ExpectTsprojUnchangedOnFailure(
            state.ProjectInfo.ProjectPath,
            () => mutation.MergeNamedElementFragment(state.ProjectInfo.ProjectPath, new MergeNamedElementFragmentRequest("DataTypes", "<DataType><Name>MissingEvidence</Name></DataType>")),
            "FragmentSource",
            "tsproj.merge-fragment should require evidence metadata on a real TwinCAT project.");

        TwinCatNodeInfo fallbackExport = state.Engineering.ExportTreeItemXml(
            state.RequireSession(),
            new ExportTreeItemXmlRequest("TIXC^MissingNode", Path.Combine(state.EvidenceDir, "missing-node-export.xml"), Recursive: true));
        IntegrationAssertEx.True(fallbackExport.UsedFallback, "Invalid tree export should return a fallback artifact instead of corrupting the run.");
        IntegrationAssertEx.True(File.Exists(fallbackExport.FilePath!), "Fallback export artifact should exist.");
        AssertFallbackExport(fallbackExport.FilePath!, "TIXC^MissingNode");

        AdsValidationService ads = new();
        AdsReadResult badRead = ads.Read(new AdsReadRequest(
            state.Config.AmsNetId,
            state.Config.AdsPort,
            "MAIN.__DefinitelyMissingSymbol",
            AdsReadDataType.UInt32,
            AutoReconnect: true));
        IntegrationAssertEx.False(badRead.Succeeded, "validation.ads-read should fail for a definitely missing runtime symbol.");
        IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(badRead.ErrorMessage), "validation.ads-read missing-symbol failure should include an error message.");

        state.Cover("TwinCatEngineeringService.ExportTreeItemXml.boundary");
        state.Cover("TwinCatTsprojMutationService.boundary-cases");
        state.Cover("AdsValidationService.Read.boundary");
    }

    private static void AtomicStepWrappersExecuteAgainstRealProject()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        IntegrationPrerequisites.RequireAdsRead(config);
        ScenarioState state = GetOrCreateBuiltScenario(config);
        if (config.EnableActivation && state.ActivationResult is null)
        {
            RequiredActivationWritesArchive();
            Thread.Sleep(config.RuntimeSettleDelayMs);
        }

        AutomationContext context = new("atomic-real-twincat", Path.Combine(state.EvidenceDir, "atomic"));
        context.State.Set(TwinCatStateKeys.Session, state.RequireSession());
        context.State.Set(TwinCatStateKeys.ProjectPath, state.ProjectInfo.ProjectPath);
        context.State.Set(TwinCatStateKeys.SolutionPath, state.ProjectInfo.SolutionPath);

        TwinCatTsprojMutationService mutation = new();
        AdsValidationService ads = new();
        IReadOnlyList<AdsReadSymbolRequest> symbols = BuildRequiredAdsSymbols(config);
        AdsReadSymbolRequest deterministicSingleRead = DeterministicSingleAdsReadSymbol();

        mutation.EnsureDataPointerValue(
            state.ProjectInfo.ProjectPath,
            new EnsureDataPointerValueRequest(state.AuxInstanceName, AtomicPointerName, state.PrimaryInstanceObjectId, AreaNo: 9, ByteOffset: 88, ByteSize: 12));

        IAutomationStep[] steps =
        [
            TwinCatAtomicSteps.SaveAll("atomic-save", state.Engineering),
            TwinCatAtomicSteps.ExportTreeItemXml(
                "atomic-export-task",
                state.Engineering,
                new ExportTreeItemXmlRequest("TIRT^" + RuntimeTaskName, Path.Combine(state.EvidenceDir, "atomic-runtime-task.xml"), Recursive: true)),
            TwinCatAtomicSteps.EnsureParameterInTsproj(
                "atomic-ensure-parameter",
                mutation,
                new EnsureParameterValueRequest(state.PrimaryInstanceName, AtomicParameterName, ValueText: "456")),
            TwinCatAtomicSteps.ClearUnrestoredVarLinksInTsproj(
                "atomic-clear-unrestored-var-links",
                mutation,
                new ClearUnrestoredVarLinksRequest()),
            TwinCatAtomicSteps.ClearInstanceDataPointerValuesInTsproj(
                "atomic-clear-aux-data-pointers",
                mutation,
                new ClearInstanceDataPointerValuesRequest(state.AuxInstanceName)),
            TwinCatAtomicSteps.ApplyInstanceDataPointerPlanInTsproj(
                "atomic-restore-aux-data-pointers",
                mutation,
                new ApplyInstanceDataPointerPlanRequest(
                [
                    new InstanceDataPointerMutation(state.AuxInstanceName, "DataIn", state.PrimaryInstanceObjectId, AreaNo: 2, ByteOffset: 0, ByteSize: 4),
                    new InstanceDataPointerMutation(state.AuxInstanceName, "DataOut", state.PrimaryInstanceObjectId, AreaNo: 1, ByteOffset: 0, ByteSize: 4),
                ])),
            TwinCatAtomicSteps.UpsertElementInTsproj(
                "atomic-upsert-element",
                mutation,
                new TsprojElementUpsertRequest(
                    [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
                    "AtomicWrapperMetadata",
                    ChildValues: [new TsprojXmlChildValue("Scenario", AtomicMetadataName)])),
            TwinCatAtomicSteps.UpsertFragmentInTsproj(
                "atomic-upsert-fragment",
                mutation,
                new TsprojFragmentUpsertRequest(
                    [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
                    "<AtomicWrapperFragment><Name>" + AtomicFragmentName + "</Name></AtomicWrapperFragment>",
                    MatchElementName: "AtomicWrapperFragment",
                    MatchNameValue: AtomicFragmentName)),
            TwinCatAtomicSteps.ApplyMutationPlanInTsproj(
                "atomic-apply-mutation-plan",
                mutation,
                new ApplyTsprojMutationPlanRequest(
                    ElementUpserts:
                    [
                        new TsprojElementUpsertRequest(
                            [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
                            "AtomicWrapperBatchMetadata",
                            ChildValues: [new TsprojXmlChildValue("Result", "Applied")])
                    ])),
            TwinCatAtomicSteps.AdsScan(
                "atomic-ads-scan",
                ads,
                new AdsPortScanRequest(config.AmsNetId, config.AdsScanPorts)),
            TwinCatAtomicSteps.AdsRead(
                "atomic-ads-read",
                ads,
                new AdsReadRequest(config.AmsNetId, config.AdsPort, deterministicSingleRead.SymbolPath, deterministicSingleRead.DataType, AutoReconnect: true)),
            TwinCatAtomicSteps.AdsReadSymbols(
                "atomic-ads-read-symbols",
                ads,
                new AdsReadSymbolsRequest(config.AmsNetId, config.AdsPort, symbols, AutoReconnect: true, ContinueOnError: false)),
        ];

        AutomationRunSummary summary = new AutomationPipelineRunner().RunAsync(context, steps).GetAwaiter().GetResult();
        IntegrationAssertEx.True(summary.Succeeded, "Atomic wrappers should execute against the real TwinCAT scenario project.");
        IntegrationAssertEx.True(File.Exists(Path.Combine(state.EvidenceDir, "atomic-runtime-task.xml")), "Atomic export should write a real ProduceXml artifact.");
        AssertAtomicRunSummary(summary, context.EvidenceRoot, config.AdsPort, state);
        AssertAtomicWrapperMutationSemantics(state);

        state.Cover("TwinCatAtomicSteps.SaveAll");
        state.Cover("TwinCatAtomicSteps.ExportTreeItemXml");
        state.Cover("TwinCatAtomicSteps.EnsureParameterInTsproj");
        state.Cover("TwinCatAtomicSteps.ClearUnrestoredVarLinksInTsproj");
        state.Cover("TwinCatAtomicSteps.ClearInstanceDataPointerValuesInTsproj");
        state.Cover("TwinCatAtomicSteps.ApplyInstanceDataPointerPlanInTsproj");
        state.Cover("TwinCatAtomicSteps.UpsertElementInTsproj");
        state.Cover("TwinCatAtomicSteps.UpsertFragmentInTsproj");
        state.Cover("TwinCatAtomicSteps.ApplyMutationPlanInTsproj");
        state.Cover("TwinCatAtomicSteps.AdsScan");
        state.Cover("TwinCatAtomicSteps.AdsRead");
        state.Cover("TwinCatAtomicSteps.AdsReadSymbols");
    }

    private static void RealScenarioCoversEveryOpenServiceInterface()
    {
        IntegrationTestConfig config = IntegrationTestConfig.Load();
        IntegrationPrerequisites.RequireTwinCat(config);
        ScenarioState state = GetOrCreateBuiltScenario(config);
        AssertCoverageMatrixContracts();

        string[] required =
        [
            "TwinCatEngineeringService.LaunchVisualStudio",
            "TwinCatEngineeringService.CreateTwinCatSolution",
            "TwinCatEngineeringService.OpenTwinCatSolution",
            "TwinCatEngineeringService.CreateCppProject",
            "TwinCatEngineeringService.ProbeCppProjectModuleArtifacts",
            "TwinCatEngineeringService.CreatePlcProject",
            "TwinCatEngineeringService.CreateModule",
            "TwinCatEngineeringService.BootstrapCppModuleArtifacts",
            "TwinCatEngineeringService.AddModuleInstance",
            "TwinCatEngineeringService.EnsureTask",
            "TwinCatEngineeringService.ExportTreeItemXml",
            "TwinCatEngineeringService.ConfigurePlcBootProject",
            "TwinCatEngineeringService.SaveAll",
            "TwinCatEngineeringService.BuildCurrentSolution",
            "TwinCatEngineeringService.ActivateConfiguration",
            "TwinCatEngineeringService.CloseVisualStudio",
            "TwinCatTsprojMutationService.ApplyMutationPlan",
            "TwinCatTsprojMutationService.UpsertElement",
            "TwinCatTsprojMutationService.UpsertFragment",
            "TwinCatTsprojMutationService.EnsureTaskDefinition",
            "TwinCatTsprojMutationService.ClearTaskLayout",
            "TwinCatTsprojMutationService.EnsureTaskVarsGroup",
            "TwinCatTsprojMutationService.EnsureTaskImage",
            "TwinCatTsprojMutationService.BindInstanceContext",
            "TwinCatTsprojMutationService.BindInstanceToTask",
            "TwinCatTsprojMutationService.EnsureCppInstance",
            "TwinCatTsprojMutationService.EnsurePlcInstance",
            "TwinCatTsprojMutationService.BindPlcInstanceToTask",
            "TwinCatTsprojMutationService.SetTaskAffinity",
            "TwinCatTsprojMutationService.SetPlcProjectProperties",
            "TwinCatTsprojMutationService.SetPlcInstanceMetadata",
            "TwinCatTsprojMutationService.ClearPlcInstanceVars",
            "TwinCatTsprojMutationService.EnsurePlcInstanceVarsGroup",
            "TwinCatTsprojMutationService.ClearPlcInitSymbols",
            "TwinCatTsprojMutationService.ClearPlcTaskPouOids",
            "TwinCatTsprojMutationService.ClearMappings",
            "TwinCatTsprojMutationService.ClearUnrestoredVarLinks",
            "TwinCatTsprojMutationService.ReplaceMappingsSection",
            "TwinCatTsprojMutationService.ReplaceProjectIoSection",
            "TwinCatTsprojMutationService.ReplaceDataTypesSection",
            "TwinCatTsprojMutationService.ReplaceSystemSettingsSection",
            "TwinCatTsprojMutationService.ApplyInstanceParameterPlan",
            "TwinCatTsprojMutationService.ClearInstanceParameterValues",
            "TwinCatTsprojMutationService.ClearInstanceDataPointerValues",
            "TwinCatTsprojMutationService.ApplyInstanceInterfacePointerPlan",
            "TwinCatTsprojMutationService.ApplyInstanceDataPointerPlan",
            "TwinCatTsprojMutationService.EnsureTaskPouOid",
            "TwinCatTsprojMutationService.EnsureInitSymbol",
            "TwinCatTsprojMutationService.EnsureMappingLink",
            "TwinCatTsprojMutationService.EnsureIoTaskImage",
            "TwinCatTsprojMutationService.EnsureParameterValue",
            "TwinCatTsprojMutationService.EnsureInterfacePointerValue",
            "TwinCatTsprojMutationService.EnsureDataPointerValue",
            "TwinCatTsprojMutationService.MergeNamedElementFragment",
            "TwinCatTsprojMutationService.ConvertObjectIdToInitSymbolData",
            "TwinCatTsprojMutationService.DeriveIoTaskImageObjectId",
            "TwinCatTsprojMutationService.DeriveNextObjectId",
            "TwinCatSigningService.SetLicense",
            "TwinCatSigningService.ResolveToolPath",
            "AdsValidationService.ScanPorts",
            "AdsValidationService.Read",
            "AdsValidationService.ReadSymbols",
        ];

        string[] missing = required
            .Where(item => !state.CoveredInterfaces.Contains(item))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IntegrationAssertEx.True(missing.Length == 0, "Real scenario did not cover open interface(s): " + string.Join(", ", missing));

        string[] missingSteps = TwinCatStepCatalog.All
            .Select(item => item.Kind)
            .Where(kind => !state.CoveredStepKinds.Contains(kind) && !ExcludedSigningStepKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IntegrationAssertEx.True(missingSteps.Length == 0, "Real scenario did not cover public step kind(s), excluding OEM signing certificate operations: " + string.Join(", ", missingSteps));

        Console.WriteLine("     Excluded signing certificate interfaces: " + string.Join(", ", ExcludedSigningInterfaces));
        Console.WriteLine("     Excluded signing certificate step kinds: " + string.Join(", ", ExcludedSigningStepKinds));
    }

    private static void AssertCoverageMatrixContracts()
    {
        IReadOnlyList<string> missing = StepCoverageMatrix.MissingCatalogKinds();
        IReadOnlyList<string> unknown = StepCoverageMatrix.UnknownMatrixKinds();

        IntegrationAssertEx.True(
            missing.Count == 0,
            "Coverage matrix is missing catalog kind(s): " + string.Join(", ", missing));
        IntegrationAssertEx.True(
            unknown.Count == 0,
            "Coverage matrix contains unknown kind(s): " + string.Join(", ", unknown));
        foreach (StepCoverageSpec spec in StepCoverageMatrix.All)
        {
            IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(spec.Scenario), $"Coverage matrix scenario must be non-empty for {spec.Kind}.");
            IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(spec.Dependencies), $"Coverage matrix dependencies must be non-empty for {spec.Kind}.");
            IntegrationAssertEx.True(spec.PassCriteria.Length >= 40, $"Coverage matrix pass criteria should be specific for {spec.Kind}.");
            AssertPassCriteriaIsEvidenceBased(spec);
        }

        string readmePath = FindIntegrationReadme();
        string text = File.ReadAllText(readmePath);
        foreach (string kind in TwinCatStepCatalog.All.Select(item => item.Kind).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            IntegrationAssertEx.Contains("`" + kind + "`", text, $"README should document step kind {kind}.");
        }
    }

    private static void AssertPassCriteriaIsEvidenceBased(StepCoverageSpec spec)
    {
        string[] evidenceTerms =
        [
            ".tsproj",
            ".vcxproj",
            ".tmc",
            ".compileinfo",
            "ADS",
            "archive",
            "ObjectId",
            "reopen",
            "export",
            "XML",
            "exact",
            "stale",
            "metadata",
            "output",
            "state",
            "exception",
            "evidence",
        ];

        bool hasEvidenceTerm = evidenceTerms.Any(term => spec.PassCriteria.Contains(term, StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.True(
            hasEvidenceTerm,
            $"Coverage matrix pass criteria for {spec.Kind} should name a concrete artifact, runtime proof, stale cleanup check, exact value, or failure evidence.");
    }

    private static string FindIntegrationReadme()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "README.md");
            if (File.Exists(candidate) &&
                File.ReadAllText(candidate).Contains("Integration Test Strategy", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Unable to locate integration test README.md from output directory.");
    }

    private static ScenarioState GetOrCreateBuiltScenario(IntegrationTestConfig config)
    {
        if (cachedState is not null)
        {
            return cachedState;
        }

        if (cachedScenarioFailure is not null)
        {
            throw new InvalidOperationException(
                "The shared real TwinCAT scenario setup already failed earlier in this run; not rebuilding another identical project.",
                cachedScenarioFailure);
        }

        IntegrationPrerequisites.RequireTwinCat(config);
        try
        {
            cachedState = BuildScenario(config);
            return cachedState;
        }
        catch (Exception ex)
        {
            cachedScenarioFailure = ex;
            throw;
        }
    }

    private static void EnsurePlcOnlyRuntimeSession(ScenarioState state)
    {
        if (!string.IsNullOrWhiteSpace(state.RuntimeProjectPath) &&
            !string.IsNullOrWhiteSpace(state.RuntimeSolutionPath) &&
            File.Exists(state.RuntimeProjectPath) &&
            File.Exists(state.RuntimeSolutionPath))
        {
            return;
        }

        string runtimeWorkDir = Path.Combine(state.Config.WorkRootBase, "rt" + Guid.NewGuid().ToString("N")[..6]);
        IntegrationTestHelper.TryCloseVisualStudio(state.Engineering, state.Session);
        state.Session = null;
        CopyDirectory(state.WorkDir, runtimeWorkDir);

        string runtimeProjectPath = Path.Combine(runtimeWorkDir, ProjectName, ProjectName + ".tsproj");
        string runtimeSolutionPath = Path.Combine(runtimeWorkDir, SolutionName + ".sln");
        string runtimeEvidenceDir = Path.Combine(runtimeWorkDir, "evidence");
        Directory.CreateDirectory(runtimeEvidenceDir);
        RemoveUnsignedCppRuntimeLoad(runtimeProjectPath);

        state.Session = state.Engineering.LaunchVisualStudio(IntegrationTestHelper.MakeLaunchRequest(state.Config));
        state.Engineering.OpenTwinCatSolution(state.RequireSession(), new OpenTwinCatSolutionRequest(runtimeSolutionPath, runtimeProjectPath));
        state.Engineering.SaveAll(state.RequireSession());

        state.RuntimeWorkDir = runtimeWorkDir;
        state.RuntimeProjectPath = runtimeProjectPath;
        state.RuntimeSolutionPath = runtimeSolutionPath;
        state.RuntimeEvidenceDir = runtimeEvidenceDir;
    }

    private static void RemoveUnsignedCppRuntimeLoad(string tsprojPath)
    {
        XDocument document = XDocument.Load(tsprojPath);
        XElement project = document.Descendants().First(element => element.Name.LocalName == "Project");
        foreach (XElement cpp in project.Elements().Where(element => element.Name.LocalName == "Cpp").ToList())
        {
            cpp.Remove();
        }

        foreach (XElement mappings in document.Root!.Elements().Where(element => element.Name.LocalName == "Mappings").ToList())
        {
            mappings.Remove();
        }

        document.Save(tsprojPath);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, directory);
            if (IsSkippedClonePath(relative))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
        }

        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            if (IsSkippedClonePath(relative))
            {
                continue;
            }

            string target = Path.Combine(targetDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsSkippedClonePath(string relativePath) =>
        relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, ".vs", StringComparison.OrdinalIgnoreCase));

    private static ScenarioState BuildScenario(IntegrationTestConfig config)
    {
        TwinCatEngineeringService engineering = new();
        TwinCatTsprojMutationService mutation = new();
        string workDir = IntegrationTestHelper.CreateWorkDir(config);
        string evidenceDir = Path.Combine(workDir, "evidence");
        Directory.CreateDirectory(evidenceDir);
        HashSet<string> coveredInterfaces = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> coveredStepKinds = new(StringComparer.OrdinalIgnoreCase);

        TwinCatEngineeringSession? session = null;
        ScenarioState? state = null;
        try
        {
            TraceScenarioStage("launch VS");
            session = engineering.LaunchVisualStudio(IntegrationTestHelper.MakeLaunchRequest(config));
            coveredInterfaces.Add("TwinCatEngineeringService.LaunchVisualStudio");
            coveredStepKinds.Add("engineering.launch-visual-studio");

            TraceScenarioStage("create XAE solution");
            TwinCatProjectInfo info = engineering.CreateTwinCatSolution(
                session,
                IntegrationTestHelper.MakeSolutionRequest(workDir, SolutionName, ProjectName));
            coveredInterfaces.Add("TwinCatEngineeringService.CreateTwinCatSolution");
            coveredStepKinds.Add("engineering.create-xae-solution");

            TraceScenarioStage("create PLC project");
            engineering.CreatePlcProject(session, new CreatePlcProjectRequest(PlcProjectName, AllowOfflineFallback: false));
            coveredInterfaces.Add("TwinCatEngineeringService.CreatePlcProject");
            coveredStepKinds.Add("engineering.create-plc-project");
            engineering.SaveAll(session);
            coveredInterfaces.Add("TwinCatEngineeringService.SaveAll");
            coveredStepKinds.Add("engineering.save-all");
            (string plcProjectName, string plcInstanceName) = ExtractFirstPlcNames(info.ProjectPath);
            WriteRuntimePou(info.SolutionDirectory, plcProjectName);

            TraceScenarioStage("create C++ project");
            TwinCatNodeInfo cppProject = engineering.CreateCppProject(session, new CreateCppProjectRequest(CppProjectName));
            coveredInterfaces.Add("TwinCatEngineeringService.CreateCppProject");
            coveredStepKinds.Add("engineering.create-cpp-project");
            AssertCreatedCppProject(cppProject, info.SolutionDirectory);

            TraceScenarioStage("probe/bootstrap C++ artifacts");
            CppProjectModuleArtifactsResult probeBeforeBootstrap = engineering.ProbeCppProjectModuleArtifacts(
                new ProbeCppProjectModuleArtifactsRequest(info.SolutionDirectory, CppProjectName));
            coveredInterfaces.Add("TwinCatEngineeringService.ProbeCppProjectModuleArtifacts");
            AssertCppProbe(probeBeforeBootstrap, requireModuleArtifacts: false);

            BootstrapCppModuleArtifactsResult bootstrap = engineering.BootstrapCppModuleArtifacts(
                new BootstrapCppModuleArtifactsRequest(info.SolutionDirectory, CppProjectName, "BootProbeModule"));
            coveredInterfaces.Add("TwinCatEngineeringService.BootstrapCppModuleArtifacts");
            AssertBootstrapModuleArtifacts(bootstrap);

            TraceScenarioStage("create aux C++ module");
            TwinCatNodeInfo auxModule = engineering.CreateModule(
                session,
                new CreateModuleRequest(CppProjectName, AuxModuleName, "TcModuleClassWizard", AllowOfflineFallback: true));
            coveredInterfaces.Add("TwinCatEngineeringService.CreateModule");
            coveredStepKinds.Add("engineering.create-module");
            AssertCreatedModule(auxModule, info.SolutionDirectory);

            string tmcPath = Path.Combine(info.SolutionDirectory, CppProjectName, CppProjectName + ".tmc");
            IntegrationAssertEx.True(
                IntegrationTestHelper.TmcHasAnyModule(tmcPath),
                $"C++ project TMC should contain module metadata: {tmcPath}");
            AssertTmcContainsModule(tmcPath, AuxModuleName);

            TraceScenarioStage("add module instances");
            TwinCatNodeInfo primaryInstance = engineering.AddModuleInstance(
                session,
                new AddModuleInstanceRequest(CppProjectName, tmcPath, "ItPrimary", ModuleClassName: null, AllowOfflineFallback: true));
            TwinCatNodeInfo auxInstance = engineering.AddModuleInstance(
                session,
                new AddModuleInstanceRequest(CppProjectName, tmcPath, "ItAux", ModuleClassName: AuxModuleName, AllowOfflineFallback: true));
            coveredInterfaces.Add("TwinCatEngineeringService.AddModuleInstance");
            coveredStepKinds.Add("engineering.add-module-instance");
            AssertCreatedModuleInstance(primaryInstance, "ItPrimary");
            AssertCreatedModuleInstance(auxInstance, "ItAux");

            TraceScenarioStage("ensure tasks");
            TwinCatNodeInfo runtimeTask = engineering.EnsureTask(
                session,
                new EnsureTaskRequest(RuntimeTaskName, 0, RuntimeTaskPriority, RuntimeTaskCycleTimeUs, RuntimeTaskAmsPort, IoAtBegin: true));
            TwinCatNodeInfo auxTask = engineering.EnsureTask(
                session,
                new EnsureTaskRequest(AuxTaskName, 0, AuxTaskPriority, AuxTaskCycleTimeUs, AuxTaskAmsPort, IoAtBegin: true));
            coveredInterfaces.Add("TwinCatEngineeringService.EnsureTask");
            coveredStepKinds.Add("engineering.ensure-task");
            AssertCreatedTask(runtimeTask, RuntimeTaskName);
            AssertCreatedTask(auxTask, AuxTaskName);

            string runtimeTaskObjectId = runtimeTask.ObjectId ?? "#x02010020";
            string auxTaskObjectId = auxTask.ObjectId ?? TwinCatTsprojMutationService.DeriveNextObjectId(runtimeTaskObjectId, 0x10);
            coveredInterfaces.Add("TwinCatTsprojMutationService.DeriveNextObjectId");
            WriteRuntimePou(info.SolutionDirectory, plcProjectName, ParseObjectIdForAds(runtimeTaskObjectId), ParseObjectIdForAds(auxTaskObjectId));

            AssertNonFallbackExport(engineering.ExportTreeItemXml(
                session,
                new ExportTreeItemXmlRequest("TIXC^" + CppProjectName, Path.Combine(evidenceDir, "cpp.before-file-mutation.xml"), Recursive: true)));
            AssertNonFallbackExport(engineering.ExportTreeItemXml(
                session,
                new ExportTreeItemXmlRequest("TIRT", Path.Combine(evidenceDir, "tasks.before-file-mutation.xml"), Recursive: true)));
            AssertTreeExport(Path.Combine(evidenceDir, "cpp.before-file-mutation.xml"), "ItCpp", "TIXC^ItCpp", "C++ Project");
            AssertTreeExport(Path.Combine(evidenceDir, "tasks.before-file-mutation.xml"), "任务", "TIRT", null);
            coveredInterfaces.Add("TwinCatEngineeringService.ExportTreeItemXml");
            coveredStepKinds.Add("engineering.export-tree-item-xml");
            engineering.SaveAll(session);
            engineering.CloseVisualStudio(session, saveBeforeClose: true);
            coveredInterfaces.Add("TwinCatEngineeringService.CloseVisualStudio");
            coveredStepKinds.Add("engineering.close-visual-studio");
            session = null;

            TraceScenarioStage("apply file mutations");
            string primaryInstanceObjectId = primaryInstance.ObjectId ?? "#x02010010";
            string auxInstanceObjectId = auxInstance.ObjectId ?? "#x02010030";

            ApplyFileMutations(
                mutation,
                info.ProjectPath,
                plcProjectName,
                plcInstanceName,
                primaryInstance.DisplayName,
                primaryInstanceObjectId,
                auxInstance.DisplayName,
                auxInstanceObjectId,
                runtimeTaskObjectId,
                auxTaskObjectId);
            AddFileMutationCoverage(coveredInterfaces, coveredStepKinds);

            AssertTsprojContains(info.ProjectPath, RuntimeTaskName, "Runtime task should be present after file mutation.");
            AssertTsprojContains(info.ProjectPath, AuxTaskName, "Aux task should be present after file mutation.");
            AssertTsprojContains(info.ProjectPath, primaryInstance.DisplayName, "Primary instance should be present after file mutation.");
            AssertTsprojContains(info.ProjectPath, auxInstance.DisplayName, "Aux instance should be present after file mutation.");
            AssertTsprojContains(info.ProjectPath, "#x02020010", "File-created C++ instance requested ObjectId should be written before XAE reopen normalization.");
            AssertTsprojContains(info.ProjectPath, "DataPointerValues", "Data pointer plan should be written before XAE reopen normalization.");
            AssertTsprojContains(info.ProjectPath, "IoTaskImage", "IO task image pointer should be written before XAE reopen normalization.");
            AssertTsprojContains(info.ProjectPath, "TcSmClass=\"PlcTask\"", "PLC instance metadata should be written before XAE reopen normalization.");
            AssertTsprojContains(info.ProjectPath, "TaskPouOid Prio=\"15\"", "TaskPouOid priority should be written before XAE reopen normalization.");
            AssertTsprojContains(info.ProjectPath, "OTCID=\"" + runtimeTaskObjectId + "\"", "TaskPouOid ObjectId should be written before XAE reopen normalization.");
            AssertTsprojContains(info.ProjectPath, "MAIN.nSeed", "PLC output vars should be present after file mutation.");
            AssertTsprojContains(info.ProjectPath, "FullCoverageBatchMetadata", "Generic mutation plan marker should be present.");

            TraceScenarioStage("reopen mutated solution");
            session = engineering.LaunchVisualStudio(IntegrationTestHelper.MakeLaunchRequest(config));
            coveredInterfaces.Add("TwinCatEngineeringService.LaunchVisualStudio");
            engineering.OpenTwinCatSolution(session, new OpenTwinCatSolutionRequest(info.SolutionPath, info.ProjectPath));
            coveredInterfaces.Add("TwinCatEngineeringService.OpenTwinCatSolution");
            coveredStepKinds.Add("engineering.open-xae-solution");
            AssertNonFallbackExport(engineering.ExportTreeItemXml(
                session,
                new ExportTreeItemXmlRequest("TIXC^" + CppProjectName, Path.Combine(evidenceDir, "cpp.after-file-mutation.xml"), Recursive: true)));
            AssertNonFallbackExport(engineering.ExportTreeItemXml(
                session,
                new ExportTreeItemXmlRequest("TIRT", Path.Combine(evidenceDir, "tasks.after-file-mutation.xml"), Recursive: true)));
            AssertTreeExport(Path.Combine(evidenceDir, "cpp.after-file-mutation.xml"), "ItCpp", "TIXC^ItCpp", "C++ Project");
            AssertTreeExport(Path.Combine(evidenceDir, "tasks.after-file-mutation.xml"), "任务", "TIRT", null);

            state = new ScenarioState(
                config,
                engineering,
                session,
                workDir,
                evidenceDir,
                info,
                Path.Combine(info.SolutionDirectory, CppProjectName, CppProjectName + ".vcxproj"),
                CppProjectName,
                plcProjectName,
                plcInstanceName,
                primaryInstance.DisplayName,
                auxInstance.DisplayName,
                primaryInstanceObjectId,
                auxInstanceObjectId,
                runtimeTaskObjectId,
                auxTaskObjectId,
                coveredInterfaces,
                coveredStepKinds);
            session = null;

            TraceScenarioStage("prepare PLC-only runtime clone");
            EnsurePlcOnlyRuntimeSession(state);

            TraceScenarioStage("build PLC-only runtime solution");
            BuildResult build = engineering.BuildCurrentSolution(state.RequireSession(), new BuildSolutionRequest(TimeoutMs: 600_000));
            coveredInterfaces.Add("TwinCatEngineeringService.BuildCurrentSolution");
            coveredStepKinds.Add("engineering.build-solution");
            state.BuildResult = build;
            IntegrationAssertEx.True(build.Succeeded, $"Build failed in ordered scenario. LastBuildInfo={build.LastBuildInfo}.{FormatBuildOutputTail(build)}");

            engineering.ConfigurePlcBootProject(state.RequireSession(), plcProjectName, autoStart: true, activateBootProject: false);
            coveredInterfaces.Add("TwinCatEngineeringService.ConfigurePlcBootProject");
            return state;
        }
        catch
        {
            IntegrationTestHelper.TryCloseVisualStudio(engineering, session);
            if (state is null)
            {
                Console.WriteLine($"     Failed scenario artifacts left for diagnosis: {workDir}");
            }

            throw;
        }
    }

    private static void TraceScenarioStage(string stage)
    {
        Console.WriteLine($"     stage: {stage}");
        Console.Out.Flush();
    }

    private static void ApplyFileMutations(
        TwinCatTsprojMutationService mutation,
        string projectPath,
        string plcProjectName,
        string plcInstanceName,
        string primaryInstanceName,
        string primaryInstanceObjectId,
        string auxInstanceName,
        string auxInstanceObjectId,
        string runtimeTaskObjectId,
        string auxTaskObjectId)
    {
        mutation.EnsureTaskDefinition(projectPath, new EnsureTaskDefinitionRequest(RuntimeTaskName, RuntimeTaskPriority, RuntimeTaskCycleTimeNs, RuntimeTaskAmsPort, IoAtBegin: true));
        mutation.EnsureTaskDefinition(projectPath, new EnsureTaskDefinitionRequest(RuntimeTaskName, RuntimeTaskPriority, RuntimeTaskCycleTimeNs, RuntimeTaskAmsPort, IoAtBegin: true));
        mutation.EnsureTaskDefinition(projectPath, new EnsureTaskDefinitionRequest(AuxTaskName, AuxTaskPriority, AuxTaskCycleTimeNs, AuxTaskAmsPort, IoAtBegin: true));
        mutation.EnsureTaskVarsGroup(projectPath, new EnsureTaskVarsGroupRequest(AuxTaskName, StaleTaskVarsGroupName, 1, 1, "StaleAux", "UDINT", 1, 32, 4));
        mutation.EnsureTaskImage(projectPath, new EnsureTaskImageRequest(AuxTaskName, ImageId: 9, SizeIn: 4, SizeOut: 4, ImageName: StaleTaskImageName));
        mutation.ClearTaskLayout(projectPath, new ClearTaskLayoutRequest(AuxTaskName));
        mutation.EnsureTaskVarsGroup(projectPath, new EnsureTaskVarsGroupRequest(AuxTaskName, "AuxInputs", 1, 1, "AuxIn", "UDINT", 4, 32, 4));
        mutation.EnsureTaskVarsGroup(projectPath, new EnsureTaskVarsGroupRequest(AuxTaskName, "AuxOutputs", 2, 1, "AuxOut", "UDINT", 4, 32, 4));
        mutation.EnsureTaskImage(projectPath, new EnsureTaskImageRequest(AuxTaskName, ImageId: 2, SizeIn: 16, SizeOut: 16, ImageName: "AuxImage", IoAtBegin: true));

        mutation.EnsureCppInstance(projectPath, new EnsureCppInstanceRequest(CppProjectName, "FileMutationCpp01", "#x02020010", ContextName: "FileMutationCtx", Priority: RuntimeTaskPriority, CycleTimeNs: RuntimeTaskCycleTimeNs));
        mutation.EnsurePlcInstance(projectPath, new EnsurePlcInstanceRequest(plcProjectName, plcInstanceName));
        mutation.BindInstanceContext(projectPath, new BindInstanceContextRequest(auxInstanceName, auxTaskObjectId, AuxTaskPriority, AuxTaskCycleTimeNs, ContextName: "AuxTaskCtx"));
        mutation.BindInstanceToTask(projectPath, new BindInstanceToTaskRequest(primaryInstanceName, runtimeTaskObjectId, RuntimeTaskPriority, RuntimeTaskCycleTimeNs));
        mutation.BindPlcInstanceToTask(projectPath, new BindPlcInstanceToTaskRequest(plcProjectName, plcInstanceName, RuntimeTaskName, runtimeTaskObjectId, RuntimeTaskPriority, RuntimeTaskCycleTimeNs));

        mutation.SetTaskAffinity(projectPath, new SetTaskAffinityRequest(RuntimeTaskName, "#x1"));
        mutation.SetTaskAffinity(projectPath, new SetTaskAffinityRequest(AuxTaskName, "#x1"));
        mutation.SetPlcProjectProperties(projectPath, new SetPlcProjectPropertiesRequest(
            plcProjectName,
            ProjectFilePath: plcProjectName + "\\" + plcProjectName + ".plcproj",
            TmcFilePath: plcProjectName + "\\" + plcProjectName + ".tmc",
            ReloadTmc: true,
            AmsPort: PlcAmsPort,
            FileArchiveSettings: "#x0002"));
        mutation.SetPlcInstanceMetadata(projectPath, new SetPlcInstanceMetadataRequest(
            plcProjectName,
            plcInstanceName,
            TcSmClass: "PlcTask",
            TmcPath: plcProjectName + "\\" + plcProjectName + ".tmc",
            KeepUnrestoredLinks: "2",
            ClassFactory: plcProjectName));

        mutation.EnsurePlcInstanceVarsGroup(projectPath, new EnsurePlcInstanceVarsGroupRequest(
            plcProjectName,
            plcInstanceName,
            StalePlcVarsGroupName,
            VarGrpType: 2,
            AreaNo: 1,
            Variables: [new PlcInstanceVarItem(StalePlcVarName, "UDINT", 96, 12)]));
        mutation.ClearPlcInstanceVars(projectPath, new ClearPlcInstanceVarsRequest(plcProjectName, plcInstanceName));
        mutation.EnsurePlcInstanceVarsGroup(projectPath, new EnsurePlcInstanceVarsGroupRequest(
            plcProjectName,
            plcInstanceName,
            "PlcTask Outputs",
            VarGrpType: 2,
            AreaNo: 1,
            Variables:
            [
                new PlcInstanceVarItem("MAIN.nSeed", "UDINT", 0, 0),
                new PlcInstanceVarItem("MAIN.nStage2Seed", "UDINT", 32, 4),
            ]));
        mutation.EnsurePlcInstanceVarsGroup(projectPath, new EnsurePlcInstanceVarsGroupRequest(
            plcProjectName,
            plcInstanceName,
            "PlcTask Inputs",
            VarGrpType: 1,
            AreaNo: 0,
            Variables:
            [
                new PlcInstanceVarItem("MAIN.nStage1", "UDINT", 0, 0),
                new PlcInstanceVarItem("MAIN.nStage2", "UDINT", 32, 4),
            ]));

        mutation.EnsureInitSymbol(projectPath, new EnsureInitSymbolRequest(plcProjectName, plcInstanceName, StaleInitSymbolName, "#x0201FFFF"));
        mutation.ClearPlcInitSymbols(projectPath, new ClearPlcInitSymbolsRequest(plcProjectName, plcInstanceName, RemoveContainerWhenEmpty: false));
        mutation.EnsureTaskPouOid(projectPath, new EnsureTaskPouOidRequest(plcProjectName, plcInstanceName, 99, "#x0201FFEE"));
        mutation.ClearPlcTaskPouOids(projectPath, new ClearPlcTaskPouOidsRequest(plcProjectName, plcInstanceName));
        mutation.EnsureTaskPouOid(projectPath, new EnsureTaskPouOidRequest(plcProjectName, plcInstanceName, RuntimeTaskPriority, runtimeTaskObjectId));
        mutation.EnsureInitSymbol(projectPath, new EnsureInitSymbolRequest(plcProjectName, plcInstanceName, "MAIN.RuntimeTaskOidProbe", runtimeTaskObjectId));
        mutation.EnsureInitSymbol(projectPath, new EnsureInitSymbolRequest(plcProjectName, plcInstanceName, "MAIN.AuxTaskOidProbe", auxTaskObjectId));

        mutation.ReplaceDataTypesSection(projectPath, new ReplaceDataTypesSectionRequest(
            "<DataTypes><DataType><Name>" + StaleDataTypeName + "</Name></DataType></DataTypes>"));
        mutation.ReplaceDataTypesSection(projectPath, new ReplaceDataTypesSectionRequest(
            "<DataTypes><DataType><Name>ST_IntegrationProcessData</Name></DataType><DataType><Name>ST_IntegrationAdsProbe</Name></DataType></DataTypes>"));
        mutation.ReplaceSystemSettingsSection(projectPath, new ReplaceSystemSettingsSectionRequest(
            "<Settings><MaxCycles>77</MaxCycles><" + StaleFragmentName + ">true</" + StaleFragmentName + "></Settings>"));
        mutation.ReplaceSystemSettingsSection(projectPath, new ReplaceSystemSettingsSectionRequest(
            "<Settings><MaxCycles>0</MaxCycles><IntegrationProbe>true</IntegrationProbe></Settings>"));
        mutation.ReplaceProjectIoSection(projectPath, new ReplaceProjectIoSectionRequest("<Io><Name>" + StaleIoName + "</Name></Io>"));
        mutation.ReplaceProjectIoSection(projectPath, new ReplaceProjectIoSectionRequest("<Io><Name>IntegrationJsonOwnedIo</Name></Io>"));
        mutation.EnsureIoTaskImage(projectPath, new EnsureIoTaskImageRequest(RuntimeTaskName, primaryInstanceName, ImageId: 1, SizeIn: 40, SizeOut: 10));

        mutation.EnsureParameterValue(projectPath, new EnsureParameterValueRequest(primaryInstanceName, StaleParameterName, ValueText: "999"));
        mutation.EnsureParameterValue(projectPath, new EnsureParameterValueRequest(auxInstanceName, StaleParameterName, ValueText: "888"));
        mutation.ClearInstanceParameterValues(projectPath, new ClearInstanceParameterValuesRequest(primaryInstanceName));
        mutation.ClearInstanceParameterValues(projectPath, new ClearInstanceParameterValuesRequest(auxInstanceName));
        mutation.ApplyInstanceParameterPlan(projectPath, new ApplyInstanceParameterPlanRequest(
        [
            new InstanceParameterMutation(primaryInstanceName, "Parameter.data1", ValueText: "123"),
            new InstanceParameterMutation(auxInstanceName, "Parameter.data1", ValueText: "17"),
        ]));
        mutation.EnsureParameterValue(projectPath, new EnsureParameterValueRequest(primaryInstanceName, "Parameter.data1", ValueText: "123"));

        mutation.ApplyInstanceInterfacePointerPlan(projectPath, new ApplyInstanceInterfacePointerPlanRequest(
        [
            new InstanceInterfacePointerMutation(primaryInstanceName, "CyclicCaller", runtimeTaskObjectId),
            new InstanceInterfacePointerMutation(auxInstanceName, "CyclicCaller", auxTaskObjectId),
        ]));
        mutation.EnsureInterfacePointerValue(projectPath, new EnsureInterfacePointerValueRequest(auxInstanceName, "CyclicCaller", auxTaskObjectId));

        mutation.EnsureDataPointerValue(projectPath, new EnsureDataPointerValueRequest(primaryInstanceName, StalePointerName, auxInstanceObjectId, AreaNo: 7, ByteOffset: 44, ByteSize: 8));
        mutation.EnsureDataPointerValue(projectPath, new EnsureDataPointerValueRequest(auxInstanceName, StalePointerName, primaryInstanceObjectId, AreaNo: 8, ByteOffset: 48, ByteSize: 8));
        mutation.ClearInstanceDataPointerValues(projectPath, new ClearInstanceDataPointerValuesRequest(primaryInstanceName));
        mutation.ClearInstanceDataPointerValues(projectPath, new ClearInstanceDataPointerValuesRequest(auxInstanceName));
        mutation.ApplyInstanceDataPointerPlan(projectPath, new ApplyInstanceDataPointerPlanRequest(
        [
            new InstanceDataPointerMutation(primaryInstanceName, "DataIn", auxInstanceObjectId, AreaNo: 2, ByteOffset: 0, ByteSize: 4),
            new InstanceDataPointerMutation(primaryInstanceName, "DataOut", auxInstanceObjectId, AreaNo: 1, ByteOffset: 0, ByteSize: 4),
            new InstanceDataPointerMutation(auxInstanceName, "DataIn", primaryInstanceObjectId, AreaNo: 2, ByteOffset: 0, ByteSize: 4),
            new InstanceDataPointerMutation(auxInstanceName, "DataOut", primaryInstanceObjectId, AreaNo: 1, ByteOffset: 0, ByteSize: 4),
        ]));
        mutation.EnsureDataPointerValue(projectPath, new EnsureDataPointerValueRequest(primaryInstanceName, "DataIn", auxInstanceObjectId, AreaNo: 2, ByteOffset: 0, ByteSize: 4));

        mutation.EnsureMappingLink(projectPath, new EnsureMappingLinkRequest(
            "TIXC^" + CppProjectName + "^" + primaryInstanceName,
            "TIPC^" + plcProjectName + "^" + plcInstanceName,
            "Output^StaleDataOut",
            "PlcTask Inputs^MAIN.nStale"));
        mutation.ClearMappings(projectPath, new ClearMappingsRequest());
        mutation.ReplaceMappingsSection(projectPath, new ReplaceMappingsSectionRequest(
            "<Mappings><OwnerA Name=\"StaleOwner\"><OwnerB Name=\"StaleTarget\"><Link VarA=\"StaleA\" VarB=\"StaleB\" /></OwnerB></OwnerA></Mappings>"));
        mutation.ReplaceMappingsSection(projectPath, new ReplaceMappingsSectionRequest("<Mappings></Mappings>"));
        mutation.UpsertFragment(projectPath, new TsprojFragmentUpsertRequest(
            [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
            "<UnrestoredVarLinks><Name>StaleUnrestored</Name></UnrestoredVarLinks>",
            MatchElementName: "UnrestoredVarLinks",
            MatchNameValue: "StaleUnrestored"));
        mutation.ClearUnrestoredVarLinks(projectPath, new ClearUnrestoredVarLinksRequest());
        mutation.EnsureMappingLink(projectPath, new EnsureMappingLinkRequest(
            "TIPC^" + plcProjectName + "^" + plcInstanceName,
            "TIXC^" + CppProjectName + "^" + primaryInstanceName,
            "PlcTask Outputs^MAIN.nSeed",
            "Input^DataIn"));
        mutation.EnsureMappingLink(projectPath, new EnsureMappingLinkRequest(
            "TIXC^" + CppProjectName + "^" + primaryInstanceName,
            "TIPC^" + plcProjectName + "^" + plcInstanceName,
            "Output^DataOut",
            "PlcTask Inputs^MAIN.nStage1"));
        mutation.EnsureMappingLink(projectPath, new EnsureMappingLinkRequest(
            "TIPC^" + plcProjectName + "^" + plcInstanceName,
            "TIXC^" + CppProjectName + "^" + auxInstanceName,
            "PlcTask Outputs^MAIN.nStage2Seed",
            "Input^DataIn"));
        mutation.EnsureMappingLink(projectPath, new EnsureMappingLinkRequest(
            "TIXC^" + CppProjectName + "^" + auxInstanceName,
            "TIPC^" + plcProjectName + "^" + plcInstanceName,
            "Output^DataOut",
            "PlcTask Inputs^MAIN.nStage2"));

        mutation.UpsertElement(projectPath, new TsprojElementUpsertRequest(
            [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
            "IntegrationMetadata",
            Attributes:
            [
                new TsprojXmlAttribute("GeneratedBy", "TwinCatAutomationKit.IntegrationTests"),
            ],
            ChildValues:
            [
                new TsprojXmlChildValue("RuntimeTask", RuntimeTaskName),
                new TsprojXmlChildValue("AuxTask", AuxTaskName),
            ]));
        mutation.UpsertFragment(projectPath, new TsprojFragmentUpsertRequest(
            [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
            "<IntegrationFragment><Name>RuntimeAdsProbe</Name><Symbols>MAIN.nCycle;MAIN.bPipelineOk</Symbols></IntegrationFragment>",
            MatchElementName: "IntegrationFragment",
            MatchNameValue: "RuntimeAdsProbe"));
        mutation.ApplyMutationPlan(projectPath, new ApplyTsprojMutationPlanRequest(
            ElementUpserts:
            [
                new TsprojElementUpsertRequest(
                    [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
                    "FullCoverageBatchMetadata",
                    Attributes: [new TsprojXmlAttribute("Source", "integration-test")],
                    ChildValues: [new TsprojXmlChildValue("ParameterPlan", "inline")])
            ],
            FragmentUpserts:
            [
                new TsprojFragmentUpsertRequest(
                    [new TsprojPathSegment("Project"), new TsprojPathSegment("System")],
                    "<FullCoverageBatchFragment><Name>RuntimeLinkGraph</Name><Primary>" + primaryInstanceName + "</Primary><Aux>" + auxInstanceName + "</Aux></FullCoverageBatchFragment>",
                    MatchElementName: "FullCoverageBatchFragment",
                    MatchNameValue: "RuntimeLinkGraph")
            ]));
        mutation.MergeNamedElementFragment(projectPath, new MergeNamedElementFragmentRequest(
            "DataTypes",
            "<DataType><Name>ST_IntegrationMergedAudit</Name></DataType>",
            MatchElementName: "DataType",
            MatchNameValue: "ST_IntegrationMergedAudit",
            FragmentSource: "OrderedTwinCatScenarioTests inline DataType fragment after replace-data-types-section creates a unique parent.",
            TargetParentPath: "root DataTypes element",
            FieldMeaning: "Name-only audit DataType used to prove documented merge-fragment wiring.",
            VerificationEvidence: "OrderedTwinCatScenarioTests reopens and builds the mutated TwinCAT project."));
    }

    private static void AddFileMutationCoverage(HashSet<string> coveredInterfaces, HashSet<string> coveredStepKinds)
    {
        foreach (string item in new[]
        {
            "TwinCatTsprojMutationService.ApplyMutationPlan",
            "TwinCatTsprojMutationService.UpsertElement",
            "TwinCatTsprojMutationService.UpsertFragment",
            "TwinCatTsprojMutationService.EnsureTaskDefinition",
            "TwinCatTsprojMutationService.ClearTaskLayout",
            "TwinCatTsprojMutationService.EnsureTaskVarsGroup",
            "TwinCatTsprojMutationService.EnsureTaskImage",
            "TwinCatTsprojMutationService.BindInstanceContext",
            "TwinCatTsprojMutationService.BindInstanceToTask",
            "TwinCatTsprojMutationService.EnsureCppInstance",
            "TwinCatTsprojMutationService.EnsurePlcInstance",
            "TwinCatTsprojMutationService.BindPlcInstanceToTask",
            "TwinCatTsprojMutationService.SetTaskAffinity",
            "TwinCatTsprojMutationService.SetPlcProjectProperties",
            "TwinCatTsprojMutationService.SetPlcInstanceMetadata",
            "TwinCatTsprojMutationService.ClearPlcInstanceVars",
            "TwinCatTsprojMutationService.EnsurePlcInstanceVarsGroup",
            "TwinCatTsprojMutationService.ClearPlcInitSymbols",
            "TwinCatTsprojMutationService.ClearPlcTaskPouOids",
            "TwinCatTsprojMutationService.ClearMappings",
            "TwinCatTsprojMutationService.ClearUnrestoredVarLinks",
            "TwinCatTsprojMutationService.ReplaceMappingsSection",
            "TwinCatTsprojMutationService.ReplaceProjectIoSection",
            "TwinCatTsprojMutationService.ReplaceDataTypesSection",
            "TwinCatTsprojMutationService.ReplaceSystemSettingsSection",
            "TwinCatTsprojMutationService.ApplyInstanceParameterPlan",
            "TwinCatTsprojMutationService.ClearInstanceParameterValues",
            "TwinCatTsprojMutationService.ClearInstanceDataPointerValues",
            "TwinCatTsprojMutationService.ApplyInstanceInterfacePointerPlan",
            "TwinCatTsprojMutationService.ApplyInstanceDataPointerPlan",
            "TwinCatTsprojMutationService.EnsureTaskPouOid",
            "TwinCatTsprojMutationService.EnsureInitSymbol",
            "TwinCatTsprojMutationService.EnsureMappingLink",
            "TwinCatTsprojMutationService.EnsureIoTaskImage",
            "TwinCatTsprojMutationService.EnsureParameterValue",
            "TwinCatTsprojMutationService.EnsureInterfacePointerValue",
            "TwinCatTsprojMutationService.EnsureDataPointerValue",
            "TwinCatTsprojMutationService.MergeNamedElementFragment",
            "TwinCatTsprojMutationService.ConvertObjectIdToInitSymbolData",
            "TwinCatTsprojMutationService.DeriveIoTaskImageObjectId",
        })
        {
            coveredInterfaces.Add(item);
        }

        foreach (string kind in new[]
        {
            "tsproj.apply-mutation-plan",
            "tsproj.upsert-element",
            "tsproj.upsert-fragment",
            "tsproj.ensure-task",
            "tsproj.clear-task-layout",
            "tsproj.ensure-task-vars-group",
            "tsproj.ensure-task-image",
            "tsproj.bind-instance-context",
            "tsproj.bind-instance-task",
            "tsproj.ensure-cpp-instance",
            "tsproj.ensure-plc-instance",
            "tsproj.bind-plc-instance-task",
            "tsproj.set-task-affinity",
            "tsproj.set-plc-project-properties",
            "tsproj.set-plc-instance-metadata",
            "tsproj.clear-plc-instance-vars",
            "tsproj.ensure-plc-instance-vars-group",
            "tsproj.clear-plc-init-symbols",
            "tsproj.clear-plc-task-pou-oids",
            "tsproj.clear-mappings",
            "tsproj.clear-unrestored-var-links",
            "tsproj.replace-mappings-section",
            "tsproj.replace-project-io-section",
            "tsproj.replace-data-types-section",
            "tsproj.replace-system-settings-section",
            "tsproj.apply-instance-parameter-plan",
            "tsproj.clear-instance-parameter-values",
            "tsproj.clear-instance-data-pointer-values",
            "tsproj.apply-instance-interface-pointer-plan",
            "tsproj.apply-instance-data-pointer-plan",
            "tsproj.ensure-task-pou-oid",
            "tsproj.ensure-init-symbol",
            "tsproj.ensure-mapping-link",
            "tsproj.ensure-io-task-image",
            "tsproj.ensure-parameter",
            "tsproj.ensure-interface-pointer",
            "tsproj.ensure-data-pointer",
            "tsproj.merge-fragment",
        })
        {
            coveredStepKinds.Add(kind);
        }
    }

    private static void AssertRealProjectMutationSemantics(ScenarioState state)
    {
        XDocument document = XDocument.Load(state.ProjectInfo.ProjectPath);

        XElement runtimeTask = RequireNamedElement(document, "Task", RuntimeTaskName);
        XElement auxTask = RequireNamedElement(document, "Task", AuxTaskName);
        AssertAttribute(runtimeTask, "Priority", RuntimeTaskPriority.ToString(CultureInfo.InvariantCulture), "RuntimeTask priority should be deterministic.");
        AssertAttribute(runtimeTask, "CycleTime", (RuntimeTaskCycleTimeNs / 100).ToString(CultureInfo.InvariantCulture), "RuntimeTask cycle time should be written in TwinCAT units.");
        AssertAttribute(runtimeTask, "AmsPort", RuntimeTaskAmsPort.ToString(CultureInfo.InvariantCulture), "RuntimeTask AMS port should be stable.");
        AssertAttribute(runtimeTask, "IoAtBegin", "true", "RuntimeTask should execute IO at task begin.");
        AssertHexAttribute(runtimeTask, "Affinity", "#x1", "RuntimeTask affinity should be written.");
        AssertTaskVarsGroup(runtimeTask, "Inputs", "1", "1", expectedVarCount: 10);
        AssertTaskVarsGroup(runtimeTask, "Outputs", "2", "1", expectedVarCount: 10);
        AssertAttribute(auxTask, "Priority", AuxTaskPriority.ToString(CultureInfo.InvariantCulture), "AuxTask priority should be deterministic.");
        AssertAttribute(auxTask, "CycleTime", (AuxTaskCycleTimeNs / 100).ToString(CultureInfo.InvariantCulture), "AuxTask cycle time should be written in TwinCAT units.");
        AssertAttribute(auxTask, "AmsPort", AuxTaskAmsPort.ToString(CultureInfo.InvariantCulture), "AuxTask AMS port should be stable.");
        AssertAttribute(auxTask, "IoAtBegin", "true", "AuxTask should execute IO at task begin.");
        AssertHexAttribute(auxTask, "Affinity", "#x1", "AuxTask affinity should be written.");
        AssertNamedCount(document, "Task", RuntimeTaskName, 1);
        AssertNamedCount(document, "Task", AuxTaskName, 1);
        AssertTaskVarsGroup(auxTask, "AuxInputs", "1", "1", expectedVarCount: 4);
        AssertTaskVarsGroup(auxTask, "AuxOutputs", "2", "1", expectedVarCount: 4);
        AssertTaskVar(auxTask, "AuxInputs", "AuxIn1", "UDINT", expectedBitOffs: null, expectedExternalAddress: null);
        AssertTaskVar(auxTask, "AuxInputs", "AuxIn4", "UDINT", "96", "12");
        AssertTaskVar(auxTask, "AuxOutputs", "AuxOut1", "UDINT", expectedBitOffs: null, expectedExternalAddress: null);
        AssertTaskVar(auxTask, "AuxOutputs", "AuxOut4", "UDINT", "96", "12");
        IntegrationAssertEx.False(HasTaskVars(auxTask, StaleTaskVarsGroupName, "StaleAux1", "UDINT"), "ClearTaskLayout should remove stale task Vars groups before rebuild.");
        XElement auxImage = auxTask.Elements().FirstOrDefault(element => element.Name.LocalName == "Image")
            ?? throw new InvalidOperationException("AuxTask image is missing.");
        AssertAttribute(auxImage, "Id", "2", "AuxTask image Id should be deterministic.");
        AssertAttribute(auxImage, "AddrType", "1", "AuxTask image address type should be deterministic.");
        AssertAttribute(auxImage, "ImageType", "1", "AuxTask image type should be deterministic.");
        AssertAttribute(auxImage, "SizeIn", "16", "AuxTask image input size should be deterministic.");
        AssertAttribute(auxImage, "SizeOut", "16", "AuxTask image output size should be deterministic.");
        IntegrationAssertEx.Equal("AuxImage", GetChildValue(auxImage, "Name"), "AuxTask image name should be deterministic.");
        IntegrationAssertEx.False(auxTask.Elements().Any(element =>
            element.Name.LocalName == "Image" &&
            string.Equals(GetChildValue(element, "Name"), StaleTaskImageName, StringComparison.OrdinalIgnoreCase)),
            "ClearTaskLayout should remove stale task Image before rebuild.");

        XElement primaryInstance = RequireNamedElement(document, "Instance", state.PrimaryInstanceName);
        XElement auxInstance = RequireNamedElement(document, "Instance", state.AuxInstanceName);
        XElement fileCreatedInstance = RequireNamedElement(document, "Instance", "FileMutationCpp01");
        AssertObjectIdAttribute(primaryInstance, state.PrimaryInstanceObjectId, "Primary C++ instance ObjectId should be preserved.");
        AssertObjectIdAttribute(auxInstance, state.AuxInstanceObjectId, "Aux C++ instance ObjectId should be preserved.");
        AssertObjectIdAttribute(fileCreatedInstance, "#x02020010", "File-created C++ instance ObjectId should be preserved.");
        AssertInstanceContext(fileCreatedInstance, string.Empty, RuntimeTaskPriority, RuntimeTaskCycleTimeNs);
        AssertInstanceContext(primaryInstance, state.RuntimeTaskObjectId, RuntimeTaskPriority, RuntimeTaskCycleTimeNs);
        AssertInstanceContext(auxInstance, state.AuxTaskObjectId, AuxTaskPriority, AuxTaskCycleTimeNs);
        AssertNamedValue(primaryInstance, "ParameterValues", "Parameter.data1", "Value", "123");
        AssertNamedValue(auxInstance, "ParameterValues", "Parameter.data1", "Value", "17");
        AssertNamedValueAbsent(primaryInstance, "ParameterValues", StaleParameterName);
        AssertNamedValueAbsent(auxInstance, "ParameterValues", StaleParameterName);
        AssertNamedValue(primaryInstance, "InterfacePointerValues", "CyclicCaller", "OTCID", state.RuntimeTaskObjectId);
        AssertNamedValue(primaryInstance, "InterfacePointerValues", "IoTaskImage", "OTCID", "#x03040010");
        AssertNamedValue(auxInstance, "InterfacePointerValues", "CyclicCaller", "OTCID", state.AuxTaskObjectId);
        AssertNamedValue(primaryInstance, "DataPointerValues", "DataIn", "OTCID", state.AuxInstanceObjectId);
        AssertNamedValue(primaryInstance, "DataPointerValues", "DataIn", "AreaNo", "2");
        AssertNamedValue(primaryInstance, "DataPointerValues", "DataIn", "ByteOffs", "0");
        AssertNamedValue(primaryInstance, "DataPointerValues", "DataIn", "ByteSize", "4");
        AssertNamedValue(primaryInstance, "DataPointerValues", "DataOut", "OTCID", state.AuxInstanceObjectId);
        AssertNamedValue(primaryInstance, "DataPointerValues", "DataOut", "AreaNo", "1");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "OTCID", state.PrimaryInstanceObjectId);
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "AreaNo", "2");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "ByteOffs", "0");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "ByteSize", "4");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "OTCID", state.PrimaryInstanceObjectId);
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "AreaNo", "1");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "ByteOffs", "0");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "ByteSize", "4");
        AssertNamedValueAbsent(primaryInstance, "DataPointerValues", StalePointerName);
        AssertNamedValueAbsent(auxInstance, "DataPointerValues", StalePointerName);
        XElement plcProject = RequirePlcProject(document, state.PlcProjectName);
        XElement plcInstance = RequirePlcInstance(plcProject, state.PlcInstanceName);
        AssertAttribute(plcProject, "AmsPort", PlcAmsPort.ToString(CultureInfo.InvariantCulture), "PLC project AMS port should be normalized.");
        AssertAttribute(plcProject, "ReloadTmc", "true", "PLC project should reload TMC.");
        AssertAttribute(plcProject, "PrjFilePath", state.PlcProjectName + "\\" + state.PlcProjectName + ".plcproj", "PLC project file path should be deterministic.");
        AssertAttribute(plcProject, "TmcFilePath", state.PlcProjectName + "\\" + state.PlcProjectName + ".tmc", "PLC TMC file path should be deterministic.");
        AssertAttribute(plcProject, "FileArchiveSettings", "#x0002", "PLC file archive settings should be deterministic.");
        AssertAttribute(plcInstance, "TcSmClass", "PlcTask", "PLC instance class should be deterministic.");
        AssertAttribute(plcInstance, "TmcPath", state.PlcProjectName + "\\" + state.PlcProjectName + ".tmc", "PLC instance TMC path should be deterministic.");
        AssertAttribute(plcInstance, "KeepUnrestoredLinks", "2", "PLC instance unrestored link policy should be explicit.");
        AssertPlcVarsGroupCount(plcInstance, "PlcTask Outputs", 1);
        AssertPlcVarsGroupCount(plcInstance, "PlcTask Inputs", 1);
        AssertPlcVarsGroup(plcInstance, "PlcTask Outputs", "2", "1", "1", expectedVarCount: 2);
        AssertPlcVarsGroup(plcInstance, "PlcTask Inputs", "1", "1", "0", expectedVarCount: 2);
        AssertPlcVar(plcInstance, "PlcTask Outputs", "MAIN.nSeed", "UDINT", "0", "0");
        AssertPlcVar(plcInstance, "PlcTask Outputs", "MAIN.nStage2Seed", "UDINT", "32", "4");
        AssertPlcVar(plcInstance, "PlcTask Inputs", "MAIN.nStage1", "UDINT", "0", "0");
        AssertPlcVar(plcInstance, "PlcTask Inputs", "MAIN.nStage2", "UDINT", "32", "4");
        IntegrationAssertEx.False(HasPlcVar(plcInstance, StalePlcVarsGroupName, StalePlcVarName), "ClearPlcInstanceVars should remove stale PLC Vars groups before rebuild.");

        XElement taskPouOid = plcInstance.Descendants().FirstOrDefault(element => element.Name.LocalName == "TaskPouOid")
            ?? throw new InvalidOperationException("PLC TaskPouOid is missing.");
        AssertAttribute(taskPouOid, "Prio", RuntimeTaskPriority.ToString(CultureInfo.InvariantCulture), "TaskPouOid priority should remain deterministic.");
        AssertAttribute(taskPouOid, "OTCID", state.RuntimeTaskObjectId, "TaskPouOid ObjectId should remain present after XAE reopen.");
        IntegrationAssertEx.False(plcInstance.Descendants().Any(element =>
            element.Name.LocalName == "TaskPouOid" &&
            string.Equals(GetAttributeValue(element, "Prio"), "99", StringComparison.OrdinalIgnoreCase)),
            "ClearPlcTaskPouOids should remove stale TaskPouOid entries before rebuild.");

        string runtimeInitData = TwinCatTsprojMutationService.ConvertObjectIdToInitSymbolData(state.RuntimeTaskObjectId);
        string auxInitData = TwinCatTsprojMutationService.ConvertObjectIdToInitSymbolData(state.AuxTaskObjectId);
        state.Cover("TwinCatTsprojMutationService.ConvertObjectIdToInitSymbolData");
        AssertInitSymbol(plcInstance, "MAIN.RuntimeTaskOidProbe", runtimeInitData);
        AssertInitSymbol(plcInstance, "MAIN.AuxTaskOidProbe", auxInitData);
        AssertInitSymbolAbsent(plcInstance, StaleInitSymbolName);

        AssertDataTypes(document, "ST_IntegrationProcessData", "ST_IntegrationAdsProbe", "ST_IntegrationMergedAudit");
        AssertElementNameAbsent(document, "DataType", StaleDataTypeName);
        AssertElementChildValue(document, "IntegrationMetadata", "RuntimeTask", RuntimeTaskName);
        AssertElementChildValue(document, "IntegrationMetadata", "AuxTask", AuxTaskName);
        AssertElementChildValue(document, "IntegrationFragment", "Symbols", "MAIN.nCycle;MAIN.bPipelineOk");
        AssertElementChildValue(document, "FullCoverageBatchMetadata", "ParameterPlan", "inline");
        AssertElementChildValue(document, "FullCoverageBatchFragment", "Primary", state.PrimaryInstanceName);
        AssertElementChildValue(document, "FullCoverageBatchFragment", "Aux", state.AuxInstanceName);
        AssertContainsElement(document, "Io", "IntegrationJsonOwnedIo");
        AssertElementNameAbsent(document, "Io", StaleIoName);
        AssertElementNameAbsent(document, StaleFragmentName, null);

        AssertMapping(document, "PlcTask Outputs^MAIN.nSeed", "Input^DataIn");
        AssertMapping(document, "Output^DataOut", "PlcTask Inputs^MAIN.nStage1");
        AssertMapping(document, "PlcTask Outputs^MAIN.nStage2Seed", "Input^DataIn");
        AssertMapping(document, "Output^DataOut", "PlcTask Inputs^MAIN.nStage2");
        IntegrationAssertEx.Equal(4, document.Descendants().Count(element => element.Name.LocalName == "Link"), "Expected exactly four deterministic mapping links.");
        IntegrationAssertEx.False(document.Descendants().Any(element =>
            element.Name.LocalName == "Link" &&
            (string.Equals(GetAttributeValue(element, "VarA"), "StaleA", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetAttributeValue(element, "VarB"), "StaleB", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetAttributeValue(element, "VarA"), "Output^StaleDataOut", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetAttributeValue(element, "VarB"), "PlcTask Inputs^MAIN.nStale", StringComparison.OrdinalIgnoreCase))),
            "ClearMappings and ReplaceMappingsSection should remove stale mapping links before deterministic rebuild.");
        IntegrationAssertEx.False(document.Descendants().Any(element => element.Name.LocalName == "UnrestoredVarLinks"), "UnrestoredVarLinks should be cleared before reopen/build.");
    }

    private static void AssertAtomicWrapperMutationSemantics(ScenarioState state)
    {
        XDocument document = XDocument.Load(state.ProjectInfo.ProjectPath);
        XElement primaryInstance = RequireNamedElement(document, "Instance", state.PrimaryInstanceName);
        XElement auxInstance = RequireNamedElement(document, "Instance", state.AuxInstanceName);

        AssertNamedValue(primaryInstance, "ParameterValues", AtomicParameterName, "Value", "456");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "OTCID", state.PrimaryInstanceObjectId);
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "AreaNo", "2");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "ByteOffs", "0");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataIn", "ByteSize", "4");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "OTCID", state.PrimaryInstanceObjectId);
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "AreaNo", "1");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "ByteOffs", "0");
        AssertNamedValue(auxInstance, "DataPointerValues", "DataOut", "ByteSize", "4");
        AssertNamedValueAbsent(auxInstance, "DataPointerValues", AtomicPointerName);
        AssertElementChildValue(document, "AtomicWrapperMetadata", "Scenario", AtomicMetadataName);
        AssertContainsElement(document, "AtomicWrapperFragment", AtomicFragmentName);
        AssertElementChildValue(document, "AtomicWrapperBatchMetadata", "Result", "Applied");
        IntegrationAssertEx.False(document.Descendants().Any(element => element.Name.LocalName == "UnrestoredVarLinks"), "Atomic ClearUnrestoredVarLinks wrapper should leave no UnrestoredVarLinks blocks.");
    }

    private static void AssertAtomicRunSummary(AutomationRunSummary summary, string evidenceRoot, int expectedAdsPort, ScenarioState state)
    {
        string[] expectedStepIds =
        [
            "atomic-save",
            "atomic-export-task",
            "atomic-ensure-parameter",
            "atomic-clear-unrestored-var-links",
            "atomic-clear-aux-data-pointers",
            "atomic-restore-aux-data-pointers",
            "atomic-upsert-element",
            "atomic-upsert-fragment",
            "atomic-apply-mutation-plan",
            "atomic-ads-scan",
            "atomic-ads-read",
            "atomic-ads-read-symbols",
        ];

        IntegrationAssertEx.Equal(expectedStepIds.Length, summary.Steps.Count, "Atomic run should execute every declared wrapper step exactly once.");
        for (int index = 0; index < expectedStepIds.Length; index++)
        {
            StepExecutionRecord record = summary.Steps[index];
            IntegrationAssertEx.Equal(expectedStepIds[index], record.StepId, "Atomic step order should be deterministic.");
            IntegrationAssertEx.True(
                record.Status is StepExecutionStatus.Succeeded or StepExecutionStatus.Indirect,
                $"Atomic step {record.StepId} should not fail or skip. Status={record.Status}.");
        }

        StepExecutionRecord export = RequireStepRecord(summary, "atomic-export-task");
        IntegrationAssertEx.Equal(StepExecutionStatus.Succeeded, export.Status, "Atomic export should report Succeeded.");
        IntegrationAssertEx.Equal("engineering.export-tree-item-xml", export.Contract.Kind, "Atomic export should use the public export step contract.");
        IntegrationAssertEx.True(export.Evidence.Any(item => item.Kind == "xml" && File.Exists(item.Path)), "Atomic export should attach a real XML evidence artifact.");
        AssertTreeExport(Path.Combine(state.EvidenceDir, "atomic-runtime-task.xml"), RuntimeTaskName, "TIRT^" + RuntimeTaskName, null);

        StepExecutionRecord clearPointers = RequireStepRecord(summary, "atomic-clear-aux-data-pointers");
        IntegrationAssertEx.Equal(StepExecutionStatus.Indirect, clearPointers.Status, "Atomic tsproj clear wrapper should report Indirect.");
        IntegrationAssertEx.Contains(state.AuxInstanceName, clearPointers.Summary, "Atomic clear pointer summary should name the instance it changed.");

        StepExecutionRecord adsScan = RequireStepRecord(summary, "atomic-ads-scan");
        IntegrationAssertEx.Equal(StepExecutionStatus.Succeeded, adsScan.Status, "Atomic ADS scan should report Succeeded.");
        string succeededPorts = RequireOutput(adsScan, "succeededPorts");
        IntegrationAssertEx.True(
            succeededPorts.Split(';', StringSplitOptions.RemoveEmptyEntries).Contains(expectedAdsPort.ToString(CultureInfo.InvariantCulture)),
            $"Atomic ADS scan should include configured runtime port {expectedAdsPort} in succeededPorts.");

        StepExecutionRecord adsRead = RequireStepRecord(summary, "atomic-ads-read");
        IntegrationAssertEx.Equal(StepExecutionStatus.Succeeded, adsRead.Status, "Atomic ADS read should report Succeeded.");
        uint atomicSingleValue = uint.Parse(RequireOutput(adsRead, "value"), CultureInfo.InvariantCulture);
        IntegrationAssertEx.Equal(RuntimeConfiguredParameter, atomicSingleValue, "Atomic ADS single read should return the deterministic configured parameter.");

        StepExecutionRecord adsReadSymbols = RequireStepRecord(summary, "atomic-ads-read-symbols");
        IntegrationAssertEx.Equal(StepExecutionStatus.Succeeded, adsReadSymbols.Status, "Atomic ADS batch read should report Succeeded.");
        IntegrationAssertEx.Equal("0", RequireOutput(adsReadSymbols, "failedCount"), "Atomic ADS batch read should have no failed symbols.");
        AdsReadSymbolResult[] atomicSymbols = System.Text.Json.JsonSerializer.Deserialize<AdsReadSymbolResult[]>(RequireOutput(adsReadSymbols, "valuesJson"))
            ?? throw new InvalidOperationException("Atomic ADS batch valuesJson should deserialize.");
        AssertRuntimeSemanticSymbols(new AdsReadSymbolsResult(state.Config.AmsNetId, expectedAdsPort, atomicSymbols), state);

        IntegrationAssertEx.True(File.Exists(Path.Combine(evidenceRoot, "run-summary.json")), "Atomic pipeline should write run-summary.json evidence.");
        IntegrationAssertEx.True(File.Exists(Path.Combine(evidenceRoot, "step-results.csv")), "Atomic pipeline should write step-results.csv evidence.");
        string csv = File.ReadAllText(Path.Combine(evidenceRoot, "step-results.csv"));
        foreach (string stepId in expectedStepIds)
        {
            IntegrationAssertEx.Contains(stepId, csv, $"Atomic step-results.csv should include {stepId}.");
        }
    }

    private static StepExecutionRecord RequireStepRecord(AutomationRunSummary summary, string stepId) =>
        summary.Steps.FirstOrDefault(step => string.Equals(step.StepId, stepId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Atomic run summary did not contain step '{stepId}'.");

    private static string RequireOutput(StepExecutionRecord record, string name)
    {
        if (!record.Outputs.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Step '{record.StepId}' should expose non-empty output '{name}'.");
        }

        return value;
    }

    private static void AssertCreatedCppProject(TwinCatNodeInfo cppProject, string solutionDirectory)
    {
        string expectedProjectPath = Path.Combine(solutionDirectory, CppProjectName, CppProjectName + ".vcxproj");
        string expectedTmcPath = Path.Combine(solutionDirectory, CppProjectName, CppProjectName + ".tmc");

        IntegrationAssertEx.Equal("TIXC^" + CppProjectName, cppProject.TreeItemPath, "C++ project tree path should be deterministic.");
        IntegrationAssertEx.Equal(CppProjectName, cppProject.DisplayName, "C++ project display name should be deterministic.");
        IntegrationAssertEx.Equal(expectedProjectPath, cppProject.FilePath, "C++ project file path should point at the generated project.");
        IntegrationAssertEx.True(File.Exists(expectedProjectPath), $"C++ project file was not created: {expectedProjectPath}");
        IntegrationAssertEx.True(File.Exists(expectedTmcPath), $"C++ TMC file was not created: {expectedTmcPath}");

        XDocument project = XDocument.Load(expectedProjectPath);
        IntegrationAssertEx.True(project.Descendants().Any(element =>
            element.Name.LocalName == "ProjectGuid" &&
            !string.IsNullOrWhiteSpace(element.Value)),
            "C++ vcxproj should contain a non-empty ProjectGuid.");
    }

    private static void AssertCppProbe(CppProjectModuleArtifactsResult probe, bool requireModuleArtifacts)
    {
        IntegrationAssertEx.True(probe.ProjectDirectoryExists, "C++ probe should see the project directory.");
        IntegrationAssertEx.True(probe.ProjectFileExists, "C++ probe should see the project file.");
        IntegrationAssertEx.True(File.Exists(probe.ProjectFilePath), $"C++ probe project path should exist: {probe.ProjectFilePath}");
        IntegrationAssertEx.True(File.Exists(probe.ProjectTmcPath), $"C++ probe TMC path should exist: {probe.ProjectTmcPath}");
        if (requireModuleArtifacts)
        {
            IntegrationAssertEx.True(probe.HasModuleEntryInTmc, "C++ probe should see a module entry in TMC.");
            IntegrationAssertEx.True(probe.HasUsableModuleGuid, "C++ probe should see a usable module GUID.");
            IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(probe.DetectedModuleName), "C++ probe should report the module name.");
            IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(probe.DetectedModuleGuid), "C++ probe should report the module GUID.");
        }
    }

    private static void AssertBootstrapModuleArtifacts(BootstrapCppModuleArtifactsResult bootstrap)
    {
        IntegrationAssertEx.Equal("BootProbeModule", bootstrap.ModuleName, "Bootstrap should report the requested module name.");
        IntegrationAssertEx.True(Guid.TryParse(bootstrap.ModuleGuid.Trim('{', '}'), out _), "Bootstrap should create a parseable module GUID.");
        IntegrationAssertEx.True(File.Exists(bootstrap.ModuleHeaderPath), $"Bootstrap should create module header: {bootstrap.ModuleHeaderPath}");
        IntegrationAssertEx.True(File.Exists(bootstrap.ModuleSourcePath), $"Bootstrap should create module source: {bootstrap.ModuleSourcePath}");
        IntegrationAssertEx.Contains("BootProbeModule", File.ReadAllText(bootstrap.ModuleHeaderPath), "Bootstrap header should contain the module class name.");
        IntegrationAssertEx.Contains("BootProbeModule", File.ReadAllText(bootstrap.ModuleSourcePath), "Bootstrap source should contain the module class name.");
    }

    private static void AssertCreatedModule(TwinCatNodeInfo module, string solutionDirectory)
    {
        IntegrationAssertEx.True(module.TreeItemPath.Contains(CppProjectName, StringComparison.OrdinalIgnoreCase), "Created module tree path should be under the C++ project.");
        IntegrationAssertEx.True(module.DisplayName.Contains(AuxModuleName, StringComparison.OrdinalIgnoreCase), "Created module display name should contain the requested module name.");

        string headerPath = Path.Combine(solutionDirectory, CppProjectName, AuxModuleName + ".h");
        string sourcePath = Path.Combine(solutionDirectory, CppProjectName, AuxModuleName + ".cpp");
        IntegrationAssertEx.True(File.Exists(headerPath), $"Aux module header should exist: {headerPath}");
        IntegrationAssertEx.True(File.Exists(sourcePath), $"Aux module source should exist: {sourcePath}");
        IntegrationAssertEx.Contains(AuxModuleName, File.ReadAllText(headerPath), "Aux module header should contain the module name.");
        IntegrationAssertEx.Contains(AuxModuleName, File.ReadAllText(sourcePath), "Aux module source should contain the module name.");
    }

    private static void AssertTmcContainsModule(string tmcPath, string moduleName)
    {
        XDocument document = XDocument.Load(tmcPath);
        bool found = document.Descendants().Any(element =>
            element.Name.LocalName == "Module" &&
            (string.Equals(GetChildValue(element, "Name"), moduleName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(GetAttributeValue(element, "Name"), moduleName, StringComparison.OrdinalIgnoreCase) ||
             element.ToString(SaveOptions.DisableFormatting).Contains(moduleName, StringComparison.OrdinalIgnoreCase)));
        IntegrationAssertEx.True(found, $"TMC should contain module '{moduleName}'.");
    }

    private static void AssertCreatedModuleInstance(TwinCatNodeInfo instance, string requestedBaseName)
    {
        IntegrationAssertEx.True(instance.TreeItemPath.Contains(CppProjectName, StringComparison.OrdinalIgnoreCase), "Module instance tree path should be under the C++ project.");
        IntegrationAssertEx.True(instance.DisplayName.Contains(requestedBaseName, StringComparison.OrdinalIgnoreCase), $"Module instance display name should include '{requestedBaseName}'.");
        IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(instance.ObjectId), $"Module instance '{requestedBaseName}' should return an ObjectId.");
        ParseTwinCatHex(instance.ObjectId!);
    }

    private static void AssertCreatedTask(TwinCatNodeInfo task, string taskName)
    {
        IntegrationAssertEx.Equal("TIRT^" + taskName, task.TreeItemPath, $"Task '{taskName}' tree path should be deterministic.");
        IntegrationAssertEx.Equal(taskName, task.DisplayName, $"Task '{taskName}' display name should be deterministic.");
        if (!string.IsNullOrWhiteSpace(task.ObjectId))
        {
            ParseTwinCatHex(task.ObjectId!);
        }
    }

    private static void AssertTreeExport(string exportPath, string expectedItemName, string expectedPathName, string? expectedSubtypeName)
    {
        IntegrationAssertEx.True(File.Exists(exportPath), $"Tree export artifact is missing: {exportPath}");
        FileInfo info = new(exportPath);
        IntegrationAssertEx.True(info.Length > 100, $"Tree export artifact should not be empty: {exportPath}");

        XDocument document = XDocument.Load(exportPath);
        XElement root = document.Root ?? throw new InvalidOperationException($"Tree export XML root is missing: {exportPath}");
        IntegrationAssertEx.Equal("TreeItem", root.Name.LocalName, $"Tree export root should be TreeItem: {exportPath}");
        IntegrationAssertEx.Equal(expectedItemName, GetChildValue(root, "ItemName"), $"Tree export ItemName should match for {exportPath}.");
        IntegrationAssertEx.Equal(expectedPathName, GetChildValue(root, "PathName"), $"Tree export PathName should match for {exportPath}.");
        if (!string.IsNullOrWhiteSpace(expectedSubtypeName))
        {
            IntegrationAssertEx.Equal(expectedSubtypeName, GetChildValue(root, "ItemSubTypeName"), $"Tree export subtype should match for {exportPath}.");
        }
    }

    private static void AssertNonFallbackExport(TwinCatNodeInfo export)
    {
        IntegrationAssertEx.False(export.UsedFallback, $"Tree export for {export.TreeItemPath} returned fallback content. See {export.FilePath}.");
    }

    private static void AssertFallbackExport(string exportPath, string expectedTreePath)
    {
        IntegrationAssertEx.True(File.Exists(exportPath), $"Fallback export artifact is missing: {exportPath}");
        string text = File.ReadAllText(exportPath);
        IntegrationAssertEx.Contains("ExportTreeItemXml fallback", text, "Fallback export should identify itself.");
        IntegrationAssertEx.Contains(expectedTreePath, text, "Fallback export should include the failed tree path.");
        IntegrationAssertEx.Contains("not found", text, "Fallback export should include the TwinCAT lookup failure.");
    }

    private static void AssertActivationArchive(string archivePath, ActivationResult activation)
    {
        FileInfo file = new(archivePath);
        IntegrationAssertEx.True(file.Exists, $"Activation archive should exist: {archivePath}");
        IntegrationAssertEx.True(file.Length > 100, $"Activation archive should not be empty: {archivePath}");
        IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(activation.ActivationCommand), "Activation should record the command/API that succeeded.");
        IntegrationAssertEx.True(activation.AttemptedCommands.Contains("ITcSysManager.StartRestartTwinCAT"), "Activation should attempt StartRestartTwinCAT.");
        IntegrationAssertEx.Equal(archivePath, activation.ConfigurationArchivePath, "Activation result should report the archive path.");

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        IntegrationAssertEx.True(archive.Entries.Count > 0, "Activation archive should contain entries.");
        IntegrationAssertEx.False(archive.Entries.Any(entry => entry.Length == 0), "Activation archive entries should not be empty placeholders.");
        bool hasConfigOrSummary = archive.Entries.Any(entry =>
            entry.FullName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.Contains("activation-summary", StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.True(hasConfigOrSummary, "Activation archive should contain a solution/configuration entry or fallback activation summary.");
        ZipArchiveEntry? interpretableEntry = archive.Entries.FirstOrDefault(entry =>
                entry.FullName.Contains("activation-summary", StringComparison.OrdinalIgnoreCase)) ??
            archive.Entries.FirstOrDefault(entry =>
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.True(interpretableEntry is not null, "Activation archive should contain an interpretable summary, XML, or solution entry.");
        using Stream stream = interpretableEntry!.Open();
        using StreamReader reader = new(stream);
        string entryText = reader.ReadToEnd();
        IntegrationAssertEx.True(
            entryText.Contains("activation", StringComparison.OrdinalIgnoreCase) ||
            entryText.Contains("TwinCAT", StringComparison.OrdinalIgnoreCase) ||
            entryText.Contains("<Tc", StringComparison.OrdinalIgnoreCase) ||
            entryText.Contains("Microsoft Visual Studio Solution File", StringComparison.OrdinalIgnoreCase),
            "Activation archive should contain interpretable configuration or activation-summary content.");
    }

    private static void AssertRuntimeProjectBuildOutputs(ScenarioState state)
    {
        string runtimeProjectPath = state.RuntimeProjectPath ?? throw new InvalidOperationException("Runtime project path is missing after build.");
        string plcProjectDirectory = Path.Combine(Path.GetDirectoryName(runtimeProjectPath)!, state.PlcProjectName);
        IntegrationAssertEx.True(File.Exists(runtimeProjectPath), $"Runtime .tsproj should exist: {runtimeProjectPath}");
        IntegrationAssertEx.True(Directory.Exists(plcProjectDirectory), $"Runtime PLC project directory should exist: {plcProjectDirectory}");
        string[] tmcFiles = Directory.GetFiles(plcProjectDirectory, "*.tmc", SearchOption.AllDirectories);
        string[] compileInfoFiles = Directory.GetFiles(plcProjectDirectory, "*.compileinfo", SearchOption.AllDirectories);
        IntegrationAssertEx.True(tmcFiles.Length > 0, "Runtime build should produce a PLC .tmc.");
        IntegrationAssertEx.True(compileInfoFiles.Length > 0, "Runtime build should produce PLC compile info.");
        IntegrationAssertEx.True(tmcFiles.All(path => new FileInfo(path).Length > 100), "Runtime PLC .tmc files should be non-empty generated outputs.");
        IntegrationAssertEx.True(compileInfoFiles.All(path => new FileInfo(path).Length > 100), "Runtime PLC compileinfo files should be non-empty generated outputs.");
    }

    private static void AssertAdsScanHitRuntimePort(AdsPortScanResult scan, int expectedPort)
    {
        AdsPortProbeResult? runtimePort = scan.Ports.FirstOrDefault(port => port.Port == expectedPort);
        if (runtimePort is null)
        {
            throw new InvalidOperationException($"ADS scan did not include configured runtime port {expectedPort}.");
        }

        IntegrationAssertEx.True(runtimePort.Succeeded, $"ADS scan should succeed on configured runtime port {expectedPort}: {runtimePort.ErrorMessage}");
        IntegrationAssertEx.True(!string.IsNullOrWhiteSpace(runtimePort.AdsState), $"ADS scan runtime port {expectedPort} should report ADS state.");
    }

    private static void AssertSigningMetadata(string projectFilePath, string expectedLicenseName)
    {
        XDocument document = XDocument.Load(projectFilePath);
        XElement group = document.Root?.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "PropertyGroup" &&
            string.Equals(GetAttributeValue(element, "Label"), "TcSign", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("C++ project should contain a TcSign PropertyGroup.");

        IntegrationAssertEx.Equal("false", GetChildValue(group, "TcSignTwinCat"), "Signing metadata should explicitly disable signing.");
        IntegrationAssertEx.Equal(expectedLicenseName, GetChildValue(group, "TcSignTwinCatCertName"), "Signing metadata should contain the configured license name.");
        IntegrationAssertEx.False(group.Elements().Any(element => element.Name.LocalName == "TcSignTwinCatCertPW"), "Signing metadata should not write a password in the non-certificate test.");
        IntegrationAssertEx.Equal(1, document.Descendants().Count(element => element.Name.LocalName == "TcSignTwinCat"), "Signing metadata should not leave duplicate TcSignTwinCat nodes.");
        IntegrationAssertEx.Equal(1, document.Descendants().Count(element => element.Name.LocalName == "TcSignTwinCatCertName"), "Signing metadata should not leave duplicate certificate-name nodes.");
    }

    private static void WriteRuntimePou(string solutionDirectory, string plcProjectName) =>
        WriteRuntimePou(solutionDirectory, plcProjectName, 0, 0);

    private static void WriteRuntimePou(string solutionDirectory, string plcProjectName, uint runtimeTaskObjectId, uint auxTaskObjectId)
    {
        string pouDir = Path.Combine(solutionDirectory, plcProjectName, "POUs");
        Directory.CreateDirectory(pouDir);
        string mainPouPath = Path.Combine(pouDir, "MAIN.TcPOU");
        string runtimeTaskObjectIdText = runtimeTaskObjectId.ToString(CultureInfo.InvariantCulture);
        string auxTaskObjectIdText = auxTaskObjectId.ToString(CultureInfo.InvariantCulture);
        string text = """
<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject>
  <POU Name="MAIN">
    <Declaration><![CDATA[
PROGRAM MAIN
VAR
    nSeed AT %Q* : UDINT := 5;
    nStage2Seed AT %Q* : UDINT := 11;
    nStage1 AT %I* : UDINT;
    nStage2 AT %I* : UDINT;
    nCycle : UDINT;
    nExpectedStage1Now : UDINT;
    nExpectedStage1Lag : UDINT;
    nExpectedStage2Now : UDINT;
    nExpectedStage2Lag : UDINT;
    nLastStage1 : UDINT;
    nLastStage2 : UDINT;
    nStage1ChangeCount : UDINT;
    nStage2ChangeCount : UDINT;
    nMismatchCount : UDINT;
    bStage1Ok : BOOL;
    bStage2Ok : BOOL;
    bPipelineOk : BOOL;
    bEverPipelineOk : BOOL;
    bHeartbeat : BOOL;
    bMonitoringEnabled : BOOL;
    bMismatchActive : BOOL;
    RuntimeTaskOidProbe : UDINT := __RUNTIME_TASK_OBJECT_ID__;
    AuxTaskOidProbe : UDINT := __AUX_TASK_OBJECT_ID__;
    nConfiguredParameter : UDINT := 12345;
    nConvertedParameter : UDINT;
    nParameterChecksum : UDINT;
    bParameterTransformOk : BOOL;
END_VAR
    ]]></Declaration>
    <Implementation>
      <ST><![CDATA[
nCycle := nCycle + 1;
bHeartbeat := NOT bHeartbeat;

IF (nCycle MOD 100) = 0 THEN
    nSeed := nSeed + 1;
    nStage2Seed := nStage2Seed + 2;
END_IF;

IF nStage1 <> nLastStage1 THEN
    nStage1ChangeCount := nStage1ChangeCount + 1;
END_IF;
IF nStage2 <> nLastStage2 THEN
    nStage2ChangeCount := nStage2ChangeCount + 1;
END_IF;
nLastStage1 := nStage1;
nLastStage2 := nStage2;

nExpectedStage1Now := nSeed + 123;
nExpectedStage1Lag := nSeed + 122;
nExpectedStage2Now := nStage2Seed + 17;
nExpectedStage2Lag := nStage2Seed + 15;

bStage1Ok := (nStage1 = nExpectedStage1Now) OR (nStage1 = nExpectedStage1Lag);
bStage2Ok := (nStage2 = nExpectedStage2Now) OR (nStage2 = nExpectedStage2Lag);
bPipelineOk := bStage1Ok AND bStage2Ok AND (nStage1ChangeCount > 0) AND (nStage2ChangeCount > 0);

nConvertedParameter := (nConfiguredParameter * 3) + 17;
nParameterChecksum := nConvertedParameter + RuntimeTaskOidProbe + AuxTaskOidProbe + 99;
bParameterTransformOk :=
    (nConfiguredParameter = 12345) AND
    (nConvertedParameter = 37052) AND
    (RuntimeTaskOidProbe <> 0) AND
    (AuxTaskOidProbe <> 0) AND
    (nParameterChecksum = (37052 + RuntimeTaskOidProbe + AuxTaskOidProbe + 99));

IF bPipelineOk THEN
    bEverPipelineOk := TRUE;
    bMonitoringEnabled := TRUE;
    bMismatchActive := FALSE;
ELSIF bMonitoringEnabled THEN
    IF NOT bMismatchActive THEN
        nMismatchCount := nMismatchCount + 1;
        bMismatchActive := TRUE;
    END_IF;
END_IF;
      ]]></ST>
    </Implementation>
  </POU>
</TcPlcObject>
""";
        text = text
            .Replace("__RUNTIME_TASK_OBJECT_ID__", runtimeTaskObjectIdText, StringComparison.Ordinal)
            .Replace("__AUX_TASK_OBJECT_ID__", auxTaskObjectIdText, StringComparison.Ordinal);
        File.WriteAllText(mainPouPath, text);
    }

    private static (string ProjectName, string InstanceName) ExtractFirstPlcNames(string tsprojPath)
    {
        XDocument document = XDocument.Load(tsprojPath);
        XElement? project = document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Plc");
        if (project is null)
        {
            throw new InvalidOperationException("No Plc/Project node found in generated .tsproj.");
        }

        string? projectName = project!.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value
            ?? project.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value;
        XElement? instance = project.Elements().FirstOrDefault(element => element.Name.LocalName == "Instance");
        string? instanceName = instance?.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value;

        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(instanceName))
        {
            throw new InvalidOperationException("Unable to resolve PLC project/instance names from generated .tsproj.");
        }

        return (projectName!, instanceName!);
    }

    private static void ExpectThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}", ex);
        }

        throw new InvalidOperationException(message + " Expected exception was not thrown.");
    }

    private static void ExpectTsprojUnchangedOnFailure(
        string tsprojPath,
        Action action,
        string expectedErrorFragment,
        string message)
    {
        string before = File.ReadAllText(tsprojPath);
        try
        {
            action();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            string after = File.ReadAllText(tsprojPath);
            IntegrationAssertEx.Equal(before, after, message + " The .tsproj must not be modified on failure.");
            IntegrationAssertEx.True(
                ex.Message.Contains(expectedErrorFragment, StringComparison.OrdinalIgnoreCase),
                message + $" Error message should include '{expectedErrorFragment}', got '{ex.Message}'.");
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{message} Expected InvalidOperationException/FormatException, got {ex.GetType().Name}: {ex.Message}", ex);
        }

        throw new InvalidOperationException(message + " Expected exception was not thrown.");
    }

    private static IReadOnlyList<AdsReadSymbolRequest> ParseAdsSymbols(IEnumerable<string> rawSymbols)
    {
        List<AdsReadSymbolRequest> parsed = [];
        foreach (string raw in rawSymbols)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string[] parts = raw.Split(':', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !Enum.TryParse(parts[1], ignoreCase: true, out AdsReadDataType type))
            {
                throw new InvalidOperationException($"Invalid ADS symbol spec '{raw}'. Expected SymbolPath:DataType.");
            }

            parsed.Add(new AdsReadSymbolRequest(parts[0], type));
        }

        if (parsed.Count == 0)
        {
            throw new InvalidOperationException("At least one ADS read symbol must be configured.");
        }

        return parsed;
    }

    private static IReadOnlyList<AdsReadSymbolRequest> BuildRequiredAdsSymbols(IntegrationTestConfig config)
    {
        Dictionary<string, AdsReadSymbolRequest> merged = ParseAdsSymbols(config.AdsReadSymbols)
            .ToDictionary(symbol => symbol.SymbolPath, StringComparer.OrdinalIgnoreCase);

        foreach (AdsReadSymbolRequest required in RequiredRuntimeAdsSymbols())
        {
            merged[required.SymbolPath] = required;
        }

        return merged.Values.ToArray();
    }

    private static IReadOnlyList<AdsReadSymbolRequest> RequiredRuntimeAdsSymbols() =>
    [
        new("MAIN.nCycle", AdsReadDataType.UInt32),
        new("MAIN.nConfiguredParameter", AdsReadDataType.UInt32),
        new("MAIN.nConvertedParameter", AdsReadDataType.UInt32),
        new("MAIN.RuntimeTaskOidProbe", AdsReadDataType.UInt32),
        new("MAIN.AuxTaskOidProbe", AdsReadDataType.UInt32),
        new("MAIN.nParameterChecksum", AdsReadDataType.UInt32),
        new("MAIN.bParameterTransformOk", AdsReadDataType.Boolean),
        new("MAIN.nMismatchCount", AdsReadDataType.UInt32),
    ];

    private static AdsReadSymbolRequest DeterministicSingleAdsReadSymbol() =>
        new("MAIN.nConfiguredParameter", AdsReadDataType.UInt32);

    private static void AssertRuntimeSemanticSymbols(AdsReadSymbolsResult batch, ScenarioState state)
    {
        Dictionary<string, string?> values = batch.Symbols.ToDictionary(item => item.SymbolPath, item => item.Value, StringComparer.OrdinalIgnoreCase);

        uint cycle = RequireUInt32(values, "MAIN.nCycle");
        uint configuredParameter = RequireUInt32(values, "MAIN.nConfiguredParameter");
        uint convertedParameter = RequireUInt32(values, "MAIN.nConvertedParameter");
        uint runtimeTaskOid = RequireUInt32(values, "MAIN.RuntimeTaskOidProbe");
        uint auxTaskOid = RequireUInt32(values, "MAIN.AuxTaskOidProbe");
        uint checksum = RequireUInt32(values, "MAIN.nParameterChecksum");
        bool transformOk = RequireBoolean(values, "MAIN.bParameterTransformOk");
        uint mismatchCount = RequireUInt32(values, "MAIN.nMismatchCount");

        uint expectedRuntimeTaskOid = ParseObjectIdForAds(state.RuntimeTaskObjectId);
        uint expectedAuxTaskOid = ParseObjectIdForAds(state.AuxTaskObjectId);
        uint expectedConvertedParameter = (RuntimeConfiguredParameter * 3) + RuntimeConversionOffset;
        uint expectedChecksum = expectedConvertedParameter + expectedRuntimeTaskOid + expectedAuxTaskOid + RuntimeChecksumSalt;

        IntegrationAssertEx.True(cycle > 0, "MAIN.nCycle should be a positive runtime counter after activation.");
        IntegrationAssertEx.Equal(RuntimeConfiguredParameter, configuredParameter, "ADS should read back the configured PLC parameter.");
        IntegrationAssertEx.Equal(expectedConvertedParameter, convertedParameter, "ADS should read back the deterministic runtime parameter conversion.");
        IntegrationAssertEx.Equal(expectedRuntimeTaskOid, runtimeTaskOid, "ADS should read back the RuntimeTask ObjectId injected through InitSymbols.");
        IntegrationAssertEx.Equal(expectedAuxTaskOid, auxTaskOid, "ADS should read back the AuxTask ObjectId injected through InitSymbols.");
        IntegrationAssertEx.Equal(expectedChecksum, checksum, "ADS should read back the checksum combining configured parameter and injected task ObjectIds.");
        IntegrationAssertEx.True(transformOk, "MAIN.bParameterTransformOk should prove the runtime conversion and InitSymbol values matched.");
        IntegrationAssertEx.Equal(0u, mismatchCount, "MAIN.nMismatchCount should stay 0 after runtime settles.");
    }

    private static uint RequireUInt32(IReadOnlyDictionary<string, string?> values, string symbolPath)
    {
        if (!values.TryGetValue(symbolPath, out string? raw) ||
            !uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
        {
            throw new InvalidOperationException($"ADS symbol '{symbolPath}' did not return a UInt32 value. Raw='{raw ?? "<missing>"}'.");
        }

        return value;
    }

    private static bool RequireBoolean(IReadOnlyDictionary<string, string?> values, string symbolPath)
    {
        if (!values.TryGetValue(symbolPath, out string? raw) ||
            !bool.TryParse(raw, out bool value))
        {
            throw new InvalidOperationException($"ADS symbol '{symbolPath}' did not return a Boolean value. Raw='{raw ?? "<missing>"}'.");
        }

        return value;
    }

    private static uint ParseObjectIdForAds(string objectId)
    {
        string trimmed = objectId.Trim();
        if (trimmed.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return uint.Parse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static string FormatAdsFailures(AdsReadSymbolsResult result) =>
        string.Join(
            "; ",
            result.Symbols
                .Where(symbol => !symbol.Succeeded)
                .Select(symbol => symbol.SymbolPath + "=" + symbol.ErrorMessage));

    private static string FormatBuildOutputTail(BuildResult build)
    {
        if (string.IsNullOrWhiteSpace(build.OutputText))
        {
            return string.Empty;
        }

        string[] lines = build.OutputText
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(40)
            .ToArray();
        return Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static void AssertTsprojContains(string projectPath, string expectedText, string message)
    {
        string content = File.ReadAllText(projectPath);
        IntegrationAssertEx.Contains(expectedText, content, message);
    }

    private static XElement RequireNamedElement(XDocument document, string elementName, string name) =>
        document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == elementName &&
            string.Equals(GetName(element), name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Element {elementName} named '{name}' was not found.");

    private static XElement RequirePlcProject(XDocument document, string plcProjectName) =>
        document.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "Project" &&
            element.Parent?.Name.LocalName == "Plc" &&
            string.Equals(GetName(element), plcProjectName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"PLC project '{plcProjectName}' was not found.");

    private static XElement RequirePlcInstance(XElement plcProject, string plcInstanceName) =>
        plcProject.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Instance" &&
            string.Equals(GetName(element), plcInstanceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"PLC instance '{plcInstanceName}' was not found.");

    private static string? GetName(XElement element) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value
        ?? GetChildValue(element, "Name");

    private static string? GetAttributeValue(XElement element, string attributeName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)?.Value;

    private static string? GetChildValue(XElement element, string childName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == childName)?.Value;

    private static void AssertAttribute(XElement element, string attributeName, string expectedValue, string message) =>
        IntegrationAssertEx.Equal(expectedValue, GetAttributeValue(element, attributeName), message);

    private static void AssertHexAttribute(XElement element, string attributeName, string expectedValue, string message)
    {
        uint expected = ParseTwinCatHex(expectedValue);
        uint actual = ParseTwinCatHex(GetAttributeValue(element, attributeName) ?? string.Empty);
        IntegrationAssertEx.Equal(expected.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture), message);
    }

    private static void AssertObjectIdAttribute(XElement element, string expectedValue, string message)
    {
        string? actualValue = GetAttributeValue(element, "OTCID") ?? GetAttributeValue(element, "Id");
        uint expected = ParseTwinCatHex(expectedValue);
        uint actual = ParseTwinCatHex(actualValue ?? string.Empty);
        IntegrationAssertEx.Equal(expected.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture), message);
    }

    private static void AssertObjectIdAttributeExists(XElement element, string message)
    {
        string? actualValue = GetAttributeValue(element, "OTCID") ?? GetAttributeValue(element, "Id");
        uint actual = ParseTwinCatHex(actualValue ?? string.Empty);
        IntegrationAssertEx.True(actual != 0, message);
    }

    private static uint ParseTwinCatHex(string value)
    {
        string raw = value.Trim();
        if (raw.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
        }

        return uint.Parse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static void AssertNamedCount(XDocument document, string elementName, string name, int expectedCount)
    {
        int count = document.Descendants().Count(element =>
            element.Name.LocalName == elementName &&
            string.Equals(GetName(element), name, StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.Equal(expectedCount, count, $"{elementName} named '{name}' should have deterministic cardinality.");
    }

    private static void AssertTaskVarsGroup(XElement task, string groupName, string expectedVarGrpType, string expectedInsertType, int expectedVarCount)
    {
        XElement group = task.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Vars" &&
            string.Equals(GetChildValue(element, "Name"), groupName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Task '{GetName(task)}' Vars group '{groupName}' is missing.");

        AssertAttribute(group, "VarGrpType", expectedVarGrpType, $"Task Vars group '{groupName}' VarGrpType should match.");
        AssertAttribute(group, "InsertType", expectedInsertType, $"Task Vars group '{groupName}' InsertType should match.");
        IntegrationAssertEx.Equal(expectedVarCount, group.Elements().Count(element => element.Name.LocalName == "Var"), $"Task Vars group '{groupName}' var count should match.");
    }

    private static void AssertTaskVar(
        XElement task,
        string groupName,
        string varName,
        string expectedType,
        string? expectedBitOffs,
        string? expectedExternalAddress)
    {
        XElement group = task.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Vars" &&
            string.Equals(GetChildValue(element, "Name"), groupName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Task '{GetName(task)}' Vars group '{groupName}' is missing.");
        XElement var = group.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Var" &&
            string.Equals(GetChildValue(element, "Name"), varName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Task Vars '{groupName}/{varName}' is missing.");

        IntegrationAssertEx.Equal(expectedType, GetChildValue(var, "Type"), $"Task Vars '{groupName}/{varName}' type should match.");
        IntegrationAssertEx.Equal(expectedBitOffs, GetChildValue(var, "BitOffs"), $"Task Vars '{groupName}/{varName}' BitOffs should match.");
        IntegrationAssertEx.Equal(expectedExternalAddress, GetChildValue(var, "ExternalAddress"), $"Task Vars '{groupName}/{varName}' ExternalAddress should match.");
    }

    private static bool HasTaskVars(XElement task, string groupName, string varName, string typeName) =>
        task.Elements()
            .Where(element => element.Name.LocalName == "Vars" &&
                              string.Equals(GetChildValue(element, "Name"), groupName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "Var"))
            .Any(varElement =>
                string.Equals(GetChildValue(varElement, "Name"), varName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetChildValue(varElement, "Type"), typeName, StringComparison.OrdinalIgnoreCase));

    private static bool HasPlcVar(XElement plcInstance, string groupName, string varName) =>
        plcInstance.Elements()
            .Where(element => element.Name.LocalName == "Vars" &&
                              string.Equals(GetChildValue(element, "Name"), groupName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(element => element.Elements().Where(child => child.Name.LocalName == "Var"))
            .Any(varElement => string.Equals(GetChildValue(varElement, "Name"), varName, StringComparison.OrdinalIgnoreCase));

    private static void AssertPlcVarsGroupCount(XElement plcInstance, string groupName, int expectedCount)
    {
        int count = plcInstance.Elements()
            .Count(element => element.Name.LocalName == "Vars" &&
                              string.Equals(GetChildValue(element, "Name"), groupName, StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.Equal(expectedCount, count, $"PLC Vars group '{groupName}' should have deterministic cardinality.");
    }

    private static void AssertPlcVarsGroup(
        XElement plcInstance,
        string groupName,
        string expectedVarGrpType,
        string expectedInsertType,
        string expectedAreaNo,
        int expectedVarCount)
    {
        XElement group = plcInstance.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Vars" &&
            string.Equals(GetChildValue(element, "Name"), groupName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"PLC Vars group '{groupName}' is missing.");

        AssertAttribute(group, "VarGrpType", expectedVarGrpType, $"PLC Vars group '{groupName}' VarGrpType should match.");
        AssertAttribute(group, "InsertType", expectedInsertType, $"PLC Vars group '{groupName}' InsertType should match.");
        AssertAttribute(group, "AreaNo", expectedAreaNo, $"PLC Vars group '{groupName}' AreaNo should match.");
        IntegrationAssertEx.Equal(expectedVarCount, group.Elements().Count(element => element.Name.LocalName == "Var"), $"PLC Vars group '{groupName}' var count should match.");
    }

    private static void AssertPlcVar(
        XElement plcInstance,
        string groupName,
        string varName,
        string expectedType,
        string expectedBitOffs,
        string expectedExternalAddress)
    {
        XElement group = plcInstance.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Vars" &&
            string.Equals(GetChildValue(element, "Name"), groupName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"PLC Vars group '{groupName}' is missing.");
        XElement var = group.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "Var" &&
            string.Equals(GetChildValue(element, "Name"), varName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"PLC var '{groupName}/{varName}' is missing.");

        IntegrationAssertEx.Equal(expectedType, GetChildValue(var, "Type"), $"PLC var '{groupName}/{varName}' type should match.");
        IntegrationAssertEx.Equal(expectedBitOffs, GetChildValue(var, "BitOffs"), $"PLC var '{groupName}/{varName}' BitOffs should match.");
        IntegrationAssertEx.Equal(expectedExternalAddress, GetChildValue(var, "ExternalAddress"), $"PLC var '{groupName}/{varName}' ExternalAddress should match.");
    }

    private static void AssertInstanceContext(XElement instance, string expectedTaskObjectId, int expectedPriority, int expectedCycleTimeNs)
    {
        XElement context = instance.Descendants().FirstOrDefault(element => element.Name.LocalName == "Context")
            ?? throw new InvalidOperationException($"Instance '{GetName(instance)}' has no Context.");
        XElement manualConfig = context.Elements().FirstOrDefault(element => element.Name.LocalName == "ManualConfig")
            ?? throw new InvalidOperationException($"Instance '{GetName(instance)}' has no ManualConfig.");
        IntegrationAssertEx.Equal(expectedTaskObjectId, GetChildValue(manualConfig, "OTCID"), $"Instance '{GetName(instance)}' should bind to the expected task ObjectId.");
        IntegrationAssertEx.Equal(expectedPriority.ToString(CultureInfo.InvariantCulture), GetChildValue(context, "Priority"), $"Instance '{GetName(instance)}' context priority should be deterministic.");
        IntegrationAssertEx.Equal(expectedCycleTimeNs.ToString(CultureInfo.InvariantCulture), GetChildValue(context, "CycleTime"), $"Instance '{GetName(instance)}' context cycle time should be deterministic.");
    }

    private static void AssertNamedValue(XElement instance, string containerName, string valueName, string childName, string expectedValue)
    {
        XElement value = instance.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == containerName)?
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "Value" &&
                string.Equals(GetChildValue(element, "Name"), valueName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{containerName}/{valueName} is missing on instance '{GetName(instance)}'.");

        IntegrationAssertEx.Equal(expectedValue, GetChildValue(value, childName), $"{containerName}/{valueName}/{childName} should match.");
    }

    private static void AssertNamedValueAbsent(XElement instance, string containerName, string valueName)
    {
        bool found = instance.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == containerName)?
            .Elements()
            .Any(element =>
                element.Name.LocalName == "Value" &&
                string.Equals(GetChildValue(element, "Name"), valueName, StringComparison.OrdinalIgnoreCase)) == true;

        IntegrationAssertEx.False(found, $"{containerName}/{valueName} should have been removed from instance '{GetName(instance)}'.");
    }

    private static void AssertInitSymbol(XElement plcInstance, string symbolName, string expectedData)
    {
        XElement symbol = plcInstance.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "InitSymbol" &&
            string.Equals(GetChildValue(element, "Name"), symbolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"InitSymbol '{symbolName}' is missing.");
        IntegrationAssertEx.Equal(expectedData, GetChildValue(symbol, "Data"), $"InitSymbol '{symbolName}' Data should use the ObjectId byte encoding.");
    }

    private static void AssertInitSymbolAbsent(XElement plcInstance, string symbolName)
    {
        bool found = plcInstance.Descendants().Any(element =>
            element.Name.LocalName == "InitSymbol" &&
            string.Equals(GetChildValue(element, "Name"), symbolName, StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.False(found, $"InitSymbol '{symbolName}' should have been removed.");
    }

    private static void AssertContainsElement(XDocument document, string elementName, string? nameValue)
    {
        bool found = document.Descendants().Any(element =>
            element.Name.LocalName == elementName &&
            (nameValue is null || string.Equals(GetName(element), nameValue, StringComparison.OrdinalIgnoreCase)));
        IntegrationAssertEx.True(found, $"Expected XML element '{elementName}' with Name='{nameValue ?? "<any>"}'.");
    }

    private static void AssertElementChildValue(XDocument document, string elementName, string childName, string expectedValue)
    {
        bool found = document.Descendants().Any(element =>
            element.Name.LocalName == elementName &&
            string.Equals(GetChildValue(element, childName), expectedValue, StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.True(found, $"Expected XML element '{elementName}' with {childName}='{expectedValue}'.");
    }

    private static void AssertDataTypes(XDocument document, params string[] expectedNames)
    {
        XElement dataTypes = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "DataTypes")
            ?? throw new InvalidOperationException("Root DataTypes section is missing.");
        string[] names = dataTypes.Elements()
            .Where(element => element.Name.LocalName == "DataType")
            .Select(element => GetChildValue(element, "Name") ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] expected = expectedNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        IntegrationAssertEx.Equal(string.Join("|", expected), string.Join("|", names), "DataTypes section should contain exactly the deterministic test types.");
    }

    private static void AssertElementNameAbsent(XDocument document, string elementName, string? nameValue)
    {
        bool found = document.Descendants().Any(element =>
            element.Name.LocalName == elementName &&
            (nameValue is null || string.Equals(GetName(element), nameValue, StringComparison.OrdinalIgnoreCase)));
        IntegrationAssertEx.False(found, $"XML element '{elementName}' with Name='{nameValue ?? "<any>"}' should have been removed.");
    }

    private static void AssertMapping(XDocument document, string varA, string varB)
    {
        bool found = document.Descendants().Any(element =>
            element.Name.LocalName == "Link" &&
            string.Equals(GetAttributeValue(element, "VarA"), varA, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetAttributeValue(element, "VarB"), varB, StringComparison.OrdinalIgnoreCase));
        IntegrationAssertEx.True(found, $"Expected mapping link {varA} -> {varB}.");
    }

    internal sealed class ScenarioState(
        IntegrationTestConfig config,
        TwinCatEngineeringService engineering,
        TwinCatEngineeringSession session,
        string workDir,
        string evidenceDir,
        TwinCatProjectInfo projectInfo,
        string cppProjectFilePath,
        string cppProjectName,
        string plcProjectName,
        string plcInstanceName,
        string primaryInstanceName,
        string auxInstanceName,
        string primaryInstanceObjectId,
        string auxInstanceObjectId,
        string runtimeTaskObjectId,
        string auxTaskObjectId,
        HashSet<string> coveredInterfaces,
        HashSet<string> coveredStepKinds)
    {
        public IntegrationTestConfig Config { get; } = config;
        public TwinCatEngineeringService Engineering { get; } = engineering;
        public TwinCatEngineeringSession? Session { get; set; } = session;
        public string WorkDir { get; } = workDir;
        public string EvidenceDir { get; } = evidenceDir;
        public TwinCatProjectInfo ProjectInfo { get; } = projectInfo;
        public string CppProjectFilePath { get; } = cppProjectFilePath;
        public string CppProjectName { get; } = cppProjectName;
        public string PlcProjectName { get; } = plcProjectName;
        public string PlcInstanceName { get; } = plcInstanceName;
        public string PrimaryInstanceName { get; } = primaryInstanceName;
        public string AuxInstanceName { get; } = auxInstanceName;
        public string PrimaryInstanceObjectId { get; } = primaryInstanceObjectId;
        public string AuxInstanceObjectId { get; } = auxInstanceObjectId;
        public string RuntimeTaskObjectId { get; } = runtimeTaskObjectId;
        public string AuxTaskObjectId { get; } = auxTaskObjectId;
        public IReadOnlySet<string> CoveredInterfaces => coveredInterfaces;
        public IReadOnlySet<string> CoveredStepKinds => coveredStepKinds;
        public BuildResult? BuildResult { get; set; }
        public ActivationResult? ActivationResult { get; set; }
        public string? RuntimeWorkDir { get; set; }
        public string? RuntimeSolutionPath { get; set; }
        public string? RuntimeProjectPath { get; set; }
        public string? RuntimeEvidenceDir { get; set; }

        public void Cover(string id) => coveredInterfaces.Add(id);

        public void CoverStep(string kind) => coveredStepKinds.Add(kind);

        public TwinCatEngineeringSession RequireSession() =>
            Session ?? throw new InvalidOperationException("The ordered scenario Visual Studio session is already closed.");

        public string FindBuiltTwinCatBinary()
        {
            string projectDirectory = Path.GetDirectoryName(CppProjectFilePath)
                ?? throw new InvalidOperationException("Unable to resolve C++ project directory.");
            string[] candidates = Directory.Exists(projectDirectory)
                ? Directory.GetFiles(projectDirectory, "*.tmx", SearchOption.AllDirectories)
                : [];
            if (candidates.Length == 0)
            {
                throw new FileNotFoundException($"No built .tmx file was found under {projectDirectory}.");
            }

            return candidates
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();
        }
    }
}
