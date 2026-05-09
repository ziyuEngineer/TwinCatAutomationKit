# Optcnc Public Step Gap Requirements / Optcnc 缺口 Step 需求

English summary: This document specifies the public steps needed to reproduce the OptcncTwinCAT sample honestly from JSON without copying project files or source trees by filesystem script.

本文是给下一个“改库 agent”的接口需求，不是调用方教程。目标是让 `run-plan` 可以像人类操作 TwinCAT/XAE/Visual Studio 一样，从空目录生成接近 `D:\2nd_year\twincat0926\OptcncTwinCAT` 的工程，而不是复制旧工程文件、批量塞 XML 或用脚本复制源码目录。

## 需求来源

当前调用方 plan：

- `t/optcnc-auto-sln-from-kit.json`
- 目标目录：`D:\3rd_year\auto_sln`
- 样例工程：`D:\2nd_year\twincat0926\OptcncTwinCAT`

已确认当前 public step 可以做：

- `engineering.create-xae-solution`
- `engineering.create-cpp-project`
- `engineering.create-module`
- `engineering.add-module-instance`
- `engineering.ensure-task`
- `tsproj.*` 任务绑定、参数、interface pointer、mapping 等专用 mutation

当前 public step 不能诚实完成：

- 在 C++/TwinCAT C++ project 里新建普通 `.cpp`、`.h`、`.hpp`、`.rc`、`.txt`、`.md`、`.license` 等文件。
- 把源码文本写入刚创建的 project item。
- 把 C++ 文件注册进 `.vcxproj` 和 `.vcxproj.filters`。
- 删除或排除 TwinCAT/VS 模板生成但样例不需要的默认文件。
- 设置 C++ project 的 configuration、include path、library path、link dependency、language standard、file-level metadata。
- 新建普通 Visual Studio C++ console app project，例如样例里的 `AdsClient`。
- 公开 TMC publish/regenerate 能力，让源码改完后可以生成可用 `.tmc`。
- 新建 PLC POU/DUT/GVL 并写入 declaration/implementation source text。

## Guardrails / 硬约束

- 禁止通过脚本直接复制样例源码目录到目标工程。
- 禁止复制样例 `.sln`、`.tsproj`、`.vcxproj`、`.vcxproj.filters`、`.props`、`.tmc`、`.tcmproj`、`.tcscopex`。
- 禁止用 `tsproj.merge-fragment`、`tsproj.replace-project-io-section`、`tsproj.replace-data-types-section`、`tsproj.replace-mappings-section` 来绕过缺口。
- C++/PLC 源码文本可以作为 JSON plan payload 输入，但必须由 public step 写入目标 project item。`files[]` 只能生成 payload 文件，不能直接生成最终工程源码文件。
- Step 实现优先使用 DTE/TwinCAT/VS 的稳定 automation path。DTE 不稳定时，`.vcxproj` / `.filters` 修改必须走 typed MSBuild/XML API，不能在 CLI 里拼字符串。
- CLI 只负责解析参数并调用 service。不要在 `StepInvokeCommand` 里实现第二套 project file mutation。
- 每个新增 public step 必须同时有 DTO、service method、`TwinCatStepCatalog` entry、direct `invoke-step` path、JSON `run-plan` 可用路径、测试和 verification story。
- `docs/reference/*` 是 generated output。实现 step 后必须从 source regenerate，不要手改。

## Optcnc 样例事实

样例 solution 顶层 project：

- `OptcncTwinCAT\OptcncTwinCAT.tsproj`
- `AdsClient\AdsClient.vcxproj`
- `Scope\Scope.tcmproj`

样例 `TIXC` 里的 C++ project：

- `Ruckig`
- `Tinyxml2`
- `MotionControl`
- `Untitled1`

样例需要的 C++ project item 类型：

- `ClCompile`：`.cpp` / `.c`
- `ClInclude`：`.h` / `.hpp` / `.hh`
- `ResourceCompile`：`.rc`
- `None`：`.tmc`、文本或其他非编译项目项

