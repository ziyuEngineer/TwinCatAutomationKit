using TwinCatAutomationKit.Abstractions;
using TwinCatAutomationKit.TwinCat;

namespace TwinCatAutomationKit.Cli;

internal static partial class GuidedBuildCommand
{
    private static class GuidedBuildDefinitions
    {
        private const string TaskMainName = "Task_Main";
        private const string TaskAuxName = "Task_Aux";
        private const int TaskMainPriority = 20;
        private const int TaskAuxPriority = 18;
        private const int TaskMainCycleTimeUs = 10_000;
        private const int TaskAuxCycleTimeUs = 20_000;
        private const int TaskMainCycleTimeNs = 10_000_000;
        private const int TaskAuxCycleTimeNs = 20_000_000;
        private const int TaskMainAmsPort = 301;
        private const int TaskAuxAmsPort = 302;
        private const int PlcProjectAmsPort = 851;
        private const string PlannedTaskMainObjectId = "#x02010010";
        private static readonly string PlannedTaskAuxObjectId = TwinCatTsprojMutationService.DeriveNextObjectId(PlannedTaskMainObjectId, 0x10);

        public static IReadOnlyList<GuidedBuildDefinition> Create(GuidedBuildOptions options)
        {
            List<GuidedBuildDefinition> steps = [];
            string expectedSolutionPath = Path.Combine(Path.GetFullPath(options.OutputRoot), options.SolutionName + ".sln");
            string expectedProjectPath = Path.Combine(Path.GetFullPath(options.OutputRoot), options.ProjectName, options.ProjectName + ".tsproj");

            Add(
                steps,
                stepId: "launch-visual-studio-initial",
                kind: "engineering.launch-visual-studio",
                title: "启动 TwinCAT XAE Visual Studio 会话",
                previewRequest: new LaunchVisualStudioRequest(Visible: options.Visible, StartupDelayMs: options.StartupDelayMs),
                runtimeRequestFactory: state => new LaunchVisualStudioRequest(Visible: state.Options.Visible, StartupDelayMs: state.Options.StartupDelayMs),
                execute: state =>
                {
                    CloseSessionIfNeeded(state, saveBeforeClose: true);
                    LaunchVisualStudioRequest request = new(Visible: state.Options.Visible, StartupDelayMs: state.Options.StartupDelayMs);
                    state.Session = state.Engineering.LaunchVisualStudio(request);
                    return StepExecutionOutcome.Success(
                        "Visual Studio 会话已启动。",
                        CreateOutputs(("session", "TwinCatEngineeringSession")));
                });

            Add(
                steps,
                stepId: "create-xae-solution",
                kind: "engineering.create-xae-solution",
                title: "创建 TwinCAT Solution 和 XAE 工程",
                previewRequest: new CreateTwinCatSolutionRequest(options.OutputRoot, options.SolutionName, options.ProjectName),
                runtimeRequestFactory: state => new CreateTwinCatSolutionRequest(state.OutputRoot, state.Options.SolutionName, state.Options.ProjectName),
                execute: state =>
                {
                    TwinCatProjectInfo info = state.Engineering.CreateTwinCatSolution(
                        RequireSession(state),
                        new CreateTwinCatSolutionRequest(state.OutputRoot, state.Options.SolutionName, state.Options.ProjectName));
                    state.SolutionPath = info.SolutionPath;
                    state.ProjectPath = info.ProjectPath;
                    state.SolutionDirectory = info.SolutionDirectory;
                    return StepExecutionOutcome.Success(
                        "TwinCAT Solution 已创建并绑定 SysManager。",
                        CreateOutputs(
                            ("solutionPath", info.SolutionPath),
                            ("projectPath", info.ProjectPath)));
                });

            Add(
                steps,
                stepId: "create-cpp-project",
                kind: "engineering.create-cpp-project",
                title: "创建 C++ 工程",
                previewRequest: new CreateCppProjectRequest(options.CppProjectName),
                runtimeRequestFactory: state => new CreateCppProjectRequest(state.Options.CppProjectName),
                execute: state =>
                {
                    TwinCatNodeInfo info = state.Engineering.CreateCppProject(
                        RequireSession(state),
                        new CreateCppProjectRequest(state.Options.CppProjectName));
                    return StepExecutionOutcome.Success(
                        "C++ 工程已创建。",
                        CreateOutputs(
                            ("treeItemPath", info.TreeItemPath),
                            ("projectFilePath", info.FilePath)));
                });

            Add(
                steps,
                stepId: "create-plc-project",
                kind: "engineering.create-plc-project",
                title: "创建 PLC 工程",
                previewRequest: new CreatePlcProjectRequest(options.PlcProjectName),
                runtimeRequestFactory: state => new CreatePlcProjectRequest(state.Options.PlcProjectName),
                execute: state =>
                {
                    TwinCatNodeInfo info = state.Engineering.CreatePlcProject(
                        RequireSession(state),
                        new CreatePlcProjectRequest(state.Options.PlcProjectName));
                    return StepExecutionOutcome.Success(
                        "PLC 工程已创建。",
                        CreateOutputs(
                            ("treeItemPath", info.TreeItemPath),
                            ("projectFilePath", info.FilePath)));
                });

            Add(
                steps,
                stepId: "save-all-initial",
                kind: "engineering.save-all",
                title: "保存初始工程变更",
                previewRequest: null,
                runtimeRequestFactory: _ => null,
                execute: state =>
                {
                    state.Engineering.SaveAll(RequireSession(state));
                    return StepExecutionOutcome.Success("Visual Studio 已执行 SaveAll。");
                });

            AddTaskEnsureStep(
                steps,
                stepId: "ensure-task-main",
                title: "创建或校正 Task_Main",
                taskName: TaskMainName,
                priority: TaskMainPriority,
                cycleTimeUs: TaskMainCycleTimeUs,
                amsPort: TaskMainAmsPort,
                ioAtBegin: true,
                objectIdSetter: (state, value) => state.TaskMainObjectId = value,
                fallbackObjectIdFactory: _ => PlannedTaskMainObjectId);

            AddTaskEnsureStep(
                steps,
                stepId: "ensure-task-aux",
                title: "创建或校正 Task_Aux",
                taskName: TaskAuxName,
                priority: TaskAuxPriority,
                cycleTimeUs: TaskAuxCycleTimeUs,
                amsPort: TaskAuxAmsPort,
                ioAtBegin: false,
                objectIdSetter: (state, value) => state.TaskAuxObjectId = value,
                fallbackObjectIdFactory: state => TwinCatTsprojMutationService.DeriveNextObjectId(state.TaskMainObjectId, 0x10));

            Add(
                steps,
                stepId: "export-cpp-xml-initial",
                kind: "engineering.export-tree-item-xml",
                title: "导出初始 C++ 节点 XML",
                previewRequest: new ExportTreeItemXmlRequest($"TIXC^{options.CppProjectName}", Path.Combine(options.OutputRoot, "_guided_build_evidence", "cpp.project.initial.xml"), Recursive: true),
                runtimeRequestFactory: state => new ExportTreeItemXmlRequest($"TIXC^{state.Options.CppProjectName}", GetEvidencePath(state, "cpp.project.initial.xml"), Recursive: true),
                execute: state =>
                {
                    string path = GetEvidencePath(state, "cpp.project.initial.xml");
                    state.Engineering.ExportTreeItemXml(
                        RequireSession(state),
                        new ExportTreeItemXmlRequest($"TIXC^{state.Options.CppProjectName}", path, Recursive: true));
                    return StepExecutionOutcome.Success(
                        "已导出 C++ 节点 XML。",
                        CreateOutputs(("xmlPath", path)),
                        [new EvidenceArtifact("cpp.project.initial", path, "xml")]);
                });

            Add(
                steps,
                stepId: "export-plc-xml-initial",
                kind: "engineering.export-tree-item-xml",
                title: "导出初始 PLC 节点 XML",
                previewRequest: new ExportTreeItemXmlRequest($"TIPC^{options.PlcProjectName}", Path.Combine(options.OutputRoot, "_guided_build_evidence", "plc.project.initial.xml"), Recursive: true),
                runtimeRequestFactory: state => new ExportTreeItemXmlRequest($"TIPC^{state.Options.PlcProjectName}", GetEvidencePath(state, "plc.project.initial.xml"), Recursive: true),
                execute: state =>
                {
                    string path = GetEvidencePath(state, "plc.project.initial.xml");
                    state.Engineering.ExportTreeItemXml(
                        RequireSession(state),
                        new ExportTreeItemXmlRequest($"TIPC^{state.Options.PlcProjectName}", path, Recursive: true));
                    return StepExecutionOutcome.Success(
                        "已导出 PLC 节点 XML。",
                        CreateOutputs(("xmlPath", path)),
                        [new EvidenceArtifact("plc.project.initial", path, "xml")]);
                });

            Add(
                steps,
                stepId: "close-visual-studio-initial",
                kind: "engineering.close-visual-studio",
                title: "关闭初始 Visual Studio 会话",
                previewRequest: new CloseVisualStudioRequest(SaveBeforeClose: true),
                runtimeRequestFactory: _ => new CloseVisualStudioRequest(SaveBeforeClose: true),
                execute: state =>
                {
                    CloseSessionIfNeeded(state, saveBeforeClose: true);
                    return StepExecutionOutcome.Success("Visual Studio 会话已关闭。");
                });

            AddOfflineTaskLayoutSteps(steps, options, TaskMainName, TaskMainPriority, TaskMainCycleTimeNs, TaskMainAmsPort, true, imageId: 1);
            AddOfflineTaskLayoutSteps(steps, options, TaskAuxName, TaskAuxPriority, TaskAuxCycleTimeNs, TaskAuxAmsPort, false, imageId: 2);

            Add(
                steps,
                stepId: "set-plc-project-properties",
                kind: "tsproj.set-plc-project-properties",
                title: "设置 PLC 工程基础属性",
                previewRequest: new SetPlcProjectPropertiesRequest(options.PlcProjectName, AmsPort: PlcProjectAmsPort),
                runtimeRequestFactory: state => new SetPlcProjectPropertiesRequest(state.Options.PlcProjectName, AmsPort: PlcProjectAmsPort),
                execute: state =>
                {
                    state.Tsproj.SetPlcProjectProperties(
                        RequireProjectPath(state),
                        new SetPlcProjectPropertiesRequest(state.Options.PlcProjectName, AmsPort: PlcProjectAmsPort));
                    return StepExecutionOutcome.Success("PLC 工程属性已写回 tsproj。");
                });

            for (int index = 1; index <= options.PlcInstanceCount; index++)
            {
                string plcInstanceName = BuildInstanceName("PlcInst", index);
                (string taskName, string taskObjectId, int priority, int cycleTimeNs) = ResolvePlannedTaskAssignment(index - 1);

                Add(
                    steps,
                    stepId: $"ensure-plc-instance-{index:00}",
                    kind: "tsproj.ensure-plc-instance",
                    title: $"确保 PLC 实例 {plcInstanceName} 存在",
                    previewRequest: new EnsurePlcInstanceRequest(options.PlcProjectName, plcInstanceName),
                    runtimeRequestFactory: state => new EnsurePlcInstanceRequest(state.Options.PlcProjectName, plcInstanceName),
                    execute: state =>
                    {
                        state.Tsproj.EnsurePlcInstance(
                            RequireProjectPath(state),
                            new EnsurePlcInstanceRequest(state.Options.PlcProjectName, plcInstanceName));
                        return StepExecutionOutcome.Success($"PLC 实例 {plcInstanceName} 已确保存在。");
                    });

                Add(
                    steps,
                    stepId: $"bind-plc-instance-{index:00}-task",
                    kind: "tsproj.bind-plc-instance-task",
                    title: $"绑定 PLC 实例 {plcInstanceName} 到 {taskName}",
                    previewRequest: new BindPlcInstanceToTaskRequest(options.PlcProjectName, plcInstanceName, taskName, taskObjectId, priority, cycleTimeNs),
                    runtimeRequestFactory: state =>
                    {
                        (string runtimeTaskName, string runtimeTaskObjectId, int runtimePriority, int runtimeCycleTimeNs) =
                            ResolveRuntimeTaskAssignment(state, index - 1);
                        return new BindPlcInstanceToTaskRequest(state.Options.PlcProjectName, plcInstanceName, runtimeTaskName, runtimeTaskObjectId, runtimePriority, runtimeCycleTimeNs);
                    },
                    execute: state =>
                    {
                        (string runtimeTaskName, string runtimeTaskObjectId, int runtimePriority, int runtimeCycleTimeNs) =
                            ResolveRuntimeTaskAssignment(state, index - 1);
                        state.Tsproj.BindPlcInstanceToTask(
                            RequireProjectPath(state),
                            new BindPlcInstanceToTaskRequest(state.Options.PlcProjectName, plcInstanceName, runtimeTaskName, runtimeTaskObjectId, runtimePriority, runtimeCycleTimeNs));
                        return StepExecutionOutcome.Success($"PLC 实例 {plcInstanceName} 已绑定到 {runtimeTaskName}。");
                    });
            }

            IReadOnlyList<string> plannedCppObjectIds = BuildPlannedCppObjectIds(options.CppInstanceCount);
            for (int index = 1; index <= options.CppInstanceCount; index++)
            {
                string cppInstanceName = BuildInstanceName("CppInst", index);
                string plannedObjectId = plannedCppObjectIds[index - 1];

                Add(
                    steps,
                    stepId: $"ensure-cpp-instance-{index:00}",
                    kind: "tsproj.ensure-cpp-instance",
                    title: $"确保 C++ 实例 {cppInstanceName} 存在",
                    previewRequest: new EnsureCppInstanceRequest(options.CppProjectName, cppInstanceName, plannedObjectId, ContextName: "GuidedCtx"),
                    runtimeRequestFactory: _ => new EnsureCppInstanceRequest(options.CppProjectName, cppInstanceName, plannedObjectId, ContextName: "GuidedCtx"),
                    execute: state =>
                    {
                        state.Tsproj.EnsureCppInstance(
                            RequireProjectPath(state),
                            new EnsureCppInstanceRequest(state.Options.CppProjectName, cppInstanceName, plannedObjectId, ContextName: "GuidedCtx"));
                        return StepExecutionOutcome.Success($"C++ 实例 {cppInstanceName} 已确保存在。");
                    });

                Add(
                    steps,
                    stepId: $"bind-cpp-instance-{index:00}-task",
                    kind: "tsproj.bind-instance-task",
                    title: $"绑定 C++ 实例 {cppInstanceName} 到任务",
                    previewRequest: CreatePlannedCppBindRequest(options, cppInstanceName, index - 1),
                    runtimeRequestFactory: state => CreateRuntimeCppBindRequest(state, cppInstanceName, index - 1),
                    execute: state =>
                    {
                        BindInstanceToTaskRequest request = CreateRuntimeCppBindRequest(state, cppInstanceName, index - 1);
                        state.Tsproj.BindInstanceToTask(RequireProjectPath(state), request);
                        return StepExecutionOutcome.Success($"C++ 实例 {cppInstanceName} 已绑定到 {request.TaskObjectId}。");
                    });
            }

            if (options.BuildSolution)
            {
                Add(
                    steps,
                    stepId: "launch-visual-studio-final",
                    kind: "engineering.launch-visual-studio",
                    title: "重新启动 Visual Studio 会话用于验证/构建",
                    previewRequest: new LaunchVisualStudioRequest(Visible: options.Visible, StartupDelayMs: options.StartupDelayMs),
                    runtimeRequestFactory: state => new LaunchVisualStudioRequest(Visible: state.Options.Visible, StartupDelayMs: state.Options.StartupDelayMs),
                    execute: state =>
                    {
                        CloseSessionIfNeeded(state, saveBeforeClose: true);
                        LaunchVisualStudioRequest request = new(Visible: state.Options.Visible, StartupDelayMs: state.Options.StartupDelayMs);
                        state.Session = state.Engineering.LaunchVisualStudio(request);
                        return StepExecutionOutcome.Success(
                            "Visual Studio 会话已重新启动。",
                            CreateOutputs(("session", "TwinCatEngineeringSession")));
                    });

                Add(
                    steps,
                    stepId: "open-xae-solution",
                    kind: "engineering.open-xae-solution",
                    title: "重新打开刚才生成的 TwinCAT Solution",
                    previewRequest: new OpenTwinCatSolutionRequest(expectedSolutionPath, expectedProjectPath),
                    runtimeRequestFactory: state => new OpenTwinCatSolutionRequest(RequireSolutionPath(state), RequireProjectPath(state)),
                    execute: state =>
                    {
                        TwinCatProjectInfo info = state.Engineering.OpenTwinCatSolution(
                            RequireSession(state),
                            new OpenTwinCatSolutionRequest(RequireSolutionPath(state), RequireProjectPath(state)));
                        state.SolutionPath = info.SolutionPath;
                        state.ProjectPath = info.ProjectPath;
                        state.SolutionDirectory = info.SolutionDirectory;
                        return StepExecutionOutcome.Success(
                            "已重新打开 TwinCAT Solution。",
                            CreateOutputs(
                                ("solutionPath", info.SolutionPath),
                                ("projectPath", info.ProjectPath)));
                    });

                Add(
                    steps,
                    stepId: "save-all-final",
                    kind: "engineering.save-all",
                    title: "保存离线写回后的工程",
                    previewRequest: null,
                    runtimeRequestFactory: _ => null,
                    execute: state =>
                    {
                        state.Engineering.SaveAll(RequireSession(state));
                        return StepExecutionOutcome.Success("已执行最终 SaveAll。");
                    });

                Add(
                    steps,
                    stepId: "build-solution",
                    kind: "engineering.build-solution",
                    title: "构建当前 Solution",
                    previewRequest: new BuildSolutionRequest(),
                    runtimeRequestFactory: _ => new BuildSolutionRequest(),
                    execute: state =>
                    {
                        BuildResult result = state.Engineering.BuildCurrentSolution(RequireSession(state), new BuildSolutionRequest());
                        if (!result.Succeeded)
                        {
                            throw new InvalidOperationException($"BuildCurrentSolution failed. LastBuildInfo={result.LastBuildInfo}.");
                        }

                        return StepExecutionOutcome.Success(
                            "Solution 构建成功。",
                            CreateOutputs(("lastBuildInfo", result.LastBuildInfo.ToString())));
                    });

                Add(
                    steps,
                    stepId: "export-cpp-xml-final",
                    kind: "engineering.export-tree-item-xml",
                    title: "导出最终 C++ 节点 XML",
                    previewRequest: new ExportTreeItemXmlRequest($"TIXC^{options.CppProjectName}", Path.Combine(options.OutputRoot, "_guided_build_evidence", "cpp.project.final.xml"), Recursive: true),
                    runtimeRequestFactory: state => new ExportTreeItemXmlRequest($"TIXC^{state.Options.CppProjectName}", GetEvidencePath(state, "cpp.project.final.xml"), Recursive: true),
                    execute: state =>
                    {
                        string path = GetEvidencePath(state, "cpp.project.final.xml");
                        state.Engineering.ExportTreeItemXml(
                            RequireSession(state),
                            new ExportTreeItemXmlRequest($"TIXC^{state.Options.CppProjectName}", path, Recursive: true));
                        return StepExecutionOutcome.Success(
                            "已导出最终 C++ 节点 XML。",
                            CreateOutputs(("xmlPath", path)),
                            [new EvidenceArtifact("cpp.project.final", path, "xml")]);
                    });

                Add(
                    steps,
                    stepId: "export-plc-xml-final",
                    kind: "engineering.export-tree-item-xml",
                    title: "导出最终 PLC 节点 XML",
                    previewRequest: new ExportTreeItemXmlRequest($"TIPC^{options.PlcProjectName}", Path.Combine(options.OutputRoot, "_guided_build_evidence", "plc.project.final.xml"), Recursive: true),
                    runtimeRequestFactory: state => new ExportTreeItemXmlRequest($"TIPC^{state.Options.PlcProjectName}", GetEvidencePath(state, "plc.project.final.xml"), Recursive: true),
                    execute: state =>
                    {
                        string path = GetEvidencePath(state, "plc.project.final.xml");
                        state.Engineering.ExportTreeItemXml(
                            RequireSession(state),
                            new ExportTreeItemXmlRequest($"TIPC^{state.Options.PlcProjectName}", path, Recursive: true));
                        return StepExecutionOutcome.Success(
                            "已导出最终 PLC 节点 XML。",
                            CreateOutputs(("xmlPath", path)),
                            [new EvidenceArtifact("plc.project.final", path, "xml")]);
                    });

                if (options.ActivateAfterBuild)
                {
                    Add(
                        steps,
                        stepId: "activate-configuration",
                        kind: "engineering.activate-configuration",
                        title: "激活 TwinCAT 配置",
                        previewRequest: new ActivateConfigurationRequest(),
                        runtimeRequestFactory: _ => new ActivateConfigurationRequest(),
                        execute: state =>
                        {
                            ActivationResult result = state.Engineering.ActivateConfiguration(RequireSession(state), new ActivateConfigurationRequest());
                            if (!result.Succeeded)
                            {
                                throw new InvalidOperationException("ActivateConfiguration did not report success.");
                            }

                            return StepExecutionOutcome.Success(
                                "TwinCAT 配置已激活。",
                                CreateOutputs(
                                    ("configurationArchivePath", result.ConfigurationArchivePath),
                                    ("activationCommand", result.ActivationCommand)));
                        });
                }

                Add(
                    steps,
                    stepId: "close-visual-studio-final",
                    kind: "engineering.close-visual-studio",
                    title: "关闭最终 Visual Studio 会话",
                    previewRequest: new CloseVisualStudioRequest(SaveBeforeClose: true),
                    runtimeRequestFactory: _ => new CloseVisualStudioRequest(SaveBeforeClose: true),
                    execute: state =>
                    {
                        CloseSessionIfNeeded(state, saveBeforeClose: true);
                        return StepExecutionOutcome.Success("最终 Visual Studio 会话已关闭。");
                    });
            }

            return steps;
        }

