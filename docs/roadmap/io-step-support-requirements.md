# IO Step Support Requirements / IO Step 支持需求

English summary: This document records the requirement for adding dedicated TwinCAT IO steps so a JSON plan can reproduce the IO topology and mappings seen in the reference `OptcncTwinCAT` project without copying an old solution or hand-patching unknown XML.

## 背景

用户希望 `TwinCatAutomationKit` 的 step 模块支持操作 TwinCAT `IO` 节点，最终让 `D:\3rd_year\auto_sln\OptcncTwinCAT.sln` 生成出的 IO 树和参考工程 `D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT.sln` 里现有 IO 树一致。

这不是要求直接复制旧工程。按照 `README.md`、`AGENTS.md` 和 `ARCHITECTURE.md` 的规则，正确方向是把人类在 XAE 里对 IO 做的操作拆成 public step、service method、CLI path、JSON plan 能组合的小步骤，并留下 XML snapshot、XAE reopen/build/evidence。

原始期望文档位置是 `D:\3rd_year\auto_sln`。当前 agent 写文件权限只覆盖本仓库，所以本文先落在 `docs/roadmap/io-step-support-requirements.md`，后续可以复制到 `D:\3rd_year\auto_sln` 或让有权限的 agent 在目标目录创建同名文档。

## 2026-05-09 当前状态

本轮已经补齐并验证了第一批 dedicated IO step surface：

```text
engineering.create-io-device
engineering.create-ethercat-box
engineering.generate-io-mappings
engineering.search-io-devices
engineering.reload-io-devices
engineering.apply-io-tree-plan
ethercat.assert-product-revisions
tsproj.ensure-io-section
tsproj.ensure-io-device
tsproj.ensure-ethercat-box
tsproj.ensure-io-pdo
tsproj.ensure-io-box-image
tsproj.ensure-mapping-info
tsproj.ensure-io-mapping-link
tsproj.apply-io-topology-plan
tsproj.assert-io-topology-shape
tsproj.describe-io-topology
tsproj.compare-io-topology
```

`engineering.create-io-device` 和 `engineering.create-ethercat-box` 是工程级 XAE/Automation Interface step，不是 `.tsproj` XML mutation：它们调用 `ITcSmTreeItem.CreateChild`。Beckhoff 官方文档给出的稳定含义是：

- EtherCAT master：在 `TIID` 下 `CreateChild(name, 111, before, vInfo)`。
- E-Bus box/terminal/module：在 EtherCAT parent 下 `CreateChild(name, 9099, before, productRevision)`，例如 `EK1100-0000-0017`。

这两个 step 让 JSON plan 可以表达“人工在 XAE IO tree 添加 EtherCAT master / terminal / drive 节点”的操作，并把后续 PDO、SyncMan、FMMU、process image metadata 交给 XAE/ESI 生成；它们不会复制参考 `.tsproj` 的 raw metadata。

`engineering.generate-io-mappings` 也是工程级 step。它优先调用当前 `ITcSysManager` COM object 上的 `ITcSmCommands.GenerateMappings` / `ITcSmCommands2.GenerateMappings`；DTE command fallback 默认关闭，因为 `TwinCAT.生成映射` 这类 menu command 可能弹确认框。调用成功后仍必须用 `tsproj.assert-io-topology-shape` / `tsproj.compare-io-topology` 做验收，不能只看 command success。

`engineering.search-io-devices` 和 `engineering.reload-io-devices` 也是工程级 step，分别调用 `ITcSmCommands.SearchDevices` 和 `ITcSmCommands.ReloadDevices` / `ITcSmCommands2` 同名方法。它们不提供 DTE menu fallback，默认 suppress UI，并且必须配合 command timeout 和后续 topology guard 使用，避免无人值守运行卡在 Visual Studio/TwinCAT 确认框上。

`engineering.apply-io-tree-plan` 是批量工程级 wrapper，只编排 `engineering.create-io-device` 和 `engineering.create-ethercat-box` 的 `CreateChild` 请求。它的 payload 只允许包含 name、subtype、parent tree path、`vInfo/productRevision`、disabled 等人类操作参数，不允许承载 `.tsproj` raw metadata。`t\optcnc-auto-sln-from-kit.json` 已加入 `optcnc-io-tree-plan.json` 和 `applyOptCncIoTreeFromXae`，但默认 `includeEngineeringIoTreePlan=false`，因为完整 EL/AX/CU/iA/KEB 节点创建仍需要本机 XAE/ESI 返回成功并由 topology guard 验证；当前默认路径仍使用可解释 `.tsproj` skeleton，不声称 PDO/process-image parity。

