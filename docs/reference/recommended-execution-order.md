# Recommended Execution Order / 推荐执行顺序

下面是新 TwinCAT 自动化运行的默认控制顺序。项目可以跳过某些步骤，但应在自己的 plan 或 evidence 中说明原因。

1. `engineering.cleanup-dte-host-processes`
   `TwinCatEngineeringService.CleanupDteHostProcesses`
   Lists or explicitly kills unattended Visual Studio/TcXaeShell host processes that can block DTE automation.
2. `engineering.launch-visual-studio`
   `TwinCatEngineeringService.LaunchVisualStudio`
   Starts or attaches a DTE session that later steps can use for XAE creation, build, and activation.
3. `engineering.create-xae-solution`
   `TwinCatEngineeringService.CreateTwinCatSolution`
   Creates a fresh TwinCAT XAE solution and binds the automation session to its ITcSysManager root.
4. `engineering.create-cpp-project`
   `TwinCatEngineeringService.CreateCppProject`
   Creates a TwinCAT C++ project beneath TIXC using a specified Beckhoff wizard id.
5. `engineering.create-vs-cpp-project`
   `TwinCatEngineeringService.CreateVisualStudioCppProject`
   Creates a regular Visual Studio C++ project inside the current solution, such as an AdsClient console application.
6. `engineering.create-scope-project`
   `TwinCatEngineeringService.CreateScopeProject`
   Creates a TwinCAT Scope project skeleton inside the current solution without copying an existing Scope project file.
7. `scope.ensure-configuration`
   `TwinCatScopeConfigurationService.EnsureConfiguration`
   Creates or updates a TwinCAT Scope .tcscopex configuration from typed chart, channel, and ADS symbol definitions.
8. `scope.assert-configuration-shape`
   `TwinCatScopeConfigurationService.AssertConfigurationShape`
   Reads a TwinCAT Scope .tcscopex configuration and asserts typed channel/chart shape without mutating it.
9. `engineering.ensure-solution-project-dependency`
   `TwinCatEngineeringService.EnsureSolutionProjectDependency`
   Ensures a Visual Studio solution ProjectDependencies entry exists from one project to another.
10. `engineering.create-io-device`
   `TwinCatEngineeringService.CreateIoDevice`
   Creates an IO device under a TwinCAT tree parent through ITcSmTreeItem.CreateChild.
11. `engineering.create-ethercat-box`
   `TwinCatEngineeringService.CreateEthercatBox`
   Creates an EtherCAT box or terminal under an EtherCAT parent through ITcSmTreeItem.CreateChild.
12. `engineering.apply-io-tree-plan`
   `TwinCatEngineeringService.ApplyIoTreePlan`
   Applies a batch IO tree payload by orchestrating engineering.create-io-device and engineering.create-ethercat-box operations.
13. `ethercat.assert-product-revisions`
   `TwinCatEtherCatDeviceDescriptionService.AssertProductRevisions`
   Asserts that EtherCAT productRevision strings used for CreateChild are present in the installed Beckhoff ESI/device-description XML files.
14. `engineering.search-io-devices`
   `TwinCatEngineeringService.SearchIoDevices`
   Invokes TwinCAT SearchDevices through ITcSmCommands so XAE can scan local IO devices without using menu commands.
15. `engineering.reload-io-devices`
   `TwinCatEngineeringService.ReloadIoDevices`
   Invokes TwinCAT ReloadDevices through ITcSmCommands so XAE can reload IO device metadata without using menu commands.
16. `engineering.generate-io-mappings`
   `TwinCatEngineeringService.GenerateIoMappings`
   Invokes TwinCAT GenerateMappings so XAE can rebuild variable mappings from the current IO tree.
17. `cpp.remove-project-item`
   `TwinCatEngineeringService.RemoveCppProjectItem`
   Removes a C++ project item registration and optional filter entry and physical file.
18. `cpp.create-project-item`
   `TwinCatEngineeringService.CreateCppProjectItem`
   Creates a C++/header/resource/None project item, optionally creating the physical file and registering it in .vcxproj and .filters.
