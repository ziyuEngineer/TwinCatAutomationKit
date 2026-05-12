using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.Core;

namespace TwinCatAutomationKit.TwinCat;

public static class TwinCatStateKeys
{
    public const string Session = "twincat.session";
    public const string SolutionPath = "twincat.solutionPath";
    public const string ProjectPath = "twincat.projectPath";
    public const string CurrentCppProject = "twincat.currentCppProject";
    public const string LastInstanceObjectId = "twincat.lastInstanceObjectId";
    public const string LastTaskObjectId = "twincat.lastTaskObjectId";
    public const string LastAdsValue = "twincat.lastAdsValue";
    public const string LastAdsValues = "twincat.lastAdsValues";
    public const string LastAdsStateAssertion = "twincat.lastAdsStateAssertion";
}

public static class TwinCatAtomicSteps
{
    public static IAutomationStep LaunchVisualStudio(
        string stepId,
        TwinCatEngineeringService service,
        LaunchVisualStudioRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Launch Visual Studio",
            "engineering.launch-visual-studio",
            context =>
            {
                TwinCatEngineeringSession session = service.LaunchVisualStudio(request);
                context.State.Set(sessionStateKey, session);
                return StepExecutionOutcome.Success(
                    "Visual Studio DTE session started.",
                    new Dictionary<string, string?> { ["progId"] = request.ProgId });
            });

