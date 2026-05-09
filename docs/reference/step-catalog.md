# TwinCatAutomationKit Step Catalog / 步骤接口索引

English summary: This generated Markdown file is the human-readable companion to `step-catalog.json`; both files must be regenerated together from `TwinCatStepCatalog.cs`.

这个文件是自动生成的人类可读 step 接口索引，列出每个 CLI step 的名字、对应方法、输入、输出和验证要求。不要手改本文件；要改内容请修改 `TwinCatStepCatalog.cs` 或文档生成器后运行 `generate-docs`。

`step-catalog.json` 是同一份 step 接口规范的机器可读版本，供调用 agent 和工具读取；它不是一个完整 JSON plan 示例。完整 plan 写法看 `docs/cli/json-plan.md`，完整例子看 `examples/json-plans/*.json`。

English API text is intentionally preserved for stability. 中文标签只帮助阅读和审核。

## 阅读方式 / Reading Guide

- 人类审查优先看下面的分类简表，再进入具体 step 详情。
- Agent 或工具默认读取 `step-catalog.json`；如果需要解释性上下文，再读本 Markdown，但不要把两份文件当成两个独立来源。
- `step-catalog.json` 和本文件必须由同一次 `generate-docs` 生成；如果只改了其中一个，视为文档不同步。
- 需要改 step 接口时，先改 `TwinCatStepCatalog.cs`，必要时改 `DocumentationSuiteWriter.cs`，然后重新生成整个 `docs/reference`。

## 分类简表 / Category Overview

| Category | Step kind | Summary |
|---|---|---|
| `cpp` | `cpp.create-project-item` | Creates a C++/header/resource/None project item, optionally creating the physical file and registering it in .vcxproj and .filters. |
| `cpp` | `cpp.remove-project-item` | Removes a C++ project item registration and optional filter entry and physical file. |
| `cpp` | `cpp.set-item-definition-property` | Sets a C++ ItemDefinitionGroup tool property such as ClCompile include paths or Link dependencies. |
| `cpp` | `cpp.set-project-item-metadata` | Sets file-level MSBuild metadata for a C++ project item, such as PrecompiledHeader or ExcludedFromBuild. |
| `cpp` | `cpp.set-project-property` | Sets a project-level or configuration-level MSBuild property in a C++ .vcxproj. |
| `cpp` | `cpp.write-project-item-content` | Writes source/resource/text payload into an existing C++ project item file and returns its content hash. |
| `engineering` | `engineering.activate-configuration` | Saves the current configuration archive when possible and then activates TwinCAT via ITcSysManager or DTE command fallback. |
| `engineering` | `engineering.add-module-instance` | Adds a TcCOM instance from a project TMC by resolving the module GUID and calling CreateChild on the project node. |
| `engineering` | `engineering.build-solution` | Runs SolutionBuild.Build and waits until the DTE build state reaches done. |
| `engineering` | `engineering.close-visual-studio` | Closes the current Visual Studio DTE session, optionally issuing SaveAll first. |
| `engineering` | `engineering.create-cpp-project` | Creates a TwinCAT C++ project beneath TIXC using a specified Beckhoff wizard id. |
| `engineering` | `engineering.create-module` | Creates a module class inside a TwinCAT C++ project via the module wizard id. |
| `engineering` | `engineering.create-plc-project` | Creates a PLC project using the first compatible Beckhoff PLC template available on the machine. |
| `engineering` | `engineering.create-vs-cpp-project` | Creates a regular Visual Studio C++ project inside the current solution, such as an AdsClient console application. |
| `engineering` | `engineering.create-xae-solution` | Creates a fresh TwinCAT XAE solution and binds the automation session to its ITcSysManager root. |
| `engineering` | `engineering.ensure-solution-project-dependency` | Ensures a Visual Studio solution ProjectDependencies entry exists from one project to another. |
| `engineering` | `engineering.ensure-task` | Creates or reuses a Task under TIRT and normalizes its timing, priority, and AMS port. |
| `engineering` | `engineering.export-tree-item-xml` | Exports ProduceXml output for any TwinCAT tree node as durable evidence. |
| `engineering` | `engineering.launch-visual-studio` | Starts a new DTE session that later steps can use for XAE creation, build, and activation. |
| `engineering` | `engineering.open-xae-solution` | Re-opens an existing TwinCAT solution and re-attaches COM references after .tsproj file mutations. |
| `engineering` | `engineering.publish-modules` | Invokes the TwinCAT C++ project PublishModules method so updated module source regenerates TMC metadata. |
| `engineering` | `engineering.save-all` | Flushes pending Visual Studio document and solution changes before build, reopen, or .tsproj file mutation steps. |
| `signing` | `signing.grant-certificate` | Grants or removes local TcSignTool authorization for a TwinCAT signing certificate. |
| `signing` | `signing.set-license` | Writes TwinCAT C++ project signing license settings used by MSBuild/TcSignTool. |
| `signing` | `signing.sign-twincat-binary` | Signs one or more built TwinCAT C++ binaries with Beckhoff TcSignTool. |
| `signing` | `signing.verify-twincat-binary` | Verifies that one or more TwinCAT binaries carry a valid TcSignTool signature. |
| `tsproj` | `tsproj.apply-instance-data-pointer-plan` | Applies a batch of instance data pointer writes in one deterministic .tsproj mutation pass. |
| `tsproj` | `tsproj.apply-instance-interface-pointer-plan` | Applies a batch of instance interface pointer writes in one deterministic .tsproj mutation pass. |
| `tsproj` | `tsproj.apply-instance-parameter-plan` | Applies a batch of instance parameter writes in one deterministic .tsproj mutation pass. |
| `tsproj` | `tsproj.apply-mutation-plan` | Applies generic element and fragment upserts in one deterministic .tsproj mutation pass. |
| `tsproj` | `tsproj.bind-instance-context` | Writes Instance TmcDesc Context/ManualConfig binding with configurable context id/name and CyclicCaller behavior. |
| `tsproj` | `tsproj.bind-instance-task` | Writes ManualConfig/OTCID and context timing values back into an instance TmcDesc, optionally also updating CyclicCaller. |
| `tsproj` | `tsproj.bind-plc-instance-task` | Writes PLC instance Context/ManualConfig binding so PLC execution context tracks an explicit task object id. |
| `tsproj` | `tsproj.clear-instance-data-pointer-values` | Clears DataPointerValues under a C++ instance TmcDesc before activation or before applying a known-good data pointer plan. |
| `tsproj` | `tsproj.clear-instance-parameter-values` | Clears ParameterValues under a C++ instance TmcDesc before applying a deterministic parameter plan. |
| `tsproj` | `tsproj.clear-mappings` | Removes all root-level Mappings sections so mapping links can be rebuilt from a known empty state. |
| `tsproj` | `tsproj.clear-plc-init-symbols` | Clears all InitSymbol entries under a PLC Instance InitSymbols section. |
| `tsproj` | `tsproj.clear-plc-instance-vars` | Removes all Vars groups under a PLC Instance node so PLC variable layout can be rebuilt deterministically. |
| `tsproj` | `tsproj.clear-plc-task-pou-oids` | Clears all TaskPouOid entries under a PLC Instance TaskPouOids section. |
| `tsproj` | `tsproj.clear-task-layout` | Removes Vars and/or Image child nodes from a Task so task layout can be rebuilt deterministically. |
| `tsproj` | `tsproj.clear-unrestored-var-links` | Removes stale UnrestoredVarLinks blocks so unresolved TwinCAT links do not survive into activation. |
| `tsproj` | `tsproj.ensure-cpp-instance` | Ensures a named C++ Instance node exists under Cpp/Project and carries a minimal TmcDesc skeleton. |
| `tsproj` | `tsproj.ensure-data-pointer` | Writes or updates a DataPointerValues entry beneath an instance TmcDesc. |
| `tsproj` | `tsproj.ensure-init-symbol` | Ensures a PLC InitSymbol exists and writes Data from the provided ObjectId using TwinCAT little-endian encoding. |
| `tsproj` | `tsproj.ensure-interface-pointer` | Writes or updates an InterfacePointerValues entry beneath an instance TmcDesc. |
| `tsproj` | `tsproj.ensure-io-task-image` | Ensures IO Task Image structure on a task and binds the instance IoTaskImage pointer to the derived or specified Image ObjectId. |
| `tsproj` | `tsproj.ensure-mapping-link` | Ensures a deterministic Mapping OwnerA/OwnerB/Link triple exists without replacing unrelated mappings. |
| `tsproj` | `tsproj.ensure-parameter` | Upserts a parameter default under TmcDesc/ParameterValues for an instance. |
| `tsproj` | `tsproj.ensure-plc-instance` | Ensures Plc/Project/Instance nodes exist for a named PLC project and instance. |
| `tsproj` | `tsproj.ensure-plc-instance-vars-group` | Ensures a named Vars group exists under a PLC Instance with deterministic Var definitions. |
| `tsproj` | `tsproj.ensure-task` | Creates or updates the declarative task node inside a .tsproj generated by XAE. |
| `tsproj` | `tsproj.ensure-task-image` | Ensures a Task Image entry exists with deterministic Id, size, and addressing attributes. |
| `tsproj` | `tsproj.ensure-task-pou-oid` | Ensures a PLC TaskPouOid entry exists with the requested priority and optional OTCID. |
| `tsproj` | `tsproj.ensure-task-vars-group` | Ensures a named Vars group exists under a Task with deterministic variable shape and addressing. |
| `tsproj` | `tsproj.merge-fragment` | Merges a captured XML fragment into a named container, acting as the escape hatch only for remaining TwinCAT XML gaps without a dedicated primitive. |
| `tsproj` | `tsproj.replace-data-types-section` | Replaces the root DataTypes section with a caller-provided fragment. |
| `tsproj` | `tsproj.replace-mappings-section` | Replaces the root-level Mappings section with a caller-provided fragment. |
| `tsproj` | `tsproj.replace-project-io-section` | Replaces the root Project/Io section with a caller-provided Io fragment. |
| `tsproj` | `tsproj.replace-system-settings-section` | Replaces System/Settings with a caller-provided Settings fragment while preserving other System children. |
| `tsproj` | `tsproj.set-plc-instance-metadata` | Updates Plc Instance metadata attributes and optional CLSID/ClassFactory fields without replacing unrelated nodes. |
| `tsproj` | `tsproj.set-plc-project-properties` | Updates Plc/Project attribute-level properties such as project paths, reload behavior, AMS port, and archive settings. |
| `tsproj` | `tsproj.set-task-affinity` | Sets Task Affinity and AdtTasks attributes for scheduler placement-sensitive workloads. |
| `tsproj` | `tsproj.upsert-element` | Upserts a generic XML element under a path-based .tsproj parent using a declared conflict policy. |
| `tsproj` | `tsproj.upsert-fragment` | Upserts a generic XML fragment under a path-based .tsproj parent using a declared conflict policy. |
| `validation` | `validation.ads-read` | Reads a PLC or TcCOM symbol over ADS so the engineering pipeline can close the loop with a runtime assertion. |
| `validation` | `validation.ads-read-symbols` | Reads multiple PLC or TcCOM symbols over ADS and prints their values as a runtime validation checkpoint. |
| `validation` | `validation.ads-scan` | Scans ADS target ports and reports whether the runtime endpoint is reachable before symbol-level ADS reads are attempted. |

