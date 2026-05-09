# Integration Test Strategy / 真实 TwinCAT 集成测试策略

English summary: This document explains why the integration suite groups the public TwinCAT step surface into seven real-machine scenarios, what each scenario proves, and the exact pass/fail evidence required for engineering, `.tsproj`, signing metadata, activation, and ADS validation steps.

这份文档是 `tests/TwinCatAutomationKit.IntegrationTests` 的审核说明。它不是简单列出“有 7 个测试”，而是说明这些测试为什么这样组织、每个测试到底证明什么、每类接口怎么算真正通过，以及哪些接口为什么明确不测。

根 [README.md](../../../README.md) 定义了项目目标：TwinCatAutomationKit 不是绕开 TwinCAT 从零手写工程文件，而是像人在 XAE 里一样使用 Beckhoff/TwinCAT 官方模板创建工程，再通过公开的细粒度 step/API 修改、build、activate、ADS readback。集成测试必须服务这个目标：证明公开接口能支撑真实 JSON plan/CLI 调用，而不是只证明测试代码自己能跑。

## 测试结论口径

当前默认 coverage 口径是：

- `TwinCatStepCatalog.All` 里共有 58 个 public step。
- 默认真实 TwinCAT 测试必须覆盖 55 个 step/interface。
- 3 个 OEM signing certificate/private-key step 明确排除：
  - `signing.grant-certificate`
  - `signing.sign-twincat-binary`
  - `signing.verify-twincat-binary`
- 排除不是 skip，也不是预期失败；它们在 `StepCoverageMatrix` 和 coverage test 中作为 `excluded-signing-certificate` 明确登记，原因是需要真实厂商/机器 signing credential，不能用伪证据冒充通过。

最后一个测试 `real scenario covers every open service interface` 会检查：

- catalog 里的 step 是否都在 `StepCoverageMatrix` 登记。
- 测试实际执行过程中是否把对应 service/interface 标记为 covered。
- README 是否提到每个 step kind。
- 只有上述 3 个 signing certificate step 可以不进入默认真实执行集合。

因此，“7 个测试通过”的含义不是“只测了 7 个接口”，而是 55 个默认 step/interface 被组织进 7 个真实机器场景里验证，并由 coverage contract 防止遗漏。

## 运行方式

```powershell
dotnet run --project .\tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests.csproj
```

本地配置文件：

```text
tests\TwinCatAutomationKit.IntegrationTests\TwinCatAutomationKit.IntegrationTests\Config\integration-test-config.json
```

必要条件：

- 本机可创建 `VisualStudio.DTE.17.0` COM session。
- Visual Studio/XAE 能创建 TwinCAT project。
- `EnableActivation=true`。
- `EnableAdsRead=true`，并且配置的 `AmsNetId` / `AdsPort` 能读到本机 runtime。
- signing certificate 三项默认不启用；`signing.set-license` 仍然验证。

缺少真实 TwinCAT/VS/activation/ADS 前置条件时，默认策略是失败而不是 skip。因为这些测试就是用来证明真实机器交互能力的。

## 组织原则

测试不是每个 step 单独创建一个工程。那样会非常慢，也会制造大量重复 artifact，而且有些接口天然依赖前一步结果，例如 instance binding 依赖 task ObjectId，ADS readback 依赖 build/activation。

当前策略是 ordered scenario：

- 前面的测试创建真实 XAE 工程、收集 task/instance/ObjectId 等地基。
- 后面的测试复用同一工程状态，验证 signing metadata、activation、ADS、boundary cases、atomic wrappers 和 coverage contract。
- 每个 step 仍然有独立判定点，不能只靠“场景最后通过”泛化带过。

## 7 个测试场景

### 1. `ordered-step-surface engineering + tsproj + reopen + build`

目的：验证工程基座是否可靠。这个测试覆盖大部分 `engineering.*` 和 `tsproj.*` step。

执行路径：