`ethercat.assert-product-revisions` 是工程级 IO tree route 的前置 file-only guard。它扫描本机 Beckhoff EtherCAT device-description XML，确认 `CreateChild` 使用的 `productRevision/vInfo` 在本机 ESI catalog 中存在；缺失时提前失败，不启动 XAE，也不会进入可能弹设备选择/确认框的路径。当前 OptCNC 全量 IO tree payload 在本机结果是 `19` 个唯一 revision 中 `15` 个匹配、`4` 个缺失：`00F5060-F000`、`iA-GAI04A0_2xMII_REV_00020020`、`iA-MPA64B0 2xMII_REV00030022`、`iA-MPS32A0 2xMII 00020020`。这说明本机缺第三方 KEB/iA ESI，不能默认开启 XAE CreateChild 全量路线。

`t\optcnc-auto-sln-from-kit.json` 已新增 `optcnc-io-topology-skeleton.json`、`applyOptCncIoTopologySkeleton`、`assertOptCncIoTopologySkeleton` 和 `assertOptCncIoImageSkeleton`。该 skeleton 只通过 typed DTO 表达可解释结构：5 个 disabled Device、4 个 EtherCAT device process-image `Image` 节点、28 个 Box 层级、2 个 `MappingInfo` 和 6 条 `TIID^设备 3 (EtherCAT)` 到 `BeckhoffDriver1` 的关键 IO mapping link；不复制 sample raw IO XML，不包含 root `ImageDatas` bitmap、Box `ImageId`、`SyncMan`、`Fmmu`、`MBoxUserCmdData`、`Slot` 或 drive-manager `<Xml>`。

新增 `tsproj.assert-io-image-references` 是 read-only guard。它检查 root `ImageDatas/ImageData`、direct Device `Image`、Device `InfoImageId` 和 `ImageId` 引用关系，能抓出“Device 有 `InfoImageId` 但缺 direct `Image`”这种半截 process-image shape。样例的 `ImageId=118` 属于已知 system image id，不对应 root `ImageData`，所以 full parity guard 必须通过 `AllowedUnbackedImageIds` 显式登记，而不能用裸 dangling-reference 规则误判。

当前 sandbox 禁止写 `D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj`，所以真实目标 direct run 被拒绝。repo 内 probe copy 已验证同一 public step 可执行：

```text
Applied IO topology plan: 5 device(s), 28 box(es), 0 PDO(s), 2 MappingInfo item(s), 6 link(s).
IO topology shape matched: 5 device(s), 28 box(es), 0 PDO(s), 14 link(s).
```

剩余硬缺口已经缩小为：如何通过 XAE/ESI/硬件扫描或受控导入生成 reference 的 root `ImageDatas` bitmap、Box `ImageId`、107 个 PDO、382 个 Entry、`SyncMan`、`Fmmu`、`MBoxUserCmdData`、`Slot` 和 drive-manager metadata，而不是把 sample raw XML 转成 JSON 复制。

## 2026-05-11 Guard 状态

`tsproj.assert-io-topology-shape` 已从粗粒度数量检查扩展为更严格的 read-only guard。现在可以断言：

- 全局 Device、Box、Device direct Image、PDO、PDO entry、MappingInfo、OwnerA、root mapping link 数量。
- Device 直接 Box 数量、直接 Image 数量、直接子元素类型计数。
- Box 的 parent box、`BoxFlags`、`ImageId`、PDO/PDO entry 数量、直接/递归子 Box 数量、直接子元素和 `EtherCAT` 子元素类型计数。

OptCNC plan 因此分成两种 shape：

```text
optcnc-io-topology-skeleton-shape.json
  typed skeleton route
  5 devices / 28 boxes / 4 device images / 0 PDO / 0 PDO entries

optcnc-io-topology-sample-shape.json
  XAE/ESI route or reference read-only assertion
  5 devices / 28 boxes / 4 device images / 107 PDO / 382 PDO entries
```

这让后续执行不能再只用“5 个 Device、28 个 Box”宣称接近 reference；如果 XAE/ESI route 没有生成 PDO、PDO entry、Box image/process data 子结构，guard 会明确失败。当前新增的是 validation surface，不是 raw XML 生成 shortcut。

## 参考输入

- 目标参考工程：`D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT.sln`
- 参考 `.tsproj`：`D:\2nd_year\twincat0926\OptcncTwinCAT\OptcncTwinCAT\OptcncTwinCAT.tsproj`
- 当前生成工程：`D:\3rd_year\auto_sln\OptcncTwinCAT.sln`
- 当前生成 `.tsproj`：`D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj`