        private static void AddOfflineTaskLayoutSteps(
            List<GuidedBuildDefinition> steps,
            GuidedBuildOptions options,
            string taskName,
            int priority,
            int cycleTimeNs,
            int amsPort,
            bool ioAtBegin,
            int imageId)
        {
            string titlePrefix = taskName;
            Add(
                steps,
                stepId: $"ensure-task-definition-{taskName}",
                kind: "tsproj.ensure-task",
                title: $"离线确保 {titlePrefix} 定义",
                previewRequest: new EnsureTaskDefinitionRequest(taskName, priority, cycleTimeNs, amsPort, ioAtBegin),
                runtimeRequestFactory: _ => new EnsureTaskDefinitionRequest(taskName, priority, cycleTimeNs, amsPort, ioAtBegin),
                execute: state =>
                {
                    state.Tsproj.EnsureTaskDefinition(
                        RequireProjectPath(state),
                        new EnsureTaskDefinitionRequest(taskName, priority, cycleTimeNs, amsPort, ioAtBegin));
                    return StepExecutionOutcome.Success($"已离线确保任务 {taskName} 定义。");
                });

            Add(
                steps,
                stepId: $"clear-task-layout-{taskName}",
                kind: "tsproj.clear-task-layout",
                title: $"清理 {titlePrefix} 的 Vars/Image 布局",
                previewRequest: new ClearTaskLayoutRequest(taskName),
                runtimeRequestFactory: _ => new ClearTaskLayoutRequest(taskName),
                execute: state =>
                {
                    state.Tsproj.ClearTaskLayout(RequireProjectPath(state), new ClearTaskLayoutRequest(taskName));
                    return StepExecutionOutcome.Success($"已清理任务 {taskName} 布局。");
                });

            Add(
                steps,
                stepId: $"ensure-task-inputs-{taskName}",
                kind: "tsproj.ensure-task-vars-group",
                title: $"重建 {titlePrefix} 的 Inputs Vars",
                previewRequest: CreateTaskVarsGroupRequest(taskName, "Inputs", 1, "REAL", 32, 4),
                runtimeRequestFactory: _ => CreateTaskVarsGroupRequest(taskName, "Inputs", 1, "REAL", 32, 4),
                execute: state =>
                {
                    state.Tsproj.EnsureTaskVarsGroup(RequireProjectPath(state), CreateTaskVarsGroupRequest(taskName, "Inputs", 1, "REAL", 32, 4));
                    return StepExecutionOutcome.Success($"已重建任务 {taskName} 的 Inputs Vars。");
                });

            Add(
                steps,
                stepId: $"ensure-task-outputs-{taskName}",
                kind: "tsproj.ensure-task-vars-group",
                title: $"重建 {titlePrefix} 的 Outputs Vars",
                previewRequest: CreateTaskVarsGroupRequest(taskName, "Outputs", 2, "BYTE", 8, 1),
                runtimeRequestFactory: _ => CreateTaskVarsGroupRequest(taskName, "Outputs", 2, "BYTE", 8, 1),
                execute: state =>
                {
                    state.Tsproj.EnsureTaskVarsGroup(RequireProjectPath(state), CreateTaskVarsGroupRequest(taskName, "Outputs", 2, "BYTE", 8, 1));
                    return StepExecutionOutcome.Success($"已重建任务 {taskName} 的 Outputs Vars。");
                });

            Add(
                steps,
                stepId: $"ensure-task-image-{taskName}",
                kind: "tsproj.ensure-task-image",
                title: $"重建 {titlePrefix} 的 Task Image",
                previewRequest: new EnsureTaskImageRequest(taskName, ImageId: imageId, SizeIn: 40, SizeOut: 10, ImageName: "Image", IoAtBegin: ioAtBegin),
                runtimeRequestFactory: _ => new EnsureTaskImageRequest(taskName, ImageId: imageId, SizeIn: 40, SizeOut: 10, ImageName: "Image", IoAtBegin: ioAtBegin),
                execute: state =>
                {
                    state.Tsproj.EnsureTaskImage(
                        RequireProjectPath(state),
                        new EnsureTaskImageRequest(taskName, ImageId: imageId, SizeIn: 40, SizeOut: 10, ImageName: "Image", IoAtBegin: ioAtBegin));
                    return StepExecutionOutcome.Success($"已重建任务 {taskName} 的 Task Image。");
                });
        }