19. `cpp.write-project-item-content`
   `TwinCatEngineeringService.WriteCppProjectItemContent`
   Writes source/resource/text payload into an existing C++ project item file and returns its content hash.
20. `cpp.set-project-property`
   `TwinCatEngineeringService.SetCppProjectProperty`
   Sets a project-level or configuration-level MSBuild property in a C++ .vcxproj.
21. `cpp.set-item-definition-property`
   `TwinCatEngineeringService.SetCppItemDefinitionProperty`
   Sets a C++ ItemDefinitionGroup tool property such as ClCompile include paths or Link dependencies.
22. `cpp.set-project-item-metadata`
   `TwinCatEngineeringService.SetCppProjectItemMetadata`
   Sets file-level MSBuild metadata for a C++ project item, such as PrecompiledHeader or ExcludedFromBuild.
23. `engineering.create-module`
   `TwinCatEngineeringService.CreateModule`
   Creates a module class inside a TwinCAT C++ project via the module wizard id.
24. `engineering.start-tmc-code-generator`
   `TwinCatEngineeringService.StartTmcCodeGenerator`
   Invokes the TwinCAT C++ project StartTmcCodeGenerator method so source annotations regenerate TMC metadata.
25. `engineering.publish-modules`
   `TwinCatEngineeringService.PublishModules`
   Invokes the TwinCAT C++ project PublishModules method so updated module source regenerates TMC metadata.
26. `engineering.verify-tmc-data-areas`
   `TwinCatEngineeringService.VerifyTmcDataAreas`
   Reads a TwinCAT C++ .tmc file and verifies expected module DataAreas and symbols before instances are created.
27. `engineering.apply-tmc-module-model`
   `TwinCatEngineeringService.ApplyTmcModuleModel`
   Applies a structured JSON module model to a TwinCAT C++ project .tmc without copying a whole known-good TMC file.
28. `engineering.add-module-instance`
   `TwinCatEngineeringService.AddModuleInstance`
   Adds a TcCOM instance from a project TMC by resolving the module GUID and calling CreateChild on the project node.
29. `engineering.ensure-task`
   `TwinCatEngineeringService.EnsureTask`
   Creates or reuses a Task under TIRT and normalizes its timing, priority, and AMS port.
30. `engineering.export-tree-item-xml`
   `TwinCatEngineeringService.ExportTreeItemXml`
   Exports ProduceXml output for any TwinCAT tree node as durable evidence.
31. `engineering.save-all`
   `TwinCatEngineeringService.SaveAll`
   Flushes pending Visual Studio document and solution changes before build, reopen, or .tsproj file mutation steps.
32. `engineering.close-visual-studio`
   `TwinCatEngineeringService.CloseVisualStudio`
   Closes the current Visual Studio DTE session, optionally issuing SaveAll first.
33. `tsproj.ensure-task`
   `TwinCatTsprojMutationService.EnsureTaskDefinition`
   Creates or updates the declarative task node inside a .tsproj generated by XAE.
34. `tsproj.clear-task-layout`
   `TwinCatTsprojMutationService.ClearTaskLayout`
   Removes Vars and/or Image child nodes from a Task so task layout can be rebuilt deterministically.
35. `tsproj.ensure-task-vars-group`
   `TwinCatTsprojMutationService.EnsureTaskVarsGroup`
   Ensures a named Vars group exists under a Task with deterministic variable shape and addressing.
36. `tsproj.ensure-task-image`
   `TwinCatTsprojMutationService.EnsureTaskImage`
   Ensures a Task Image entry exists with deterministic Id, size, and addressing attributes.
37. `tsproj.ensure-cpp-instance`
   `TwinCatTsprojMutationService.EnsureCppInstance`
   Ensures a named C++ Instance node exists under Cpp/Project and carries a minimal TmcDesc skeleton.
38. `tsproj.ensure-plc-instance`
   `TwinCatTsprojMutationService.EnsurePlcInstance`
   Ensures Plc/Project/Instance nodes exist for a named PLC project and instance.
39. `tsproj.bind-instance-context`
   `TwinCatTsprojMutationService.BindInstanceContext`
   Writes Instance TmcDesc Context/ManualConfig binding with configurable context id/name and CyclicCaller behavior.