- 启动真实 VS/XAE DTE。
- 用 Beckhoff XAE template 创建 `.sln` 和 `.tsproj`。
- 先创建 PLC project，再创建 C++ project/module/instances/tasks。
- 读取真实 TwinCAT 返回的 task ObjectId。
- 关闭 VS，对 `.tsproj` 执行集中 file mutation。
- 重新打开完整 C++/PLC 工程，导出 `TIXC` 和 `TIRT` XML evidence。
- 派生 PLC-only runtime clone，执行 build。

为什么要 PLC-only runtime clone：

- 默认排除了 C++ signing certificate 三项。
- 如果 build/activation 阶段加载 unsigned C++ `.tmx`，TwinCAT 会弹出 OEM certificate warning，并且 runtime 不会加载该 C++ binary。
- 所以完整工程用于验证 C++/PLC 结构、mutation、reopen、export；runtime build/activation/ADS 用同源 PLC-only clone 验证，避免用签名问题污染非 signing 测试。

通过判定：

- `.sln`、`.tsproj`、PLC project、C++ project、`.vcxproj`、`.tmc`、module source artifacts 存在。
- XAE 可以 reopen mutated `.tsproj`。
- `ExportTreeItemXml` 对 C++ tree 和 task tree 成功产出 XML。
- `.tsproj` 精确断言 task priority/cycle/AMS port、task image、PLC vars、instance context、parameter、interface pointer、data pointer、mapping、generic fragment、TaskPouOid、InitSymbols 等语义。
- PLC-only runtime clone 上 `BuildCurrentSolution` 必须 `Succeeded=true` 且 `LastBuildInfo=0`。

### 2. `signing set-license writes C++ project metadata without certificate`

目的：验证 `signing.set-license` 这个不依赖真实私钥的 signing metadata 接口。

执行路径：

- 复用 ordered scenario 生成的 C++ project。
- 调用 signing service 写入 `TcSign` 相关 MSBuild metadata。
- 默认 `EnableSigning=false`，不尝试签名 binary。

通过判定：

- C++ `.vcxproj` 中出现预期 signing metadata。
- 不写入证书密码。
- 不把密码暴露到 command line。
- 这个测试不替代 `signing.sign-twincat-binary`，后者仍作为明确排除项。

### 3. `required activation writes configuration archive`

目的：验证真实 TwinCAT activation step 能执行，并留下 durable evidence。

执行路径：

- 复用 PLC-only runtime clone。
- 调用 `ActivateConfiguration`。
- 要求输出 `activated.tszip`。

通过判定：

- `ActivationResult.Succeeded=true`。
- activation archive 文件真实存在。
- 如果本机 `ITcSysManager.SaveConfiguration` 没有生成目标 archive，service 会写 fallback evidence zip，但测试仍要求 archive path 存在。

### 4. `required ADS scan/read validates runtime symbols`

目的：验证 runtime 不是“刚好激活了”，而是测试写入的 PLC 内容真的运行并可由 ADS 读回。

执行路径：

- 确保 activation 已执行。
- `validation.ads-scan` 扫描配置 ADS ports。
- `validation.ads-read-symbols` batch 读取配置 symbols 加测试强制 symbols。
- `validation.ads-read` 再对固定 deterministic symbol 做 single read。

严格通过判定：

- 至少一个配置 ADS port 可读 state。
- batch read 所有 symbols 成功。
- single read 固定读取 `MAIN.nConfiguredParameter`，必须精确返回 `12345`；不能依赖配置文件里“第一个 symbol”碰巧可读。
- ADS 精确读回这些 runtime proof：