        private static void AddTaskEnsureStep(
            List<GuidedBuildDefinition> steps,
            string stepId,
            string title,
            string taskName,
            int priority,
            int cycleTimeUs,
            int amsPort,
            bool ioAtBegin,
            Action<GuidedBuildRuntimeState, string> objectIdSetter,
            Func<GuidedBuildRuntimeState, string> fallbackObjectIdFactory)
        {
            Add(
                steps,
                stepId: stepId,
                kind: "engineering.ensure-task",
                title: title,
                previewRequest: new EnsureTaskRequest(taskName, TaskSubtype: 0, Priority: priority, CycleTimeUs: cycleTimeUs, AmsPort: amsPort, IoAtBegin: ioAtBegin),
                runtimeRequestFactory: _ => new EnsureTaskRequest(taskName, TaskSubtype: 0, Priority: priority, CycleTimeUs: cycleTimeUs, AmsPort: amsPort, IoAtBegin: ioAtBegin),
                execute: state =>
                {
                    TwinCatNodeInfo info = state.Engineering.EnsureTask(
                        RequireSession(state),
                        new EnsureTaskRequest(taskName, TaskSubtype: 0, Priority: priority, CycleTimeUs: cycleTimeUs, AmsPort: amsPort, IoAtBegin: ioAtBegin));
                    string objectId = string.IsNullOrWhiteSpace(info.ObjectId)
                        ? fallbackObjectIdFactory(state)
                        : info.ObjectId!;
                    objectIdSetter(state, objectId);
                    return StepExecutionOutcome.Success(
                        $"任务 {taskName} 已就绪。",
                        CreateOutputs(
                            ("treeItemPath", info.TreeItemPath),
                            ("objectId", objectId)));
                });
        }