## 详细接口 / Step Details

### `cpp.create-project-item`

- 方法 Method: `TwinCatEngineeringService.CreateCppProjectItem`
- 分类 Category: `cpp`
- 功能摘要 Summary: Creates a C++/header/resource/None project item, optionally creating the physical file and registering it in .vcxproj and .filters.
- 前置条件 Preconditions:
  - The target C++ .vcxproj must already exist.
  - RelativePath must be inside the project directory.
- 输入 Inputs:
  - `ProjectName` (`string`): C++ project name.
  - `RelativePath` (`string`): Path relative to the C++ project directory.
  - `ItemType` (`CppProjectItemType`): MSBuild item type, or Infer from extension. Example: `Infer`.
  - `Filter` (`string`): Optional .vcxproj.filters display filter.
  - `AddToProject` (`bool`): Whether to register the item in .vcxproj. Example: `true`.
  - `CreatePhysicalFile` (`bool`): Whether to create an empty physical file. Example: `true`.
  - `ConflictPolicy` (`ProjectItemConflictPolicy`): FailIfExists, KeepExisting, or ReplaceProjectRegistration. Example: `FailIfExists`.
  - `AllowMsBuildFallback` (`bool`): Allow typed MSBuild XML update when DTE ProjectItems is not stable. Example: `true`.
- 输出 Outputs:
  - `projectFilePath` (`string`): Updated .vcxproj path.
  - `filePath` (`string`): Physical project item path.
  - `itemType` (`CppProjectItemType`): Resolved MSBuild item type.
  - `addedToProject` (`bool`): Whether .vcxproj registration exists after the step.
- 验证 Verification:
  - Verify the file exists, .vcxproj contains the requested Include, .filters contains filter mapping when requested, and reopen shows the item.

### `cpp.remove-project-item`

- 方法 Method: `TwinCatEngineeringService.RemoveCppProjectItem`
- 分类 Category: `cpp`
- 功能摘要 Summary: Removes a C++ project item registration and optional filter entry and physical file.
- 前置条件 Preconditions:
  - The target C++ .vcxproj must already exist.
- 输入 Inputs:
  - `ProjectName` (`string`): C++ project name.
  - `RelativePath` (`string`): Path relative to the C++ project directory.
  - `ItemType` (`CppProjectItemType`): MSBuild item type, or Infer from extension. Example: `Infer`.
  - `DeletePhysicalFile` (`bool`): Whether to delete the physical file. Example: `true`.
  - `RemoveFilterEntry` (`bool`): Whether to remove the .vcxproj.filters item mapping. Example: `true`.
  - `IgnoreMissing` (`bool`): Treat missing item/file as success. Example: `false`.
- 输出 Outputs:
  - `removedFromProject` (`bool`): Whether .vcxproj registration was removed.
  - `deletedFile` (`bool`): Whether a physical file was deleted.
- 验证 Verification:
  - Verify .vcxproj/.filters no longer reference the item and the file is absent when DeletePhysicalFile=true.

### `cpp.set-item-definition-property`

- 方法 Method: `TwinCatEngineeringService.SetCppItemDefinitionProperty`
- 分类 Category: `cpp`
- 功能摘要 Summary: Sets a C++ ItemDefinitionGroup tool property such as ClCompile include paths or Link dependencies.
- 前置条件 Preconditions:
  - The target C++ .vcxproj must already exist.
- 输入 Inputs:
  - `ProjectName` (`string`): C++ project name.
  - `ToolName` (`string`): Tool element name such as ClCompile, Link, ResourceCompile, or PostBuildEvent.
  - `PropertyName` (`string`): Tool property element name.
  - `Value` (`string`): Property value, including inherited macros when required.
  - `Condition` (`string`): Optional ItemDefinitionGroup Condition.
- 输出 Outputs:
  - `projectFilePath` (`string`): Updated .vcxproj path.
  - `toolName` (`string`): Tool element name.
  - `propertyName` (`string`): Property element name.
  - `condition` (`string`): Applied condition, if any.
- 验证 Verification:
  - Inspect ItemDefinitionGroup XML and confirm build logs use include paths, library paths, language standard, dependencies, or events.

### `cpp.set-project-item-metadata`

- 方法 Method: `TwinCatEngineeringService.SetCppProjectItemMetadata`
- 分类 Category: `cpp`
- 功能摘要 Summary: Sets file-level MSBuild metadata for a C++ project item, such as PrecompiledHeader or ExcludedFromBuild.
- 前置条件 Preconditions:
  - The target item must already be registered in .vcxproj.
- 输入 Inputs:
  - `ProjectName` (`string`): C++ project name.
  - `RelativePath` (`string`): Path relative to the C++ project directory.
  - `ItemType` (`CppProjectItemType`): MSBuild item type containing the item.
  - `MetadataName` (`string`): Metadata element name.
  - `Value` (`string`): Metadata value.
  - `Condition` (`string`): Optional item Condition.
- 输出 Outputs:
  - `projectFilePath` (`string`): Updated .vcxproj path.
  - `relativePath` (`string`): Item Include path.
  - `metadataName` (`string`): Metadata element name.
  - `condition` (`string`): Applied condition, if any.
- 验证 Verification:
  - Inspect the item XML and confirm build output respects include/exclude or PCH metadata.

### `cpp.set-project-property`

- 方法 Method: `TwinCatEngineeringService.SetCppProjectProperty`
- 分类 Category: `cpp`
- 功能摘要 Summary: Sets a project-level or configuration-level MSBuild property in a C++ .vcxproj.
- 前置条件 Preconditions:
  - The target C++ .vcxproj must already exist.