40. `tsproj.set-task-affinity`
   `TwinCatTsprojMutationService.SetTaskAffinity`
   Sets Task Affinity and/or AdtTasks attributes for scheduler placement-sensitive workloads.
41. `tsproj.set-plc-project-properties`
   `TwinCatTsprojMutationService.SetPlcProjectProperties`
   Updates Plc/Project attribute-level properties such as project paths, reload behavior, AMS port, and archive settings.
42. `tsproj.set-plc-instance-metadata`
   `TwinCatTsprojMutationService.SetPlcInstanceMetadata`
   Updates Plc Instance metadata attributes and optional CLSID/ClassFactory fields without replacing unrelated nodes.
43. `tsproj.clear-plc-instance-vars`
   `TwinCatTsprojMutationService.ClearPlcInstanceVars`
   Removes all Vars groups under a PLC Instance node so PLC variable layout can be rebuilt deterministically.
44. `tsproj.ensure-plc-instance-vars-group`
   `TwinCatTsprojMutationService.EnsurePlcInstanceVarsGroup`
   Ensures a named Vars group exists under a PLC Instance with deterministic Var definitions.
45. `tsproj.clear-plc-task-pou-oids`
   `TwinCatTsprojMutationService.ClearPlcTaskPouOids`
   Clears all TaskPouOid entries under a PLC Instance TaskPouOids section.
46. `tsproj.clear-plc-init-symbols`
   `TwinCatTsprojMutationService.ClearPlcInitSymbols`
   Clears all InitSymbol entries under a PLC Instance InitSymbols section.
47. `tsproj.clear-mappings`
   `TwinCatTsprojMutationService.ClearMappings`
   Removes all root-level Mappings sections so mapping links can be rebuilt from a known empty state.
48. `tsproj.clear-unrestored-var-links`
   `TwinCatTsprojMutationService.ClearUnrestoredVarLinks`
   Removes stale UnrestoredVarLinks blocks so unresolved TwinCAT links do not survive into activation.
49. `tsproj.replace-mappings-section`
   `TwinCatTsprojMutationService.ReplaceMappingsSection`
   Replaces the root-level Mappings section with a caller-provided fragment.
50. `tsproj.replace-project-io-section`
   `TwinCatTsprojMutationService.ReplaceProjectIoSection`
   Replaces the root Project/Io section with a caller-provided Io fragment.
51. `tsproj.ensure-io-section`
   `TwinCatTsprojMutationService.EnsureIoSection`
   Ensures the root Project/Io section exists without replacing other Project children.
52. `tsproj.ensure-io-device`
   `TwinCatTsprojMutationService.EnsureIoDevice`
   Creates or updates a Project/Io Device with structured TwinCAT IO identity fields.
53. `tsproj.ensure-ethercat-box`
   `TwinCatTsprojMutationService.EnsureEthercatBox`
   Creates or updates an EtherCAT Box under a Device or parent Box, preserving nested topology.
54. `tsproj.ensure-io-box-image`
   `TwinCatTsprojMutationService.EnsureIoBoxImage`
   Creates or updates a Box ImageId and optional image metadata without replacing the Box.
55. `tsproj.ensure-io-pdo`
   `TwinCatTsprojMutationService.EnsureIoPdo`
   Creates or updates a Box/EtherCAT Pdo and its Entry children using structured PDO fields.
56. `tsproj.ensure-mapping-info`
   `TwinCatTsprojMutationService.EnsureMappingInfo`
   Creates or updates a root Mappings/MappingInfo entry.
57. `tsproj.ensure-io-mapping-link`
   `TwinCatTsprojMutationService.EnsureIoMappingLink`
   Creates or updates a mapping OwnerA/OwnerB/Link with optional IO/TcCOM mapping attributes.
58. `tsproj.apply-io-topology-plan`
   `TwinCatTsprojMutationService.ApplyIoTopologyPlan`
   Applies a batch IO topology payload by orchestrating dedicated IO Device, Box, PDO, MappingInfo, and Link primitives.
59. `tsproj.replace-data-types-section`
   `TwinCatTsprojMutationService.ReplaceDataTypesSection`
   Replaces the root DataTypes section with a caller-provided fragment.