样例需要的 C++ build settings：

- `Ruckig` 和 `Tinyxml2` 是 `StaticLibrary`。
- `Ruckig` 使用 `$(ProjectDir)extern\include` 和 `stdcpp17`。
- `MotionControl` 使用 `stdcpp17`、多个 `AdditionalIncludeDirectories`，并链接 `Ruckig.lib`。
- `MotionControl` 需要 `AdditionalLibraryDirectories` 指向 `Ruckig\$(TcProductsRootDirName)\$(Platform)\$(Configuration)`。
- `MotionControl` 有 file-level metadata：`TcPch.cpp` 的 `PrecompiledHeader=Create`，`MotionControlDriver.cpp` 在 TwinCAT OS config 排除，`MotionControlMain.cpp` 在 TwinCAT RT config 排除。
- `AdsClient` 是普通 VS C++ console app，链接 `TcAdsDll.lib`，include/lib path 指向 `$(TWINCAT3DIR)..\AdsApi\TcAdsDll\...`。

当前没有在样例中发现 `.plcproj`、`.TcPOU`、`.TcDUT`、`.TcGVL`。PLC step 仍然需要，因为调用方明确要求 PLC 也走同样“新建 item 后写 source”的模型；但对 Optcnc 当前样例，P0 是 C++/resource/project settings。

## P0 必须新增的 Public Steps

这些 step 是去掉 `copy-optcnc-code-assets.ps1` 之后，诚实迁移源码并让工程可 build 的最低完整接口。

### `engineering.create-vs-cpp-project`

人类操作：在 Visual Studio solution 中 `Add > New Project`，创建普通 C++ project，例如 `Console App`。

分类 Category：`engineering`

建议 DTO：

```csharp
public sealed record CreateVisualStudioCppProjectRequest(
    string ProjectName,
    string? ProjectDirectory = null,
    string TemplateKind = "ConsoleApplication",
    IReadOnlyList<string>? CandidateTemplatePaths = null,
    string? PlatformToolset = null,
    bool AllowTemplateFallback = false);
```

输入：

- `ProjectName`：solution 中的 project name，例如 `AdsClient`。
- `ProjectDirectory`：可选。默认 `${solutionDirectory}\${ProjectName}`。
- `TemplateKind`：模板语义名。P0 至少支持 `ConsoleApplication`。
- `CandidateTemplatePaths`：可选模板路径列表，供不同 VS/TwinCAT 安装差异使用。
- `PlatformToolset`：可选，例如 `v143`。
- `AllowTemplateFallback`：默认 `false`。如果模板不可用，不要偷偷写 `.vcxproj` 伪造 project；应失败并说明缺哪个模板。

输出：

- `projectFilePath`
- `projectGuid`
- `projectDirectory`

验证：

- `.sln` 中能看到新 project。
- `.vcxproj` 文件存在。
- DTE solution model 能按 `ProjectName` 找到该 project。
- 关闭并重开 solution 后 project 仍存在。

Optcnc 用途：

- 创建 `AdsClient`，后续用 `cpp.*` step 创建/写入 `SampleRPC.cpp`、`Test.cpp`、`TcServices.h`、`TcEventLoggerServices.h`、`MotionControlServices.h`。

### `engineering.ensure-solution-project-dependency`

人类操作：在 Visual Studio 里设置 solution project dependency / build dependency。

分类 Category：`engineering`

建议 DTO：

```csharp
public sealed record EnsureSolutionProjectDependencyRequest(
    string ProjectName,
    string DependsOnProjectName);
```

输入：

- `ProjectName`：依赖方，例如 `AdsClient`。
- `DependsOnProjectName`：被依赖方，例如 `OptcncTwinCAT`。

输出：

- `projectGuid`
- `dependsOnProjectGuid`

验证：

- `.sln` 中存在 `ProjectSection(ProjectDependencies)`。
- DTE `SolutionBuild.BuildDependencies` 能读回依赖关系。