- 输入 Inputs:
  - `ProjectName` (`string`): C++ project name.
  - `PropertyName` (`string`): MSBuild property element name.
  - `Value` (`string`): Property value.
  - `Condition` (`string`): Optional PropertyGroup Condition.
  - `PropertyGroupLabel` (`string`): Optional PropertyGroup Label, such as Globals or Configuration.
- 输出 Outputs:
  - `projectFilePath` (`string`): Updated .vcxproj path.
  - `propertyName` (`string`): Property name.
  - `condition` (`string`): Applied condition, if any.
- 验证 Verification:
  - Inspect .vcxproj and confirm the PropertyGroup contains the requested value; build log should reflect build-affecting properties.

### `cpp.write-project-item-content`

- 方法 Method: `TwinCatEngineeringService.WriteCppProjectItemContent`
- 分类 Category: `cpp`
- 功能摘要 Summary: Writes source/resource/text payload into an existing C++ project item file and returns its content hash.
- 前置条件 Preconditions:
  - The target C++ project must already exist.
  - ContentText and ContentFile are mutually exclusive.
- 输入 Inputs:
  - `ProjectName` (`string`): C++ project name.
  - `RelativePath` (`string`): Path relative to the C++ project directory.
  - `ContentText` (`string`): Inline source payload.
  - `ContentFile` (`string`): Payload file path produced by the JSON plan files[] section or another explicit caller-owned payload.
  - `Encoding` (`string`): utf-8, utf-8-bom, or ascii. Example: `utf-8`.
  - `NewLine` (`string`): preserve, crlf, or lf. Example: `preserve`.
  - `WritePolicy` (`ProjectItemWritePolicy`): FailIfMissing, FailIfNonEmpty, or Overwrite. Example: `Overwrite`.
  - `RequireProjectRegistration` (`bool`): Fail if .vcxproj does not already register the item. Example: `false`.
- 输出 Outputs:
  - `filePath` (`string`): Written file path.
  - `sha256` (`string`): SHA256 hash of the written bytes.
  - `bytesWritten` (`long`): Number of bytes written.
- 验证 Verification:
  - Compare the output hash with the payload and reopen the item in VS.

### `engineering.activate-configuration`

- 方法 Method: `TwinCatEngineeringService.ActivateConfiguration`
- 分类 Category: `engineering`
- 功能摘要 Summary: Saves the current configuration archive when possible and then activates TwinCAT via ITcSysManager or DTE command fallback.
- 前置条件 Preconditions:
  - A solution must be loaded and its ITcSysManager must be available.
- 输入 Inputs:
  - `SaveConfigurationArchive` (`bool`): Whether to attempt SaveConfiguration before activate. Example: `true`.
  - `ConfigurationArchivePath` (`string`): Optional override for the generated .tszip path.
- 输出 Outputs:
  - `activationCommand` (`string`): The command or fallback path used for activation.
- 验证 Verification:
  - Confirm that either ITcSysManager.ActivateConfiguration or a DTE command succeeded.

### `engineering.add-module-instance`

- 方法 Method: `TwinCatEngineeringService.AddModuleInstance`
- 分类 Category: `engineering`
- 功能摘要 Summary: Adds a TcCOM instance from a project TMC by resolving the module GUID and calling CreateChild on the project node.
- 前置条件 Preconditions:
  - The target C++ project and its .tmc file must exist.
- 输入 Inputs:
  - `ProjectName` (`string`): Owning C++ project name.
  - `ProjectTmcPath` (`string`): Absolute path to the project TMC.
  - `InstanceBaseName` (`string`): Base name to use when creating the instance.
  - `ModuleClassName` (`string`): Optional module class name when the TMC contains multiple classes.
  - `AllowOfflineFallback` (`bool`): Whether AddModuleInstance may inject a deterministic instance skeleton through .tsproj file mutation when COM CreateChild fails. Example: `true`.
- 输出 Outputs:
  - `treeItemPath` (`string`): TwinCAT tree path for the instance.
  - `objectId` (`string`): Resolved instance ObjectId / OTCID.
- 验证 Verification:
  - Export the instance XML and confirm that the expected CLSID and Name are present.

### `engineering.build-solution`

- 方法 Method: `TwinCatEngineeringService.BuildCurrentSolution`
- 分类 Category: `engineering`
- 功能摘要 Summary: Runs SolutionBuild.Build and waits until the DTE build state reaches done.
- 前置条件 Preconditions:
  - A solution must be loaded in the DTE session.
- 输入 Inputs:
  - `TimeoutMs` (`int`): Maximum build wait time. Example: `300000`.
- 输出 Outputs:
  - `lastBuildInfo` (`int`): DTE LastBuildInfo value.
- 验证 Verification:
  - Treat LastBuildInfo == 0 as the engineering success condition.

### `engineering.close-visual-studio`

- 方法 Method: `TwinCatEngineeringService.CloseVisualStudio`
- 分类 Category: `engineering`
- 功能摘要 Summary: Closes the current Visual Studio DTE session, optionally issuing SaveAll first.
- 前置条件 Preconditions:
  - A live DTE session must exist.
- 输入 Inputs:
  - `SaveBeforeClose` (`bool`): Whether SaveAll should run before closing the session. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Confirm that the DTE process exits and the session object is no longer usable.

### `engineering.create-cpp-project`

- 方法 Method: `TwinCatEngineeringService.CreateCppProject`
- 分类 Category: `engineering`
- 功能摘要 Summary: Creates a TwinCAT C++ project beneath TIXC using a specified Beckhoff wizard id.
- 前置条件 Preconditions:
  - A bound TwinCAT engineering session must exist.
- 输入 Inputs:
  - `ProjectName` (`string`): Requested C++ project name.
  - `WizardId` (`string`): Wizard identifier for the project template. Example: `TcVersionedDriverWizard`.
- 输出 Outputs:
  - `treeItemPath` (`string`): TwinCAT tree path for the C++ project.
  - `projectFilePath` (`string`): Expected .vcxproj path.
- 验证 Verification:
  - Verify the TIXC child exists and the .vcxproj file lands on disk.

### `engineering.create-module`

- 方法 Method: `TwinCatEngineeringService.CreateModule`
- 分类 Category: `engineering`
- 功能摘要 Summary: Creates a module class inside a TwinCAT C++ project via the module wizard id.
- 前置条件 Preconditions:
  - A TwinCAT C++ project must already exist.
- 输入 Inputs:
  - `ProjectName` (`string`): Owning C++ project name.
  - `ModuleName` (`string`): Requested module class name.
  - `WizardId` (`string`): Beckhoff module wizard id. Example: `TcModuleClassWizard`.
  - `AllowOfflineFallback` (`bool`): Whether CreateModule may patch module skeleton artifacts when module wizard automation fails. Example: `true`.
- 输出 Outputs:
  - `treeItemPath` (`string`): TwinCAT tree path for the created module node.
  - `moduleCppPath` (`string`): Expected .cpp path for the module.
- 验证 Verification:
  - Confirm that the module files exist and the tree node is visible after save/refresh.

### `engineering.create-plc-project`

- 方法 Method: `TwinCatEngineeringService.CreatePlcProject`
- 分类 Category: `engineering`
- 功能摘要 Summary: Creates a PLC project using the first compatible Beckhoff PLC template available on the machine.
- 前置条件 Preconditions:
  - A bound TwinCAT engineering session must exist.
- 输入 Inputs:
  - `ProjectName` (`string`): PLC project name.
  - `AllowOfflineFallback` (`bool`): Whether CreatePlcProject may synthesize a minimal PLC project file mutation when COM/template creation fails. Example: `true`.
- 输出 Outputs:
  - `treeItemPath` (`string`): TwinCAT tree path for the PLC project.
  - `projectFilePath` (`string`): Expected .plcproj path.
- 验证 Verification:
  - Check that the PLC node exists and that the .plcproj file was created.

### `engineering.create-vs-cpp-project`

- 方法 Method: `TwinCatEngineeringService.CreateVisualStudioCppProject`
- 分类 Category: `engineering`
- 功能摘要 Summary: Creates a regular Visual Studio C++ project inside the current solution, such as an AdsClient console application.
- 前置条件 Preconditions:
  - A solution must already be loaded in the DTE session.
  - An installed Visual Studio C++ template or explicit AllowTemplateFallback=true is required.
- 输入 Inputs:
  - `ProjectName` (`string`): Visual Studio C++ project name.
  - `ProjectDirectory` (`string`): Optional project directory. Defaults to SolutionDirectory/ProjectName.
  - `TemplateKind` (`string`): Template semantic kind. P0 supports ConsoleApplication. Example: `ConsoleApplication`.
  - `CandidateTemplatePaths` (`string[]`): Optional explicit template paths for machine-specific VS installs.
  - `PlatformToolset` (`string`): Optional PlatformToolset value such as v143.
  - `AllowTemplateFallback` (`bool`): Whether the service may synthesize a minimal MSBuild C++ project if no installed template is found. Example: `false`.
