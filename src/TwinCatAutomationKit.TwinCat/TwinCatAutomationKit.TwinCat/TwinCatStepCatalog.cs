using TwinCatAutomationKit.Abstractions;

namespace TwinCatAutomationKit.TwinCat;

public static class TwinCatStepCatalog
{
    private static readonly IReadOnlyList<StepContract> ContractsInternal = new[]
    {
        new StepContract(
            "engineering.launch-visual-studio",
            "TwinCatEngineeringService.LaunchVisualStudio",
            "engineering",
            "Starts a new DTE session that later steps can use for XAE creation, build, and activation.",
            new[] { "Visual Studio with TwinCAT XAE must be installed on the machine." },
            new[]
            {
                new StepParameterContract("ProgId", "string", false, "Visual Studio DTE ProgId.", TwinCatPathDefaults.DefaultVisualStudioProgId),
                new StepParameterContract("StartupDelayMs", "int", false, "Warm-up delay before the DTE session is used.", "5000"),
                new StepParameterContract("Visible", "bool", false, "Whether the launched Visual Studio window should be visible.", "true")
            },
            new[]
            {
                new StepOutputContract("session", "TwinCatEngineeringSession", "Live DTE session stored in pipeline state.")
            },
            new[] { "Read back DTE version or solution state to confirm the COM server is reachable." }),

        new StepContract(
            "engineering.create-xae-solution",
            "TwinCatEngineeringService.CreateTwinCatSolution",
            "engineering",
            "Creates a fresh TwinCAT XAE solution and binds the automation session to its ITcSysManager root.",
            new[] { "An active DTE session must exist.", "At least one default TwinCAT XAE template path must exist on the machine." },
            new[]
            {
                new StepParameterContract("SolutionDirectory", "string", true, "Directory where the solution should be created."),
                new StepParameterContract("SolutionName", "string", true, "Visual Studio solution name."),
                new StepParameterContract("ProjectName", "string", true, "TwinCAT project name.")
            },
            new[]
            {
                new StepOutputContract("solutionPath", "string", "Absolute .sln path."),
                new StepOutputContract("projectPath", "string", "Absolute .tsproj path.")
            },
            new[] { "Check that the .sln and .tsproj files exist and that ITcSysManager can be retrieved." }),

        new StepContract(
            "engineering.open-xae-solution",
            "TwinCatEngineeringService.OpenTwinCatSolution",
            "engineering",
            "Re-opens an existing TwinCAT solution and re-attaches COM references after .tsproj file mutations.",
            new[] { "An active DTE session must exist.", "The target .sln and .tsproj must already exist." },
            new[]
            {
                new StepParameterContract("SolutionPath", "string", true, "Absolute .sln path."),
                new StepParameterContract("ProjectPath", "string", true, "Absolute .tsproj path.")
            },
            new[]
            {
                new StepOutputContract("solutionPath", "string", "Absolute .sln path."),
                new StepOutputContract("projectPath", "string", "Absolute .tsproj path.")
            },
            new[] { "Confirm that the project can be found again in the DTE solution model." }),

        new StepContract(
            "engineering.create-cpp-project",
            "TwinCatEngineeringService.CreateCppProject",
            "engineering",
            "Creates a TwinCAT C++ project beneath TIXC using a specified Beckhoff wizard id.",
            new[] { "A bound TwinCAT engineering session must exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Requested C++ project name."),
                new StepParameterContract("WizardId", "string", false, "Wizard identifier for the project template.", "TcVersionedDriverWizard")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the C++ project."),
                new StepOutputContract("projectFilePath", "string", "Expected .vcxproj path.")
            },
            new[] { "Verify the TIXC child exists and the .vcxproj file lands on disk." }),

        new StepContract(
            "engineering.create-vs-cpp-project",
            "TwinCatEngineeringService.CreateVisualStudioCppProject",
            "engineering",
            "Creates a regular Visual Studio C++ project inside the current solution, such as an AdsClient console application.",
            new[] { "A solution must already be loaded in the DTE session.", "An installed Visual Studio C++ template or explicit AllowTemplateFallback=true is required." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Visual Studio C++ project name."),
                new StepParameterContract("ProjectDirectory", "string", false, "Optional project directory. Defaults to SolutionDirectory/ProjectName."),
                new StepParameterContract("TemplateKind", "string", false, "Template semantic kind. P0 supports ConsoleApplication.", "ConsoleApplication"),
                new StepParameterContract("CandidateTemplatePaths", "string[]", false, "Optional explicit template paths for machine-specific VS installs."),
                new StepParameterContract("PlatformToolset", "string", false, "Optional PlatformToolset value such as v143."),
                new StepParameterContract("AllowTemplateFallback", "bool", false, "Whether the service may synthesize a minimal MSBuild C++ project if no installed template is found.", "false")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Absolute .vcxproj path."),
                new StepOutputContract("projectGuid", "string", "Project GUID registered in the .vcxproj/.sln."),
                new StepOutputContract("projectDirectory", "string", "Absolute project directory.")
            },
            new[] { "Verify the .sln contains the project, .vcxproj exists, DTE can find it by name, and reopen keeps it visible." }),

        new StepContract(
            "engineering.ensure-solution-project-dependency",
            "TwinCatEngineeringService.EnsureSolutionProjectDependency",
            "engineering",
            "Ensures a Visual Studio solution ProjectDependencies entry exists from one project to another.",
            new[] { "Both projects must already exist in the saved .sln." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Dependent project name."),
                new StepParameterContract("DependsOnProjectName", "string", true, "Project that must build before ProjectName.")
            },
            new[]
            {
                new StepOutputContract("projectGuid", "string", "Dependent project GUID."),
                new StepOutputContract("dependsOnProjectGuid", "string", "Dependency project GUID.")
            },
            new[] { "Inspect .sln ProjectSection(ProjectDependencies) and reload the solution to confirm the dependency remains." }),

        new StepContract(
            "engineering.create-plc-project",
            "TwinCatEngineeringService.CreatePlcProject",
            "engineering",
            "Creates a PLC project using the first compatible Beckhoff PLC template available on the machine.",
            new[] { "A bound TwinCAT engineering session must exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "PLC project name."),
                new StepParameterContract("AllowOfflineFallback", "bool", false, "Whether CreatePlcProject may synthesize a minimal PLC project file mutation when COM/template creation fails.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the PLC project."),
                new StepOutputContract("projectFilePath", "string", "Expected .plcproj path.")
            },
            new[] { "Check that the PLC node exists and that the .plcproj file was created." }),

