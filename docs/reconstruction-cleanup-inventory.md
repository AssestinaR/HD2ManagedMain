# 重建链路清理盘点

**盘点日期：** 2026-07-18
**行为基线：** `a5279e2 feat: checkpoint verified cross-armor shell reconstruction`
**范围：** Core、Adaptation 与 Manager 的 Unit/Patch 重建调用；不改变现有输出行为。

## 目标边界

- Manager 只发起操作、显示 facts/计划/报告；不得解析或写入 Patch。
- Core 持有 Mod facts、目标 archive 选择、same-key/cross-armor 的业务计划、文件事务和用户可读报告；不得拥有 Patch 二进制、GPU、Unit、BoneInfo 或 sidecar 的技术实现。
- Adaptation 是唯一的 Patch/sidecar/Unit/GPU/Material/Texture 读取、重建、写出和回读实现层。

## 当前运行时入口

| 入口 | Manager 调用 | Core 当前职责 | Adaptation 当前职责 | 结论 |
|---|---|---|---|---|
| Same-key 重建 | `ModDetailsPageViewModel` → `IModSameKeyReconstructionService` | facts 新鲜度、archive 选择、计划、输出目录/报告、验收 | 实际 source/target Unit 读取、target-shell 输出、archive 写入 | Core 仍泄漏 Unit/Patch 技术细节；须抽出 Adaptation 门面后收敛。 |
| Cross-armor 候选 | `CrossArmorTransferPlanWindow` → `ICrossArmorTransferCandidateService` | 已批准 mapping、候选策略、报告、输出事务 | source/target 读取、TransformInfo/BoneInfo 处理、SDK target-shell 重建、archive 写入 | 已采用正确底层实现，但 service 自己编排大量 Adaptation 类型；须迁移为 Adaptation operation。 |
| facts/索引/部署 | 多个 Manager service/view model | SQLite facts、archive 索引、部署、冲突分析 | Patch 解析器/资源读取被按需调用 | 保留在 Core；不是重建旧链路。 |

## 已确认的重复/遗留实现

### A. 可直接删除候选：无生产调用的旧 patch 批处理栈

以下 factory 仅在 `CoreServices` 内彼此构造；全仓库没有 Manager 或其他生产代码调用它们：

- `PatchArchiveDryWriter` / `PatchArchiveFileWriter` / `PatchArchiveBatchPlanner`
- `PatchUnitMeshEditor` / `PatchUnitMeshReplacementPlanner`
- `PatchUnitMeshAutomationReporter` / `PatchUnitMeshFolderAutomationReporter`
- `PatchUnitMeshSourceCatalogBuilder`
- 对应 `I*` 接口、Domain plan/DTO 和仅验证该栈的 Core tests。

它们是早期的 Core 自行解析、dry-run 重组并写出 Patch 的实现，与 `HD2ModAdaptation.PatchReconstruction.PatchArchiveWriter` 重复。删除前应先用代码引用检查再次确认 production callers 为零；随后实现和其专属测试必须在同一提交移除。

### B. 必须迁移后才能删除：Core 的旧 Unit 适配/计划栈

`SameKeyReconstructionPlanningService` 曾直接依赖下列 Core 技术类型：

- `PatchTocScanner`、`PatchUnitMeshReader`、`ArchiveUnitMeshReader`
- `UnitMeshAdaptationPlanner`、`UnitMeshReplacementStrategy`
- `UnitMeshReader`、`UnitMeshWriter`、`UnitMeshMinifier`、`UnitMeshRetargeter`

其中 same-key 的 source/target 读取、候选选择与 SDK target-shell dry-run 已于 `5e936aa` 切换到 Adaptation。`UnitMeshAdaptationPlanner`、`UnitMeshReplacementStrategy` 及其专属接口/测试已经没有 production caller，可单独删除。其余 Core Unit reader/writer/minifier/retargeter 仍被 catalog/material-fallback 或历史测试使用，必须继续按调用图拆分，不能在本批混删。