| Symbol | 期望 |
|---|---|
| `MAIN.nCycle` | 大于 0，证明 PLC task 在运行。 |
| `MAIN.nConfiguredParameter` | `12345`，证明测试写入的 PLC 参数进入 runtime。 |
| `MAIN.nConvertedParameter` | `37052`，即 `(12345 * 3) + 17`，证明 PLC 运行时执行了确定转换。 |
| `MAIN.RuntimeTaskOidProbe` | 等于真实 TwinCAT `RuntimeTask` ObjectId 的数值形式。 |
| `MAIN.AuxTaskOidProbe` | 等于真实 TwinCAT `AuxTask` ObjectId 的数值形式。 |
| `MAIN.RuntimeOnlyInitSymbolProbe` | 等于真实 TwinCAT `AuxTask` ObjectId 的数值形式；PLC 源码里默认是 `0`，只能由 `.tsproj` `InitSymbol` 注入。 |
| `MAIN.nParameterChecksum` | `nConvertedParameter + RuntimeTaskOidProbe + AuxTaskOidProbe + 99`。 |
| `MAIN.bParameterTransformOk` | `true`，PLC 内部总判定成立。 |
| `MAIN.nMismatchCount` | `0`，runtime settle 后没有检测到 mismatch。 |

这个测试曾经因为 `RuntimeTaskOidProbe` ADS 读回为 0 而失败。失败说明旧标准太宽，只读 `nCycle` 不足以证明 runtime 语义成立。现在该断言是硬条件，不匹配就失败。

### 5. `real TwinCAT boundary cases fail safely`

目的：验证接口在真实工程上的错误输入会 fail safely，而不是静默写坏 XML。

覆盖例子：

- `tsproj.ensure-task-vars-group` 拒绝无效 count。
- `tsproj.set-task-affinity` 拒绝不存在的 task。
- `tsproj.bind-instance-task` 拒绝不存在的 instance。
- high-risk fragment merge 缺少必填 evidence 字段时失败。

通过判定：

- 每个非法调用必须抛出预期异常。
- 异常类型和消息能定位到错误字段或错误值，例如 `GroupName`、`Image Id`、`ObjectId`、`ByteOffset`、`VarA`。
- 每个 `.tsproj` mutation 失败用例都会比较调用前后文件内容；失败时 `.tsproj` 必须 byte-for-byte unchanged。
- batch plan 失败必须证明没有 partial write，例如第一项合法、第二项非法时，第一项也不能落盘。
- 不能出现“错误输入被接受但后面 build 才炸”的情况。

### 6. `atomic step wrappers execute against real TwinCAT project`

目的：验证 `TwinCatAtomicSteps` wrapper 不只是包了一层类型，而是真的能在已建立的真实工程/session/runtime 上执行。

执行路径：

- 对现有真实工程执行 atomic wrapper。
- 覆盖 engineering wrapper、activation wrapper、ADS wrapper 等公开执行路径。

通过判定：

- wrapper 调用返回 success outcome。
- `AutomationRunSummary` 必须包含每个 wrapper step，顺序、`StepExecutionStatus`、step kind、关键 outputs 都要匹配。
- `ExportTreeItemXml` wrapper 必须带 XML evidence artifact，并且 artifact 可按 `TreeItem` 解析。
- 对 `.tsproj` wrapper，不能只看 wrapper 返回 success；测试会重新读取 `.tsproj`，确认 wrapper 写入的 parameter、data pointer、generic element/fragment 是独有精确值，并确认 wrapper clear 掉的 stale data pointer / `UnrestoredVarLinks` 不再存在。
- 对 runtime wrapper，`ADS scan` 必须包含配置 runtime port，`ADS read` 必须返回 `MAIN.nConfiguredParameter=12345`，`ADS read symbols` 必须反序列化 `valuesJson` 并复用严格 runtime symbol 判定。
- pipeline evidence 必须写出 `run-summary.json` 和 `step-results.csv`，CSV 中必须包含每个 wrapper step id。
- 覆盖集合中登记 `TwinCatAtomicSteps.*` interface，防止只测 service method 不测 wrapper path。

当前 atomic wrapper scenario 只执行 `SaveAll`、`ExportTreeItemXml`、若干 `.tsproj` wrapper 和 ADS wrapper。`LaunchVisualStudio`、`CreateXaeSolution`、`BuildSolution`、`ActivateConfiguration` 等 wrapper 仍由 service-level 场景证明底层行为，尚未作为 wrapper path 单独执行；后续如果要求 wrapper 全量闭环，应补一个独立 wrapper scenario。

### 7. `real scenario covers every open service interface`