        private static EnsureTaskVarsGroupRequest CreateTaskVarsGroupRequest(
            string taskName,
            string groupName,
            int varGrpType,
            string typeName,
            int bitStride,
            int externalAddressStride) =>
            new(
                taskName,
                GroupName: groupName,
                VarGrpType: varGrpType,
                InsertType: 1,
                BaseVarName: "Var ",
                TypeName: typeName,
                Count: 10,
                BitStride: bitStride,
                ExternalAddressStride: externalAddressStride);

        private static IReadOnlyList<string> BuildPlannedCppObjectIds(int count)
        {
            List<string> ids = [];
            string current = "#x02020010";
            for (int index = 0; index < count; index++)
            {
                ids.Add(current);
                current = TwinCatTsprojMutationService.DeriveNextObjectId(current, 0x10);
            }

            return ids;
        }

        private static BindInstanceToTaskRequest CreatePlannedCppBindRequest(GuidedBuildOptions options, string instanceName, int zeroBasedIndex)
        {
            (string taskName, string taskObjectId, int priority, int cycleTimeNs) = ResolvePlannedTaskAssignment(zeroBasedIndex);
            _ = taskName;
            return new BindInstanceToTaskRequest(instanceName, taskObjectId, priority, cycleTimeNs, IncludeCyclicCaller: true);
        }