注意：`UnitMeshAdaptationPlanner` 的候选选择语义仍影响 same-key plan。迁移时必须先将它的稳定 candidate/evidence DTO 和测试迁到 Adaptation，不能以 cross-armor 的显式 mapping 直接替代 same-key 自动选择。

### C. 应保留但去技术化：Core 的业务服务

- `ModSameKeyReconstructionService`
- `CrossArmorTransferCandidateService`
- `AssetArchiveIndexService`、facts store、archive hash/index 状态服务
- `EquipmentUnitCatalogService`
- Mod library、部署、冲突和 profile 服务

这些服务可继续存在，但不能自行创建 `PatchTocScanner`、`PatchUnitMeshReader`、`GameDataUnitMeshReader`、`PatchArchiveWriter` 或解析 Unit payload。它们应调用 Adaptation operation，并以 Core DTO 承载业务计划和报告。

### D. Adaptation 内仍待后续盘点的历史分支

不属于本轮删除范围，但应在 Core 清理完成后另起盘点：

- `StrictUnitMeshTransfer`、`StrictUnitMeshEditPreparer`、`SdkStyleUnitOutputBuilder`；前者已经标记 obsolete，仍产生编译警告。
- `TargetShellUnitOutputBuilder` / `TargetShellPatchReconstructor` 与 SDK-style target-shell 输出并存。
- `SdkStyleTargetShellPatchOutputBuilder.CreateWithSectionRebuild`：跨护甲成功链路明确禁止 generic section rebuild，保留与否须先查独立调用方和测试用途。
- `Processing/AdaptiveOutputWriter` 与 PatchReconstruction writer 的职责可能重叠。

这些项目不得和 Core 第一批删除混在同一个提交中。

## 推荐提交批次

### 批次 1：删除无生产调用的 Core dry-run 批处理栈

1. 再做引用检查，确认 A 类类型仅被自身、CoreServices 和专属测试引用。
2. 删除 A 类实现、接口、Domain DTO、专属 tests 和 `CoreServices` factory。
3. 运行 Core/Adaptation 全量 tests；不需要游戏测试，因为运行时入口不变。

### 批次 2：把 same-key 自动计划迁入 Adaptation

1. 在 Adaptation 建立 same-key planning operation，输入为 source Patch、已选 archive 候选与明确 Game Data 路径；输出稳定候选、evidence 与结构诊断。
2. 将 candidate 选择、stream/section/bone evidence 和对应 Core tests 移到 Adaptation tests。
3. Core 的 `SameKeyReconstructionPlanningService` 收缩为 facts/archive 选择适配器，或由新的 Core orchestration DTO 替代。
4. 保持 `IModSameKeyReconstructionService` 的 Manager 合同不变；需执行 same-key smoke，涉及写出时必须 Blender/游戏验收。

### 批次 3：把 same-key/cross-armor 的技术执行收敛为 Adaptation operation

1. Adaptation operation 负责 source/target read、target-shell build、Unit/Composite removals、archive write 和回读。
2. Core service 仅传已批准 facts/mapping、创建输出事务、汇总报告。
3. 删除 Core 中剩余 Unit reader/writer/retargeter/minifier 及相关接口/测试。
4. 对 same-key 与 cross-armor 各做一次候选生成和外部验收；cross-armor 必须回归已成功的胸部位置、骨骼和材质。

### 批次 4：独立清理 Adaptation 历史实现

按 D 类逐个建立调用图、迁移测试、删除。每个可能影响 Unit 输出的批次单独提交并要求游戏验收。

## 不可违反的验收条件

- 任何删除前先确认生产调用点为零，测试引用不算生产调用。
- same-key 输出继续删除旧 Unit/Composite、写 current target shell、原样保留其余 entries 与 sidecars。
- cross-armor 保持已验证的：source palette 语义、target inverse-joint 重建、position-only mesh-space 变换、来源表面向量编码。
- 不把 Material/Texture closure、provider 推断或 section rebuild 重新引入 Unit 更新的前置条件。
- 每批提交前通过 `git diff --check` 与全量单元测试；输出行为变更的批次再做对应 smoke 和 Blender/游戏验收。