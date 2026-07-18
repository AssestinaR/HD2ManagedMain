# 重建链路清理盘点

**盘点日期：** 2026-07-18
**行为基线：** `6de5c33 refactor: move Core Unit reads to Adaptation`；已实际验收 patch 重构与跨护甲替换。
**范围：** Core、Adaptation 与 Manager 的 Unit/Patch 重建调用；不改变现有输出行为。

## 目标边界

- Manager 只发起操作、显示 facts/计划/报告；不得解析或写入 Patch。
- Core 持有 Mod facts、目标 archive 选择、same-key/cross-armor 的业务计划、文件事务和用户可读报告；不得拥有 Patch 二进制、GPU、Unit、BoneInfo 或 sidecar 的技术实现。
- Adaptation 是唯一的 Patch/sidecar/Unit/GPU/Material/Texture 读取、重建、写出和回读实现层。

## 当前运行时入口

| 入口 | Manager 调用 | Core 当前职责 | Adaptation 当前职责 | 结论 |
|---|---|---|---|---|
| Same-key 重建 | `ModDetailsPageViewModel` → `IModSameKeyReconstructionService` | facts 新鲜度、archive 选择、计划、输出目录/报告、验收 | 实际 source/target Unit 读取、target-shell 输出、archive 写入 | 输出已验收；Core 仍直接编排多个 Adaptation 技术类型，尚可收敛为 operation。 |
| Cross-armor 候选 | `CrossArmorTransferPlanWindow` → `ICrossArmorTransferCandidateService` | 已批准 mapping、候选策略、报告、输出事务 | source/target 读取、TransformInfo/BoneInfo 处理、SDK target-shell 重建、archive 写入 | 输出已验收；Core service 仍包含大量技术编排和诊断投影，尚可迁移为 Adaptation operation。 |
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

其中 same-key 的 source/target 读取、候选选择与 SDK target-shell dry-run 已于 `5e936aa` 切换到 Adaptation。`UnitMeshAdaptationPlanner`、`UnitMeshReplacementStrategy` 及其专属接口/测试已经没有 production caller，已删除。

随后，`CurrentGameMaterialFallbackResolver` 改为通过 Adaptation `GameDataUnitMeshReader` 读取明确 archive Unit，`EquipmentUnitCatalogService` 改为通过 Adaptation `PatchTocScanner` 与 `PatchUnitMeshReader` 检测 source 几何。确认无 Core production caller 后，Core 的 archive/patch Unit reader、Unit reader/writer/minifier/retargeter、bone reader、二进制 Unit model 及专属测试已删除。Core 仅保留 `PatchEntryPayload`，供不属于 Unit 解析的材质依赖闭包服务读取 patch payload。

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

## 剩余清理清单（按风险排序）

### 1. Core 重建编排仍未完全去技术化（高价值，需单独验收）

- `ModSameKeyReconstructionService` 仍直接构造 Adaptation scanner、reader、writer、target-shell output builder 和 patch archive writer。
- `CrossArmorTransferCandidateService` 仍直接构造并配置 Adaptation 的 rig reader、bone/transform diagnostics、reencoder、writer、output builder、material resolver 与 archive writer。
- 两者都应最终改为调用输入明确、输出稳定的 Adaptation operation；Core 只保留 archive/facts 选择、已批准 mapping、输出事务与用户报告。

这是剩余的主要架构项，但也是最不应仓促处理的一项：它们位于已通过游戏验证的输出链路，改动后必须重新执行 same-key 和 cross-armor 的实际验收。

### 2. Core 仍保有通用 Patch/Archive 二进制辅助实现（中风险，先分离 Material closure）

- `PatchTocScanner`、`PatchEntryPayloadReader`、`GameDataPackageResolver`、`StingrayMaterialReferenceReader` 以及相关 Core 接口/测试仍存在。
- 它们目前服务于 mod facts、冲突/部署图、材质/纹理闭包与 archive fallback；其中 `ArchiveDependencyResolver` 仍自行读取 Game Data 的 TOC、stream 和 GPU sidecar。
- 不能按“包含 Patch”一概删除：部署、索引、文件分组和只读 facts 属于 Core 的合法职责。下一步应仅把 Material closure 的二进制读取迁为 Adaptation operation，再复查哪些基础 scanner 可安全保留为只读 facts 适配器、哪些应删除。

### 3. Core 的 same-key 轻量报告 DTO（低风险，非功能阻塞）

- `UnitMeshAdaptationPlan`、`UnitMeshAdaptationStep`、`UnitMeshReplacementCandidate` 等当前只是 Core → Manager 的报告合同，不再携带 Unit binary model 或 serialized payload。
- 可在 operation 收敛时改名为业务化的 reconstruction report；现在删除会无谓触及 Manager 合同，暂不建议优先处理。

### 4. Adaptation 历史输出分支（独立高风险清理）

- 已标记 obsolete 但仍有测试和编译 warning：`StrictUnitMeshTransfer`、`StrictUnitMeshEditPreparer`、`SdkStyleUnitOutputBuilder`。2026-07-18 已尝试将其测试切至 `Processing.MeshTransfer` 并移除旧实现；100 个 Adaptation 测试中有 4 个失败（target-layout 分量重编码、未映射 influence 保留、材质 slot/section 扩容）。因此当前 `MeshTransfer` 不具备 strict 路径的完整兼容语义，旧分支必须保留，直至先补齐 parity 测试与实现。
- 仍有独立测试覆盖的旧 target-shell 支路：`TargetShellUnitOutputBuilder`、`TargetShellPatchReconstructor`。
- `SdkStyleTargetShellPatchOutputBuilder.CreateWithSectionRebuild` 已于 2026-07-18 在全仓库调用图确认无生产或测试调用后删除，避免跨护甲链路意外选择 generic section rebuild；底层 `SdkStyleTargetShellUnitReconstructor(allowSectionRebuild: true)` 和其专属测试仍保留，后续须作为独立兼容能力重新评估。
- `Processing/AdaptiveOutputWriter` 需要单独确认是否有独立产品入口，再决定删除或吸收。

### 明确保留、不是清理候选

- Patch 文件名、sidecar 归组、mod library 导入/导出、部署/回滚、冲突检测与 facts/SQLite archive index。
- `EquipmentUnitCatalogService`、`CurrentGameMaterialFallbackResolver` 的业务判断本身；它们已不依赖 Core Unit reader。
- Adaptation 当前 SDK target-shell reconstruction、archive writer、Unit/GPU/stream reader/writer 及其回读验证。

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

按 D 类逐个建立调用图、迁移测试、删除。`StrictUnitMeshTransfer` 必须先使 `MeshTransfer` 通过其 7 个兼容语义测试，且补充生产 target-shell smoke 后才可删除。每个可能影响 Unit 输出的批次单独提交并要求游戏验收。

## 不可违反的验收条件

- 任何删除前先确认生产调用点为零，测试引用不算生产调用。
- same-key 输出继续删除旧 Unit/Composite、写 current target shell、原样保留其余 entries 与 sidecars。
- cross-armor 保持已验证的：source palette 语义、target inverse-joint 重建、position-only mesh-space 变换、来源表面向量编码。
- 不把 Material/Texture closure、provider 推断或 section rebuild 重新引入 Unit 更新的前置条件。
- 每批提交前通过 `git diff --check` 与全量单元测试；输出行为变更的批次再做对应 smoke 和 Blender/游戏验收。