对照结果：

- 参考 `.tsproj` 有 `/TcSmProject/Project/Io`，XML 长度约 `108163` 字符。
- 当前 `auto_sln` `.tsproj` 缺少 `/TcSmProject/Project/Io`。
- 参考 `.tsproj` 有 `/TcSmProject/Mappings`，XML 长度约 `2838` 字符。
- 当前 `auto_sln` `.tsproj` 缺少 `/TcSmProject/Mappings`。

## 参考 IO 最终形态

参考工程的 `Project/Io` 下有 5 个直接 `Device`：

| Device Id | Name | Disabled | DevType | AmsPort | AmsNetId | Box count |
|---|---|---|---|---|---|---|
| `1` | `设备 1 (RT-Ethernet Adapter)` | `true` | `109` |  |  | `0` |
| `3` | `设备 3 (EtherCAT)` | `true` | `111` | `28675` | `169.254.101.118.4.1` | `20` |
| `4` | `设备 4 (EtherCAT)` | `true` | `111` | `28676` | `169.254.101.118.5.1` | `4` |
| `5` | `设备 5 (EtherCAT)` | `true` | `111` | `28677` | `169.254.101.118.6.1` | `2` |
| `6` | `设备 6 (EtherCAT)` | `true` | `111` | `28678` | `169.254.101.118.7.1` | `2` |

`设备 3 (EtherCAT)` 的 Box/Terminal/Drive 树包括：

- `节点 1 (CU2508)`
- `术语 2 (EK1100)`
- `术语 3 (EL1889)`
- `术语 4 (EL1889)`
- `术语 5 (EL1904)`
- `术语 6 (EL6910)`
- `术语 7 (EL9410)`
- `术语 8 (EL2889)`
- `术语 9 (EL2889)`
- `术语 10 (EL2904)`
- `术语 21 (EL9011)`
- `驱动器 11 (AX5118-0000-0214)`
- `术语 12 (AX5805)`
- `驱动器 13 (AX5125-0000-0214)`
- `驱动器 15 (AX5160-0000-0214)`，`Disabled=true`
- `术语 16 (AX5806)`
- `驱动器 17 (AX5125-0000-0214)`，`Disabled=true`
- `术语 18 (AX5805)`
- `驱动器 19 (AX5140-0000-0214)`，`Disabled=true`
- `术语 20 (AX5805)`

其他 EtherCAT device：

- `设备 4 (EtherCAT)`：`节点 22 (CU2508)`、`节点 23 (iA-MPS32A0 2xMII)`、`节点 24 (iA-MPS32A0 2xMII)`、`节点 25 (iA-GAI04A0 2xMII)`。
- `设备 5 (EtherCAT)`：`节点 26 (CU2508)`、`节点 27 (iA-MPA64B0 2xMII)`。
- `设备 6 (EtherCAT)`：`节点 28 (CU2508)`、`节点 29 (KEBDriveImage.bmp)`。

这些 Box 不是只有名字和 Id。参考 XML 中很多 Box 带 `Pdo`、`Var`、`ImageId`、`BoxFlags`、驱动器和 safety terminal 的专用结构。后续实现不能只创建空节点后宣布完成。

## 参考 Mappings 最终形态

参考工程根级 `/TcSmProject/Mappings` 包含：

- 2 个 `MappingInfo`。
- 5 个 `OwnerA`。

IO 到 TcCOM 的关键映射：

```text
OwnerA: TIID^设备 3 (EtherCAT)
  OwnerB: TIXC^MotionControl^BeckhoffDriver1
    术语 2 (EK1100)^术语 8 (EL2889)^Channel 1^Output -> Outputs^Power
    驱动器 13 (AX5125-0000-0214)^AT^Drive status word -> Inputs^DriverIOIn^StatusWord
    驱动器 13 (AX5125-0000-0214)^AT^Position feedback 1 value -> Inputs^DriverIOIn^ActualPos
    驱动器 13 (AX5125-0000-0214)^AT^Position feedback 2 value (external feedback) -> Inputs^DriverIOIn^ActualExtPos
    驱动器 13 (AX5125-0000-0214)^MDT^Master control word -> Outputs^DriverIOOut^ControlWord
    驱动器 13 (AX5125-0000-0214)^MDT^Position command value -> Outputs^DriverIOOut^TargetPosition
```

TcCOM 内部模块映射也在同一个 `Mappings` 段中，例如：

- `TIXC^MotionControl^AxesGroup0` 到 `Axis0`、`Axis1`、`Axis2`、`CommandsExecuter`。
- `Axis0` 到 `BeckhoffDriver1`。
- `Axis1` 到 `SimDriver1`。
- `Axis2` 到 `SimDriver2`。