Optcnc 用途：

- 样例中 `AdsClient` 依赖 `OptcncTwinCAT` project。

### `cpp.create-project-item`

人类操作：在 C++ project 中 `Add > New Item`，创建一个文件；必要时创建物理子目录和 VS filter。

分类 Category：`cpp`

建议 DTO：

```csharp
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

public sealed record CreateCppProjectItemRequest(
    string ProjectName,
    string RelativePath,
    CppProjectItemType ItemType = CppProjectItemType.Infer,
    string? Filter = null,
    bool AddToProject = true,
    bool CreatePhysicalFile = true,
    ProjectItemConflictPolicy ConflictPolicy = ProjectItemConflictPolicy.FailIfExists,
    bool AllowMsBuildFallback = true);
```

输入：

- `ProjectName`：C++ project name，例如 `MotionControl`、`Ruckig`、`Tinyxml2`、`AdsClient`。
- `RelativePath`：相对 project directory 的路径，例如 `AxesGroup\AxesGroup.cpp`。必须拒绝 rooted path 和 `..`。
- `ItemType`：MSBuild item type。`Infer` 规则必须至少支持：
  - `.cpp`、`.c`、`.cxx` -> `ClCompile`
  - `.h`、`.hpp`、`.hh`、`.hxx` -> `ClInclude`
  - `.rc` -> `ResourceCompile`
  - 其他 -> `None`
- `Filter`：`.vcxproj.filters` 中的显示 filter，例如 `Axes Group FIles\Interpolation Files`。必须保留调用方大小写和空格，不要纠正样例里的拼写。
- `AddToProject`：是否注册进 `.vcxproj`。第三方 include-only 文件也可能只需要落盘，不注册为 project item。
- `CreatePhysicalFile`：默认创建空文件。
- `ConflictPolicy`：文件或 project registration 已存在时如何处理。
- `AllowMsBuildFallback`：DTE `ProjectItems` 不稳定时允许 service 用 typed MSBuild API 更新 `.vcxproj` 和 `.filters`。

输出：

- `projectFilePath`
- `filePath`
- `itemType`
- `filter`
- `addedToProject`

验证：

- 文件存在于 project directory 下。
- `AddToProject=true` 时，`.vcxproj` 有对应 item。
- `Filter` 非空时，`.vcxproj.filters` 有对应 `Filter` 和 item 映射。
- 重开 solution 后 VS Solution Explorer 能看到 item。

Optcnc 用途：

- 创建 `MotionControl` 下的 `AxesGroup\*.cpp/.h`、`Axis\*.cpp/.h`、`Device\*.cpp/.h`、`CommandsExecuter\*.cpp/.h`、`Utilities\*.h/.hpp`、`MotionControl.rc`。
- 创建 `Ruckig` 下的 `extern\src\ruckig\*.cpp` 和 `extern\include\...` 支持文件。
- 创建 `Tinyxml2` 下的 `extern\tinyxml2.cpp/.h`。
- 创建 `AdsClient` 下的 `.cpp/.h`。

### `cpp.write-project-item-content`

人类操作：打开刚创建的 C++/resource/text project item，把源码文本粘贴或输入进去并保存。

分类 Category：`cpp`

建议 DTO：

```csharp
public enum ProjectItemWritePolicy
{
    FailIfMissing,
    FailIfNonEmpty,
    Overwrite
}

public sealed record WriteCppProjectItemContentRequest(
    string ProjectName,
    string RelativePath,
    string? ContentText = null,
    string? ContentFile = null,
    string Encoding = "utf-8",
    string NewLine = "preserve",
    ProjectItemWritePolicy WritePolicy = ProjectItemWritePolicy.Overwrite,
    bool RequireProjectRegistration = false);
```

输入：