- 输出 Outputs:
  - `projectFilePath` (`string`): Absolute .vcxproj path.
  - `projectGuid` (`string`): Project GUID registered in the .vcxproj/.sln.
  - `projectDirectory` (`string`): Absolute project directory.
- 验证 Verification:
  - Verify the .sln contains the project, .vcxproj exists, DTE can find it by name, and reopen keeps it visible.

### `engineering.create-xae-solution`

- 方法 Method: `TwinCatEngineeringService.CreateTwinCatSolution`
- 分类 Category: `engineering`
- 功能摘要 Summary: Creates a fresh TwinCAT XAE solution and binds the automation session to its ITcSysManager root.
- 前置条件 Preconditions:
  - An active DTE session must exist.
  - At least one default TwinCAT XAE template path must exist on the machine.
- 输入 Inputs:
  - `SolutionDirectory` (`string`): Directory where the solution should be created.
  - `SolutionName` (`string`): Visual Studio solution name.
  - `ProjectName` (`string`): TwinCAT project name.
- 输出 Outputs:
  - `solutionPath` (`string`): Absolute .sln path.
  - `projectPath` (`string`): Absolute .tsproj path.
- 验证 Verification:
  - Check that the .sln and .tsproj files exist and that ITcSysManager can be retrieved.

### `engineering.ensure-solution-project-dependency`

- 方法 Method: `TwinCatEngineeringService.EnsureSolutionProjectDependency`
- 分类 Category: `engineering`
- 功能摘要 Summary: Ensures a Visual Studio solution ProjectDependencies entry exists from one project to another.
- 前置条件 Preconditions:
  - Both projects must already exist in the saved .sln.
- 输入 Inputs:
  - `ProjectName` (`string`): Dependent project name.
  - `DependsOnProjectName` (`string`): Project that must build before ProjectName.
- 输出 Outputs:
  - `projectGuid` (`string`): Dependent project GUID.
  - `dependsOnProjectGuid` (`string`): Dependency project GUID.
- 验证 Verification:
  - Inspect .sln ProjectSection(ProjectDependencies) and reload the solution to confirm the dependency remains.

### `engineering.ensure-task`

- 方法 Method: `TwinCatEngineeringService.EnsureTask`
- 分类 Category: `engineering`
- 功能摘要 Summary: Creates or reuses a Task under TIRT and normalizes its timing, priority, and AMS port.
- 前置条件 Preconditions:
  - A bound TwinCAT engineering session must exist.
- 输入 Inputs:
  - `TaskName` (`string`): Task node name.
  - `TaskSubtype` (`int`): TwinCAT task subtype passed to CreateChild.
  - `Priority` (`int`): Task priority.
  - `CycleTimeUs` (`int`): Task cycle time in microseconds.
  - `AmsPort` (`int`): Assigned AMS port.
  - `IoAtBegin` (`bool`): Optional TaskDef IoAtBegin setting. Example: `true`.
- 输出 Outputs:
  - `treeItemPath` (`string`): TwinCAT tree path for the task.
  - `objectId` (`string`): Task OTCID / ObjectId.
- 验证 Verification:
  - Read back ProduceXml(true) and confirm TaskDef/Context values were updated.

### `engineering.export-tree-item-xml`

- 方法 Method: `TwinCatEngineeringService.ExportTreeItemXml`
- 分类 Category: `engineering`
- 功能摘要 Summary: Exports ProduceXml output for any TwinCAT tree node as durable evidence.
- 前置条件 Preconditions:
  - A bound TwinCAT engineering session must exist.
  - The target tree path must be valid.
- 输入 Inputs:
  - `TreeItemPath` (`string`): TwinCAT tree path such as TIXC^MyProject.
  - `DestinationPath` (`string`): Absolute output file path.
  - `Recursive` (`bool`): Whether ProduceXml(true) should be used. Example: `false`.
- 输出 Outputs:
  - `xmlPath` (`string`): Saved XML evidence file.
- 验证 Verification:
  - Re-open the XML file and verify it contains the expected node metadata.

### `engineering.launch-visual-studio`

- 方法 Method: `TwinCatEngineeringService.LaunchVisualStudio`
- 分类 Category: `engineering`
- 功能摘要 Summary: Starts a new DTE session that later steps can use for XAE creation, build, and activation.
- 前置条件 Preconditions:
  - Visual Studio with TwinCAT XAE must be installed on the machine.
- 输入 Inputs:
  - `ProgId` (`string`): Visual Studio DTE ProgId. Example: `VisualStudio.DTE.17.0`.
  - `StartupDelayMs` (`int`): Warm-up delay before the DTE session is used. Example: `5000`.
  - `Visible` (`bool`): Whether the launched Visual Studio window should be visible. Example: `true`.
- 输出 Outputs:
  - `session` (`TwinCatEngineeringSession`): Live DTE session stored in pipeline state.
- 验证 Verification:
  - Read back DTE version or solution state to confirm the COM server is reachable.

### `engineering.open-xae-solution`

- 方法 Method: `TwinCatEngineeringService.OpenTwinCatSolution`
- 分类 Category: `engineering`
- 功能摘要 Summary: Re-opens an existing TwinCAT solution and re-attaches COM references after .tsproj file mutations.
- 前置条件 Preconditions:
  - An active DTE session must exist.
  - The target .sln and .tsproj must already exist.
- 输入 Inputs:
  - `SolutionPath` (`string`): Absolute .sln path.
  - `ProjectPath` (`string`): Absolute .tsproj path.
- 输出 Outputs:
  - `solutionPath` (`string`): Absolute .sln path.
  - `projectPath` (`string`): Absolute .tsproj path.
- 验证 Verification:
  - Confirm that the project can be found again in the DTE solution model.

### `engineering.publish-modules`

- 方法 Method: `TwinCatEngineeringService.PublishModules`
- 分类 Category: `engineering`
- 功能摘要 Summary: Invokes the TwinCAT C++ project PublishModules method so updated module source regenerates TMC metadata.
- 前置条件 Preconditions:
  - The target TwinCAT C++ project must exist and expose PublishModules in its tree XML.
- 输入 Inputs:
  - `ProjectName` (`string`): TwinCAT C++ project name.
  - `PostPublishDelayMs` (`int`): Delay after triggering PublishModules. Example: `5000`.
  - `WaitForUpdatedTmcTimeoutMs` (`int`): Maximum wait for the project .tmc timestamp to update. Example: `30000`.
- 输出 Outputs:
  - `updatedTmcPath` (`string`): Project .tmc path observed after publish.
  - `succeeded` (`bool`): Whether publish left a readable project .tmc.
  - `updated` (`bool`): Whether the .tmc timestamp or content changed during this publish call.
- 验证 Verification:
  - Check that the .tmc is readable and contains the expected module classes; updated=true means the timestamp or content changed during this publish call.

### `engineering.save-all`

- 方法 Method: `TwinCatEngineeringService.SaveAll`
- 分类 Category: `engineering`
- 功能摘要 Summary: Flushes pending Visual Studio document and solution changes before build, reopen, or .tsproj file mutation steps.
- 前置条件 Preconditions:
  - A live DTE session must exist.
- 输入 Inputs:
  - (none)
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-check file timestamps or solution dirtiness after the command.

### `signing.grant-certificate`

- 方法 Method: `TwinCatSigningService.GrantCertificate`
- 分类 Category: `signing`
- 功能摘要 Summary: Grants or removes local TcSignTool authorization for a TwinCAT signing certificate.
- 前置条件 Preconditions:
  - TwinCAT SDK TcSignTool.exe must be installed.
  - The certificate file must exist on this machine.
- 输入 Inputs:
  - `CertificatePath` (`string`): Path to the .tccert certificate file.
  - `Password` (`string`): Certificate password. Prefer CLI password-file or password-env-var input; it is never emitted in step outputs.
  - `RemoveGrant` (`bool`): Remove the local grant instead of adding it. Example: `false`.
  - `Quiet` (`bool`): Pass /q to TcSignTool. Example: `true`.
  - `ToolPath` (`string`): Optional TcSignTool.exe override. Example: `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\SDK\Bin\TcSignTool.exe`.
- 输出 Outputs:
  - `exitCode` (`int`): TcSignTool process exit code.
  - `commandLine` (`string`): Redacted command line used for evidence.