当前 catalog 里已经有 `tsproj.ensure-mapping-link`、`tsproj.ensure-mapping-info` 和 `tsproj.apply-io-topology-plan`。剩余问题不再是 skeleton/mapping primitive 缺失，而是完整 PDO/process-image/drive metadata 的合法来源和 XAE reopen proof。

## 当前已有 Step 能力

已存在但不足以表达完整 IO 人类操作：

- `tsproj.ensure-io-section`、`tsproj.ensure-io-device`、`tsproj.ensure-ethercat-box`、`tsproj.ensure-io-pdo`、`tsproj.ensure-io-box-image`、`tsproj.ensure-mapping-info`、`tsproj.ensure-io-mapping-link`、`tsproj.apply-io-topology-plan`：已实现，可表达可解释 IO skeleton、PDO 和 mapping primitives。
- `tsproj.assert-io-topology-shape`、`tsproj.describe-io-topology`、`tsproj.compare-io-topology`：已实现，可作为只读 guard 和 normalized diff evidence。
- `engineering.search-io-devices`、`engineering.reload-io-devices`、`engineering.generate-io-mappings`：已实现，可表达 XAE 中扫描/重载设备和生成 mappings 的人类操作；真实结果仍取决于本机硬件、ESI 和 XAE 状态，必须用 topology guard 验证。
- `engineering.apply-io-tree-plan`：已实现，可批量表达 XAE `CreateChild` IO tree 人类操作；OptCNC plan 中默认关闭，打开后必须紧跟 `tsproj.assert-io-topology-shape` / `describe` / `compare`，不能只看 wrapper success。
- `tsproj.replace-project-io-section`：整段替换 `Project/Io`。可作为一次性 migration/evidence 工具，不应作为长期 public recipe 的唯一方案。
- `tsproj.replace-mappings-section`：整段替换根级 `Mappings`。同样只能作为过渡方案。
- `tsproj.ensure-io-task-image`：创建 task image，并绑定 instance `IoTaskImage` pointer；它不是 EtherCAT device/terminal topology 创建能力。
- `tsproj.upsert-fragment`、`tsproj.apply-mutation-plan`、`tsproj.merge-fragment`：generic/escape hatch。只有字段含义、父路径、验证方式清楚时才允许使用；不能绕过 dedicated API 缺口。

下一步建议补的不是更多 raw XML primitive，而是 XAE/ESI 来源 workflow：

```text
engineering.export-io-topology-model
tsproj.apply-io-topology-model
```

这些 step 必须记录 XAE/ESI 版本、扫描/导入来源、稳定模型字段和 reopen/build/compare evidence。

## 需要新增的 Dedicated Step

下面是建议拆分。命名可以由实现 agent 最终确认，但必须遵守 `<category>.<verb>-<target>`。

1. `tsproj.ensure-io-section`

   确保 `/TcSmProject/Project/Io` 存在，保持其他 `Project` 子节点不变。输出应包含是否新建、device count。

2. `tsproj.ensure-io-device`

   创建或更新一个 IO `Device`。输入至少包括 `DeviceId`、`Name`、`DevType`、`Disabled`、`DevFlags`、`AmsPort`、`AmsNetId`、`RemoteName`、`InfoImageId`、`AddressInfo`。需要支持 RT-Ethernet Adapter 和 EtherCAT device。

3. `tsproj.ensure-ethercat-box`

   在指定 `Device` 或 parent `Box` 下创建/更新 `Box`。输入至少包括 `DeviceId`、`ParentBoxId`、`BoxId`、`Name`、`BoxType`、`Disabled`、`BoxFlags`、`ImageId`、以及必要的 identity/address fields。要支持 terminal、drive、coupler、多层 Box。

4. `tsproj.ensure-io-pdo`

   在指定 `Box` 下创建/更新 `Pdo` 和其 process data entries。输入应能表达 `AT`、`MDT`、`Channel`、`Var`、bit size/offset、type、index/subindex 等从参考 XML 中能读出的字段。

5. `tsproj.ensure-io-box-image`

   创建/更新 Box 的 `ImageId` 和相关 image metadata。参考工程中很多 Box 依赖具体 `ImageId`，不能丢。

6. `tsproj.ensure-mapping-info`

   创建/更新根级 `Mappings/MappingInfo`，输入包括 `Identifier`、`Id`，必要时支持后续发现的其他属性。