- `ProjectName`
- `RelativePath`
- `ContentText` 或 `ContentFile` 二选一。`ContentFile` 是 JSON plan payload，不是目标工程文件复制源。
- `Encoding`：至少支持 `utf-8`、`utf-8-bom`、`ascii`。
- `NewLine`：`preserve`、`crlf`、`lf`。
- `WritePolicy`
- `RequireProjectRegistration`：为 `true` 时，如果文件没有在 `.vcxproj` 中注册则失败。

输出：

- `filePath`
- `sha256`
- `bytesWritten`

验证：

- 文件内容 hash 与输入 payload 一致。
- VS reopen 后文件可打开。
- 该 step 不修改 `.vcxproj`，只写已有 project file 的内容。

重要约束：

- 不能把样例目录当成 `ContentFile` 直接读。调用方如果要复用旧源码，必须先把源码文本显式放入 JSON payload 或 payload 文件，再由这个 step 写入目标 item。
- 这一步是“填代码”，不是“复制工程资产脚本”。

### `cpp.remove-project-item`

人类操作：在 C++ project 中删除模板生成但目标工程不需要的 item，可选择是否从磁盘删除文件。

分类 Category：`cpp`

建议 DTO：

```csharp
public sealed record RemoveCppProjectItemRequest(
    string ProjectName,
    string RelativePath,
    CppProjectItemType ItemType = CppProjectItemType.Infer,
    bool DeletePhysicalFile = true,
    bool RemoveFilterEntry = true,
    bool IgnoreMissing = false);
```

输入：

- `ProjectName`
- `RelativePath`
- `ItemType`
- `DeletePhysicalFile`
- `RemoveFilterEntry`
- `IgnoreMissing`

输出：

- `removedFromProject`
- `deletedFile`

验证：

- `.vcxproj` 和 `.filters` 不再引用该 item。
- `DeletePhysicalFile=true` 时文件不存在。
- 重开 solution 后 item 不再显示。

Optcnc 用途：

- 删除 TwinCAT/VS 模板生成但 `Ruckig`、`Tinyxml2`、`AdsClient` 样例不需要的默认 `.cpp/.h` item。

### `cpp.set-project-property`

人类操作：在 Visual Studio project property pages 中设置 project-level 或 configuration-level property。

分类 Category：`cpp`

建议 DTO：

```csharp
public sealed record SetCppProjectPropertyRequest(
    string ProjectName,
    string PropertyName,
    string Value,
    string? Condition = null,
    string? PropertyGroupLabel = null);
```

输入：

- `ProjectName`
- `PropertyName`：例如 `ConfigurationType`、`PlatformToolset`、`CharacterSet`、`TcSignTwinCat`。
- `Value`
- `Condition`：例如 `'$(Configuration)|$(Platform)'=='Release|TwinCAT RT (x64)'`。
- `PropertyGroupLabel`：例如 `Globals`、`Configuration`、`UserMacros`。可空。

输出：

- `projectFilePath`
- `propertyName`
- `condition`

验证：

- `.vcxproj` 中目标 `PropertyGroup` 存在且 property 值正确。
- DTE project property 或 build log 能反映该设置。

Optcnc 用途：

- 把 `Ruckig`、`Tinyxml2` 每个 TwinCAT config 的 `ConfigurationType` 设置为 `StaticLibrary`。
- 设置 `AdsClient` 的 `ConfigurationType=Application`、`CharacterSet=Unicode`。
- 设置 `MotionControl` `Release|TwinCAT OS (x64)` 的 `TcSignTwinCat=true`，如果不复用现有 signing step。

### `cpp.set-item-definition-property`

人类操作：在 C++ project property pages 中设置 `C/C++`、`Linker`、`Resource`、`Build Events` 等配置属性。

分类 Category：`cpp`

建议 DTO：

```csharp
public sealed record SetCppItemDefinitionPropertyRequest(
    string ProjectName,
    string ToolName,
    string PropertyName,
    string Value,
    string? Condition = null);
```

输入：