        new StepContract(
            "engineering.create-module",
            "TwinCatEngineeringService.CreateModule",
            "engineering",
            "Creates a module class inside a TwinCAT C++ project via the module wizard id.",
            new[] { "A TwinCAT C++ project must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Owning C++ project name."),
                new StepParameterContract("ModuleName", "string", true, "Requested module class name."),
                new StepParameterContract("WizardId", "string", true, "Beckhoff module wizard id.", "TcModuleClassWizard"),
                new StepParameterContract("AllowOfflineFallback", "bool", false, "Whether CreateModule may patch module skeleton artifacts when module wizard automation fails.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the created module node."),
                new StepOutputContract("moduleCppPath", "string", "Expected .cpp path for the module.")
            },
            new[] { "Confirm that the module files exist and the tree node is visible after save/refresh." }),

        new StepContract(
            "engineering.publish-modules",
            "TwinCatEngineeringService.PublishModules",
            "engineering",
            "Invokes the TwinCAT C++ project PublishModules method so updated module source regenerates TMC metadata.",
            new[] { "The target TwinCAT C++ project must exist and expose PublishModules in its tree XML." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "TwinCAT C++ project name."),
                new StepParameterContract("PostPublishDelayMs", "int", false, "Delay after triggering PublishModules.", "5000"),
                new StepParameterContract("WaitForUpdatedTmcTimeoutMs", "int", false, "Maximum wait for the project .tmc timestamp to update.", "30000")
            },
            new[]
            {
                new StepOutputContract("updatedTmcPath", "string", "Project .tmc path observed after publish."),
                new StepOutputContract("succeeded", "bool", "Whether publish left a readable project .tmc."),
                new StepOutputContract("updated", "bool", "Whether the .tmc timestamp or content changed during this publish call.")
            },
            new[] { "Check that the .tmc is readable and contains the expected module classes; updated=true means the timestamp or content changed during this publish call." }),

        new StepContract(
            "engineering.add-module-instance",
            "TwinCatEngineeringService.AddModuleInstance",
            "engineering",
            "Adds a TcCOM instance from a project TMC by resolving the module GUID and calling CreateChild on the project node.",
            new[] { "The target C++ project and its .tmc file must exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "Owning C++ project name."),
                new StepParameterContract("ProjectTmcPath", "string", true, "Absolute path to the project TMC."),
                new StepParameterContract("InstanceBaseName", "string", true, "Base name to use when creating the instance."),
                new StepParameterContract("ModuleClassName", "string", false, "Optional module class name when the TMC contains multiple classes."),
                new StepParameterContract("AllowOfflineFallback", "bool", false, "Whether AddModuleInstance may inject a deterministic instance skeleton through .tsproj file mutation when COM CreateChild fails.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the instance."),
                new StepOutputContract("objectId", "string", "Resolved instance ObjectId / OTCID.")
            },
            new[] { "Export the instance XML and confirm that the expected CLSID and Name are present." }),

        new StepContract(
            "engineering.ensure-task",
            "TwinCatEngineeringService.EnsureTask",
            "engineering",
            "Creates or reuses a Task under TIRT and normalizes its timing, priority, and AMS port.",
            new[] { "A bound TwinCAT engineering session must exist." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("TaskSubtype", "int", true, "TwinCAT task subtype passed to CreateChild."),
                new StepParameterContract("Priority", "int", true, "Task priority."),
                new StepParameterContract("CycleTimeUs", "int", true, "Task cycle time in microseconds."),
                new StepParameterContract("AmsPort", "int", true, "Assigned AMS port."),
                new StepParameterContract("IoAtBegin", "bool", false, "Optional TaskDef IoAtBegin setting.", "true")
            },
            new[]
            {
                new StepOutputContract("treeItemPath", "string", "TwinCAT tree path for the task."),
                new StepOutputContract("objectId", "string", "Task OTCID / ObjectId.")
            },
            new[] { "Read back ProduceXml(true) and confirm TaskDef/Context values were updated." }),

        new StepContract(
            "engineering.export-tree-item-xml",
            "TwinCatEngineeringService.ExportTreeItemXml",
            "engineering",
            "Exports ProduceXml output for any TwinCAT tree node as durable evidence.",
            new[] { "A bound TwinCAT engineering session must exist.", "The target tree path must be valid." },
            new[]
            {
                new StepParameterContract("TreeItemPath", "string", true, "TwinCAT tree path such as TIXC^MyProject."),
                new StepParameterContract("DestinationPath", "string", true, "Absolute output file path."),
                new StepParameterContract("Recursive", "bool", false, "Whether ProduceXml(true) should be used.", "false")
            },
            new[]
            {
                new StepOutputContract("xmlPath", "string", "Saved XML evidence file.")
            },
            new[] { "Re-open the XML file and verify it contains the expected node metadata." }),

        new StepContract(
            "engineering.save-all",
            "TwinCatEngineeringService.SaveAll",
            "engineering",
            "Flushes pending Visual Studio document and solution changes before build, reopen, or .tsproj file mutation steps.",
            new[] { "A live DTE session must exist." },
            Array.Empty<StepParameterContract>(),
            Array.Empty<StepOutputContract>(),
            new[] { "Re-check file timestamps or solution dirtiness after the command." }),

        new StepContract(
            "engineering.close-visual-studio",
            "TwinCatEngineeringService.CloseVisualStudio",
            "engineering",
            "Closes the current Visual Studio DTE session, optionally issuing SaveAll first.",
            new[] { "A live DTE session must exist." },
            new[]
            {
                new StepParameterContract("SaveBeforeClose", "bool", false, "Whether SaveAll should run before closing the session.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Confirm that the DTE process exits and the session object is no longer usable." }),

        new StepContract(
            "engineering.build-solution",
            "TwinCatEngineeringService.BuildCurrentSolution",
            "engineering",
            "Runs SolutionBuild.Build and waits until the DTE build state reaches done.",
            new[] { "A solution must be loaded in the DTE session." },
            new[]
            {
                new StepParameterContract("TimeoutMs", "int", false, "Maximum build wait time.", "300000")
            },
            new[]
            {
                new StepOutputContract("lastBuildInfo", "int", "DTE LastBuildInfo value.")
            },
            new[] { "Treat LastBuildInfo == 0 as the engineering success condition." }),

        new StepContract(
            "cpp.create-project-item",
            "TwinCatEngineeringService.CreateCppProjectItem",
            "cpp",
            "Creates a C++/header/resource/None project item, optionally creating the physical file and registering it in .vcxproj and .filters.",
            new[] { "The target C++ .vcxproj must already exist.", "RelativePath must be inside the project directory." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ItemType", "CppProjectItemType", false, "MSBuild item type, or Infer from extension.", "Infer"),
                new StepParameterContract("Filter", "string", false, "Optional .vcxproj.filters display filter."),
                new StepParameterContract("AddToProject", "bool", false, "Whether to register the item in .vcxproj.", "true"),
                new StepParameterContract("CreatePhysicalFile", "bool", false, "Whether to create an empty physical file.", "true"),
                new StepParameterContract("ConflictPolicy", "ProjectItemConflictPolicy", false, "FailIfExists, KeepExisting, or ReplaceProjectRegistration.", "FailIfExists"),
                new StepParameterContract("AllowMsBuildFallback", "bool", false, "Allow typed MSBuild XML update when DTE ProjectItems is not stable.", "true")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("filePath", "string", "Physical project item path."),
                new StepOutputContract("itemType", "CppProjectItemType", "Resolved MSBuild item type."),
                new StepOutputContract("addedToProject", "bool", "Whether .vcxproj registration exists after the step.")
            },
            new[] { "Verify the file exists, .vcxproj contains the requested Include, .filters contains filter mapping when requested, and reopen shows the item." }),

        new StepContract(
            "cpp.write-project-item-content",
            "TwinCatEngineeringService.WriteCppProjectItemContent",
            "cpp",
            "Writes source/resource/text payload into an existing C++ project item file and returns its content hash.",
            new[] { "The target C++ project must already exist.", "ContentText and ContentFile are mutually exclusive." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ContentText", "string", false, "Inline source payload."),
                new StepParameterContract("ContentFile", "string", false, "Payload file path produced by the JSON plan files[] section or another explicit caller-owned payload."),
                new StepParameterContract("Encoding", "string", false, "utf-8, utf-8-bom, or ascii.", "utf-8"),
                new StepParameterContract("NewLine", "string", false, "preserve, crlf, or lf.", "preserve"),
                new StepParameterContract("WritePolicy", "ProjectItemWritePolicy", false, "FailIfMissing, FailIfNonEmpty, or Overwrite.", "Overwrite"),
                new StepParameterContract("RequireProjectRegistration", "bool", false, "Fail if .vcxproj does not already register the item.", "false")
            },
            new[]
            {
                new StepOutputContract("filePath", "string", "Written file path."),
                new StepOutputContract("sha256", "string", "SHA256 hash of the written bytes."),
                new StepOutputContract("bytesWritten", "long", "Number of bytes written.")
            },
            new[] { "Compare the output hash with the payload and reopen the item in VS." }),

        new StepContract(
            "cpp.remove-project-item",
            "TwinCatEngineeringService.RemoveCppProjectItem",
            "cpp",
            "Removes a C++ project item registration and optional filter entry and physical file.",
            new[] { "The target C++ .vcxproj must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ItemType", "CppProjectItemType", false, "MSBuild item type, or Infer from extension.", "Infer"),
                new StepParameterContract("DeletePhysicalFile", "bool", false, "Whether to delete the physical file.", "true"),
                new StepParameterContract("RemoveFilterEntry", "bool", false, "Whether to remove the .vcxproj.filters item mapping.", "true"),
                new StepParameterContract("IgnoreMissing", "bool", false, "Treat missing item/file as success.", "false")
            },
            new[]
            {
                new StepOutputContract("removedFromProject", "bool", "Whether .vcxproj registration was removed."),
                new StepOutputContract("deletedFile", "bool", "Whether a physical file was deleted.")
            },
            new[] { "Verify .vcxproj/.filters no longer reference the item and the file is absent when DeletePhysicalFile=true." }),

        new StepContract(
            "cpp.set-project-property",
            "TwinCatEngineeringService.SetCppProjectProperty",
            "cpp",
            "Sets a project-level or configuration-level MSBuild property in a C++ .vcxproj.",
            new[] { "The target C++ .vcxproj must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("PropertyName", "string", true, "MSBuild property element name."),
                new StepParameterContract("Value", "string", true, "Property value."),
                new StepParameterContract("Condition", "string", false, "Optional PropertyGroup Condition."),
                new StepParameterContract("PropertyGroupLabel", "string", false, "Optional PropertyGroup Label, such as Globals or Configuration.")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("propertyName", "string", "Property name."),
                new StepOutputContract("condition", "string", "Applied condition, if any.")
            },
            new[] { "Inspect .vcxproj and confirm the PropertyGroup contains the requested value; build log should reflect build-affecting properties." }),

        new StepContract(
            "cpp.set-item-definition-property",
            "TwinCatEngineeringService.SetCppItemDefinitionProperty",
            "cpp",
            "Sets a C++ ItemDefinitionGroup tool property such as ClCompile include paths or Link dependencies.",
            new[] { "The target C++ .vcxproj must already exist." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("ToolName", "string", true, "Tool element name such as ClCompile, Link, ResourceCompile, or PostBuildEvent."),
                new StepParameterContract("PropertyName", "string", true, "Tool property element name."),
                new StepParameterContract("Value", "string", true, "Property value, including inherited macros when required."),
                new StepParameterContract("Condition", "string", false, "Optional ItemDefinitionGroup Condition.")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("toolName", "string", "Tool element name."),
                new StepOutputContract("propertyName", "string", "Property element name."),
                new StepOutputContract("condition", "string", "Applied condition, if any.")
            },
            new[] { "Inspect ItemDefinitionGroup XML and confirm build logs use include paths, library paths, language standard, dependencies, or events." }),

        new StepContract(
            "cpp.set-project-item-metadata",
            "TwinCatEngineeringService.SetCppProjectItemMetadata",
            "cpp",
            "Sets file-level MSBuild metadata for a C++ project item, such as PrecompiledHeader or ExcludedFromBuild.",
            new[] { "The target item must already be registered in .vcxproj." },
            new[]
            {
                new StepParameterContract("ProjectName", "string", true, "C++ project name."),
                new StepParameterContract("RelativePath", "string", true, "Path relative to the C++ project directory."),
                new StepParameterContract("ItemType", "CppProjectItemType", true, "MSBuild item type containing the item."),
                new StepParameterContract("MetadataName", "string", true, "Metadata element name."),
                new StepParameterContract("Value", "string", true, "Metadata value."),
                new StepParameterContract("Condition", "string", false, "Optional item Condition.")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated .vcxproj path."),
                new StepOutputContract("relativePath", "string", "Item Include path."),
                new StepOutputContract("metadataName", "string", "Metadata element name."),
                new StepOutputContract("condition", "string", "Applied condition, if any.")
            },
            new[] { "Inspect the item XML and confirm build output respects include/exclude or PCH metadata." }),

        new StepContract(
            "signing.grant-certificate",
            "TwinCatSigningService.GrantCertificate",
            "signing",
            "Grants or removes local TcSignTool authorization for a TwinCAT signing certificate.",
            new[] { "TwinCAT SDK TcSignTool.exe must be installed.", "The certificate file must exist on this machine." },
            new[]
            {
                new StepParameterContract("CertificatePath", "string", true, "Path to the .tccert certificate file."),
                new StepParameterContract("Password", "string", false, "Certificate password. Prefer CLI password-file or password-env-var input; it is never emitted in step outputs."),
                new StepParameterContract("RemoveGrant", "bool", false, "Remove the local grant instead of adding it.", "false"),
                new StepParameterContract("Quiet", "bool", false, "Pass /q to TcSignTool.", "true"),
                new StepParameterContract("ToolPath", "string", false, "Optional TcSignTool.exe override.", TwinCatPathDefaults.DefaultTcSignToolPath)
            },
            new[]
            {
                new StepOutputContract("exitCode", "int", "TcSignTool process exit code."),
                new StepOutputContract("commandLine", "string", "Redacted command line used for evidence.")
            },
            new[] { "Run signing.sign-twincat-binary and signing.verify-twincat-binary after grant to close the loop." }),

        new StepContract(
            "signing.sign-twincat-binary",
            "TwinCatSigningService.Sign",
            "signing",
            "Signs one or more built TwinCAT C++ binaries with Beckhoff TcSignTool.",
            new[] { "engineering.build-solution must have produced the target .tmx/.sys binary.", "The certificate file must exist on this machine." },
            new[]
            {
                new StepParameterContract("CertificatePath", "string", true, "Path to the .tccert certificate file."),
                new StepParameterContract("TargetPaths", "string[]", true, "One or more TwinCAT binary paths to sign."),
                new StepParameterContract("Password", "string", false, "Certificate password. Prefer CLI password-file or password-env-var input; it is piped to TcSignTool instead of placed on the process command line."),
                new StepParameterContract("Quiet", "bool", false, "Pass /q to TcSignTool.", "true"),
                new StepParameterContract("ToolPath", "string", false, "Optional TcSignTool.exe override.", TwinCatPathDefaults.DefaultTcSignToolPath)
            },
            new[]
            {
                new StepOutputContract("targetPaths", "string", "Semicolon-separated signed binary paths."),
                new StepOutputContract("exitCode", "int", "TcSignTool process exit code."),
                new StepOutputContract("commandLine", "string", "Redacted command line used for evidence.")
            },
            new[] { "Run signing.verify-twincat-binary and then activate the configuration with the signed binary." }),

        new StepContract(
            "signing.set-license",
            "TwinCatSigningService.SetLicense",
            "signing",
            "Writes TwinCAT C++ project signing license settings used by MSBuild/TcSignTool.",
            new[] { "The C++ .vcxproj file must exist.", "Run this before engineering.build-solution so the generated .tmx is signed during build." },
            new[]
            {
                new StepParameterContract("ProjectFilePath", "string", true, "Path to the C++ .vcxproj file, or resolve it from ProjectPath and CppProjectName."),
                new StepParameterContract("LicenseName", "string", false, "TwinCAT signing certificate/license name written to TcSignTwinCatCertName.", "optcnc"),
                new StepParameterContract("Password", "string", false, "TwinCAT signing certificate/license password written to TcSignTwinCatCertPW. Prefer password-file or password-env-var outside demo plans."),
                new StepParameterContract("EnableSigning", "bool", false, "Write TcSignTwinCat=true so TwinCAT MSBuild signs the output.", "true")
            },
            new[]
            {
                new StepOutputContract("projectFilePath", "string", "Updated C++ .vcxproj path."),
                new StepOutputContract("licenseName", "string", "Configured TwinCAT signing license name."),
                new StepOutputContract("enableSigning", "bool", "Whether TwinCAT signing is enabled."),
                new StepOutputContract("passwordWritten", "bool", "Whether a password value was written; the password itself is never emitted.")
            },
            new[] { "Inspect the C++ .vcxproj or generated TcSignTwinCatCmd.txt after build; do not expect ADS/runtime state to change until the project is rebuilt and activated." }),

        new StepContract(
            "signing.verify-twincat-binary",
            "TwinCatSigningService.Verify",
            "signing",
            "Verifies that one or more TwinCAT binaries carry a valid TcSignTool signature.",
            new[] { "The target TwinCAT binary must already exist." },
            new[]
            {
                new StepParameterContract("TargetPaths", "string[]", true, "One or more TwinCAT binary paths to verify."),
                new StepParameterContract("Quiet", "bool", false, "Pass /q to TcSignTool.", "true"),
                new StepParameterContract("ToolPath", "string", false, "Optional TcSignTool.exe override.", TwinCatPathDefaults.DefaultTcSignToolPath),
                new StepParameterContract("AllowTestModeWarning", "bool", false, "Treat TcSignTool exit code 2 as success when the output says the file has a signature but the OEM certificate is not signed by Beckhoff.", "false")
            },
            new[]
            {
                new StepOutputContract("targetPaths", "string", "Semicolon-separated verified binary paths."),
                new StepOutputContract("exitCode", "int", "TcSignTool process exit code."),
                new StepOutputContract("acceptedTestModeWarning", "bool", "Whether a TcSignTool test-mode certificate warning was accepted."),
                new StepOutputContract("commandLine", "string", "Redacted command line used for evidence.")
            },
            new[] { "Treat exit code 0 as signed/verified; for local test certificates, set AllowTestModeWarning only when test-mode activation is acceptable.", "Keep the signed binary path as activation evidence." }),

        new StepContract(
            "engineering.activate-configuration",
            "TwinCatEngineeringService.ActivateConfiguration",
            "engineering",
            "Saves the current configuration archive when possible and then activates TwinCAT via ITcSysManager or DTE command fallback.",
            new[] { "A solution must be loaded and its ITcSysManager must be available." },
            new[]
            {
                new StepParameterContract("SaveConfigurationArchive", "bool", false, "Whether to attempt SaveConfiguration before activate.", "true"),
                new StepParameterContract("ConfigurationArchivePath", "string", false, "Optional override for the generated .tszip path.")
            },
            new[]
            {
                new StepOutputContract("activationCommand", "string", "The command or fallback path used for activation.")
            },
            new[] { "Confirm that either ITcSysManager.ActivateConfiguration or a DTE command succeeded." }),

        new StepContract(
            "tsproj.ensure-task",
            "TwinCatTsprojMutationService.EnsureTaskDefinition",
            "tsproj",
            "Creates or updates the declarative task node inside a .tsproj generated by XAE.",
            new[] { "The target .tsproj must already exist on disk." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("Priority", "int", true, "Task priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "Task cycle time in nanoseconds."),
                new StepParameterContract("AmsPort", "int", true, "Assigned AMS port."),
                new StepParameterContract("IoAtBegin", "bool", false, "Optional TaskDef IoAtBegin setting.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the Task attributes match the requested values." }),

        new StepContract(
            "tsproj.clear-task-layout",
            "TwinCatTsprojMutationService.ClearTaskLayout",
            "tsproj",
            "Removes Vars and/or Image child nodes from a Task so task layout can be rebuilt deterministically.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("RemoveVars", "bool", false, "Whether to remove all Vars groups under the task.", "true"),
                new StepParameterContract("RemoveImage", "bool", false, "Whether to remove all Image entries under the task.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the selected child node categories were removed from the target Task." }),

        new StepContract(
            "tsproj.ensure-task-vars-group",
            "TwinCatTsprojMutationService.EnsureTaskVarsGroup",
            "tsproj",
            "Ensures a named Vars group exists under a Task with deterministic variable shape and addressing.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("GroupName", "string", true, "Vars group Name element value, for example Inputs."),
                new StepParameterContract("VarGrpType", "int", true, "Vars VarGrpType attribute value."),
                new StepParameterContract("InsertType", "int", true, "Vars InsertType attribute value."),
                new StepParameterContract("BaseVarName", "string", true, "Base text used for generated Var names."),
                new StepParameterContract("TypeName", "string", true, "TwinCAT variable type, for example REAL."),
                new StepParameterContract("Count", "int", true, "Number of Var nodes to generate."),
                new StepParameterContract("BitStride", "int", true, "Bit offset stride between generated Vars."),
                new StepParameterContract("ExternalAddressStride", "int", true, "ExternalAddress increment per generated Var after the first."),
                new StepParameterContract("FirstExternalAddress", "int", false, "Optional explicit first ExternalAddress value."),
                new StepParameterContract("StartIndex", "int", false, "Starting suffix index for generated Var names.", "1"),
                new StepParameterContract("ReplaceExistingGroup", "bool", false, "Whether to replace existing Vars groups with the same Name.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the target Task has the expected Vars group name, variable count, and field values." }),

        new StepContract(
            "tsproj.ensure-task-image",
            "TwinCatTsprojMutationService.EnsureTaskImage",
            "tsproj",
            "Ensures a Task Image entry exists with deterministic Id, size, and addressing attributes.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("ImageId", "int", false, "Task Image Id attribute value.", "1"),
                new StepParameterContract("AddressType", "int", false, "Task Image AddrType attribute value.", "1"),
                new StepParameterContract("ImageType", "int", false, "Task Image ImageType attribute value.", "1"),
                new StepParameterContract("SizeIn", "int", false, "Task Image input byte size.", "40"),
                new StepParameterContract("SizeOut", "int", false, "Task Image output byte size.", "10"),
                new StepParameterContract("ImageName", "string", false, "Task Image Name child value.", "Image"),
                new StepParameterContract("IoAtBegin", "bool", false, "Optional Task IoAtBegin attribute update."),
                new StepParameterContract("ReplaceExistingImage", "bool", false, "Whether existing Task Image nodes should be replaced.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the target Task contains the expected Image attributes and Name value." }),

        new StepContract(
            "tsproj.ensure-cpp-instance",
            "TwinCatTsprojMutationService.EnsureCppInstance",
            "tsproj",
            "Ensures a named C++ Instance node exists under Cpp/Project and carries a minimal TmcDesc skeleton.",
            new[] { "The target C++ Project must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("CppProjectName", "string", true, "C++ project node name under Cpp."),
                new StepParameterContract("InstanceName", "string", true, "C++ instance display name."),
                new StepParameterContract("ObjectId", "string", true, "Instance OTCID/ObjectId, for example #x02010010."),
                new StepParameterContract("ContextId", "int", false, "Context Id value used for lookup/create.", "1"),
                new StepParameterContract("ContextName", "string", false, "Context Name value to enforce.", "FallbackCtx"),
                new StepParameterContract("Priority", "int", false, "ManualConfig/Priority value used for skeleton.", "0"),
                new StepParameterContract("CycleTimeNs", "int", false, "ManualConfig/CycleTime value used for skeleton.", "0")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify Cpp/Project/Instance plus TmcDesc/Contexts/ParameterValues/InterfacePointerValues/DataPointerValues exist." }),

        new StepContract(
            "tsproj.ensure-plc-instance",
            "TwinCatTsprojMutationService.EnsurePlcInstance",
            "tsproj",
            "Ensures Plc/Project/Instance nodes exist for a named PLC project and instance.",
            new[] { "The target .tsproj must already exist on disk." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify Plc/Project(Name)/Instance(Name) exists for the requested names." }),

        new StepContract(
            "tsproj.bind-instance-context",
            "TwinCatTsprojMutationService.BindInstanceContext",
            "tsproj",
            "Writes Instance TmcDesc Context/ManualConfig binding with configurable context id/name and CyclicCaller behavior.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("TaskObjectId", "string", true, "Task OTCID, for example #x02010020."),
                new StepParameterContract("Priority", "int", true, "Context priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "Context cycle time in nanoseconds."),
                new StepParameterContract("ContextId", "int", false, "Context Id value used for lookup/create.", "1"),
                new StepParameterContract("ContextName", "string", false, "Optional Context Name value to enforce."),
                new StepParameterContract("IncludeCyclicCaller", "bool", false, "Whether to ensure InterfacePointerValues/CyclicCaller exists.", "true"),
                new StepParameterContract("RemoveCyclicCallerWhenExcluded", "bool", false, "Whether to remove CyclicCaller when IncludeCyclicCaller is false.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm Context Id/ManualConfig/OTCID plus optional CyclicCaller state." }),

        new StepContract(
            "tsproj.set-task-affinity",
            "TwinCatTsprojMutationService.SetTaskAffinity",
            "tsproj",
            "Sets Task Affinity and AdtTasks attributes for scheduler placement-sensitive workloads.",
            new[] { "The target Task node must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("Affinity", "string", true, "TwinCAT affinity mask or list, for example 1 or 0,2."),
                new StepParameterContract("EnableAdtTasks", "bool", false, "Whether to force AdtTasks=true on the task.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the Task node includes Affinity and AdtTasks attributes." }),

        new StepContract(
            "tsproj.set-plc-project-properties",
            "TwinCatTsprojMutationService.SetPlcProjectProperties",
            "tsproj",
            "Updates Plc/Project attribute-level properties such as project paths, reload behavior, AMS port, and archive settings.",
            new[] { "The target PLC Project must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("ProjectFilePath", "string", false, "Value for the PrjFilePath attribute."),
                new StepParameterContract("TmcFilePath", "string", false, "Value for the TmcFilePath attribute."),
                new StepParameterContract("ReloadTmc", "bool", false, "Optional ReloadTmc attribute value."),
                new StepParameterContract("AmsPort", "int", false, "Optional PLC project AmsPort attribute value."),
                new StepParameterContract("FileArchiveSettings", "string", false, "Optional FileArchiveSettings attribute value, for example #x0002.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the PLC Project attribute values were applied as requested." }),

        new StepContract(
            "tsproj.set-plc-instance-metadata",
            "TwinCatTsprojMutationService.SetPlcInstanceMetadata",
            "tsproj",
            "Updates Plc Instance metadata attributes and optional CLSID/ClassFactory fields without replacing unrelated nodes.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("TcSmClass", "string", false, "Optional TcSmClass attribute value."),
                new StepParameterContract("TmcPath", "string", false, "Optional TmcPath attribute value."),
                new StepParameterContract("KeepUnrestoredLinks", "string", false, "Optional KeepUnrestoredLinks attribute value."),
                new StepParameterContract("Clsid", "string", false, "Optional CLSID element value."),
                new StepParameterContract("ClassFactory", "string", false, "Optional ClassFactory attribute on CLSID.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify PLC instance attributes and CLSID metadata values." }),

        new StepContract(
            "tsproj.clear-plc-instance-vars",
            "TwinCatTsprojMutationService.ClearPlcInstanceVars",
            "tsproj",
            "Removes all Vars groups under a PLC Instance node so PLC variable layout can be rebuilt deterministically.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the PLC instance contains no Vars child elements." }),

        new StepContract(
            "tsproj.ensure-plc-instance-vars-group",
            "TwinCatTsprojMutationService.EnsurePlcInstanceVarsGroup",
            "tsproj",
            "Ensures a named Vars group exists under a PLC Instance with deterministic Var definitions.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("GroupName", "string", true, "Vars group Name value, for example PlcTask Outputs."),
                new StepParameterContract("VarGrpType", "int", true, "Vars VarGrpType attribute value."),
                new StepParameterContract("InsertType", "int", false, "Vars InsertType attribute value.", "1"),
                new StepParameterContract("AreaNo", "int", false, "Optional Vars AreaNo attribute value."),
                new StepParameterContract("Variables", "IReadOnlyList<PlcInstanceVarItem>", false, "Variable definitions written under Vars/Var."),
                new StepParameterContract("ReplaceExistingGroup", "bool", false, "Whether to replace an existing Vars group with the same Name.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the PLC instance Vars group attributes and Var entries." }),

        new StepContract(
            "tsproj.clear-plc-init-symbols",
            "TwinCatTsprojMutationService.ClearPlcInitSymbols",
            "tsproj",
            "Clears all InitSymbol entries under a PLC Instance InitSymbols section.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Whether empty InitSymbols container should be removed.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the PLC instance has no InitSymbol entries." }),

        new StepContract(
            "tsproj.clear-plc-task-pou-oids",
            "TwinCatTsprojMutationService.ClearPlcTaskPouOids",
            "tsproj",
            "Clears all TaskPouOid entries under a PLC Instance TaskPouOids section.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Whether empty TaskPouOids container should be removed.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the PLC instance has no TaskPouOid entries." }),

        new StepContract(
            "tsproj.clear-mappings",
            "TwinCatTsprojMutationService.ClearMappings",
            "tsproj",
            "Removes all root-level Mappings sections so mapping links can be rebuilt from a known empty state.",
            new[] { "The target .tsproj must already exist on disk." },
            Array.Empty<StepParameterContract>(),
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify root-level Mappings nodes are absent." }),

        new StepContract(
            "tsproj.clear-unrestored-var-links",
            "TwinCatTsprojMutationService.ClearUnrestoredVarLinks",
            "tsproj",
            "Removes stale UnrestoredVarLinks blocks so unresolved TwinCAT links do not survive into activation.",
            new[] { "The target .tsproj must already exist on disk." },
            Array.Empty<StepParameterContract>(),
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify no UnrestoredVarLinks nodes remain before rebuilding mappings." }),

        new StepContract(
            "tsproj.replace-mappings-section",
            "TwinCatTsprojMutationService.ReplaceMappingsSection",
            "tsproj",
            "Replaces the root-level Mappings section with a caller-provided fragment.",
            new[] { "The target .tsproj must already exist on disk.", "The fragment must be a valid Mappings element." },
            new[]
            {
                new StepParameterContract("MappingsXml", "string", true, "Mappings XML fragment to write, rooted at <Mappings>.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and compare the Mappings section against the requested fragment." }),

        new StepContract(
            "tsproj.replace-project-io-section",
            "TwinCatTsprojMutationService.ReplaceProjectIoSection",
            "tsproj",
            "Replaces the root Project/Io section with a caller-provided Io fragment.",
            new[] { "A root-level Project node must exist in the .tsproj.", "The fragment must be a valid Io element." },
            new[]
            {
                new StepParameterContract("IoXml", "string", true, "Io XML fragment to write, rooted at <Io>.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the root Project contains the requested Io section." }),

        new StepContract(
            "tsproj.replace-data-types-section",
            "TwinCatTsprojMutationService.ReplaceDataTypesSection",
            "tsproj",
            "Replaces the root DataTypes section with a caller-provided fragment.",
            new[] { "The target .tsproj must already exist on disk.", "The fragment must be a valid DataTypes element." },
            new[]
            {
                new StepParameterContract("DataTypesXml", "string", true, "DataTypes XML fragment to write, rooted at <DataTypes>."),
                new StepParameterContract("InsertBeforeProject", "bool", false, "Whether to place DataTypes before the root Project element when present.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify DataTypes appears in the expected root position with requested content." }),

        new StepContract(
            "tsproj.replace-system-settings-section",
            "TwinCatTsprojMutationService.ReplaceSystemSettingsSection",
            "tsproj",
            "Replaces System/Settings with a caller-provided Settings fragment while preserving other System children.",
            new[] { "A System node must exist in the .tsproj.", "The fragment must be a valid Settings element." },
            new[]
            {
                new StepParameterContract("SettingsXml", "string", true, "Settings XML fragment to write, rooted at <Settings>."),
                new StepParameterContract("InsertBeforeTasks", "bool", false, "Whether to insert Settings before System/Tasks when Tasks exists.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify System/Settings content and insertion position." }),

        new StepContract(
            "tsproj.clear-instance-parameter-values",
            "TwinCatTsprojMutationService.ClearInstanceParameterValues",
            "tsproj",
            "Clears ParameterValues under a C++ instance TmcDesc before applying a deterministic parameter plan.",
            new[] { "The referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Remove the ParameterValues container instead of leaving it empty.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm stale ParameterValues entries are gone before new values are applied." }),

        new StepContract(
            "tsproj.apply-instance-parameter-plan",
            "TwinCatTsprojMutationService.ApplyInstanceParameterPlan",
            "tsproj",
            "Applies a batch of instance parameter writes in one deterministic .tsproj mutation pass.",
            new[] { "Each referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("Items", "IReadOnlyList<InstanceParameterMutation>", true, "Batch entries containing InstanceName, ParameterName, and optional Value/Enum/String payloads.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm each target instance received the expected ParameterValues updates." }),

        new StepContract(
            "tsproj.clear-instance-data-pointer-values",
            "TwinCatTsprojMutationService.ClearInstanceDataPointerValues",
            "tsproj",
            "Clears DataPointerValues under a C++ instance TmcDesc before activation or before applying a known-good data pointer plan.",
            new[] { "The referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("RemoveContainerWhenEmpty", "bool", false, "Remove the DataPointerValues container instead of leaving it empty.", "false")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm stale DataPointerValues entries are gone before activation." }),

        new StepContract(
            "tsproj.apply-instance-interface-pointer-plan",
            "TwinCatTsprojMutationService.ApplyInstanceInterfacePointerPlan",
            "tsproj",
            "Applies a batch of instance interface pointer writes in one deterministic .tsproj mutation pass.",
            new[] { "Each referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("Items", "IReadOnlyList<InstanceInterfacePointerMutation>", true, "Batch entries containing InstanceName, PointerName, and ObjectId.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify InterfacePointerValues entries for each requested instance/pointer pair." }),

        new StepContract(
            "tsproj.apply-instance-data-pointer-plan",
            "TwinCatTsprojMutationService.ApplyInstanceDataPointerPlan",
            "tsproj",
            "Applies a batch of instance data pointer writes in one deterministic .tsproj mutation pass.",
            new[] { "Each referenced instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("Items", "IReadOnlyList<InstanceDataPointerMutation>", true, "Batch entries containing InstanceName, PointerName, ObjectId, AreaNo, ByteOffset, and ByteSize.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify DataPointerValues entries for each requested instance/pointer pair." }),

        new StepContract(
            "tsproj.ensure-io-task-image",
            "TwinCatTsprojMutationService.EnsureIoTaskImage",
            "tsproj",
            "Ensures IO Task Image structure on a task and binds the instance IoTaskImage pointer to the derived or specified Image ObjectId.",
            new[] { "The target Task and instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("TaskName", "string", true, "Task node name."),
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("ImageId", "int", false, "Task Image Id used to derive the default ObjectId.", "1"),
                new StepParameterContract("SizeIn", "int", false, "Task Image input size in bytes.", "40"),
                new StepParameterContract("SizeOut", "int", false, "Task Image output size in bytes.", "10"),
                new StepParameterContract("PointerName", "string", false, "Interface pointer name to bind on the instance.", "IoTaskImage"),
                new StepParameterContract("EnsureDefaultTaskVariables", "bool", false, "Whether to regenerate default Inputs/Outputs Vars groups.", "true"),
                new StepParameterContract("InputRealCount", "int", false, "Default input REAL variable count.", "10"),
                new StepParameterContract("OutputByteCount", "int", false, "Default output BYTE variable count.", "10"),
                new StepParameterContract("IoAtBegin", "bool", false, "Whether to set Task IoAtBegin.", "true"),
                new StepParameterContract("ImageObjectId", "string", false, "Optional explicit image ObjectId override.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm Task Image plus instance InterfacePointerValues/IoTaskImage were updated." }),

        new StepContract(
            "tsproj.bind-instance-task",
            "TwinCatTsprojMutationService.BindInstanceToTask",
            "tsproj",
            "Writes ManualConfig/OTCID and context timing values back into an instance TmcDesc, optionally also updating CyclicCaller.",
            new[] { "The target .tsproj must already exist on disk.", "The task ObjectId must already be known." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("TaskObjectId", "string", true, "Task OTCID, for example #x02010010."),
                new StepParameterContract("Priority", "int", true, "Context priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "Context cycle time in nanoseconds."),
                new StepParameterContract("IncludeCyclicCaller", "bool", false, "Whether to also bind InterfacePointerValues/CyclicCaller.", "true")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj or produce instance XML again and confirm OTCID/CycleTime/Priority are present." }),

        new StepContract(
            "tsproj.bind-plc-instance-task",
            "TwinCatTsprojMutationService.BindPlcInstanceToTask",
            "tsproj",
            "Writes PLC instance Context/ManualConfig binding so PLC execution context tracks an explicit task object id.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("PlcTaskName", "string", true, "PLC task name written into Context/Name."),
                new StepParameterContract("TaskObjectId", "string", true, "Task OTCID, for example #x02010020."),
                new StepParameterContract("Priority", "int", true, "PLC context priority."),
                new StepParameterContract("CycleTimeNs", "int", true, "PLC context cycle time in nanoseconds."),
                new StepParameterContract("ContextId", "int", false, "PLC context Id value.", "0")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the PLC instance Context/ManualConfig contains OTCID/Priority/CycleTime." }),

        new StepContract(
            "tsproj.ensure-task-pou-oid",
            "TwinCatTsprojMutationService.EnsureTaskPouOid",
            "tsproj",
            "Ensures a PLC TaskPouOid entry exists with the requested priority and optional OTCID.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("Priority", "int", true, "TaskPouOid Prio attribute value."),
                new StepParameterContract("ObjectId", "string", false, "Optional TaskPouOid OTCID value.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm TaskPouOids/TaskPouOid has the requested Prio and OTCID." }),

        new StepContract(
            "tsproj.ensure-init-symbol",
            "TwinCatTsprojMutationService.EnsureInitSymbol",
            "tsproj",
            "Ensures a PLC InitSymbol exists and writes Data from the provided ObjectId using TwinCAT little-endian encoding.",
            new[] { "The target PLC Project and PLC Instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("PlcProjectName", "string", true, "PLC project node name under Plc."),
                new StepParameterContract("PlcInstanceName", "string", true, "PLC instance display name."),
                new StepParameterContract("SymbolName", "string", true, "InitSymbol name, for example MAIN.fbStateMachine.oidInstance."),
                new StepParameterContract("ObjectId", "string", true, "ObjectId used to produce InitSymbol Data, for example #x02010010."),
                new StepParameterContract("TypeName", "string", false, "InitSymbol Type element value.", "OTCID"),
                new StepParameterContract("TypeGuid", "string", false, "GUID attribute written on InitSymbol Type.", "{18071995-0000-0000-0000-00000000000F}"),
                new StepParameterContract("AreaNo", "string", false, "InitSymbol AreaNo value.", "#x00000003")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm InitSymbols contains the requested symbol name/type/area/data." }),

        new StepContract(
            "tsproj.ensure-parameter",
            "TwinCatTsprojMutationService.EnsureParameterValue",
            "tsproj",
            "Upserts a parameter default under TmcDesc/ParameterValues for an instance.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("ParameterName", "string", true, "Parameter name."),
                new StepParameterContract("ValueText", "string", false, "Scalar value text."),
                new StepParameterContract("EnumText", "string", false, "Enum value text."),
                new StepParameterContract("StringText", "string", false, "String value text.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the Value node now exists with the expected child elements." }),

        new StepContract(
            "tsproj.ensure-interface-pointer",
            "TwinCatTsprojMutationService.EnsureInterfacePointerValue",
            "tsproj",
            "Writes or updates an InterfacePointerValues entry beneath an instance TmcDesc.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("PointerName", "string", true, "Interface pointer field name."),
                new StepParameterContract("ObjectId", "string", true, "Referenced OTCID.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the named InterfacePointerValues/Value entry contains the OTCID." }),

        new StepContract(
            "tsproj.ensure-data-pointer",
            "TwinCatTsprojMutationService.EnsureDataPointerValue",
            "tsproj",
            "Writes or updates a DataPointerValues entry beneath an instance TmcDesc.",
            new[] { "The target instance must already exist in the .tsproj." },
            new[]
            {
                new StepParameterContract("InstanceName", "string", true, "Exact TwinCAT instance display name."),
                new StepParameterContract("PointerName", "string", true, "Data pointer field name."),
                new StepParameterContract("ObjectId", "string", true, "Referenced OTCID."),
                new StepParameterContract("AreaNo", "int", true, "TwinCAT data area number."),
                new StepParameterContract("ByteOffset", "int", true, "Offset inside the area."),
                new StepParameterContract("ByteSize", "int", true, "Byte width of the pointed segment.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm the named DataPointerValues/Value entry matches the requested offsets." }),

        new StepContract(
            "tsproj.ensure-mapping-link",
            "TwinCatTsprojMutationService.EnsureMappingLink",
            "tsproj",
            "Ensures a deterministic Mapping OwnerA/OwnerB/Link triple exists without replacing unrelated mappings.",
            new[] { "Owner names and variable names must match TwinCAT path/value expectations." },
            new[]
            {
                new StepParameterContract("OwnerAName", "string", true, "OwnerA Name attribute, for example TIXC^Demo^ObjA."),
                new StepParameterContract("OwnerBName", "string", true, "OwnerB Name attribute, for example TIXC^Demo^ObjB."),
                new StepParameterContract("VarA", "string", true, "VarA attribute value."),
                new StepParameterContract("VarB", "string", true, "VarB attribute value.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and confirm Mappings contains the requested OwnerA/OwnerB/Link entry." }),

        new StepContract(
            "tsproj.upsert-element",
            "TwinCatTsprojMutationService.UpsertElement",
            "tsproj",
            "Upserts a generic XML element under a path-based .tsproj parent using a declared conflict policy.",
            new[] { "The target .tsproj must already exist on disk.", "Prefer dedicated tsproj primitives when one exists for the same intent." },
            new[]
            {
                new StepParameterContract("ParentPath", "IReadOnlyList<TsprojPathSegment>", true, "Path from root to the parent element, optionally matching <Name> values."),
                new StepParameterContract("ElementName", "string", true, "Element local name to insert or update."),
                new StepParameterContract("MatchNameValue", "string", false, "Optional <Name> child value used to match an existing element."),
                new StepParameterContract("Attributes", "IReadOnlyList<TsprojXmlAttribute>", false, "Attributes to write on the target element."),
                new StepParameterContract("ChildValues", "IReadOnlyList<TsprojXmlChildValue>", false, "Simple child element values to write under the target element."),
                new StepParameterContract("ConflictPolicy", "TsprojMutationConflictPolicy", false, "ReplaceExisting, KeepExisting, or FailOnConflict.", "ReplaceExisting")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify the resolved parent contains exactly the intended element shape." }),

        new StepContract(
            "tsproj.upsert-fragment",
            "TwinCatTsprojMutationService.UpsertFragment",
            "tsproj",
            "Upserts a generic XML fragment under a path-based .tsproj parent using a declared conflict policy.",
            new[] { "The target .tsproj must already exist on disk.", "The fragment must already be shaped like Beckhoff expects." },
            new[]
            {
                new StepParameterContract("ParentPath", "IReadOnlyList<TsprojPathSegment>", true, "Path from root to the parent element, optionally matching <Name> values."),
                new StepParameterContract("FragmentXml", "string", true, "XML fragment to insert or update."),
                new StepParameterContract("MatchElementName", "string", false, "Local element name used to match an existing sibling."),
                new StepParameterContract("MatchNameValue", "string", false, "Optional <Name> value used to match an existing sibling."),
                new StepParameterContract("ConflictPolicy", "TsprojMutationConflictPolicy", false, "ReplaceExisting, KeepExisting, or FailOnConflict.", "ReplaceExisting")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and compare the resolved parent before/after the fragment upsert." }),

        new StepContract(
            "tsproj.apply-mutation-plan",
            "TwinCatTsprojMutationService.ApplyMutationPlan",
            "tsproj",
            "Applies generic element and fragment upserts in one deterministic .tsproj mutation pass.",
            new[] { "The target .tsproj must already exist on disk.", "Use this for JSON-owned low-level mutations that do not yet have a dedicated primitive." },
            new[]
            {
                new StepParameterContract("ElementUpserts", "IReadOnlyList<TsprojElementUpsertRequest>", false, "Element upserts to apply in order."),
                new StepParameterContract("FragmentUpserts", "IReadOnlyList<TsprojFragmentUpsertRequest>", false, "Fragment upserts to apply in order.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and verify all requested low-level mutations landed; FailOnConflict aborts the plan." }),

        new StepContract(
            "tsproj.merge-fragment",
            "TwinCatTsprojMutationService.MergeNamedElementFragment",
            "tsproj",
            "Merges a captured XML fragment into a named container, acting as the escape hatch only for remaining TwinCAT XML gaps without a dedicated primitive.",
            new[]
            {
                "The target .tsproj must already exist on disk.",
                "Use a dedicated tsproj primitive instead when one exists for the same intent.",
                "The fragment must come from a known-good .tsproj source, with documented target parent path, field meanings, source evidence, and test or real-machine evidence.",
                "The target parent local name must be unambiguous in the .tsproj."
            },
            new[]
            {
                new StepParameterContract("ParentElementName", "string", true, "Container local name inside the .tsproj."),
                new StepParameterContract("FragmentXml", "string", true, "XML fragment to merge."),
                new StepParameterContract("MatchElementName", "string", false, "Local element name used to find and replace an existing sibling."),
                new StepParameterContract("MatchNameValue", "string", false, "Optional <Name> value used to find the existing sibling."),
                new StepParameterContract("ReplaceExisting", "bool", false, "Whether a matched sibling should be replaced.", "true"),
                new StepParameterContract("FragmentSource", "string", true, "Known-good .tsproj/evidence source for the fragment."),
                new StepParameterContract("TargetParentPath", "string", true, "Human-readable target parent path/layer in the .tsproj."),
                new StepParameterContract("FieldMeaning", "string", true, "Short explanation of the fragment fields and why they are required."),
                new StepParameterContract("VerificationEvidence", "string", true, "Test, XAE reopen/build/activation, or dated evidence proving this fragment is safe enough to use.")
            },
            Array.Empty<StepOutputContract>(),
            new[] { "Re-open the .tsproj and compare the target container before/after the merge.", "For engineering behavior, follow with XAE reopen/build/activation or a dated evidence note explaining why runtime proof is not available." }),

        new StepContract(
            "validation.ads-scan",
            "AdsValidationService.ScanPorts",
            "validation",
            "Scans ADS target ports and reports whether the runtime endpoint is reachable before symbol-level ADS reads are attempted.",
            new[] { "TwinCAT runtime or router must be installed on the target machine.", "Use this before symbol reads when activation or ADS routing is uncertain." },
            new[]
            {
                new StepParameterContract("NetId", "string", false, "AMS NetId, or local for the local router.", "local"),
                new StepParameterContract("Ports", "int[]", false, "Comma-separated ADS ports to probe.", "100,200,300,800,851,852,10000")
            },
            new[]
            {
                new StepOutputContract("openPortCount", "int", "Number of ports with readable ADS state."),
                new StepOutputContract("failedPortCount", "int", "Number of ports that failed to connect or read state."),
                new StepOutputContract("portsJson", "json", "Per-port ADS state or error message.")
            },
            new[] { "If 100 or 10000 are not reachable, treat later PLC symbol read failures as runtime/router/permission failures rather than symbol-path failures." }),

        new StepContract(
            "validation.ads-read",
            "AdsValidationService.Read",
            "validation",
            "Reads a PLC or TcCOM symbol over ADS so the engineering pipeline can close the loop with a runtime assertion.",
            new[] { "The target runtime must be reachable over ADS.", "The symbol path and AMS endpoint must already be known." },
            new[]
            {
                new StepParameterContract("NetId", "string", true, "AMS NetId, for example 127.0.0.1.1.1."),
                new StepParameterContract("Port", "int", true, "ADS port, for example 851."),
                new StepParameterContract("SymbolPath", "string", true, "Fully qualified PLC or TcCOM symbol name."),
                new StepParameterContract("DataType", "AdsReadDataType", true, "Expected runtime data type."),
                new StepParameterContract("AutoReconnect", "bool", false, "Whether to force a TwinCAT reconnect attempt before reading.", "false")
            },
            new[]
            {
                new StepOutputContract("value", "string", "Stringified ADS value.")
            },
            new[] { "Treat a successful read plus a domain assertion on the returned value as the runtime acceptance check." }),

        new StepContract(
            "validation.ads-read-symbols",
            "AdsValidationService.ReadSymbols",
            "validation",
            "Reads multiple PLC or TcCOM symbols over ADS and prints their values as a runtime validation checkpoint.",
            new[] { "The target runtime must be reachable over ADS.", "The symbol paths, data types, and AMS endpoint must already be known." },
            new[]
            {
                new StepParameterContract("NetId", "string", true, "AMS NetId, for example 127.0.0.1.1.1."),
                new StepParameterContract("Port", "int", true, "ADS port, for example 851."),
                new StepParameterContract("Symbols", "IReadOnlyList<AdsReadSymbolRequest>", true, "Symbols to read; direct CLI accepts --symbols=MAIN.nSeed:UInt32;MAIN.bPipelineOk:Boolean or --json-file."),
                new StepParameterContract("AutoReconnect", "bool", false, "Whether to force a TwinCAT reconnect attempt before reading.", "false"),
                new StepParameterContract("ContinueOnError", "bool", false, "Whether the step should succeed when one or more symbols fail.", "false")
            },
            new[]
            {
                new StepOutputContract("succeededCount", "int", "Number of symbols read successfully."),
                new StepOutputContract("failedCount", "int", "Number of symbols that failed to read."),
                new StepOutputContract("valuesText", "string", "Human-readable symbol=value list printed in the step result."),
                new StepOutputContract("valuesJson", "json", "Per-symbol ADS read results including data types and errors.")
            },
            new[] { "Use after activation and ADS port scan; treat the printed values plus expected domain assertions as runtime proof." })
    };

    private static readonly IReadOnlyDictionary<string, StepContract> Lookup =
        ContractsInternal.ToDictionary(item => item.Kind, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<StepContract> All => ContractsInternal;

    public static IReadOnlyList<string> RecommendedExecutionOrder { get; } = new[]
    {
        "engineering.launch-visual-studio",
        "engineering.create-xae-solution",
        "engineering.create-cpp-project",
        "engineering.create-vs-cpp-project",
        "engineering.ensure-solution-project-dependency",
        "cpp.remove-project-item",
        "cpp.create-project-item",
        "cpp.write-project-item-content",
        "cpp.set-project-property",
        "cpp.set-item-definition-property",
        "cpp.set-project-item-metadata",
        "engineering.create-module",
        "engineering.publish-modules",
        "engineering.add-module-instance",
        "engineering.ensure-task",
        "engineering.export-tree-item-xml",
        "engineering.save-all",
        "engineering.close-visual-studio",
        "tsproj.ensure-task",
        "tsproj.clear-task-layout",
        "tsproj.ensure-task-vars-group",
        "tsproj.ensure-task-image",
        "tsproj.ensure-cpp-instance",
        "tsproj.ensure-plc-instance",
        "tsproj.bind-instance-context",
        "tsproj.set-task-affinity",
        "tsproj.set-plc-project-properties",
        "tsproj.set-plc-instance-metadata",
        "tsproj.clear-plc-instance-vars",
        "tsproj.ensure-plc-instance-vars-group",
        "tsproj.clear-plc-task-pou-oids",
        "tsproj.clear-plc-init-symbols",
        "tsproj.clear-mappings",
        "tsproj.clear-unrestored-var-links",
        "tsproj.replace-mappings-section",
        "tsproj.replace-project-io-section",
        "tsproj.replace-data-types-section",
        "tsproj.replace-system-settings-section",
        "tsproj.ensure-io-task-image",
        "tsproj.bind-instance-task",
        "tsproj.bind-plc-instance-task",
        "tsproj.ensure-task-pou-oid",
        "tsproj.ensure-init-symbol",
        "tsproj.clear-instance-parameter-values",
        "tsproj.clear-instance-data-pointer-values",
        "tsproj.apply-instance-parameter-plan",
        "tsproj.apply-instance-interface-pointer-plan",
        "tsproj.apply-instance-data-pointer-plan",
        "tsproj.ensure-parameter",
        "tsproj.ensure-interface-pointer",
        "tsproj.ensure-data-pointer",
        "tsproj.ensure-mapping-link",
        "tsproj.upsert-element",
        "tsproj.upsert-fragment",
        "tsproj.apply-mutation-plan",
        "tsproj.merge-fragment",
        "engineering.open-xae-solution",
        "engineering.build-solution",
        "signing.grant-certificate",
        "signing.set-license",
        "signing.sign-twincat-binary",
        "signing.verify-twincat-binary",
        "engineering.activate-configuration",
        "validation.ads-scan",
        "validation.ads-read",
        "validation.ads-read-symbols"
    };

    public static StepContract Require(string kind)
    {
        if (!Lookup.TryGetValue(kind, out StepContract? contract))
        {
            throw new KeyNotFoundException($"Unknown TwinCatAutomationKit contract '{kind}'.");
        }

        return contract;
    }
}