- 验证 Verification:
  - Run signing.sign-twincat-binary and signing.verify-twincat-binary after grant to close the loop.

### `signing.set-license`

- 方法 Method: `TwinCatSigningService.SetLicense`
- 分类 Category: `signing`
- 功能摘要 Summary: Writes TwinCAT C++ project signing license settings used by MSBuild/TcSignTool.
- 前置条件 Preconditions:
  - The C++ .vcxproj file must exist.
  - Run this before engineering.build-solution so the generated .tmx is signed during build.
- 输入 Inputs:
  - `ProjectFilePath` (`string`): Path to the C++ .vcxproj file, or resolve it from ProjectPath and CppProjectName.
  - `LicenseName` (`string`): TwinCAT signing certificate/license name written to TcSignTwinCatCertName. Example: `optcnc`.
  - `Password` (`string`): TwinCAT signing certificate/license password written to TcSignTwinCatCertPW. Prefer password-file or password-env-var outside demo plans.
  - `EnableSigning` (`bool`): Write TcSignTwinCat=true so TwinCAT MSBuild signs the output. Example: `true`.
- 输出 Outputs:
  - `projectFilePath` (`string`): Updated C++ .vcxproj path.
  - `licenseName` (`string`): Configured TwinCAT signing license name.
  - `enableSigning` (`bool`): Whether TwinCAT signing is enabled.
  - `passwordWritten` (`bool`): Whether a password value was written; the password itself is never emitted.
- 验证 Verification:
  - Inspect the C++ .vcxproj or generated TcSignTwinCatCmd.txt after build; do not expect ADS/runtime state to change until the project is rebuilt and activated.

### `signing.sign-twincat-binary`

- 方法 Method: `TwinCatSigningService.Sign`
- 分类 Category: `signing`
- 功能摘要 Summary: Signs one or more built TwinCAT C++ binaries with Beckhoff TcSignTool.
- 前置条件 Preconditions:
  - engineering.build-solution must have produced the target .tmx/.sys binary.
  - The certificate file must exist on this machine.
- 输入 Inputs:
  - `CertificatePath` (`string`): Path to the .tccert certificate file.
  - `TargetPaths` (`string[]`): One or more TwinCAT binary paths to sign.
  - `Password` (`string`): Certificate password. Prefer CLI password-file or password-env-var input; it is piped to TcSignTool instead of placed on the process command line.
  - `Quiet` (`bool`): Pass /q to TcSignTool. Example: `true`.
  - `ToolPath` (`string`): Optional TcSignTool.exe override. Example: `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\SDK\Bin\TcSignTool.exe`.
- 输出 Outputs:
  - `targetPaths` (`string`): Semicolon-separated signed binary paths.
  - `exitCode` (`int`): TcSignTool process exit code.
  - `commandLine` (`string`): Redacted command line used for evidence.
- 验证 Verification:
  - Run signing.verify-twincat-binary and then activate the configuration with the signed binary.

### `signing.verify-twincat-binary`

- 方法 Method: `TwinCatSigningService.Verify`
- 分类 Category: `signing`
- 功能摘要 Summary: Verifies that one or more TwinCAT binaries carry a valid TcSignTool signature.
- 前置条件 Preconditions:
  - The target TwinCAT binary must already exist.
- 输入 Inputs:
  - `TargetPaths` (`string[]`): One or more TwinCAT binary paths to verify.
  - `Quiet` (`bool`): Pass /q to TcSignTool. Example: `true`.
  - `ToolPath` (`string`): Optional TcSignTool.exe override. Example: `C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\SDK\Bin\TcSignTool.exe`.
  - `AllowTestModeWarning` (`bool`): Treat TcSignTool exit code 2 as success when the output says the file has a signature but the OEM certificate is not signed by Beckhoff. Example: `false`.
- 输出 Outputs:
  - `targetPaths` (`string`): Semicolon-separated verified binary paths.
  - `exitCode` (`int`): TcSignTool process exit code.
  - `acceptedTestModeWarning` (`bool`): Whether a TcSignTool test-mode certificate warning was accepted.
  - `commandLine` (`string`): Redacted command line used for evidence.
- 验证 Verification:
  - Treat exit code 0 as signed/verified; for local test certificates, set AllowTestModeWarning only when test-mode activation is acceptable.
  - Keep the signed binary path as activation evidence.

### `tsproj.apply-instance-data-pointer-plan`

- 方法 Method: `TwinCatTsprojMutationService.ApplyInstanceDataPointerPlan`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Applies a batch of instance data pointer writes in one deterministic .tsproj mutation pass.
- 前置条件 Preconditions:
  - Each referenced instance must already exist in the .tsproj.
- 输入 Inputs:
  - `Items` (`IReadOnlyList<InstanceDataPointerMutation>`): Batch entries containing InstanceName, PointerName, ObjectId, AreaNo, ByteOffset, and ByteSize.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify DataPointerValues entries for each requested instance/pointer pair.

### `tsproj.apply-instance-interface-pointer-plan`

- 方法 Method: `TwinCatTsprojMutationService.ApplyInstanceInterfacePointerPlan`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Applies a batch of instance interface pointer writes in one deterministic .tsproj mutation pass.
- 前置条件 Preconditions:
  - Each referenced instance must already exist in the .tsproj.
- 输入 Inputs:
  - `Items` (`IReadOnlyList<InstanceInterfacePointerMutation>`): Batch entries containing InstanceName, PointerName, and ObjectId.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify InterfacePointerValues entries for each requested instance/pointer pair.

### `tsproj.apply-instance-parameter-plan`

- 方法 Method: `TwinCatTsprojMutationService.ApplyInstanceParameterPlan`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Applies a batch of instance parameter writes in one deterministic .tsproj mutation pass.
- 前置条件 Preconditions:
  - Each referenced instance must already exist in the .tsproj.
- 输入 Inputs:
  - `Items` (`IReadOnlyList<InstanceParameterMutation>`): Batch entries containing InstanceName, ParameterName, and optional Value/Enum/String payloads.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm each target instance received the expected ParameterValues updates.

### `tsproj.apply-mutation-plan`

- 方法 Method: `TwinCatTsprojMutationService.ApplyMutationPlan`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Applies generic element and fragment upserts in one deterministic .tsproj mutation pass.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
  - Use this for JSON-owned low-level mutations that do not yet have a dedicated primitive.
- 输入 Inputs:
  - `ElementUpserts` (`IReadOnlyList<TsprojElementUpsertRequest>`): Element upserts to apply in order.
  - `FragmentUpserts` (`IReadOnlyList<TsprojFragmentUpsertRequest>`): Fragment upserts to apply in order.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify all requested low-level mutations landed; FailOnConflict aborts the plan.

### `tsproj.bind-instance-context`

- 方法 Method: `TwinCatTsprojMutationService.BindInstanceContext`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Writes Instance TmcDesc Context/ManualConfig binding with configurable context id/name and CyclicCaller behavior.
- 前置条件 Preconditions:
  - The target instance must already exist in the .tsproj.
- 输入 Inputs:
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `TaskObjectId` (`string`): Task OTCID, for example #x02010020.
  - `Priority` (`int`): Context priority.
  - `CycleTimeNs` (`int`): Context cycle time in nanoseconds.
  - `ContextId` (`int`): Context Id value used for lookup/create. Example: `1`.
  - `ContextName` (`string`): Optional Context Name value to enforce.
  - `IncludeCyclicCaller` (`bool`): Whether to ensure InterfacePointerValues/CyclicCaller exists. Example: `true`.
  - `RemoveCyclicCallerWhenExcluded` (`bool`): Whether to remove CyclicCaller when IncludeCyclicCaller is false. Example: `false`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm Context Id/ManualConfig/OTCID plus optional CyclicCaller state.

### `tsproj.bind-instance-task`

- 方法 Method: `TwinCatTsprojMutationService.BindInstanceToTask`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Writes ManualConfig/OTCID and context timing values back into an instance TmcDesc, optionally also updating CyclicCaller.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
  - The task ObjectId must already be known.
- 输入 Inputs:
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `TaskObjectId` (`string`): Task OTCID, for example #x02010010.
  - `Priority` (`int`): Context priority.
  - `CycleTimeNs` (`int`): Context cycle time in nanoseconds.
  - `IncludeCyclicCaller` (`bool`): Whether to also bind InterfacePointerValues/CyclicCaller. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj or produce instance XML again and confirm OTCID/CycleTime/Priority are present.

### `tsproj.bind-plc-instance-task`