60. `tsproj.replace-system-settings-section`
   `TwinCatTsprojMutationService.ReplaceSystemSettingsSection`
   Replaces System/Settings with a caller-provided Settings fragment while preserving other System children.
61. `tsproj.ensure-system-settings`
   `TwinCatTsprojMutationService.EnsureSystemSettings`
   Ensures typed System/Settings values such as Cpu and IoIdleTask without replacing the whole Settings section.
62. `tsproj.ensure-io-task-image`
   `TwinCatTsprojMutationService.EnsureIoTaskImage`
   Ensures IO Task Image structure on a task and binds the instance IoTaskImage pointer to the derived or specified Image ObjectId.
63. `tsproj.bind-instance-task`
   `TwinCatTsprojMutationService.BindInstanceToTask`
   Writes ManualConfig/OTCID and context timing values back into an instance TmcDesc, optionally also updating CyclicCaller.
64. `tsproj.bind-plc-instance-task`
   `TwinCatTsprojMutationService.BindPlcInstanceToTask`
   Writes PLC instance Context/ManualConfig binding so PLC execution context tracks an explicit task object id.
65. `tsproj.ensure-task-pou-oid`
   `TwinCatTsprojMutationService.EnsureTaskPouOid`
   Ensures a PLC TaskPouOid entry exists with the requested priority and optional OTCID.
66. `tsproj.ensure-init-symbol`
   `TwinCatTsprojMutationService.EnsureInitSymbol`
   Ensures a PLC InitSymbol exists and writes Data from the provided ObjectId using TwinCAT little-endian encoding.
67. `tsproj.clear-instance-parameter-values`
   `TwinCatTsprojMutationService.ClearInstanceParameterValues`
   Clears ParameterValues under a C++ instance TmcDesc before applying a deterministic parameter plan.
68. `tsproj.clear-instance-data-pointer-values`
   `TwinCatTsprojMutationService.ClearInstanceDataPointerValues`
   Clears DataPointerValues under a C++ instance TmcDesc before activation or before applying a known-good data pointer plan.
69. `tsproj.apply-instance-parameter-plan`
   `TwinCatTsprojMutationService.ApplyInstanceParameterPlan`
   Applies a batch of instance parameter writes in one deterministic .tsproj mutation pass.
70. `tsproj.apply-instance-interface-pointer-plan`
   `TwinCatTsprojMutationService.ApplyInstanceInterfacePointerPlan`
   Applies a batch of instance interface pointer writes in one deterministic .tsproj mutation pass.
71. `tsproj.apply-instance-data-pointer-plan`
   `TwinCatTsprojMutationService.ApplyInstanceDataPointerPlan`
   Applies a batch of instance data pointer writes in one deterministic .tsproj mutation pass.
72. `tsproj.refresh-cpp-instance-tmc-desc`
   `TwinCatTsprojMutationService.RefreshCppInstanceTmcDesc`
   Refreshes existing C++ instance TmcDesc metadata from the project .tmc while preserving instance context and value sections.
73. `tsproj.ensure-parameter`
   `TwinCatTsprojMutationService.EnsureParameterValue`
   Upserts a parameter default under TmcDesc/ParameterValues for an instance.
74. `tsproj.ensure-interface-pointer`
   `TwinCatTsprojMutationService.EnsureInterfacePointerValue`
   Writes or updates an InterfacePointerValues entry beneath an instance TmcDesc.
75. `tsproj.ensure-data-pointer`
   `TwinCatTsprojMutationService.EnsureDataPointerValue`
   Writes or updates a DataPointerValues entry beneath an instance TmcDesc.
76. `tsproj.ensure-mapping-link`
   `TwinCatTsprojMutationService.EnsureMappingLink`
   Ensures a deterministic Mapping OwnerA/OwnerB/Link triple exists without replacing unrelated mappings.
77. `tsproj.assert-data-pointer-shape`
   `TwinCatTsprojMutationService.AssertDataPointerShape`
   Reads a .tsproj and asserts that C++ instance DataPointerValues and root Mappings links still match the requested shape.
