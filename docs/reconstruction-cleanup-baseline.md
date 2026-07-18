# 重建链路后续清理基线

**建立日期：** 2026-07-18
**代码基线：** `2bd4ec8 refactor: remove unused core material closure resolver`
**验收基线：** same-key Patch 重构与 cross-armor 护甲替换均已完成实际流程测试；当前文档仅记录后续清理边界，避免重复调用图分析。

## 已完成，后续不再重复盘点

- `HD2ModManager` 仅负责 UI 编排；Patch/Unit/GPU/sidecar 的二进制写入不在 Manager 内实现。
- same-key 的 binary execution 已由 Adaptation `SameKeyTargetShellReconstructionOperation` 负责：current target shell 构建、旧 Unit/Composite 删除、archive 写入、sidecar 边界检查及 Unit/BoneInfo 回读。
- cross-armor 的 binary output 已由 Adaptation `CrossArmorTargetShellPatchOperation` 负责：SDK target-shell 输出、旧 Unit/Composite 删除、依赖条目合并、archive 写入和输出 Unit 集合验证。
- Core 旧的 Unit binary model、reader/writer/minifier/retargeter、骨骼 reader 及其专属实现已删除。
- Core 无生产调用的 `ArchiveDependencyResolver` 与其 factory 已删除；候选写出使用 Adaptation `MaterialDependencyResolver`。
- `SdkStyleTargetShellPatchOutputBuilder.CreateWithSectionRebuild` 已删除；cross-armor 成功链路不得回退到 generic section rebuild。

## 当前明确边界

| 层 | 允许的职责 | 不允许的职责 |
|---|---|---|
| Manager | 交互、后台任务、进度、页面状态、调用 Core | 解析或写入 Patch/Unit/sidecar |
| Core | 模组库、SQLite facts、archive 索引、业务计划、部署、用户报告 | 自行拥有 Patch/Unit/GPU/BoneInfo binary reader/writer 或技术重建 |
| Adaptation | Patch/Unit/sidecar/Material/Texture 二进制读取、重建、写出、回读 | Manager UI 或 profile/library 业务 |

## 尚未清理项

### A. Core 到 Adaptation 的剩余技术编排（高风险，必须实际验收）

1. `CrossArmorTransferCandidateService`
   - 仍在 Core 中读取 source/target Unit、解析材质闭包、读取 avatar rig、进行骨骼/skin 诊断、扩展 TransformInfo、展开 LOD family、准备 work items、做回读诊断和生成报告。
   - 已迁出的部分是最终 Patch 写出；继续迁移会触及已游戏验收的 cross-armor 链路。
   - 迁移后必须重新执行：候选生成、same-key 流程与 cross-armor 游戏内外观/姿态/材质/原版壳回归。

2. `SameKeyReconstructionPlanningService`
   - same-key 的执行已在 Adaptation；规划服务仍在 Core 内进行 source/current-target Unit 读取和 planning DTO 投影。
   - 后续可收敛为 Adaptation planning operation；Core 保留 archive/facts 选择和 Manager 报告合同。

3. `EquipmentUnitCatalogService` / `CurrentGameMaterialFallbackResolver`
   - 两者通过 Adaptation reader 获取只读 Unit facts。
   - 先判定是否继续作为合法 Core facts adapter；若保留，不应再视作 binary execution 清理目标。

### B. Core 的只读 Patch / archive 基础设施（中风险，逐 caller 判定）

- `PatchTocScanner`、`PatchEntryPayloadReader`、`GameDataPackageResolver`、`StingrayMaterialReferenceReader` 和对应接口/工厂仍存在。
- 不能按类型名批量删除：它们可能服务于 facts、部署、冲突、导入、只读索引及材质验证。
- 每一项后续处理前都需要确认：是否为生产 caller 使用、是否只读、是否与 Adaptation 实现重复、迁移后是否仍需要 Core 业务适配层。

### C. Core 报告 DTO（低风险、非阻塞）

- `UnitMeshAdaptationPlan`、`UnitMeshAdaptationStep`、`UnitMeshReplacementCandidate` 及相关 enum 当前是 Core → Manager 的报告合同。
- 它们不再携带 binary model；可在 planning operation 收敛后改成业务化命名，但不建议为删除而单独触及 UI 合同。

### D. Adaptation 历史输出路径（独立高风险清理）

1. `StrictUnitMeshTransfer`、`StrictUnitMeshEditPreparer`、`SdkStyleUnitOutputBuilder`
   - 有专属测试且仍互相引用。
   - `MeshTransfer` 尚未达到 strict parity：已验证缺少 target-layout 分量重编码、未映射 influence 保留、material slot/section 扩容等语义。
   - 前置条件：先补齐 parity 实现与测试，再通过 strict 测试、production target-shell smoke 和实际验收；此前禁止删除。

2. `TargetShellUnitOutputBuilder`、`TargetShellPatchReconstructor`
   - 当前仅确认有专属测试，尚未确认是否有诊断工具或间接 production caller。
   - 后续动作：先完成调用图与输出行为对比；若生产调用为零，则在同一提交删除实现、专属测试和只服务该链的 DTO。

3. `Processing/AdaptiveOutputWriter`
   - 当前没有确认的主运行时入口，但可能服务诊断/旧工具。
   - 后续动作：先审计所有 diagnostics 和工具项目的调用关系、输出 sidecar/closure 能力；不可按“无 Manager 调用”直接删除。

4. `SdkStyleTargetShellUnitReconstructor(allowSectionRebuild: true)`
   - 仍作为独立兼容能力与专属测试存在。
   - cross-armor 成功链路明确不得调用它；是否删除需独立决定，不能与业务迁移混批。

## 不可违反的回归条件

- same-key：删除旧 Unit/Composite、写入 current target shell、原样保留其余 entries 与 `.stream`/`.gpu_resources`，且回读通过。
- cross-armor：保持 source palette 语义、target inverse-joint matrix 重建、仅 position 的 mesh-space transform、来源 surface vector 编码、完整 target shell minify/replacement 策略；禁止 generic section rebuild 进入成功链。
- 不将 Material/Texture closure、provider 推断、全局扫描作为 Unit 更新的隐式前置条件。
- 删除前确认 production caller 为零；测试和 diagnostics caller 必须单独列明并处置。
- 每批先 `git diff --check`、Core/Adaptation 全量测试、Release build；触及任何实际输出路径时，额外要求对应流程和游戏验收。

## 建议顺序

1. 先只做 `TargetShellUnitOutputBuilder` / `TargetShellPatchReconstructor` 调用图与输出对比。
2. 再审计 `AdaptiveOutputWriter` 的 diagnostics/tool caller。
3. 之后才考虑 same-key planning operation 或 cross-armor 准备阶段下沉；两者都属于高风险输出链重构。
4. 最后处理 Strict parity；未补齐兼容语义前不删除旧路径。