- 方法 Method: `TwinCatTsprojMutationService.BindPlcInstanceToTask`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Writes PLC instance Context/ManualConfig binding so PLC execution context tracks an explicit task object id.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
  - `PlcTaskName` (`string`): PLC task name written into Context/Name.
  - `TaskObjectId` (`string`): Task OTCID, for example #x02010020.
  - `Priority` (`int`): PLC context priority.
  - `CycleTimeNs` (`int`): PLC context cycle time in nanoseconds.
  - `ContextId` (`int`): PLC context Id value. Example: `0`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify the PLC instance Context/ManualConfig contains OTCID/Priority/CycleTime.

### `tsproj.clear-instance-data-pointer-values`

- 方法 Method: `TwinCatTsprojMutationService.ClearInstanceDataPointerValues`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Clears DataPointerValues under a C++ instance TmcDesc before activation or before applying a known-good data pointer plan.
- 前置条件 Preconditions:
  - The referenced instance must already exist in the .tsproj.
- 输入 Inputs:
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `RemoveContainerWhenEmpty` (`bool`): Remove the DataPointerValues container instead of leaving it empty. Example: `false`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm stale DataPointerValues entries are gone before activation.

### `tsproj.clear-instance-parameter-values`

- 方法 Method: `TwinCatTsprojMutationService.ClearInstanceParameterValues`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Clears ParameterValues under a C++ instance TmcDesc before applying a deterministic parameter plan.
- 前置条件 Preconditions:
  - The referenced instance must already exist in the .tsproj.
- 输入 Inputs:
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `RemoveContainerWhenEmpty` (`bool`): Remove the ParameterValues container instead of leaving it empty. Example: `false`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm stale ParameterValues entries are gone before new values are applied.

### `tsproj.clear-mappings`

- 方法 Method: `TwinCatTsprojMutationService.ClearMappings`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Removes all root-level Mappings sections so mapping links can be rebuilt from a known empty state.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
- 输入 Inputs:
  - (none)
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify root-level Mappings nodes are absent.

### `tsproj.clear-plc-init-symbols`

- 方法 Method: `TwinCatTsprojMutationService.ClearPlcInitSymbols`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Clears all InitSymbol entries under a PLC Instance InitSymbols section.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
  - `RemoveContainerWhenEmpty` (`bool`): Whether empty InitSymbols container should be removed. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the PLC instance has no InitSymbol entries.

### `tsproj.clear-plc-instance-vars`

- 方法 Method: `TwinCatTsprojMutationService.ClearPlcInstanceVars`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Removes all Vars groups under a PLC Instance node so PLC variable layout can be rebuilt deterministically.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the PLC instance contains no Vars child elements.

### `tsproj.clear-plc-task-pou-oids`

- 方法 Method: `TwinCatTsprojMutationService.ClearPlcTaskPouOids`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Clears all TaskPouOid entries under a PLC Instance TaskPouOids section.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
  - `RemoveContainerWhenEmpty` (`bool`): Whether empty TaskPouOids container should be removed. Example: `false`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the PLC instance has no TaskPouOid entries.

### `tsproj.clear-task-layout`

- 方法 Method: `TwinCatTsprojMutationService.ClearTaskLayout`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Removes Vars and/or Image child nodes from a Task so task layout can be rebuilt deterministically.
- 前置条件 Preconditions:
  - The target Task node must already exist in the .tsproj.
- 输入 Inputs:
  - `TaskName` (`string`): Task node name.
  - `RemoveVars` (`bool`): Whether to remove all Vars groups under the task. Example: `true`.
  - `RemoveImage` (`bool`): Whether to remove all Image entries under the task. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the selected child node categories were removed from the target Task.

### `tsproj.clear-unrestored-var-links`

- 方法 Method: `TwinCatTsprojMutationService.ClearUnrestoredVarLinks`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Removes stale UnrestoredVarLinks blocks so unresolved TwinCAT links do not survive into activation.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
- 输入 Inputs:
  - (none)
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify no UnrestoredVarLinks nodes remain before rebuilding mappings.

### `tsproj.ensure-cpp-instance`

- 方法 Method: `TwinCatTsprojMutationService.EnsureCppInstance`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures a named C++ Instance node exists under Cpp/Project and carries a minimal TmcDesc skeleton.
- 前置条件 Preconditions:
  - The target C++ Project must already exist in the .tsproj.
- 输入 Inputs:
  - `CppProjectName` (`string`): C++ project node name under Cpp.
  - `InstanceName` (`string`): C++ instance display name.
  - `ObjectId` (`string`): Instance OTCID/ObjectId, for example #x02010010.
  - `ContextId` (`int`): Context Id value used for lookup/create. Example: `1`.
  - `ContextName` (`string`): Context Name value to enforce. Example: `FallbackCtx`.
  - `Priority` (`int`): ManualConfig/Priority value used for skeleton. Example: `0`.
  - `CycleTimeNs` (`int`): ManualConfig/CycleTime value used for skeleton. Example: `0`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify Cpp/Project/Instance plus TmcDesc/Contexts/ParameterValues/InterfacePointerValues/DataPointerValues exist.

### `tsproj.ensure-data-pointer`

- 方法 Method: `TwinCatTsprojMutationService.EnsureDataPointerValue`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Writes or updates a DataPointerValues entry beneath an instance TmcDesc.
- 前置条件 Preconditions:
  - The target instance must already exist in the .tsproj.
- 输入 Inputs:
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `PointerName` (`string`): Data pointer field name.
  - `ObjectId` (`string`): Referenced OTCID.
  - `AreaNo` (`int`): TwinCAT data area number.
  - `ByteOffset` (`int`): Offset inside the area.
  - `ByteSize` (`int`): Byte width of the pointed segment.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the named DataPointerValues/Value entry matches the requested offsets.

### `tsproj.ensure-init-symbol`

- 方法 Method: `TwinCatTsprojMutationService.EnsureInitSymbol`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures a PLC InitSymbol exists and writes Data from the provided ObjectId using TwinCAT little-endian encoding.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
  - `SymbolName` (`string`): InitSymbol name, for example MAIN.fbStateMachine.oidInstance.
  - `ObjectId` (`string`): ObjectId used to produce InitSymbol Data, for example #x02010010.
  - `TypeName` (`string`): InitSymbol Type element value. Example: `OTCID`.
  - `TypeGuid` (`string`): GUID attribute written on InitSymbol Type. Example: `{18071995-0000-0000-0000-00000000000F}`.
  - `AreaNo` (`string`): InitSymbol AreaNo value. Example: `#x00000003`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm InitSymbols contains the requested symbol name/type/area/data.

### `tsproj.ensure-interface-pointer`

- 方法 Method: `TwinCatTsprojMutationService.EnsureInterfacePointerValue`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Writes or updates an InterfacePointerValues entry beneath an instance TmcDesc.
- 前置条件 Preconditions:
  - The target instance must already exist in the .tsproj.
- 输入 Inputs:
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `PointerName` (`string`): Interface pointer field name.
  - `ObjectId` (`string`): Referenced OTCID.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the named InterfacePointerValues/Value entry contains the OTCID.

### `tsproj.ensure-io-task-image`

- 方法 Method: `TwinCatTsprojMutationService.EnsureIoTaskImage`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures IO Task Image structure on a task and binds the instance IoTaskImage pointer to the derived or specified Image ObjectId.
- 前置条件 Preconditions:
  - The target Task and instance must already exist in the .tsproj.
- 输入 Inputs:
  - `TaskName` (`string`): Task node name.
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `ImageId` (`int`): Task Image Id used to derive the default ObjectId. Example: `1`.
  - `SizeIn` (`int`): Task Image input size in bytes. Example: `40`.
  - `SizeOut` (`int`): Task Image output size in bytes. Example: `10`.
  - `PointerName` (`string`): Interface pointer name to bind on the instance. Example: `IoTaskImage`.
  - `EnsureDefaultTaskVariables` (`bool`): Whether to regenerate default Inputs/Outputs Vars groups. Example: `true`.
  - `InputRealCount` (`int`): Default input REAL variable count. Example: `10`.
  - `OutputByteCount` (`int`): Default output BYTE variable count. Example: `10`.
  - `IoAtBegin` (`bool`): Whether to set Task IoAtBegin. Example: `true`.
  - `ImageObjectId` (`string`): Optional explicit image ObjectId override.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm Task Image plus instance InterfacePointerValues/IoTaskImage were updated.

### `tsproj.ensure-mapping-link`

- 方法 Method: `TwinCatTsprojMutationService.EnsureMappingLink`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures a deterministic Mapping OwnerA/OwnerB/Link triple exists without replacing unrelated mappings.
- 前置条件 Preconditions:
  - Owner names and variable names must match TwinCAT path/value expectations.