目的：这是 coverage contract，不验证业务行为本身，而是防止新增 public step 后忘记补测试和文档。

通过判定：

- `StepCoverageMatrix.MissingCatalogKinds()` 为空。
- `StepCoverageMatrix.UnknownMatrixKinds()` 为空。
- 每个 `StepCoverageSpec` 的 `Scenario`、`Dependencies`、`PassCriteria` 非空。
- 每条 `PassCriteria` 必须足够具体，并包含 artifact、XML、ADS、ObjectId、exact value、stale cleanup、exception 或 evidence 这类可执行判据。
- 实际 covered step kinds 包含所有默认要求的 step。
- README 提到每个 step kind。
- 只有 3 个 signing certificate step 在 excluded set。

## 每类 step 的通过标准

### Engineering / XAE step

不能只看 COM 调用没抛异常。必须至少满足对应 artifact 或 XAE proof：

- 创建类接口：文件、目录、project node、module metadata 必须存在。
- 创建类接口还要检查返回值：tree path、display name、file path、ObjectId 必须和请求/工程结构对应。
- `engineering.create-module` / `engineering.add-module-instance` 当前默认允许 offline fallback；测试必须明确断言 fallback artifact 能被 full-project reopen/export 接受。严格 COM wizard/CreateChild 路径需要 `AllowOfflineFallback=false` 的单独 evidence，不能由 fallback 结果替代。
- reopen 类接口：XAE 必须能重新打开 mutated solution。
- export 类接口：正常路径不允许 `UsedFallback=true`；`ProduceXml` evidence 必须写出，并解析为 `TreeItem` XML，检查 `ItemName`、`PathName`、subtype 和非空内容。
- 只有 missing-node boundary case 允许 fallback；fallback artifact 必须包含失败 tree path 和 TwinCAT lookup failure，而不能伪装成成功 export。
- build 类接口：`BuildCurrentSolution` 必须成功，`LastBuildInfo=0`，并产生 PLC `.tmc` / `.compileinfo`。
- build 产物不能只是存在；PLC `.tmc` / `.compileinfo` 必须是非空生成文件。
- activation 类接口：activation result 成功，记录实际 command，执行 restart，并有非空 archive；archive entry 不能是空占位，archive 内必须有可解释的 solution/config XML 或 fallback activation summary。

### `.tsproj` mutation step

通过标准是精确 XML 语义断言，不是字符串大概出现：

- task：priority、cycle、AMS port、affinity、image、vars group。
- instance：ObjectId、context、manual config、task binding。
- PLC：project path、AmsPort、ReloadTmc、instance metadata、`CLSID/ClassFactory`、vars、TaskPouOid、InitSymbols。
- C++ pointer/parameter/data pointer：container 和具体 value 都要对。
- mapping：四条 deterministic link 存在，`OwnerA` / `OwnerB` / `VarA` / `VarB` 都要匹配，link 数量也要匹配。
- generic mutation：目标 parent、fragment marker、evidence 字段要存在。
- task/PLC vars：不仅检查 symbol 名，还检查 group type、area、type、bit offset、external address 和数量。
- generic mutation：不仅检查 element 名，还检查 child value 和 attributes；`DataTypes` 必须是精确集合。

对于所有 `clear-*` 和 `replace-*` 类接口，测试采用更严格的污染恢复策略：

- 先写入明显错误的 stale 值，例如 `StalePointer`、`Parameter.stale=999`、`ST_StaleReplacedType`、stale mapping link、stale `UnrestoredVarLinks`。
- 再调用被测 `clear-*` 或 `replace-*` 接口。
- 再用专用 API 重建目标结构。
- 最后在 XAE reopen 之后重新读取 `.tsproj`，同时断言 stale 值不存在、目标值精确存在、关键集合数量正确。

XAE reopen/export 是第二层 proof：证明 XML 不只是“写得像”，还被真实 TwinCAT 接受。`InitSymbols` 还必须有 runtime-only ADS symbol proof：PLC 源码默认值为 `0`，只有 `.tsproj` `InitSymbol` 生效时才会读回目标 ObjectId。