    public static IAutomationStep CreateXaeSolution(
        string stepId,
        TwinCatEngineeringService service,
        CreateTwinCatSolutionRequest request,
        string sessionStateKey = TwinCatStateKeys.Session,
        string solutionPathStateKey = TwinCatStateKeys.SolutionPath,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Create XAE Solution",
            "engineering.create-xae-solution",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatProjectInfo info = service.CreateTwinCatSolution(session, request);
                context.State.Set(solutionPathStateKey, info.SolutionPath);
                context.State.Set(projectPathStateKey, info.ProjectPath);
                return StepExecutionOutcome.Success(
                    "TwinCAT XAE solution created.",
                    new Dictionary<string, string?>
                    {
                        ["solutionPath"] = info.SolutionPath,
                        ["projectPath"] = info.ProjectPath
                    });
            });

    public static IAutomationStep OpenXaeSolution(
        string stepId,
        TwinCatEngineeringService service,
        OpenTwinCatSolutionRequest request,
        string sessionStateKey = TwinCatStateKeys.Session,
        string solutionPathStateKey = TwinCatStateKeys.SolutionPath,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Open XAE Solution",
            "engineering.open-xae-solution",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatProjectInfo info = service.OpenTwinCatSolution(session, request);
                context.State.Set(solutionPathStateKey, info.SolutionPath);
                context.State.Set(projectPathStateKey, info.ProjectPath);
                return StepExecutionOutcome.Success(
                    "TwinCAT XAE solution reopened.",
                    new Dictionary<string, string?>
                    {
                        ["solutionPath"] = info.SolutionPath,
                        ["projectPath"] = info.ProjectPath
                    });
            });

    public static IAutomationStep CreateCppProject(
        string stepId,
        TwinCatEngineeringService service,
        CreateCppProjectRequest request,
        string sessionStateKey = TwinCatStateKeys.Session,
        string projectNameStateKey = TwinCatStateKeys.CurrentCppProject) =>
        Create(
            stepId,
            "Create C++ Project",
            "engineering.create-cpp-project",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatNodeInfo info = service.CreateCppProject(session, request);
                context.State.Set(projectNameStateKey, request.ProjectName);
                return StepExecutionOutcome.Success(
                    "TwinCAT C++ project created.",
                    new Dictionary<string, string?>
                    {
                        ["treeItemPath"] = info.TreeItemPath,
                        ["projectFilePath"] = info.FilePath
                    });
            });

    public static IAutomationStep CreateVisualStudioCppProject(
        string stepId,
        TwinCatEngineeringService service,
        CreateVisualStudioCppProjectRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Create Visual Studio C++ Project",
            "engineering.create-vs-cpp-project",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                VisualStudioCppProjectInfo info = service.CreateVisualStudioCppProject(session, request);
                return StepExecutionOutcome.Success(
                    "Visual Studio C++ project created.",
                    new Dictionary<string, string?>
                    {
                        ["projectFilePath"] = info.ProjectFilePath,
                        ["projectGuid"] = info.ProjectGuid,
                        ["projectDirectory"] = info.ProjectDirectory
                    });
            });

    public static IAutomationStep EnsureSolutionProjectDependency(
        string stepId,
        TwinCatEngineeringService service,
        EnsureSolutionProjectDependencyRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Ensure Solution Project Dependency",
            "engineering.ensure-solution-project-dependency",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                SolutionProjectDependencyResult result = service.EnsureSolutionProjectDependency(session, request);
                return StepExecutionOutcome.Success(
                    "Solution project dependency ensured.",
                    new Dictionary<string, string?>
                    {
                        ["projectGuid"] = result.ProjectGuid,
                        ["dependsOnProjectGuid"] = result.DependsOnProjectGuid
                    });
            });

    public static IAutomationStep CreatePlcProject(
        string stepId,
        TwinCatEngineeringService service,
        CreatePlcProjectRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Create PLC Project",
            "engineering.create-plc-project",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatNodeInfo info = service.CreatePlcProject(session, request);
                return StepExecutionOutcome.Success(
                    "TwinCAT PLC project created.",
                    new Dictionary<string, string?>
                    {
                        ["treeItemPath"] = info.TreeItemPath,
                        ["projectFilePath"] = info.FilePath
                    });
            });

    public static IAutomationStep CreateModule(
        string stepId,
        TwinCatEngineeringService service,
        CreateModuleRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Create Module",
            "engineering.create-module",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatNodeInfo info = service.CreateModule(session, request);
                return StepExecutionOutcome.Success(
                    "TwinCAT module creation invoked.",
                    new Dictionary<string, string?>
                    {
                        ["moduleCppPath"] = info.FilePath
                    });
            });

    public static IAutomationStep PublishModules(
        string stepId,
        TwinCatEngineeringService service,
        PublishModulesRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Publish Modules",
            "engineering.publish-modules",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                PublishModulesResult result = service.PublishModules(session, request);
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        "TwinCAT C++ modules published.",
                        new Dictionary<string, string?>
                        {
                            ["updatedTmcPath"] = result.UpdatedTmcPath,
                            ["succeeded"] = result.Succeeded ? "true" : "false",
                            ["updated"] = result.Updated ? "true" : "false"
                        })
                    : StepExecutionOutcome.Failed("TwinCAT C++ module publish did not produce a readable project TMC.");
            });

    public static IAutomationStep StartTmcCodeGenerator(
        string stepId,
        TwinCatEngineeringService service,
        StartTmcCodeGeneratorRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Start TMC Code Generator",
            "engineering.start-tmc-code-generator",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                StartTmcCodeGeneratorResult result = service.StartTmcCodeGenerator(session, request);
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        "TwinCAT C++ TMC code generator completed.",
                        new Dictionary<string, string?>
                        {
                            ["updatedTmcPath"] = result.UpdatedTmcPath,
                            ["succeeded"] = result.Succeeded ? "true" : "false",
                            ["updated"] = result.Updated ? "true" : "false"
                        })
                    : StepExecutionOutcome.Failed("TwinCAT C++ TMC code generator did not produce a readable project TMC.");
            });

    public static IAutomationStep VerifyTmcDataAreas(
        string stepId,
        TwinCatEngineeringService service,
        VerifyTmcDataAreasRequest request) =>
        Create(
            stepId,
            "Verify TMC Data Areas",
            "engineering.verify-tmc-data-areas",
            _ =>
            {
                VerifyTmcDataAreasResult result = service.VerifyTmcDataAreas(request);
                Dictionary<string, string?> outputs = new()
                {
                    ["projectTmcPath"] = result.ProjectTmcPath,
                    ["expectedModuleCount"] = result.ExpectedModuleCount.ToString(CultureInfo.InvariantCulture),
                    ["matchedModuleCount"] = result.MatchedModuleCount.ToString(CultureInfo.InvariantCulture),
                    ["errorsJson"] = JsonSerializer.Serialize(result.Errors)
                };

                return result.Succeeded
                    ? StepExecutionOutcome.Success(result.Summary, outputs)
                    : new StepExecutionOutcome(
                        StepExecutionStatus.Failed,
                        result.Summary,
                        outputs,
                        Array.Empty<EvidenceArtifact>());
            });

    public static IAutomationStep ApplyTmcModuleModel(
        string stepId,
        TwinCatEngineeringService service,
        ApplyTmcModuleModelRequest request) =>
        Create(
            stepId,
            "Apply TMC Module Model",
            "engineering.apply-tmc-module-model",
            _ =>
            {
                ApplyTmcModuleModelResult result = service.ApplyTmcModuleModel(request);
                return StepExecutionOutcome.Success(
                    result.Summary,
                    new Dictionary<string, string?>
                    {
                        ["projectTmcPath"] = result.ProjectTmcPath,
                        ["moduleCount"] = result.ModuleCount.ToString(CultureInfo.InvariantCulture)
                    },
                    [new EvidenceArtifact("project-tmc", result.ProjectTmcPath, "tmc")]);
            });

    public static IAutomationStep AddModuleInstance(
        string stepId,
        TwinCatEngineeringService service,
        AddModuleInstanceRequest request,
        string sessionStateKey = TwinCatStateKeys.Session,
        string lastInstanceObjectIdStateKey = TwinCatStateKeys.LastInstanceObjectId) =>
        Create(
            stepId,
            "Add Module Instance",
            "engineering.add-module-instance",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatNodeInfo info = service.AddModuleInstance(session, request);
                if (!string.IsNullOrWhiteSpace(info.ObjectId))
                {
                    context.State.Set(lastInstanceObjectIdStateKey, info.ObjectId);
                }

                return StepExecutionOutcome.Success(
                    "TwinCAT module instance added.",
                    new Dictionary<string, string?>
                    {
                        ["treeItemPath"] = info.TreeItemPath,
                        ["objectId"] = info.ObjectId
                    });
            });

    public static IAutomationStep EnsureTask(
        string stepId,
        TwinCatEngineeringService service,
        EnsureTaskRequest request,
        string sessionStateKey = TwinCatStateKeys.Session,
        string lastTaskObjectIdStateKey = TwinCatStateKeys.LastTaskObjectId) =>
        Create(
            stepId,
            "Ensure Task",
            "engineering.ensure-task",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatNodeInfo info = service.EnsureTask(session, request);
                if (!string.IsNullOrWhiteSpace(info.ObjectId))
                {
                    context.State.Set(lastTaskObjectIdStateKey, info.ObjectId);
                }

                return StepExecutionOutcome.Success(
                    "TwinCAT task created or updated.",
                    new Dictionary<string, string?>
                    {
                        ["treeItemPath"] = info.TreeItemPath,
                        ["objectId"] = info.ObjectId
                    });
            });

    public static IAutomationStep ExportTreeItemXml(
        string stepId,
        TwinCatEngineeringService service,
        ExportTreeItemXmlRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Export Tree Item XML",
            "engineering.export-tree-item-xml",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                TwinCatNodeInfo info = service.ExportTreeItemXml(session, request);
                return StepExecutionOutcome.Success(
                    "TwinCAT tree item XML exported.",
                    new Dictionary<string, string?> { ["xmlPath"] = info.FilePath },
                    new[] { new EvidenceArtifact("tree-item-xml", info.FilePath!, "xml") });
            });

    public static IAutomationStep SaveAll(
        string stepId,
        TwinCatEngineeringService service,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Save All",
            "engineering.save-all",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                service.SaveAll(session);
                return StepExecutionOutcome.Success("Visual Studio save-all executed.");
            });

    public static IAutomationStep CloseVisualStudio(
        string stepId,
        TwinCatEngineeringService service,
        CloseVisualStudioRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Close Visual Studio",
            "engineering.close-visual-studio",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                service.CloseVisualStudio(session, request.SaveBeforeClose);
                session.Dispose();
                return StepExecutionOutcome.Success(
                    "Visual Studio session closed.",
                    new Dictionary<string, string?> { ["saveBeforeClose"] = request.SaveBeforeClose ? "true" : "false" });
            });

    public static IAutomationStep BuildSolution(
        string stepId,
        TwinCatEngineeringService service,
        BuildSolutionRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Build Solution",
            "engineering.build-solution",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                BuildResult result = service.BuildCurrentSolution(session, request);
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        "Solution build succeeded.",
                        new Dictionary<string, string?> { ["lastBuildInfo"] = result.LastBuildInfo.ToString() })
                    : StepExecutionOutcome.Failed($"Solution build failed. LastBuildInfo={result.LastBuildInfo}.");
            });

    public static IAutomationStep CreateCppProjectItem(
        string stepId,
        TwinCatEngineeringService service,
        CreateCppProjectItemRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Create C++ Project Item",
            "cpp.create-project-item",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                CppProjectItemResult result = service.CreateCppProjectItem(session, request);
                return StepExecutionOutcome.Success(
                    "C++ project item created.",
                    new Dictionary<string, string?>
                    {
                        ["projectFilePath"] = result.ProjectFilePath,
                        ["filePath"] = result.FilePath,
                        ["itemType"] = result.ItemType.ToString(),
                        ["filter"] = result.Filter,
                        ["addedToProject"] = result.AddedToProject ? "true" : "false"
                    });
            });

    public static IAutomationStep WriteCppProjectItemContent(
        string stepId,
        TwinCatEngineeringService service,
        WriteCppProjectItemContentRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Write C++ Project Item Content",
            "cpp.write-project-item-content",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                CppProjectItemContentResult result = service.WriteCppProjectItemContent(session, request);
                return StepExecutionOutcome.Success(
                    "C++ project item content written.",
                    new Dictionary<string, string?>
                    {
                        ["filePath"] = result.FilePath,
                        ["sha256"] = result.Sha256,
                        ["bytesWritten"] = result.BytesWritten.ToString()
                    });
            });

    public static IAutomationStep RemoveCppProjectItem(
        string stepId,
        TwinCatEngineeringService service,
        RemoveCppProjectItemRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Remove C++ Project Item",
            "cpp.remove-project-item",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                RemoveCppProjectItemResult result = service.RemoveCppProjectItem(session, request);
                return StepExecutionOutcome.Success(
                    "C++ project item removed.",
                    new Dictionary<string, string?>
                    {
                        ["removedFromProject"] = result.RemovedFromProject ? "true" : "false",
                        ["deletedFile"] = result.DeletedFile ? "true" : "false"
                    });
            });

    public static IAutomationStep SetCppProjectProperty(
        string stepId,
        TwinCatEngineeringService service,
        SetCppProjectPropertyRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Set C++ Project Property",
            "cpp.set-project-property",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                CppProjectPropertyResult result = service.SetCppProjectProperty(session, request);
                return StepExecutionOutcome.Success(
                    "C++ project property set.",
                    new Dictionary<string, string?>
                    {
                        ["projectFilePath"] = result.ProjectFilePath,
                        ["propertyName"] = result.PropertyName,
                        ["condition"] = result.Condition
                    });
            });

    public static IAutomationStep SetCppItemDefinitionProperty(
        string stepId,
        TwinCatEngineeringService service,
        SetCppItemDefinitionPropertyRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Set C++ Item Definition Property",
            "cpp.set-item-definition-property",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                CppItemDefinitionPropertyResult result = service.SetCppItemDefinitionProperty(session, request);
                return StepExecutionOutcome.Success(
                    "C++ item definition property set.",
                    new Dictionary<string, string?>
                    {
                        ["projectFilePath"] = result.ProjectFilePath,
                        ["toolName"] = result.ToolName,
                        ["propertyName"] = result.PropertyName,
                        ["condition"] = result.Condition
                    });
            });

    public static IAutomationStep SetCppProjectItemMetadata(
        string stepId,
        TwinCatEngineeringService service,
        SetCppProjectItemMetadataRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Set C++ Project Item Metadata",
            "cpp.set-project-item-metadata",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                CppProjectItemMetadataResult result = service.SetCppProjectItemMetadata(session, request);
                return StepExecutionOutcome.Success(
                    "C++ project item metadata set.",
                    new Dictionary<string, string?>
                    {
                        ["projectFilePath"] = result.ProjectFilePath,
                        ["relativePath"] = result.RelativePath,
                        ["metadataName"] = result.MetadataName,
                        ["condition"] = result.Condition
                    });
            });

    public static IAutomationStep ActivateConfiguration(
        string stepId,
        TwinCatEngineeringService service,
        ActivateConfigurationRequest request,
        string sessionStateKey = TwinCatStateKeys.Session) =>
        Create(
            stepId,
            "Activate Configuration",
            "engineering.activate-configuration",
            context =>
            {
                TwinCatEngineeringSession session = context.State.GetRequired<TwinCatEngineeringSession>(sessionStateKey);
                ActivationResult result = service.ActivateConfiguration(session, request);
                return StepExecutionOutcome.Success(
                    "TwinCAT configuration activated.",
                    new Dictionary<string, string?>
                    {
                        ["activationCommand"] = result.ActivationCommand,
                        ["configurationArchivePath"] = result.ConfigurationArchivePath
                    });
            });

    public static IAutomationStep EnsureTaskInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureTaskDefinitionRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure Task In Tsproj",
            "tsproj.ensure-task",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureTaskDefinition(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Task definition updated in tsproj.");
            });

    public static IAutomationStep ClearTaskLayoutInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearTaskLayoutRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear Task Layout In Tsproj",
            "tsproj.clear-task-layout",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearTaskLayout(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Task Vars/Image layout cleared in tsproj.");
            });

    public static IAutomationStep EnsureTaskVarsGroupInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureTaskVarsGroupRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure Task Vars Group In Tsproj",
            "tsproj.ensure-task-vars-group",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureTaskVarsGroup(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Task Vars group updated in tsproj.");
            });

    public static IAutomationStep EnsureTaskImageInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureTaskImageRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure Task Image In Tsproj",
            "tsproj.ensure-task-image",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureTaskImage(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Task image updated in tsproj.");
            });

    public static IAutomationStep EnsureCppInstanceInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureCppInstanceRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure C++ Instance In Tsproj",
            "tsproj.ensure-cpp-instance",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureCppInstance(tsprojPath, request);
                return StepExecutionOutcome.Indirect("C++ instance skeleton updated in tsproj.");
            });

    public static IAutomationStep EnsurePlcInstanceInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsurePlcInstanceRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure PLC Instance In Tsproj",
            "tsproj.ensure-plc-instance",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsurePlcInstance(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC project/instance skeleton updated in tsproj.");
            });

    public static IAutomationStep BindInstanceContextInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        BindInstanceContextRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Bind Instance Context In Tsproj",
            "tsproj.bind-instance-context",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.BindInstanceContext(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Instance context binding updated in tsproj.");
            });

    public static IAutomationStep SetTaskAffinityInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        SetTaskAffinityRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Set Task Affinity In Tsproj",
            "tsproj.set-task-affinity",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.SetTaskAffinity(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Task affinity updated in tsproj.");
            });

    public static IAutomationStep SetPlcProjectPropertiesInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        SetPlcProjectPropertiesRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Set PLC Project Properties In Tsproj",
            "tsproj.set-plc-project-properties",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.SetPlcProjectProperties(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC project properties updated in tsproj.");
            });

    public static IAutomationStep SetPlcInstanceMetadataInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        SetPlcInstanceMetadataRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Set PLC Instance Metadata In Tsproj",
            "tsproj.set-plc-instance-metadata",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.SetPlcInstanceMetadata(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC instance metadata updated in tsproj.");
            });

    public static IAutomationStep ClearPlcInstanceVarsInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearPlcInstanceVarsRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear PLC Instance Vars In Tsproj",
            "tsproj.clear-plc-instance-vars",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearPlcInstanceVars(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC instance Vars cleared in tsproj.");
            });

    public static IAutomationStep EnsurePlcInstanceVarsGroupInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsurePlcInstanceVarsGroupRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure PLC Instance Vars Group In Tsproj",
            "tsproj.ensure-plc-instance-vars-group",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsurePlcInstanceVarsGroup(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC instance Vars group updated in tsproj.");
            });

    public static IAutomationStep ClearPlcInitSymbolsInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearPlcInitSymbolsRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear PLC InitSymbols In Tsproj",
            "tsproj.clear-plc-init-symbols",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearPlcInitSymbols(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC InitSymbols cleared in tsproj.");
            });

    public static IAutomationStep ClearPlcTaskPouOidsInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearPlcTaskPouOidsRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear PLC TaskPouOids In Tsproj",
            "tsproj.clear-plc-task-pou-oids",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearPlcTaskPouOids(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC TaskPouOids cleared in tsproj.");
            });

    public static IAutomationStep ClearMappingsInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearMappingsRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear Mappings In Tsproj",
            "tsproj.clear-mappings",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearMappings(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Mappings sections removed from tsproj.");
            });

    public static IAutomationStep ClearUnrestoredVarLinksInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearUnrestoredVarLinksRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear Unrestored Var Links In Tsproj",
            "tsproj.clear-unrestored-var-links",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearUnrestoredVarLinks(tsprojPath, request);
                return StepExecutionOutcome.Indirect("UnrestoredVarLinks blocks cleared in tsproj.");
            });

    public static IAutomationStep ReplaceMappingsSectionInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ReplaceMappingsSectionRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Replace Mappings Section In Tsproj",
            "tsproj.replace-mappings-section",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ReplaceMappingsSection(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Mappings section replaced in tsproj.");
            });

    public static IAutomationStep ReplaceProjectIoSectionInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ReplaceProjectIoSectionRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Replace Project Io Section In Tsproj",
            "tsproj.replace-project-io-section",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ReplaceProjectIoSection(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Project Io section replaced in tsproj.");
            });

    public static IAutomationStep EnsureIoSectionInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureIoSectionRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure IO Section In Tsproj",
            "tsproj.ensure-io-section",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                EnsureIoSectionResult result = service.EnsureIoSection(tsprojPath, request);
                return StepExecutionOutcome.Indirect(
                    result.Created
                        ? "Project Io section created in tsproj."
                        : "Project Io section already existed in tsproj.");
            });

    public static IAutomationStep EnsureIoDeviceInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureIoDeviceRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure IO Device In Tsproj",
            "tsproj.ensure-io-device",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureIoDevice(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"IO Device {request.DeviceId} updated in tsproj.");
            });

    public static IAutomationStep EnsureEthercatBoxInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureEthercatBoxRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure EtherCAT Box In Tsproj",
            "tsproj.ensure-ethercat-box",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureEthercatBox(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"EtherCAT Box {request.BoxId} updated in tsproj.");
            });

    public static IAutomationStep EnsureIoPdoInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureIoPdoRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure IO PDO In Tsproj",
            "tsproj.ensure-io-pdo",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureIoPdo(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"IO PDO {request.Name} updated in tsproj.");
            });

    public static IAutomationStep EnsureIoBoxImageInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureIoBoxImageRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure IO Box Image In Tsproj",
            "tsproj.ensure-io-box-image",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureIoBoxImage(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"IO Box {request.BoxId} image metadata updated in tsproj.");
            });

    public static IAutomationStep EnsureMappingInfoInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureMappingInfoRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure MappingInfo In Tsproj",
            "tsproj.ensure-mapping-info",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureMappingInfo(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"MappingInfo {request.Id} updated in tsproj.");
            });

    public static IAutomationStep EnsureIoMappingLinkInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureIoMappingLinkRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure IO Mapping Link In Tsproj",
            "tsproj.ensure-io-mapping-link",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureIoMappingLink(tsprojPath, request);
                return StepExecutionOutcome.Indirect("IO mapping link updated in tsproj.");
            });

    public static IAutomationStep ApplyIoTopologyPlanInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ApplyIoTopologyPlanRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Apply IO Topology Plan In Tsproj",
            "tsproj.apply-io-topology-plan",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                ApplyIoTopologyPlanResult result = service.ApplyIoTopologyPlan(tsprojPath, request);
                return StepExecutionOutcome.Indirect(result.Summary);
            });

    public static IAutomationStep ReplaceDataTypesSectionInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ReplaceDataTypesSectionRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Replace DataTypes Section In Tsproj",
            "tsproj.replace-data-types-section",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ReplaceDataTypesSection(tsprojPath, request);
                return StepExecutionOutcome.Indirect("DataTypes section replaced in tsproj.");
            });

    public static IAutomationStep ReplaceSystemSettingsSectionInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ReplaceSystemSettingsSectionRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Replace System Settings Section In Tsproj",
            "tsproj.replace-system-settings-section",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ReplaceSystemSettingsSection(tsprojPath, request);
                return StepExecutionOutcome.Indirect("System Settings section replaced in tsproj.");
            });

    public static IAutomationStep EnsureSystemSettingsInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureSystemSettingsRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure System Settings In Tsproj",
            "tsproj.ensure-system-settings",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureSystemSettings(tsprojPath, request);
                return StepExecutionOutcome.Indirect("System Settings ensured in tsproj.");
            });

    public static IAutomationStep ApplyInstanceParameterPlanInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ApplyInstanceParameterPlanRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Apply Instance Parameter Plan In Tsproj",
            "tsproj.apply-instance-parameter-plan",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ApplyInstanceParameterPlan(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Instance parameter plan applied in tsproj.");
            });

    public static IAutomationStep ClearInstanceParameterValuesInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearInstanceParameterValuesRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear Instance Parameter Values In Tsproj",
            "tsproj.clear-instance-parameter-values",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearInstanceParameterValues(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"ParameterValues cleared for {request.InstanceName} in tsproj.");
            });

    public static IAutomationStep ClearInstanceDataPointerValuesInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ClearInstanceDataPointerValuesRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Clear Instance Data Pointer Values In Tsproj",
            "tsproj.clear-instance-data-pointer-values",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ClearInstanceDataPointerValues(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"DataPointerValues cleared for {request.InstanceName} in tsproj.");
            });

    public static IAutomationStep ApplyInstanceInterfacePointerPlanInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ApplyInstanceInterfacePointerPlanRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Apply Instance Interface Pointer Plan In Tsproj",
            "tsproj.apply-instance-interface-pointer-plan",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ApplyInstanceInterfacePointerPlan(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Instance interface pointer plan applied in tsproj.");
            });

    public static IAutomationStep ApplyInstanceDataPointerPlanInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ApplyInstanceDataPointerPlanRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Apply Instance Data Pointer Plan In Tsproj",
            "tsproj.apply-instance-data-pointer-plan",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ApplyInstanceDataPointerPlan(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Instance data pointer plan applied in tsproj.");
            });

    public static IAutomationStep RefreshCppInstanceTmcDescInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        RefreshCppInstanceTmcDescRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Refresh C++ Instance TmcDesc In Tsproj",
            "tsproj.refresh-cpp-instance-tmc-desc",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                RefreshCppInstanceTmcDescResult result = service.RefreshCppInstanceTmcDesc(tsprojPath, request);
                return result.Succeeded
                    ? StepExecutionOutcome.Indirect(result.Summary)
                    : new StepExecutionOutcome(
                        StepExecutionStatus.Failed,
                        result.Summary,
                        new Dictionary<string, string?>
                        {
                            ["projectPath"] = result.ProjectPath,
                            ["refreshedCount"] = result.RefreshedCount.ToString(CultureInfo.InvariantCulture),
                            ["errorsJson"] = JsonSerializer.Serialize(result.Errors)
                        },
                        Array.Empty<EvidenceArtifact>());
            });

    public static IAutomationStep SetCppInstanceMetadataInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        SetCppInstanceMetadataRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Set C++ Instance Metadata In Tsproj",
            "tsproj.set-cpp-instance-metadata",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.SetCppInstanceMetadata(tsprojPath, request);
                return StepExecutionOutcome.Indirect($"C++ instance metadata {request.InstanceName} updated in tsproj.");
            });

    public static IAutomationStep EnsureIoTaskImageInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureIoTaskImageRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure IO Task Image In Tsproj",
            "tsproj.ensure-io-task-image",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureIoTaskImage(tsprojPath, request);
                return StepExecutionOutcome.Indirect("IO task image definition updated in tsproj.");
            });

    public static IAutomationStep BindInstanceToTaskInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        BindInstanceToTaskRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Bind Instance To Task In Tsproj",
            "tsproj.bind-instance-task",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.BindInstanceToTask(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Instance binding updated in tsproj.");
            });

    public static IAutomationStep BindPlcInstanceToTaskInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        BindPlcInstanceToTaskRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Bind PLC Instance To Task In Tsproj",
            "tsproj.bind-plc-instance-task",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.BindPlcInstanceToTask(tsprojPath, request);
                return StepExecutionOutcome.Indirect("PLC instance binding updated in tsproj.");
            });

    public static IAutomationStep EnsureParameterInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureParameterValueRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure Parameter In Tsproj",
            "tsproj.ensure-parameter",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureParameterValue(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Parameter default updated in tsproj.");
            });

    public static IAutomationStep EnsureInterfacePointerInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureInterfacePointerValueRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure Interface Pointer In Tsproj",
            "tsproj.ensure-interface-pointer",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureInterfacePointerValue(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Interface pointer updated in tsproj.");
            });

    public static IAutomationStep EnsureDataPointerInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureDataPointerValueRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure Data Pointer In Tsproj",
            "tsproj.ensure-data-pointer",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureDataPointerValue(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Data pointer updated in tsproj.");
            });

    public static IAutomationStep EnsureTaskPouOidInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureTaskPouOidRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure TaskPouOid In Tsproj",
            "tsproj.ensure-task-pou-oid",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureTaskPouOid(tsprojPath, request);
                return StepExecutionOutcome.Indirect("TaskPouOid updated in tsproj.");
            });

    public static IAutomationStep EnsureInitSymbolInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureInitSymbolRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure InitSymbol In Tsproj",
            "tsproj.ensure-init-symbol",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureInitSymbol(tsprojPath, request);
                return StepExecutionOutcome.Indirect("InitSymbol updated in tsproj.");
            });

    public static IAutomationStep EnsureMappingLinkInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        EnsureMappingLinkRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Ensure Mapping Link In Tsproj",
            "tsproj.ensure-mapping-link",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.EnsureMappingLink(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Mapping link updated in tsproj.");
            });

    public static IAutomationStep MergeFragmentIntoTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        MergeNamedElementFragmentRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Merge Fragment Into Tsproj",
            "tsproj.merge-fragment",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.MergeNamedElementFragment(tsprojPath, request);
                return StepExecutionOutcome.Indirect("XML fragment merged into tsproj.");
            });

    public static IAutomationStep UpsertElementInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        TsprojElementUpsertRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Upsert Element In Tsproj",
            "tsproj.upsert-element",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.UpsertElement(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Generic XML element upserted in tsproj.");
            });

    public static IAutomationStep UpsertFragmentInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        TsprojFragmentUpsertRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Upsert Fragment In Tsproj",
            "tsproj.upsert-fragment",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.UpsertFragment(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Generic XML fragment upserted in tsproj.");
            });

    public static IAutomationStep ApplyMutationPlanInTsproj(
        string stepId,
        TwinCatTsprojMutationService service,
        ApplyTsprojMutationPlanRequest request,
        string projectPathStateKey = TwinCatStateKeys.ProjectPath) =>
        Create(
            stepId,
            "Apply Mutation Plan In Tsproj",
            "tsproj.apply-mutation-plan",
            context =>
            {
                string tsprojPath = context.State.GetRequired<string>(projectPathStateKey);
                service.ApplyMutationPlan(tsprojPath, request);
                return StepExecutionOutcome.Indirect("Generic XML mutation plan applied in tsproj.");
            });

    public static IAutomationStep AdsScan(
        string stepId,
        AdsValidationService service,
        AdsPortScanRequest request) =>
        Create(
            stepId,
            "ADS Scan",
            "validation.ads-scan",
            _ =>
            {
                AdsPortScanResult result = service.ScanPorts(request);
                string succeededPorts = string.Join(";", result.Ports.Where(port => port.Succeeded).Select(port => port.Port.ToString()));
                return result.AnySucceeded
                    ? StepExecutionOutcome.Success(
                        "ADS port scan found a reachable endpoint.",
                        new Dictionary<string, string?> { ["succeededPorts"] = succeededPorts })
                    : StepExecutionOutcome.Failed("ADS port scan found no reachable endpoint.");
            });

    public static IAutomationStep AdsRead(
        string stepId,
        AdsValidationService service,
        AdsReadRequest request,
        string lastValueStateKey = TwinCatStateKeys.LastAdsValue) =>
        Create(
            stepId,
            "ADS Read",
            "validation.ads-read",
            context =>
            {
                AdsReadResult result = service.Read(request);
                if (!result.Succeeded)
                {
                    return StepExecutionOutcome.Failed("ADS read failed: " + result.ErrorMessage);
                }

                context.State.Set(lastValueStateKey, result.Value);
                return StepExecutionOutcome.Success(
                    "ADS read succeeded.",
                    new Dictionary<string, string?> { ["value"] = result.Value });
            });

    public static IAutomationStep AdsReadSymbols(
        string stepId,
        AdsValidationService service,
        AdsReadSymbolsRequest request,
        string lastValuesStateKey = TwinCatStateKeys.LastAdsValues) =>
        Create(
            stepId,
            "ADS Read Symbols",
            "validation.ads-read-symbols",
            context =>
            {
                AdsReadSymbolsResult result = service.ReadSymbols(request);
                string valuesText = string.Join(
                    "; ",
                    result.Symbols.Select(symbol =>
                        symbol.Succeeded
                            ? $"{symbol.SymbolPath}={symbol.Value}"
                            : $"{symbol.SymbolPath}=<failed: {symbol.ErrorMessage}>"));

                context.State.Set(lastValuesStateKey, result);
                IReadOnlyDictionary<string, string?> outputs = new Dictionary<string, string?>
                {
                    ["succeededCount"] = result.SucceededCount.ToString(),
                    ["failedCount"] = result.FailedCount.ToString(),
                    ["valuesText"] = valuesText,
                    ["valuesJson"] = JsonSerializer.Serialize(result.Symbols, AdsJsonOptions)
                };

                if (!result.Succeeded && !request.ContinueOnError)
                {
                    return new StepExecutionOutcome(
                        StepExecutionStatus.Failed,
                        "ADS read symbols failed: " + valuesText,
                        outputs,
                        Array.Empty<EvidenceArtifact>());
                }

                return StepExecutionOutcome.Success(
                    "ADS read symbols completed: " + valuesText,
                    outputs);
            });

    public static IAutomationStep AssertAdsState(
        string stepId,
        AdsValidationService service,
        AssertAdsStateRequest request,
        string lastResultStateKey = TwinCatStateKeys.LastAdsStateAssertion) =>
        Create(
            stepId,
            "Assert ADS State",
            "validation.assert-ads-state",
            context =>
            {
                AssertAdsStateResult result = service.AssertStates(request);
                string statesText = string.Join(
                    "; ",
                    result.Ports.Select(port =>
                        port.Succeeded
                            ? $"{port.Port}={port.ActualAdsState}"
                            : $"{port.Port}=<expected {port.ExpectedAdsState}, actual {port.ActualAdsState ?? "(unreachable)"}: {port.ErrorMessage}>"));

                context.State.Set(lastResultStateKey, result);
                IReadOnlyDictionary<string, string?> outputs = new Dictionary<string, string?>
                {
                    ["succeededCount"] = result.SucceededCount.ToString(),
                    ["failedCount"] = result.FailedCount.ToString(),
                    ["statesText"] = statesText,
                    ["statesJson"] = JsonSerializer.Serialize(result.Ports, AdsJsonOptions)
                };

                if (!result.Succeeded)
                {
                    return new StepExecutionOutcome(
                        StepExecutionStatus.Failed,
                        "ADS state assertion failed: " + statesText,
                        outputs,
                        Array.Empty<EvidenceArtifact>());
                }

                return StepExecutionOutcome.Success(
                    "ADS state assertion succeeded: " + statesText,
                    outputs);
            });

    public static IAutomationStep MarkEventLogWindow(
        string stepId,
        AdsValidationService service,
        MarkEventLogWindowRequest request,
        string lastMarkerStateKey = "event.lastWindowMarker") =>
        Create(
            stepId,
            "Mark Event Log Window",
            "validation.mark-event-log-window",
            context =>
            {
                EventLogWindowMarker marker = service.MarkEventLogWindow(request);
                context.State.Set(lastMarkerStateKey, marker);
                IReadOnlyDictionary<string, string?> outputs = new Dictionary<string, string?>
                {
                    ["markedAt"] = marker.MarkedAt.ToString("O"),
                    ["lastEntryIndex"] = marker.LastEntryIndex?.ToString(),
                    ["markerJson"] = JsonSerializer.Serialize(marker, AdsJsonOptions)
                };

                return StepExecutionOutcome.Success(
                    $"Event log window marked for {marker.ProviderName}.",
                    outputs);
            });

    public static IAutomationStep AssertEventLogWindow(
        string stepId,
        AdsValidationService service,
        AssertEventLogWindowRequest request,
        string lastResultStateKey = "event.lastWindowAssertion") =>
        Create(
            stepId,
            "Assert Event Log Window",
            "validation.assert-event-log-window",
            context =>
            {
                AssertEventLogWindowResult result = service.AssertEventLogWindow(request);
                context.State.Set(lastResultStateKey, result);
                IReadOnlyDictionary<string, string?> outputs = new Dictionary<string, string?>
                {
                    ["observedEventCount"] = result.ObservedEventCount.ToString(),
                    ["errorOrCriticalCount"] = result.ErrorOrCriticalCount.ToString(),
                    ["configAdsStateCount"] = result.ConfigAdsStateCount.ToString(),
                    ["errorsText"] = string.Join("; ", result.Errors),
                    ["assertionJson"] = JsonSerializer.Serialize(result, AdsJsonOptions)
                };

                if (!result.Succeeded)
                {
                    return new StepExecutionOutcome(
                        StepExecutionStatus.Failed,
                        result.Summary,
                        outputs,
                        Array.Empty<EvidenceArtifact>());
                }

                return StepExecutionOutcome.Success(result.Summary, outputs);
            });

    public static IAutomationStep AssertProcessCrashWindow(
        string stepId,
        AdsValidationService service,
        AssertProcessCrashWindowRequest request,
        string lastResultStateKey = "event.lastProcessCrashAssertion") =>
        Create(
            stepId,
            "Assert Process Crash Window",
            "validation.assert-process-crash-window",
            context =>
            {
                AssertProcessCrashWindowResult result = service.AssertProcessCrashWindow(request);
                context.State.Set(lastResultStateKey, result);
                IReadOnlyDictionary<string, string?> outputs = new Dictionary<string, string?>
                {
                    ["observedEventCount"] = result.ObservedEventCount.ToString(),
                    ["matchingEventCount"] = result.MatchingEventCount.ToString(),
                    ["errorsText"] = string.Join("; ", result.Errors),
                    ["assertionJson"] = JsonSerializer.Serialize(result, AdsJsonOptions)
                };

                if (!result.Succeeded)
                {
                    return new StepExecutionOutcome(
                        StepExecutionStatus.Failed,
                        result.Summary,
                        outputs,
                        Array.Empty<EvidenceArtifact>());
                }

                return StepExecutionOutcome.Success(result.Summary, outputs);
            });

    public static IAutomationStep GrantSigningCertificate(
        string stepId,
        TwinCatSigningService service,
        GrantTwinCatSigningCertificateRequest request) =>
        Create(
            stepId,
            "Grant Signing Certificate",
            "signing.grant-certificate",
            _ =>
            {
                TwinCatSignToolResult result = service.GrantCertificate(request);
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        "TwinCAT signing certificate grant updated.",
                        new Dictionary<string, string?>
                        {
                            ["exitCode"] = result.ExitCode.ToString(),
                            ["commandLine"] = result.RedactedCommandLine
                        })
                    : StepExecutionOutcome.Failed($"TcSignTool grant failed. ExitCode={result.ExitCode}. {result.Output}");
            });

    public static IAutomationStep SignTwinCatBinary(
        string stepId,
        TwinCatSigningService service,
        SignTwinCatBinaryRequest request) =>
        Create(
            stepId,
            "Sign TwinCAT Binary",
            "signing.sign-twincat-binary",
            _ =>
            {
                TwinCatSignToolResult result = service.Sign(request);
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        "TwinCAT binary signed.",
                        new Dictionary<string, string?>
                        {
                            ["targetPaths"] = string.Join(";", result.TargetPaths),
                            ["exitCode"] = result.ExitCode.ToString(),
                            ["commandLine"] = result.RedactedCommandLine
                        },
                        result.TargetPaths
                            .Select(path => new EvidenceArtifact("signed-twincat-binary", path, Path.GetExtension(path).TrimStart('.')))
                        .ToList())
                    : StepExecutionOutcome.Failed($"TcSignTool sign failed. ExitCode={result.ExitCode}. {result.Output}");
            });

    public static IAutomationStep SetTwinCatSigningLicense(
        string stepId,
        TwinCatSigningService service,
        SetTwinCatSigningLicenseRequest request) =>
        Create(
            stepId,
            "Set TwinCAT Signing License",
            "signing.set-license",
            _ =>
            {
                TwinCatSigningLicenseResult result = service.SetLicense(request);
                return StepExecutionOutcome.Success(
                    "TwinCAT signing license configured.",
                    new Dictionary<string, string?>
                    {
                        ["projectFilePath"] = result.ProjectFilePath,
                        ["licenseName"] = result.LicenseName,
                        ["enableSigning"] = result.EnableSigning ? "true" : "false",
                        ["passwordWritten"] = result.PasswordWritten ? "true" : "false"
                    },
                    [new EvidenceArtifact("twincat-cpp-project", result.ProjectFilePath, "vcxproj")]);
            });

    public static IAutomationStep VerifyTwinCatBinarySignature(
        string stepId,
        TwinCatSigningService service,
        VerifyTwinCatBinarySignatureRequest request) =>
        Create(
            stepId,
            "Verify TwinCAT Binary Signature",
            "signing.verify-twincat-binary",
            _ =>
            {
                TwinCatSignToolResult result = service.Verify(request);
                bool acceptedTestModeWarning = request.AllowTestModeWarning && result.ExitCode != 0;
                return result.Succeeded
                    ? StepExecutionOutcome.Success(
                        acceptedTestModeWarning
                            ? "TwinCAT binary signature verified; accepted TcSignTool test-mode certificate warning."
                            : "TwinCAT binary signature verified.",
                        new Dictionary<string, string?>
                        {
                            ["targetPaths"] = string.Join(";", result.TargetPaths),
                            ["exitCode"] = result.ExitCode.ToString(),
                            ["acceptedTestModeWarning"] = acceptedTestModeWarning ? "true" : "false",
                            ["commandLine"] = result.RedactedCommandLine
                        },
                        result.TargetPaths
                            .Select(path => new EvidenceArtifact("verified-twincat-binary", path, Path.GetExtension(path).TrimStart('.')))
                            .ToList())
                    : StepExecutionOutcome.Failed($"TcSignTool verify failed. ExitCode={result.ExitCode}. {result.Output}");
            });

    private static IAutomationStep Create(
        string stepId,
        string displayName,
        string kind,
        Func<AutomationContext, StepExecutionOutcome> executor) =>
        new DelegateAutomationStep(
            stepId,
            displayName,
            TwinCatStepCatalog.Require(kind),
            (context, _) => Task.FromResult(executor(context)));

    private static readonly JsonSerializerOptions AdsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