- `ProjectName`
- `ToolName`：至少支持 `ClCompile`、`Link`、`ResourceCompile`、`PostBuildEvent`。
- `PropertyName`：例如 `AdditionalIncludeDirectories`、`LanguageStandard`、`AdditionalLibraryDirectories`、`AdditionalDependencies`、`SubSystem`、`PreprocessorDefinitions`、`Command`。
- `Value`：调用方传完整值，包含需要保留的 inherited macro，例如 `%(AdditionalIncludeDirectories)`。
- `Condition`：可选 config/platform condition。

输出：

- `projectFilePath`
- `toolName`
- `propertyName`
- `condition`

验证：

- `.vcxproj` 对应 `ItemDefinitionGroup` 下的 tool property 值正确。
- Build log 中 include path、library path、dependency 或 language standard 生效。

Optcnc 用途：

- `Ruckig`：`ClCompile.AdditionalIncludeDirectories=$(ProjectDir)extern\include;%(AdditionalIncludeDirectories)`；`LanguageStandard=stdcpp17`。
- `Tinyxml2`：`LanguageStandard=stdcpp17`。
- `MotionControl`：设置 include path、`LanguageStandard=stdcpp17`、`Link.AdditionalLibraryDirectories`、`Link.AdditionalDependencies=Ruckig.lib;%(AdditionalDependencies)`、`PostBuildEvent.Command=copy MotionControlServices.h $(SolutionDir)AdsClient\`。
- `AdsClient`：设置 `ClCompile.AdditionalIncludeDirectories`、`PreprocessorDefinitions`、`Link.AdditionalLibraryDirectories`、`Link.AdditionalDependencies=TcAdsDll.lib;%(AdditionalDependencies)`、`Link.SubSystem=Console`。

### `cpp.set-project-item-metadata`

人类操作：在 VS project item 属性中设置 file-level build metadata。

分类 Category：`cpp`

建议 DTO：

```csharp
public sealed record SetCppProjectItemMetadataRequest(
    string ProjectName,
    string RelativePath,
    CppProjectItemType ItemType,
    string MetadataName,
    string Value,
    string? Condition = null);
```

输入：

- `ProjectName`
- `RelativePath`
- `ItemType`
- `MetadataName`：例如 `PrecompiledHeader`、`ExcludedFromBuild`。
- `Value`
- `Condition`

输出：

- `projectFilePath`
- `relativePath`
- `metadataName`
- `condition`

验证：

- `.vcxproj` 对应 item 下有正确 metadata。
- Build log 证明文件被正确 include/exclude 或 PCH 行为生效。

Optcnc 用途：

- `TcPch.cpp` 对所有 TwinCAT config 设置 `PrecompiledHeader=Create`。
- `MotionControlDriver.cpp` 在 `TwinCAT OS (x64)` config 设置 `ExcludedFromBuild=true`。
- `MotionControlMain.cpp` 在 `TwinCAT RT (x86/x64)` config 设置 `ExcludedFromBuild=true`。

### `engineering.publish-modules`

人类操作：对 TwinCAT C++ project 执行 publish modules / TMC code generation，让改过的 module source 和 interface 重新生成 `.tmc`。

分类 Category：`engineering`

现状：

- `PublishModulesRequest` 和 `PublishModulesResult` DTO 已存在于 `TwinCatRequests.cs`。
- 当前没有完整 public step spec、direct CLI path 和 JSON path。实现者应优先复用现有 DTO，补齐 service/CLI/catalog/tests，而不是发明重复接口。

建议 DTO 保持或最小扩展：

```csharp
public sealed record PublishModulesRequest(
    string ProjectName,
    int PostPublishDelayMs = 5000,
    int WaitForUpdatedTmcTimeoutMs = 30000);