对于失败路径，测试采用 fail-before-write 策略：

- 非法空名、非法 ObjectId、非法 count/size/offset、错误 section fragment、generic XML conflict policy 等都必须在真实 `.tsproj` 上失败。
- 失败消息必须点名错误字段或错误值。
- 失败前后 `.tsproj` 文件内容必须完全一致。
- batch API 不能部分成功；任何一项非法时，本批次前面合法项也不能落盘。

### Validation / runtime step

runtime step 必须有闭环：

- build 成功。
- activation 成功。
- ADS 能连接。
- ADS 读回测试写入并由 PLC 转换过的确定值。

只读一个 heartbeat 或 counter 不够，因为那只能证明 runtime 存活，不能证明工程内容符合我们设置。

ADS scan 也不能只看“任意 port 成功”。测试要求配置的 runtime port 成功返回 ADS state；另外 boundary test 会读取一个确定不存在的 symbol，必须失败并带错误信息。

`validation.ads-read` 也不能只读“配置里的第一个 symbol”。测试固定读取 `MAIN.nConfiguredParameter` 并要求精确等于 `12345`，这样 single read path 和 batch runtime proof 共享同一确定语义。

### Signing step

默认只验证不依赖私钥的 `signing.set-license`：

- metadata 写入。
- 不暴露 password。
- 不伪造 binary signing 成功。

真实 binary sign/verify 需要 OEM credential，因此默认排除。以后如果本机提供真实 credential，可以单独开启并补 evidence。

## 当前 step 判定矩阵