        private static BindInstanceToTaskRequest CreateRuntimeCppBindRequest(GuidedBuildRuntimeState state, string instanceName, int zeroBasedIndex)
        {
            (string taskName, string taskObjectId, int priority, int cycleTimeNs) = ResolveRuntimeTaskAssignment(state, zeroBasedIndex);
            _ = taskName;
            return new BindInstanceToTaskRequest(instanceName, taskObjectId, priority, cycleTimeNs, IncludeCyclicCaller: true);
        }

        private static (string TaskName, string TaskObjectId, int Priority, int CycleTimeNs) ResolvePlannedTaskAssignment(int zeroBasedIndex) =>
            zeroBasedIndex % 2 == 0
                ? (TaskMainName, PlannedTaskMainObjectId, TaskMainPriority, TaskMainCycleTimeNs)
                : (TaskAuxName, PlannedTaskAuxObjectId, TaskAuxPriority, TaskAuxCycleTimeNs);

        private static (string TaskName, string TaskObjectId, int Priority, int CycleTimeNs) ResolveRuntimeTaskAssignment(GuidedBuildRuntimeState state, int zeroBasedIndex) =>
            zeroBasedIndex % 2 == 0
                ? (TaskMainName, state.TaskMainObjectId, TaskMainPriority, TaskMainCycleTimeNs)
                : (TaskAuxName, state.TaskAuxObjectId, TaskAuxPriority, TaskAuxCycleTimeNs);

        private static string BuildInstanceName(string prefix, int index) => $"{prefix}{index:00}";

        private static void Add(
            List<GuidedBuildDefinition> steps,
            string stepId,
            string kind,
            string title,
            object? previewRequest,
            Func<GuidedBuildRuntimeState, object?> runtimeRequestFactory,
            Func<GuidedBuildRuntimeState, StepExecutionOutcome> execute)
        {
            StepContract contract = TwinCatStepCatalog.Require(kind);
            steps.Add(new GuidedBuildDefinition(
                new GuidedBuildPlanStep(
                    stepId,
                    kind,
                    contract.MethodName,
                    title,
                    SerializeJson(previewRequest) ?? "{}"),
                runtimeRequestFactory,
                execute));
        }
    }

}