```

输入：

- `ProjectName`
- `PostPublishDelayMs`
- `WaitForUpdatedTmcTimeoutMs`

输出：

- `updatedTmcPath`
- `succeeded`

验证：

- `.tmc` timestamp 在 publish 后更新，或 content hash 变化。
- `engineering.add-module-instance` 后续能从 `.tmc` 解析 module GUID。
- build 能继续执行。

Optcnc 用途：

- `MotionControl` 的 module `.cpp/.h` 和 interface source 由 step 写完后，必须 publish，再添加 `SimDriver1`、`Axis1` 等实例。

## P1 PLC Public Steps

这些 step 对当前 Optcnc 样例不是 P0，因为样例没有 PLC project/source 文件；但调用方明确要求 PLC 也不能用文件复制。实现者可以和 P0 一起做，避免以后返工。

### `plc.create-pou`

人类操作：在 TwinCAT PLC project 中 `Add > POU`。

建议 DTO：

```csharp
public enum PlcPouType
{
    Program,
    FunctionBlock,
    Function
}

public enum PlcImplementationLanguage
{
    ST,
    FBD,
    LD,
    CFC,
    SFC,
    IL
}

public sealed record CreatePlcPouRequest(
    string PlcProjectName,
    string PouName,
    PlcPouType PouType,
    PlcImplementationLanguage Language = PlcImplementationLanguage.ST,
    string? FolderPath = null,
    string? ReturnType = null,
    ProjectItemConflictPolicy ConflictPolicy = ProjectItemConflictPolicy.FailIfExists);
```

验证：

- PLC tree 中能看到 POU。
- `.plcproj` 引用该 `.TcPOU`。
- 对应 `.TcPOU` 文件存在。

### `plc.create-dut`

人类操作：在 TwinCAT PLC project 中 `Add > DUT`。

建议 DTO：

```csharp
public enum PlcDutKind
{
    Struct,
    Enum,
    Alias,
    Union
}

public sealed record CreatePlcDutRequest(
    string PlcProjectName,
    string DutName,
    PlcDutKind DutKind = PlcDutKind.Struct,
    string? FolderPath = null,
    ProjectItemConflictPolicy ConflictPolicy = ProjectItemConflictPolicy.FailIfExists);
```

验证：

- PLC tree 中能看到 DUT。
- `.plcproj` 引用该 `.TcDUT`。

### `plc.create-gvl`

人类操作：在 TwinCAT PLC project 中 `Add > Global Variable List`。

建议 DTO：

```csharp
public sealed record CreatePlcGvlRequest(
    string PlcProjectName,
    string GvlName,
    string? FolderPath = null,
    ProjectItemConflictPolicy ConflictPolicy = ProjectItemConflictPolicy.FailIfExists);
```

验证：

- PLC tree 中能看到 GVL。
- `.plcproj` 引用该 `.TcGVL`。

### `plc.write-item-source`

人类操作：打开 PLC POU/DUT/GVL，把 declaration 和 implementation source 填进去并保存。

建议 DTO：

```csharp
public enum PlcSourceItemKind
{
    Pou,
    Dut,
    Gvl
}

public sealed record WritePlcItemSourceRequest(
    string PlcProjectName,
    PlcSourceItemKind ItemKind,
    string ItemName,
    string? FolderPath = null,
    string? DeclarationText = null,
    string? DeclarationFile = null,
    string? ImplementationText = null,
    string? ImplementationFile = null,
    PlcImplementationLanguage Language = PlcImplementationLanguage.ST,
    ProjectItemWritePolicy WritePolicy = ProjectItemWritePolicy.Overwrite);