| Step kind | 场景 | 自动判定 |
|---|---|---|
| `engineering.launch-visual-studio` | `ordered-step-surface` | DTE session 可创建，后续真实 XAE 操作可继续。 |
| `engineering.create-xae-solution` | `ordered-step-surface` | `.sln` 和 `.tsproj` 由 Beckhoff XAE template 创建。 |
| `engineering.create-cpp-project` | `ordered-step-surface` | 返回的 tree path/display name/file path 精确匹配；`.vcxproj` 有 `ProjectGuid`，`.tmc` 存在。 |
| `engineering.create-vs-cpp-project` | `ordered-step-surface` | 创建普通 VS C++ `AdsClient` project，`.sln`、`.vcxproj` 和 DTE model 都能定位。 |
| `engineering.ensure-solution-project-dependency` | `ordered-step-surface` | `.sln` 中出现 `ProjectSection(ProjectDependencies)`，`AdsClient` 依赖 TwinCAT project 的 GUID 精确匹配。 |
| `engineering.create-plc-project` | `ordered-step-surface` | `.tsproj` 可解析 PLC project/instance，测试写入 `MAIN.TcPOU` payload，并由 build/ADS 证明 payload 加载。 |
| `engineering.create-module` | `ordered-step-surface` | 辅助 module 写入 header/source/TMC metadata，返回名包含请求名；允许 fallback 时必须被 full-project reopen/export 接受，不冒充严格 wizard proof。 |
| `engineering.publish-modules` | `ordered-step-surface` | 调用 `PublishModules` 后 `.tmc` 可读并包含 `AuxModule` metadata；`updated` output 记录本次 timestamp/hash 是否变化。 |
| `engineering.add-module-instance` | `ordered-step-surface` | primary/aux instance 返回可解析 ObjectId，DisplayName 包含请求名，并在 `.tsproj` 中按精确 ObjectId 存在；允许 fallback 时必须被 full-project reopen/export 接受。 |
| `engineering.ensure-task` | `ordered-step-surface` | 两个 task 返回可解析 ObjectId，并在 reopen 后精确匹配 priority、cycle、AMS port、affinity、layout。 |
| `engineering.export-tree-item-xml` | `ordered-step-surface` | `TIXC` 和 `TIRT` 导出 XML evidence，并解析校验 `TreeItem`、`ItemName`、`PathName`、subtype。 |
| `engineering.save-all` | `ordered-step-surface` | close/reopen 后 `.tsproj` mutation 仍存在，build 可继续。 |
| `engineering.close-visual-studio` | `ordered-step-surface` | 关闭 VS 后执行 `.tsproj` file mutation；runner 结束统一清理 session。 |
| `engineering.open-xae-solution` | `ordered-step-surface` | file mutation 后 XAE reopen 不抛异常，且 export XML 成功。 |
| `engineering.build-solution` | `ordered-step-surface` | PLC-only runtime clone 上 `BuildCurrentSolution` 返回成功和 `LastBuildInfo=0`，并生成或更新非空 PLC `.tmc` / `.compileinfo`。 |
| `engineering.activate-configuration` | `activation-ads-runtime` | `EnableActivation=true` 时 activation 成功，记录 command/restart，并写出非空 `activated.tszip`。 |
| `cpp.create-project-item` | `ordered-step-surface` / `atomic-step-wrappers` | 创建 `.cpp`、`.h`、`.rc`、`None` item，物理文件存在，`.vcxproj` 注册，`.filters` filter mapping 精确存在。 |
| `cpp.write-project-item-content` | `ordered-step-surface` / `atomic-step-wrappers` | payload 内容写入 project item，SHA256 / bytes output 与文件一致，且 `RequireProjectRegistration=true` 能防止未注册文件。 |
| `cpp.remove-project-item` | `ordered-step-surface` | `.vcxproj` 和 `.filters` 不再引用 exact item，物理 `.license` 文件被删除。 |
| `cpp.set-project-property` | `ordered-step-surface` | `.vcxproj` XML 中 `CharacterSet` / `ConfigurationType` 等 property 写入目标 `PropertyGroup`。 |
| `cpp.set-item-definition-property` | `ordered-step-surface` | `.vcxproj` XML 中 `ClCompile.LanguageStandard`、include path、Link setting 等 tool property 精确存在。 |
| `cpp.set-project-item-metadata` | `ordered-step-surface` / `atomic-step-wrappers` | `.vcxproj` item metadata 写入 `ExcludedFromBuild` / `PrecompiledHeader`，atomic summary 记录 wrapper 输出。 |
| `signing.set-license` | `signing-metadata` | 先污染 stale signing password/duplicate metadata，再写入唯一 `TcSign` PropertyGroup，`TcSignTwinCat=false`、license name 精确匹配、无 password、无重复节点。 |
| `signing.grant-certificate` | `excluded-signing-certificate` | 默认排除；需要本机真实 TwinCAT OEM signing certificate/private key。 |
| `signing.sign-twincat-binary` | `excluded-signing-certificate` | 默认排除；没有真实 OEM signing credential 时不伪造通过。 |
| `signing.verify-twincat-binary` | `excluded-signing-certificate` | 默认排除；`sign` 被排除时没有可验证的真实签名产物。 |
| `tsproj.ensure-task` | `ordered-step-surface` | `RuntimeTask` 和 `AuxTask` attributes 写入；重复 ensure 不产生错误。 |
| `tsproj.clear-task-layout` | `ordered-step-surface` | 先写入 stale `Vars` / `Image`，clear 后重建 `AuxInputs` / `AuxOutputs` / `Image`，reopen 后 stale layout 不存在。 |
| `tsproj.ensure-task-vars-group` | `ordered-step-surface` | `AuxInputs` / `AuxOutputs` 按 group type、count、type、offset、external address 精确生成。 |
| `tsproj.ensure-task-image` | `ordered-step-surface` | `AuxTask` 的 `Image` Id/address type/image type/size/name 精确写入，reopen 接受。 |
| `tsproj.ensure-cpp-instance` | `ordered-step-surface` | `FileMutationCpp01` instance skeleton、ObjectId、context priority/cycle 和 `TmcDesc` containers 插入。 |
| `tsproj.ensure-plc-instance` | `ordered-step-surface` | PLC instance node 存在，后续 metadata/vars mutation 可定位。 |
| `tsproj.bind-instance-context` | `ordered-step-surface` | aux instance context `ManualConfig` 和 `CyclicCaller` 指向 `AuxTask`。 |
| `tsproj.bind-instance-task` | `ordered-step-surface` | primary instance context `ManualConfig` 和 `CyclicCaller` 指向 `RuntimeTask`。 |
| `tsproj.bind-plc-instance-task` | `ordered-step-surface` | PLC context `ManualConfig` 指向 `RuntimeTask` ObjectId。 |
| `tsproj.set-task-affinity` | `ordered-step-surface` | 两个 task 都写入 `Affinity` 和 `AdtTasks`。 |
| `tsproj.set-plc-project-properties` | `ordered-step-surface` | PLC project 的 path、`ReloadTmc`、`AmsPort`、archive settings 被写入。 |
| `tsproj.set-plc-instance-metadata` | `ordered-step-surface` | PLC instance metadata 和 `CLSID/ClassFactory` 写入，不替换 vars。 |
| `tsproj.clear-plc-instance-vars` | `ordered-step-surface` | 先写入 stale PLC vars，clear 后重建 deterministic input/output groups，reopen 后 stale var 不存在。 |
| `tsproj.ensure-plc-instance-vars-group` | `ordered-step-surface` | `MAIN.nSeed`、`MAIN.nStage1` 等 PLC process-image vars 的 group type、AreaNo、type、offset、external address 精确匹配。 |
| `tsproj.clear-plc-init-symbols` | `ordered-step-surface` | 先写入 stale `InitSymbol`，clear 后重建 ObjectId-derived symbols，reopen 后 stale symbol 不存在。 |
| `tsproj.clear-plc-task-pou-oids` | `ordered-step-surface` | 先写入 priority 99 stale `TaskPouOid`，clear 后重建 runtime `TaskPouOid`，reopen 后 stale entry 不存在。 |
| `tsproj.ensure-task-pou-oid` | `ordered-step-surface` | PLC `TaskPouOid` 写入 runtime task priority/ObjectId。 |
| `tsproj.ensure-init-symbol` | `ordered-step-surface` | `MAIN.RuntimeTaskOidProbe`、`MAIN.AuxTaskOidProbe` 和 runtime-only probe 写入 ObjectId-derived data；XML 断言 `Type/GUID/AreaNo/Data`，ADS 证明 runtime-only value 由 `InitSymbol` 注入。 |
| `tsproj.replace-data-types-section` | `ordered-step-surface` | 先替换成 stale `ST_StaleReplacedType`，再替换为 deterministic test types，reopen 后 stale type 不存在。 |
| `tsproj.replace-system-settings-section` | `ordered-step-surface` | 先替换成带 `StaleFragment` 的 settings，再替换为目标 settings，reopen 后 stale marker 不存在且 `Tasks` 保留。 |
| `tsproj.replace-project-io-section` | `ordered-step-surface` | 先替换成 stale `Io`，再替换为 JSON-owned 非关键 runtime section，reopen 后 stale Io 不存在。 |
| `tsproj.ensure-io-task-image` | `ordered-step-surface` | task image 与 primary instance `IoTaskImage` pointer 一起生成，并精确指向 `#x03040010`。 |
| `tsproj.clear-instance-parameter-values` | `ordered-step-surface` | 先写入 stale `Parameter.stale=999/888`，clear 后由 plan 重建，reopen 后 stale parameter 不存在。 |
| `tsproj.apply-instance-parameter-plan` | `ordered-step-surface` | batch plan 精确写入 primary `Parameter.data1=123` 和 aux `Parameter.data1=17`，reopen 后按值断言。 |
| `tsproj.ensure-parameter` | `ordered-step-surface` / `atomic-step-wrappers` | 单项 parameter upsert 在 batch 后保持幂等；wrapper 路径另外写入独有 `Parameter.atomicWrapper=456` 并按值断言。 |
| `tsproj.apply-instance-interface-pointer-plan` | `ordered-step-surface` | batch plan 写入 primary/aux `CyclicCaller`。 |
| `tsproj.ensure-interface-pointer` | `ordered-step-surface` | 单项 interface pointer upsert 在 batch 后仍保持幂等。 |
| `tsproj.clear-instance-data-pointer-values` | `ordered-step-surface` / `atomic-step-wrappers` | 先写入 stale `StalePointer` / `AtomicWrapperPointer`，clear 后由 plan 重建，reopen 后 stale pointer 不存在。 |
| `tsproj.apply-instance-data-pointer-plan` | `ordered-step-surface` / `atomic-step-wrappers` | batch plan 精确写入 primary/aux `DataIn` / `DataOut` 的 `OTCID`、`AreaNo`、`ByteOffs`、`ByteSize`。 |
| `tsproj.ensure-data-pointer` | `ordered-step-surface` | 单项 data pointer upsert 在 batch 后保持幂等，reopen 后精确字段不变。 |
| `tsproj.clear-mappings` | `ordered-step-surface` | 先写入 stale mapping link，clear 后重建 4 条 deterministic links，reopen 后 stale link 不存在且 link 数量等于 4。 |
| `tsproj.replace-mappings-section` | `ordered-step-surface` | 先替换成 stale known-good `Mappings`，再替换为空 section 并重建 deterministic links，reopen 后 stale link 不存在。 |
| `tsproj.clear-unrestored-var-links` | `ordered-step-surface` / `atomic-step-wrappers` | 先写入 stale `UnrestoredVarLinks`，clear 后 reopen/build 或 wrapper 断言中都必须不存在。 |
| `tsproj.ensure-mapping-link` | `ordered-step-surface` | 四条 PLC/C++ process-image mapping links 写入，完整工程 XAE reopen/export 接受。 |
| `tsproj.upsert-element` | `ordered-step-surface` | generic element upsert 在 `Project/System` 下写入 metadata，并精确校验 attributes/child values。 |
| `tsproj.upsert-fragment` | `ordered-step-surface` | generic fragment upsert 在 `Project/System` 下写入 named fragment，并精确校验 child values。 |
| `tsproj.apply-mutation-plan` | `ordered-step-surface` | batch generic mutation 写入 element 和 fragment，并精确校验 batch child values；负例证明 valid+invalid batch 不 partial write。 |
| `tsproj.merge-fragment` | `ordered-step-surface` | 在唯一 `DataTypes` parent 下执行带 evidence 字段的 named fragment merge，最终 `DataTypes` 集合必须精确。 |
| `validation.ads-scan` | `activation-ads-runtime` | `EnableAdsRead=true` 时配置的 runtime port 必须可读 ADS state，不接受仅其他 port 成功。 |
| `validation.ads-read-symbols` | `activation-ads-runtime` | batch ADS read 必须精确读回配置参数、转换结果、task ObjectId probes、runtime-only InitSymbol probe、checksum、transform-ok flag 和 mismatch count。 |
| `validation.ads-read` | `activation-ads-runtime` | 固定读取 `MAIN.nConfiguredParameter` 并精确返回 `12345`，证明 batch 与 single read 两条 ADS path 都读到同一确定 runtime 语义。 |

## 维护规则

- 新增 public step kind 时，必须同时更新 `StepCoverageMatrix.cs` 和本文档；`real scenario covers every open service interface` 会检查遗漏。
- `StepCoverageMatrix` 的通过条件必须写成可执行证据，不接受“调用成功”“不抛异常”这类宽泛口径。
- 新增 `.tsproj` mutation 时，要优先写专用 API 和精确 XML 断言；不要只用 generic XML 操作掩盖能力缺口。
- 新增 `.tsproj` mutation 的负例时，必须证明失败不改文件；batch API 还要证明没有 partial write。
- 新增 runtime 行为时，要有 build、activation、ADS readback 或等价真实 evidence；只检查文件存在不够。
- 真实失败时优先保留短路径 workdir 或把关键 XML/build/ADS 结果沉淀到 `docs/evidence`，不要只留下 root `*.log`。
- 3 个 OEM signing certificate step 只有在本机提供真实 credential 并补充 evidence 后，才应从默认排除项移出。