78. `tsproj.assert-io-topology-shape`
   `TwinCatTsprojMutationService.AssertIoTopologyShape`
   Reads a .tsproj and asserts the Project/Io topology and root Mappings shape without mutating TwinCAT metadata.
79. `tsproj.assert-io-image-references`
   `TwinCatTsprojMutationService.AssertIoImageReferences`
   Reads a .tsproj and asserts IO process-image references without mutating TwinCAT metadata.
80. `tsproj.describe-io-topology`
   `TwinCatTsprojMutationService.DescribeIoTopology`
   Reads a .tsproj and emits a normalized IO topology summary without copying or mutating TwinCAT metadata XML.
81. `tsproj.compare-io-topology`
   `TwinCatTsprojMutationService.CompareIoTopology`
   Compares two .tsproj files through normalized IO topology facts and reports stable count/key/field differences.
82. `tsproj.upsert-element`
   `TwinCatTsprojMutationService.UpsertElement`
   Upserts a generic XML element under a path-based .tsproj parent using a declared conflict policy.
83. `tsproj.upsert-fragment`
   `TwinCatTsprojMutationService.UpsertFragment`
   Upserts a generic XML fragment under a path-based .tsproj parent using a declared conflict policy.
84. `tsproj.apply-mutation-plan`
   `TwinCatTsprojMutationService.ApplyMutationPlan`
   Applies generic element and fragment upserts in one deterministic .tsproj mutation pass.
85. `tsproj.merge-fragment`
   `TwinCatTsprojMutationService.MergeNamedElementFragment`
   Merges a captured XML fragment into a named container, acting as the escape hatch only for remaining TwinCAT XML gaps without a dedicated primitive.
86. `engineering.open-xae-solution`
   `TwinCatEngineeringService.OpenTwinCatSolution`
   Re-opens an existing TwinCAT solution and re-attaches COM references after .tsproj file mutations.
87. `engineering.build-solution`
   `TwinCatEngineeringService.BuildCurrentSolution`
   Builds the loaded solution through DTE, an unattended devenv.com command-line build, or an MSBuild project sequence.
88. `signing.grant-certificate`
   `TwinCatSigningService.GrantCertificate`
   Grants or removes local TcSignTool authorization for a TwinCAT signing certificate.
89. `signing.set-license`
   `TwinCatSigningService.SetLicense`
   Writes TwinCAT C++ project signing license settings used by MSBuild/TcSignTool.
90. `signing.sign-twincat-binary`
   `TwinCatSigningService.Sign`
   Signs one or more built TwinCAT C++ binaries with Beckhoff TcSignTool.
91. `signing.verify-twincat-binary`
   `TwinCatSigningService.Verify`
   Verifies that one or more TwinCAT binaries carry a valid TcSignTool signature.
92. `engineering.activate-configuration`
   `TwinCatEngineeringService.ActivateConfiguration`
   Saves the current configuration archive when possible and then activates TwinCAT via ITcSysManager; DTE command fallback is opt-in for interactive troubleshooting.
93. `validation.ads-scan`
   `AdsValidationService.ScanPorts`
   Scans ADS target ports and reports whether the runtime endpoint is reachable before symbol-level ADS reads are attempted.
94. `validation.assert-ads-state`
   `AdsValidationService.AssertStates`
   Asserts that specific ADS ports are reachable and in the expected ADS state, turning activation false positives into hard failures.
95. `validation.mark-event-log-window`
   `AdsValidationService.MarkEventLogWindow`
   Marks the current Windows event-log position so a later step can assert only events from the same activation window.
96. `validation.assert-event-log-window`
   `AdsValidationService.AssertEventLogWindow`
   Asserts that no forbidden TcSysSrv Windows event-log entries appeared after a marker or within a recent time window.
97. `validation.ads-read`
   `AdsValidationService.Read`
   Reads a PLC or TcCOM symbol over ADS so the engineering pipeline can close the loop with a runtime assertion.
98. `validation.ads-read-symbols`
   `AdsValidationService.ReadSymbols`
   Reads multiple PLC or TcCOM symbols over ADS and prints their values as a runtime validation checkpoint.