```

输入约束：

- `DeclarationText` 和 `DeclarationFile` 二选一。
- POU 可选 `ImplementationText` / `ImplementationFile`。
- DUT/GVL 不应要求 implementation。
- Payload 必须是 PLC source text，不是完整 `.TcPOU` / `.TcDUT` / `.TcGVL` XML。实现应由 service 负责序列化到 TwinCAT 文件格式。

验证：

- TwinCAT PLC editor reopen 后能看到 declaration/implementation。
- PLC compile/build 成功。
- `.TcPOU` / `.TcDUT` / `.TcGVL` 文件存在且由 source text 生成，不是从旧工程复制的 XML。

## P1 Full Optcnc Parity Steps

这些不是“源码复制作弊”本身的缺口，但如果目标是让 `D:\3rd_year\auto_sln` 更接近样例完整工程，也需要 public step。不要让 `guided-build` 或 sample-specific JSON 掩盖这些缺口。

### `engineering.create-scope-project`

人类操作：在 solution 中添加 TwinCAT Scope project。

建议输入：

- `ProjectName`
- `ProjectDirectory`
- `TemplateKind` 或 `CandidateTemplatePaths`

输出：

- `projectFilePath`
- `projectGuid`

验证：

- `.sln` 包含 `.tcmproj` project。
- Scope project 能在 VS/TwinCAT 中打开。

后续仍需要 Scope configuration step。不要复制样例 `.tcmproj` / `.tcscopex`。

### `scope.write-configuration`

人类操作：在 Scope project 中配置 chart、axis、channel、symbol。

建议输入：

- `ProjectName`
- `ConfigurationFile` 或 typed JSON payload
- chart/channel/symbol 列表

验证：

- Scope project reopen 后通道存在。
- 不能接受旧 `.tcscopex` 全文件复制作为唯一实现。

### `io.ensure-ethercat-device`

人类操作：在 TwinCAT IO tree 下添加 EtherCAT master/device。

建议输入：

- `DeviceName`
- `ParentTreePath`，默认 `TIID`
- `DeviceType` / `Subtype` / `EsiName`，按 TwinCAT COM 能稳定接受的字段设计
- `Disabled`
- `AdapterName` 或 `AdapterId` 可选

验证：

- `engineering.export-tree-item-xml` 能看到 device。
- `.tsproj` 中 IO tree 有对应 device，但不能通过 bulk `replace-project-io-section` 生成。

### `io.ensure-ethercat-box`

人类操作：在 EtherCAT device 下添加 slave/box。

建议输入：

- `DeviceName`
- `BoxName`
- `VendorId`
- `ProductCode`
- `RevisionNo`
- `AutoIncAddress` / fixed address
- `Disabled`

验证：

- XAE reopen 后 box 存在。
- 后续 mapping owner path 可引用该 box/PDO。

### `io.ensure-pdo-entry`

人类操作：配置 EtherCAT PDO entry。

建议输入：

- `OwnerTreePath`
- `Direction`：`Input` / `Output`
- `PdoIndex`
- `EntryIndex`
- `SubIndex`
- `Name`
- `DataType`
- `BitSize`

验证：

- Exported IO XML 能看到 PDO entry。
- `tsproj.ensure-mapping-link` 可以把 IO var 和 `TIXC^MotionControl^BeckhoffDriver1` var 连接起来。

### `tsproj.set-cpp-instance-metadata`

人类操作：在 TwinCAT C++ instance 属性中设置 instance-level metadata，例如 disabled state 或固定 ObjectId。

建议 DTO：

```csharp
public sealed record SetCppInstanceMetadataRequest(
    string CppProjectName,
    string InstanceName,
    string? ObjectId = null,
    bool? Disabled = null,
    bool? KeepUnrestoredLinks = null,
    string? ClassFactoryId = null,
    string? ModuleClassName = null);