- 输入 Inputs:
  - `OwnerAName` (`string`): OwnerA Name attribute, for example TIXC^Demo^ObjA.
  - `OwnerBName` (`string`): OwnerB Name attribute, for example TIXC^Demo^ObjB.
  - `VarA` (`string`): VarA attribute value.
  - `VarB` (`string`): VarB attribute value.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm Mappings contains the requested OwnerA/OwnerB/Link entry.

### `tsproj.ensure-parameter`

- 方法 Method: `TwinCatTsprojMutationService.EnsureParameterValue`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Upserts a parameter default under TmcDesc/ParameterValues for an instance.
- 前置条件 Preconditions:
  - The target instance must already exist in the .tsproj.
- 输入 Inputs:
  - `InstanceName` (`string`): Exact TwinCAT instance display name.
  - `ParameterName` (`string`): Parameter name.
  - `ValueText` (`string`): Scalar value text.
  - `EnumText` (`string`): Enum value text.
  - `StringText` (`string`): String value text.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the Value node now exists with the expected child elements.

### `tsproj.ensure-plc-instance`

- 方法 Method: `TwinCatTsprojMutationService.EnsurePlcInstance`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures Plc/Project/Instance nodes exist for a named PLC project and instance.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify Plc/Project(Name)/Instance(Name) exists for the requested names.

### `tsproj.ensure-plc-instance-vars-group`

- 方法 Method: `TwinCatTsprojMutationService.EnsurePlcInstanceVarsGroup`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures a named Vars group exists under a PLC Instance with deterministic Var definitions.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
  - `GroupName` (`string`): Vars group Name value, for example PlcTask Outputs.
  - `VarGrpType` (`int`): Vars VarGrpType attribute value.
  - `InsertType` (`int`): Vars InsertType attribute value. Example: `1`.
  - `AreaNo` (`int`): Optional Vars AreaNo attribute value.
  - `Variables` (`IReadOnlyList<PlcInstanceVarItem>`): Variable definitions written under Vars/Var.
  - `ReplaceExistingGroup` (`bool`): Whether to replace an existing Vars group with the same Name. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify the PLC instance Vars group attributes and Var entries.

### `tsproj.ensure-task`

- 方法 Method: `TwinCatTsprojMutationService.EnsureTaskDefinition`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Creates or updates the declarative task node inside a .tsproj generated by XAE.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
- 输入 Inputs:
  - `TaskName` (`string`): Task node name.
  - `Priority` (`int`): Task priority.
  - `CycleTimeNs` (`int`): Task cycle time in nanoseconds.
  - `AmsPort` (`int`): Assigned AMS port.
  - `IoAtBegin` (`bool`): Optional TaskDef IoAtBegin setting. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify the Task attributes match the requested values.

### `tsproj.ensure-task-image`

- 方法 Method: `TwinCatTsprojMutationService.EnsureTaskImage`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures a Task Image entry exists with deterministic Id, size, and addressing attributes.
- 前置条件 Preconditions:
  - The target Task node must already exist in the .tsproj.
- 输入 Inputs:
  - `TaskName` (`string`): Task node name.
  - `ImageId` (`int`): Task Image Id attribute value. Example: `1`.
  - `AddressType` (`int`): Task Image AddrType attribute value. Example: `1`.
  - `ImageType` (`int`): Task Image ImageType attribute value. Example: `1`.
  - `SizeIn` (`int`): Task Image input byte size. Example: `40`.
  - `SizeOut` (`int`): Task Image output byte size. Example: `10`.
  - `ImageName` (`string`): Task Image Name child value. Example: `Image`.
  - `IoAtBegin` (`bool`): Optional Task IoAtBegin attribute update.
  - `ReplaceExistingImage` (`bool`): Whether existing Task Image nodes should be replaced. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the target Task contains the expected Image attributes and Name value.

### `tsproj.ensure-task-pou-oid`

- 方法 Method: `TwinCatTsprojMutationService.EnsureTaskPouOid`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures a PLC TaskPouOid entry exists with the requested priority and optional OTCID.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
  - `Priority` (`int`): TaskPouOid Prio attribute value.
  - `ObjectId` (`string`): Optional TaskPouOid OTCID value.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm TaskPouOids/TaskPouOid has the requested Prio and OTCID.

### `tsproj.ensure-task-vars-group`

- 方法 Method: `TwinCatTsprojMutationService.EnsureTaskVarsGroup`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Ensures a named Vars group exists under a Task with deterministic variable shape and addressing.
- 前置条件 Preconditions:
  - The target Task node must already exist in the .tsproj.
- 输入 Inputs:
  - `TaskName` (`string`): Task node name.
  - `GroupName` (`string`): Vars group Name element value, for example Inputs.
  - `VarGrpType` (`int`): Vars VarGrpType attribute value.
  - `InsertType` (`int`): Vars InsertType attribute value.
  - `BaseVarName` (`string`): Base text used for generated Var names.
  - `TypeName` (`string`): TwinCAT variable type, for example REAL.
  - `Count` (`int`): Number of Var nodes to generate.
  - `BitStride` (`int`): Bit offset stride between generated Vars.
  - `ExternalAddressStride` (`int`): ExternalAddress increment per generated Var after the first.
  - `FirstExternalAddress` (`int`): Optional explicit first ExternalAddress value.
  - `StartIndex` (`int`): Starting suffix index for generated Var names. Example: `1`.
  - `ReplaceExistingGroup` (`bool`): Whether to replace existing Vars groups with the same Name. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the target Task has the expected Vars group name, variable count, and field values.

### `tsproj.merge-fragment`

- 方法 Method: `TwinCatTsprojMutationService.MergeNamedElementFragment`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Merges a captured XML fragment into a named container, acting as the escape hatch only for remaining TwinCAT XML gaps without a dedicated primitive.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
  - Use a dedicated tsproj primitive instead when one exists for the same intent.
  - The fragment must come from a known-good .tsproj source, with documented target parent path, field meanings, source evidence, and test or real-machine evidence.
  - The target parent local name must be unambiguous in the .tsproj.
- 输入 Inputs:
  - `ParentElementName` (`string`): Container local name inside the .tsproj.
  - `FragmentXml` (`string`): XML fragment to merge.
  - `MatchElementName` (`string`): Local element name used to find and replace an existing sibling.
  - `MatchNameValue` (`string`): Optional <Name> value used to find the existing sibling.
  - `ReplaceExisting` (`bool`): Whether a matched sibling should be replaced. Example: `true`.
  - `FragmentSource` (`string`): Known-good .tsproj/evidence source for the fragment.
  - `TargetParentPath` (`string`): Human-readable target parent path/layer in the .tsproj.
  - `FieldMeaning` (`string`): Short explanation of the fragment fields and why they are required.
  - `VerificationEvidence` (`string`): Test, XAE reopen/build/activation, or dated evidence proving this fragment is safe enough to use.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and compare the target container before/after the merge.
  - For engineering behavior, follow with XAE reopen/build/activation or a dated evidence note explaining why runtime proof is not available.

### `tsproj.replace-data-types-section`

- 方法 Method: `TwinCatTsprojMutationService.ReplaceDataTypesSection`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Replaces the root DataTypes section with a caller-provided fragment.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
  - The fragment must be a valid DataTypes element.
- 输入 Inputs:
  - `DataTypesXml` (`string`): DataTypes XML fragment to write, rooted at <DataTypes>.
  - `InsertBeforeProject` (`bool`): Whether to place DataTypes before the root Project element when present. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify DataTypes appears in the expected root position with requested content.

### `tsproj.replace-mappings-section`

- 方法 Method: `TwinCatTsprojMutationService.ReplaceMappingsSection`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Replaces the root-level Mappings section with a caller-provided fragment.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
  - The fragment must be a valid Mappings element.
- 输入 Inputs:
  - `MappingsXml` (`string`): Mappings XML fragment to write, rooted at <Mappings>.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and compare the Mappings section against the requested fragment.

### `tsproj.replace-project-io-section`

- 方法 Method: `TwinCatTsprojMutationService.ReplaceProjectIoSection`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Replaces the root Project/Io section with a caller-provided Io fragment.
- 前置条件 Preconditions:
  - A root-level Project node must exist in the .tsproj.
  - The fragment must be a valid Io element.
- 输入 Inputs:
  - `IoXml` (`string`): Io XML fragment to write, rooted at <Io>.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify the root Project contains the requested Io section.

### `tsproj.replace-system-settings-section`