7. `tsproj.ensure-io-mapping-link`

   可以复用或扩展 `tsproj.ensure-mapping-link`。如果 IO mapping 只需要 `OwnerAName/OwnerBName/VarA/VarB`，则优先复用；如果需要 `Size`、`RestoreInfo`、`GrpA`、`TypeA`、`InOutA`、`GuidA` 等属性，则扩展 DTO 或新增 dedicated step，不要把属性塞进 generic XML。

8. `tsproj.apply-io-topology-plan`

   Batch wrapper，读取 JSON payload，一次应用多个 `Device`、`Box`、`Pdo`、`MappingInfo` 和 `Link`。这个 step 只能编排 dedicated primitive，不应复制 XML mutation logic。

## 推荐实现路线

### Phase 1: Evidence 和字段含义调查

- 从参考 `.tsproj` 导出 `Project/Io`、`Mappings`、相关 XML snapshot。
- 在 XAE 中手动新增一个最小 EtherCAT device、一个 terminal、一个 drive 或从 scan/import 创建，保存前后 XML diff。
- 记录每个字段含义：`Id`、`DevType`、`DevFlags`、`AmsPort`、`AmsNetId`、`InfoImageId`、`BoxType`、`BoxFlags`、`ImageId`、`Pdo`、`Var`。
- 如果某字段只能从 known-good XML 获取，必须记录 `FragmentSource`、`TargetParentPath`、`FieldMeaning`、`VerificationEvidence`。

### Phase 2: 最小可用 IO topology API

- 实现 `ensure-io-section`、`ensure-io-device`、`ensure-ethercat-box`。
- 在 `TwinCatTsprojMutationService` 中集中实现 XML mutation。
- DTO 只放在 `Abstractions`，TwinCAT XML 逻辑只放在 `TwinCat`。
- 接入 `TwinCatStepCatalog`、`StepInvocationCatalog`、`StepInvokeCommand`。
- 更新 integration tests，使用真实 XAE 模板生成的 `.tsproj` 验证 reopen 后 IO tree 存在。

### Phase 3: PDO 和映射

- 实现 `ensure-io-pdo`、`ensure-mapping-info`。
- 复用或扩展 `ensure-mapping-link`，让参考工程中的 6 条 IO-to-`BeckhoffDriver1` link 可以由 plan 表达。
- 对 `Mappings` 的验证不能只看 XML 字符串；至少要 reopen XAE 并导出 tree item XML 或 build。

### Phase 4: Batch plan 和参考工程复刻

- 增加 `apply-io-topology-plan`，输入 JSON payload。
- 在 `examples/json-plans` 或 `docs/evidence` 中保留一个 `OptcncTwinCAT` IO payload 示例，但不要把 `D:\2nd_year\...` 作为可复用路径写进通用 example。
- 用 JSON plan 在短路径 workspace 生成工程，再应用 IO topology，最后和参考工程做 normalized XML 对比。

### Phase 5: Runtime/evidence

- 因参考工程中的 IO devices 当前都是 `Disabled=true`，第一阶段验收可以是 XAE reopen + build + normalized XML snapshot。
- 如果要证明真实硬件 IO，必须单独开启 device，并补充 activation archive 或 ADS/process image readback。不能把 build success 当成 runtime proof。

## 验收标准

完成后应满足：

- `run-plan` 能从 JSON payload 创建 `Project/Io` 和 `Mappings`，不依赖复制旧 solution。
- `D:\3rd_year\auto_sln\OptcncTwinCAT\OptcncTwinCAT.tsproj` 能生成与参考工程等价的 IO device/box/topology/mapping 结构。
- XAE reopen 后 IO tree 可见，设备、Box、terminal、drive 名称和层级正确。
- 参考中 5 个 Device、28 个 Box、2 个 `MappingInfo`、5 个 `OwnerA` 和关键 6 条 IO mapping link 均可验证。
- Step spec、implementation、CLI、JSON plan 行为一致。
- `docs/reference/step-catalog.json` 和 `docs/reference/step-catalog.md` 由 `generate-docs` 同步再生成，不能手改。
- 真实机器验证结果写入 `docs/evidence`，带中文 summary 和 raw log 指向。

## 明确禁止

- 禁止把 `Project/Io` 整段 XML 复制成最终长期方案。
- 禁止让 CLI 自己编辑 XML，必须调用 public service method。
- 禁止在有 dedicated primitive 时使用 `tsproj.upsert-*`、`tsproj.merge-fragment`。
- 禁止在字段含义不清楚时新增 generic fragment。
- 禁止把 `D:\2nd_year\twincat0926\...` 这种本机参考路径写成通用 example 的默认依赖。
- 禁止只让 `.tsproj` 文本有节点，而不做 XAE reopen/build/evidence。