```

约束：

- `ObjectId` 必须检查全 project 唯一，不能和 task、PLC、IO object 冲突。
- 不要接受整段 `TmcDesc` XML 作为输入。`TmcDesc` 应来自 module publish 和 add instance 流程。

验证：

- Exported instance XML 有对应 metadata。
- Reopen 后 metadata 保持。
- Mapping、parameter、pointer steps 仍能按 instance name 找到目标。

### `tsproj.ensure-symbol-watch`

人类操作：在 TwinCAT project 中创建 Symbol Watch entry。

建议输入：

- `WatchName`
- `Symbols`：symbol path、data type、comment、optional display format
- `ConflictPolicy`

验证：

- XAE reopen 后 watch 存在。
- `.tsproj` root `SymbolWatch` 不是通过 full-section replacement 复制。

## 不建议现在实现的内容

不要在没有 evidence 的情况下发明下面这些泛化 step：

- `tsproj.ensure-data-type`
- `tsproj.ensure-image-data`
- 任意 root `DataTypes` / `ImageDatas` XML builder

原因：这些 section 很可能应由 TwinCAT template、TMC publish、IO topology 或 build/activation 流程生成。当前缺口的直接原因是 source/project item/project settings 不能通过 public step 表达。若 P0/P1 IO steps 完成后仍缺 root `DataTypes` 或 `ImageDatas`，下一步必须先按 `docs/agent-change-playbook.md` 收集人类操作、变更前后 XML、节点路径、字段含义和真实 verification evidence，再补 dedicated primitive。不要猜。

## JSON Plan 目标形态

P0 step 完成后，Optcnc plan 应改成这种顺序：

1. `engineering.create-xae-solution`
2. `engineering.create-cpp-project` 创建 `Ruckig`、`Tinyxml2`、`MotionControl`
3. `engineering.create-vs-cpp-project` 创建 `AdsClient`
4. `cpp.remove-project-item` 清理模板默认项
5. `cpp.create-project-item` 为每个 `.cpp/.h/.hpp/.rc` 新建 item
6. `cpp.write-project-item-content` 把 payload 中的源码写入 item
7. `cpp.set-project-property` 设置 project/config properties
8. `cpp.set-item-definition-property` 设置 include/lib/link/language/resource/build event
9. `cpp.set-project-item-metadata` 设置 PCH 和 ExcludedFromBuild
10. `engineering.create-module` 创建 `MotionControl` module class
11. 对 module 生成文件执行 `cpp.write-project-item-content`，写入真实 module source
12. `engineering.publish-modules`
13. `engineering.add-module-instance`
14. 现有 `engineering.ensure-task` 和 `tsproj.*` 参数、pointer、mapping steps
15. `engineering.build-solution`

## Acceptance Gate / 完成标准

实现者完成 P0 后，必须提供：

- 所有新 step 的 DTO、service、catalog、CLI direct path、JSON plan path。
- `docs/reference/step-catalog.json` 和 `docs/reference/step-catalog.md` 已 regenerate。
- 至少一个 integration test 在真实 XAE/VS 工程里验证：
  - 新建 C++ project item。
  - 写入源码内容。
  - `.vcxproj` 和 `.filters` 注册正确。
  - 设置 include/lib/language/item metadata 后 build 成功或能进入明确的下一类 TwinCAT failure。
  - 新建普通 VS C++ console project 并加入 solution。
  - `engineering.publish-modules` 能更新 `.tmc` 或给出清晰失败。
- 更新 `t/optcnc-auto-sln-from-kit.json`，把源码迁移从“缺口”改为 public step 调用。
- 不引入任何复制样例 project 文件或源码目录的脚本。

## Handoff Prompt / 交给下一个 Agent 的文案

请在 `D:\3rd_year\TwinCatAutomationKit` 中实现 `docs/roadmap/optcnc-public-step-gap-requirements.md` 里的 P0 public steps。严格遵守 `AGENTS.md`、`docs/agent-change-playbook.md`、`ARCHITECTURE.md` 的层边界：DTO 放 `Abstractions`，TwinCAT/VS/MSBuild 行为放 `TwinCat` service，CLI 只解析并调用 service，step spec 改 `TwinCatStepCatalog.cs`，最后运行 `generate-docs` 更新 `docs/reference/*`。不要复制样例 `.sln/.tsproj/.vcxproj/.filters/.tmc`，不要写文件系统复制源码目录的脚本，也不要用 generic `.tsproj` XML escape hatch。完成后更新 `t/optcnc-auto-sln-from-kit.json`，用新增 step 显式创建 C++/resource project item、写入源码 payload、设置 C++ build settings、创建 `AdsClient`、publish `MotionControl` modules，并给出 dry-run 和真实 XAE/VS integration evidence。