- 方法 Method: `TwinCatTsprojMutationService.ReplaceSystemSettingsSection`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Replaces System/Settings with a caller-provided Settings fragment while preserving other System children.
- 前置条件 Preconditions:
  - A System node must exist in the .tsproj.
  - The fragment must be a valid Settings element.
- 输入 Inputs:
  - `SettingsXml` (`string`): Settings XML fragment to write, rooted at <Settings>.
  - `InsertBeforeTasks` (`bool`): Whether to insert Settings before System/Tasks when Tasks exists. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify System/Settings content and insertion position.

### `tsproj.set-plc-instance-metadata`

- 方法 Method: `TwinCatTsprojMutationService.SetPlcInstanceMetadata`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Updates Plc Instance metadata attributes and optional CLSID/ClassFactory fields without replacing unrelated nodes.
- 前置条件 Preconditions:
  - The target PLC Project and PLC Instance must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `PlcInstanceName` (`string`): PLC instance display name.
  - `TcSmClass` (`string`): Optional TcSmClass attribute value.
  - `TmcPath` (`string`): Optional TmcPath attribute value.
  - `KeepUnrestoredLinks` (`string`): Optional KeepUnrestoredLinks attribute value.
  - `Clsid` (`string`): Optional CLSID element value.
  - `ClassFactory` (`string`): Optional ClassFactory attribute on CLSID.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify PLC instance attributes and CLSID metadata values.

### `tsproj.set-plc-project-properties`

- 方法 Method: `TwinCatTsprojMutationService.SetPlcProjectProperties`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Updates Plc/Project attribute-level properties such as project paths, reload behavior, AMS port, and archive settings.
- 前置条件 Preconditions:
  - The target PLC Project must already exist in the .tsproj.
- 输入 Inputs:
  - `PlcProjectName` (`string`): PLC project node name under Plc.
  - `ProjectFilePath` (`string`): Value for the PrjFilePath attribute.
  - `TmcFilePath` (`string`): Value for the TmcFilePath attribute.
  - `ReloadTmc` (`bool`): Optional ReloadTmc attribute value.
  - `AmsPort` (`int`): Optional PLC project AmsPort attribute value.
  - `FileArchiveSettings` (`string`): Optional FileArchiveSettings attribute value, for example #x0002.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify the PLC Project attribute values were applied as requested.

### `tsproj.set-task-affinity`

- 方法 Method: `TwinCatTsprojMutationService.SetTaskAffinity`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Sets Task Affinity and AdtTasks attributes for scheduler placement-sensitive workloads.
- 前置条件 Preconditions:
  - The target Task node must already exist in the .tsproj.
- 输入 Inputs:
  - `TaskName` (`string`): Task node name.
  - `Affinity` (`string`): TwinCAT affinity mask or list, for example 1 or 0,2.
  - `EnableAdtTasks` (`bool`): Whether to force AdtTasks=true on the task. Example: `true`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and confirm the Task node includes Affinity and AdtTasks attributes.

### `tsproj.upsert-element`

- 方法 Method: `TwinCatTsprojMutationService.UpsertElement`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Upserts a generic XML element under a path-based .tsproj parent using a declared conflict policy.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
  - Prefer dedicated tsproj primitives when one exists for the same intent.
- 输入 Inputs:
  - `ParentPath` (`IReadOnlyList<TsprojPathSegment>`): Path from root to the parent element, optionally matching <Name> values.
  - `ElementName` (`string`): Element local name to insert or update.
  - `MatchNameValue` (`string`): Optional <Name> child value used to match an existing element.
  - `Attributes` (`IReadOnlyList<TsprojXmlAttribute>`): Attributes to write on the target element.
  - `ChildValues` (`IReadOnlyList<TsprojXmlChildValue>`): Simple child element values to write under the target element.
  - `ConflictPolicy` (`TsprojMutationConflictPolicy`): ReplaceExisting, KeepExisting, or FailOnConflict. Example: `ReplaceExisting`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and verify the resolved parent contains exactly the intended element shape.

### `tsproj.upsert-fragment`

- 方法 Method: `TwinCatTsprojMutationService.UpsertFragment`
- 分类 Category: `tsproj`
- 功能摘要 Summary: Upserts a generic XML fragment under a path-based .tsproj parent using a declared conflict policy.
- 前置条件 Preconditions:
  - The target .tsproj must already exist on disk.
  - The fragment must already be shaped like Beckhoff expects.
- 输入 Inputs:
  - `ParentPath` (`IReadOnlyList<TsprojPathSegment>`): Path from root to the parent element, optionally matching <Name> values.
  - `FragmentXml` (`string`): XML fragment to insert or update.
  - `MatchElementName` (`string`): Local element name used to match an existing sibling.
  - `MatchNameValue` (`string`): Optional <Name> value used to match an existing sibling.
  - `ConflictPolicy` (`TsprojMutationConflictPolicy`): ReplaceExisting, KeepExisting, or FailOnConflict. Example: `ReplaceExisting`.
- 输出 Outputs:
  - (none)
- 验证 Verification:
  - Re-open the .tsproj and compare the resolved parent before/after the fragment upsert.

### `validation.ads-read`

- 方法 Method: `AdsValidationService.Read`
- 分类 Category: `validation`
- 功能摘要 Summary: Reads a PLC or TcCOM symbol over ADS so the engineering pipeline can close the loop with a runtime assertion.
- 前置条件 Preconditions:
  - The target runtime must be reachable over ADS.
  - The symbol path and AMS endpoint must already be known.
- 输入 Inputs:
  - `NetId` (`string`): AMS NetId, for example 127.0.0.1.1.1.
  - `Port` (`int`): ADS port, for example 851.
  - `SymbolPath` (`string`): Fully qualified PLC or TcCOM symbol name.
  - `DataType` (`AdsReadDataType`): Expected runtime data type.
  - `AutoReconnect` (`bool`): Whether to force a TwinCAT reconnect attempt before reading. Example: `false`.
- 输出 Outputs:
  - `value` (`string`): Stringified ADS value.
- 验证 Verification:
  - Treat a successful read plus a domain assertion on the returned value as the runtime acceptance check.

### `validation.ads-read-symbols`

- 方法 Method: `AdsValidationService.ReadSymbols`
- 分类 Category: `validation`
- 功能摘要 Summary: Reads multiple PLC or TcCOM symbols over ADS and prints their values as a runtime validation checkpoint.
- 前置条件 Preconditions:
  - The target runtime must be reachable over ADS.
  - The symbol paths, data types, and AMS endpoint must already be known.
- 输入 Inputs:
  - `NetId` (`string`): AMS NetId, for example 127.0.0.1.1.1.
  - `Port` (`int`): ADS port, for example 851.
  - `Symbols` (`IReadOnlyList<AdsReadSymbolRequest>`): Symbols to read; direct CLI accepts --symbols=MAIN.nSeed:UInt32;MAIN.bPipelineOk:Boolean or --json-file.
  - `AutoReconnect` (`bool`): Whether to force a TwinCAT reconnect attempt before reading. Example: `false`.
  - `ContinueOnError` (`bool`): Whether the step should succeed when one or more symbols fail. Example: `false`.
- 输出 Outputs:
  - `succeededCount` (`int`): Number of symbols read successfully.
  - `failedCount` (`int`): Number of symbols that failed to read.
  - `valuesText` (`string`): Human-readable symbol=value list printed in the step result.
  - `valuesJson` (`json`): Per-symbol ADS read results including data types and errors.
- 验证 Verification:
  - Use after activation and ADS port scan; treat the printed values plus expected domain assertions as runtime proof.

### `validation.ads-scan`

- 方法 Method: `AdsValidationService.ScanPorts`
- 分类 Category: `validation`
- 功能摘要 Summary: Scans ADS target ports and reports whether the runtime endpoint is reachable before symbol-level ADS reads are attempted.
- 前置条件 Preconditions:
  - TwinCAT runtime or router must be installed on the target machine.
  - Use this before symbol reads when activation or ADS routing is uncertain.
- 输入 Inputs:
  - `NetId` (`string`): AMS NetId, or local for the local router. Example: `local`.
  - `Ports` (`int[]`): Comma-separated ADS ports to probe. Example: `100,200,300,800,851,852,10000`.
- 输出 Outputs:
  - `openPortCount` (`int`): Number of ports with readable ADS state.
  - `failedPortCount` (`int`): Number of ports that failed to connect or read state.
  - `portsJson` (`json`): Per-port ADS state or error message.
- 验证 Verification:
  - If 100 or 10000 are not reachable, treat later PLC symbol read failures as runtime/router/permission failures rather than symbol-path failures.

