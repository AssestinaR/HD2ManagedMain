# Patch 修复与护甲替换：进度、日志与任务反馈改造方案

## 1. 文档目标与范围

本文档定义以下一次性写出操作的用户反馈和诊断遥测方案：

- 单 Mod Same-key Patch 修复；
- 批量过时 Mod 修复；
- 跨护甲模型替换候选生成。

目标是让用户看到稳定、易懂的阶段进度，让开发者能够通过精确日志定位耗时、跳过、失败、取消和输出验证问题。

本方案不改变模型解析、Patch 重建、材质闭包和信息中心的业务职责。运行进度是瞬态操作状态，不得新增为 `ModInformationKind`，也不得放入 `IModInformationCenter` 的缓存或 producer 链路。

## 2. 当前问题

现有流程主要只有开始/结束消息，中间阶段不可见：

- Same-key 修复缺少核心阶段进度回调；
- 批量修复只在整体结束时更新汇总；
- 跨护甲已有部分进度模型，但尚未统一接入任务中心；
- Same-key 和跨护甲的取消令牌没有完全贯通；
- Core 内部的计划、重建、写出和验证阶段缺少统一运行日志；
- 高频进度如果直接转换为通知，会造成消息刷屏。

主要现有入口和流程位置：

- `HD2ModManager/ViewModels/ModDetailsPageViewModel.cs`：单项 Same-key 修复；
- `HD2ModCore/Infrastructure/ModSameKeyReconstructionService.cs`：Same-key 编排；
- `HD2ModCore/Infrastructure/ModRepairBatchService.cs`：批量修复、备份、替换和恢复；
- `HD2ModAdaptation/PatchReconstruction/UnitMesh/SameKeyTargetShellReconstructionOperation.cs`：Same-key 重建、写出和回读验证；
- `HD2ModCore/Infrastructure/CrossArmorTransferCandidateService.cs`：跨护甲候选生成；
- `HD2ModManager/ViewModels/CrossArmorCandidateOutputPageViewModel.cs`：跨护甲页面状态；
- `HD2ModManager/Services/BackgroundTaskService.cs`：后台任务阶段、进度、取消和终态；
- `HD2ModManager/Services/NotificationService.cs`：用户消息；
- `HD2ModManager/Services/LogService.cs`：Manager 日志。

## 3. 推荐架构

采用 Core Application 层的统一运行遥测契约，由 Manager 负责适配 UI、任务中心和日志。

建议新增 `OperationProgressEvent` 或等价契约，至少包含：

- `OperationId`：单次操作 GUID；
- `ParentOperationId`：批量修复子 Mod 与批次的关联；
- `OperationKind`：`SameKeyCandidate`、`CrossArmorCandidate`、`RepairBatch`、`RepairBatchItem`；
- `StageId`：稳定机器标识，不使用本地化文本；
- `StageText`：用户可读阶段文本；
- `Sequence`、`TimestampUtc`；
- `State`：`Started`、`Progress`、`Warning`、`Completed`、`Failed`、`CancelRequested`、`Canceled`、`CommitStarted`、`Committed`；
- `Completed`、`Total`、`Fraction`：允许未知值；未知时不伪造百分比；
- `Scope`：Mod、Patch、目标 Unit、批次序号等受控标识；
- `Metrics`：耗时、Unit 数、mesh 数、跳过数和失败数；
- `IssueCode`：稳定的失败/跳过原因码；
- `Message`：安全的用户摘要或诊断摘要。

### 3.1 Manager 适配

统一桥接器将事件映射为：

- `Started`：任务开始，显示首个阶段；
- `Progress`：更新 `BackgroundTaskService` 的阶段和百分比；
- `Completed`：任务完成；
- `Canceled`：任务取消；
- `Failed`：显示安全失败摘要；
- `Warning`：在任务详情或日志中聚合，不逐条弹通知。

服务层不直接依赖 WPF。UI 线程更新必须由 ViewModel/桥接器通过 `Progress<T>` 或 Dispatcher 完成。

## 4. 用户消息规则

用户消息栏只显示一级阶段和关键状态转折，不显示 AssetKey、完整路径、异常堆栈或内部 ABI 术语。

### 4.1 Same-key 修复

1. 正在检查修复条件；
2. 正在读取来源与目标数据；
3. 正在重建当前版本 Unit（`n/m`）；
4. 正在写入并验证候选；
5. 已完成：重建 `n` 个 Unit；
6. 已取消；
7. 修复失败：建议查看任务详情和诊断日志。

### 4.2 批量修复

1. 正在准备批量修复（`0/N`）；
2. 正在修复 Mod（`i/N`）；
3. 正在备份并提交已验证结果；
4. 批量完成：修复 `r`，跳过 `s`，失败 `f`，取消 `c`。

批量子 Mod 的详细阶段进入任务详情或日志，不为每个阶段弹独立通知。

### 4.3 跨护甲替换

1. 正在读取游戏索引；
2. 正在准备来源模型与材质；
3. 正在重建目标护甲（批次 `i/n`）；
4. 正在写入候选 Patch；
5. 正在验证输出（`i/n`）；
6. 候选已生成，等待游戏内验证；
7. 候选生成失败或已取消。

## 5. 阶段定义

### 5.1 Same-key

| StageId | 含义 |
|---|---|
| `InspectEligibility` | 检查源 Patch、Game Data、索引和可写条件 |
| `LoadFacts` | 读取已有分析、索引和信息中心事实 |
| `Plan` | 生成 Unit/mesh 重建计划 |
| `BuildCandidate` | 读取 Payload 并重建当前版本 Unit |
| `WriteCandidate` | 写入隔离 Output |
| `ValidateCandidate` | 回读并执行结构验证 |
| `Finalize` | 写报告并汇总结果 |

### 5.2 跨护甲

| StageId | 含义 |
|---|---|
| `LoadGameIndex` | 读取当前游戏资产索引 |
| `PrepareSourceAndMaterials` | 准备来源模型、材质和目标映射 |
| `RebuildTargetBatch` | 按批次重建目标护甲 |
| `WriteFinalPatch` | 写入候选 Patch 和 sidecar |
| `ValidateOutput` | 回读、ABI、几何和依赖验证 |
| `WriteReport` | 写出诊断/性能报告 |
| `Finalize` | 汇总候选结果 |

### 5.3 批量修复

父操作阶段：

| StageId | 含义 |
|---|---|
| `BatchPrepare` | 收集并去重待修复 Mod |
| `RepairMod` | 当前 Mod 的 Same-key 子流程 |
| `WriteBatchManifest` | 写出批量审计清单 |
| `RefreshDerivedState` | 刷新已修复 Mod 的派生状态 |
| `Finalize` | 汇总终态计数 |

父操作使用“已进入终态 Mod 数 / 请求 Mod 数”计算总体进度；子操作携带 `ParentOperationId`。

## 6. Debug 日志规范

建议每次操作写入结构化 JSONL 运行日志。每条事件至少包含：

- `timestampUtc`、`level`、`eventName`；
- `operationId`、`parentOperationId`、`operationKind`；
- `stageId`、`sequence`、`state`；
- `completed`、`total`、`fraction`、`elapsedMs`；
- Mod 节点和 Patch 的受控标识；
- Unit、mesh、批次和验证统计；
- `issueCode`、异常类型和安全摘要；
- `repairedCount`、`skippedCount`、`failedCount`、`canceledCount`；
- 输出验证结果、备份结果和恢复结果。

### 6.1 记录级别

- 阶段开始/结束：全部记录；
- 批次开始/结束：全部记录；
- 单 Unit：默认只记录失败、跳过和聚合统计；
- AssetKey、mesh 明细：仅 Debug 模式或限量记录；
- 提交边界、验证失败、取消和恢复失败：永不采样；
- 异常堆栈只写错误日志，不进入普通用户消息。

### 6.2 限流

- UI 进度最多约 10 Hz；
- 任务中心仅在 250 ms 或进度变化至少 1% 时更新；
- 日志阶段事件全部保留；连续细粒度事件按约 1 秒或 5% 聚合；
- 日志需要限制单次大小和保留数量，避免大型批量操作产生不可控日志。

完整绝对路径只允许进入本机 Debug 日志，不进入用户消息或可分享报告。

## 7. 取消和提交安全

取消必须区分请求、已取消和提交临界区：

- `CancelRequested`：用户请求取消，显示“正在取消”；
- 读取、分析、重建、验证和候选写入阶段，在 Unit、批次和大文件边界检查令牌；
- 批量取消后不再开始下一个 Mod；
- `CommitStarted` 后不得中断删除/替换/恢复临界区；当前提交完成后再生效；
- 无论完成、失败还是取消，都必须写批量审计 manifest；
- 取消不是失败，日志等级、任务终态和用户消息必须区分。

批量审计建议保存：

- 请求、开始、完成、修复、跳过、失败、取消和未开始数量；
- 每个 Mod 的阶段、输出、备份、提交和恢复结果；
- 开始/结束时间和失败原因码。

## 8. 分阶段实施计划

### 第一阶段：契约和桥接

- 在 Core Application 定义统一遥测事件；
- 在 Manager 增加桥接器；
- 保持现有信息中心链路不变；
- 先将跨护甲已有进度模型适配到统一事件。

### 第二阶段：跨护甲

- 接入 `BackgroundTaskService`；
- 贯通取消令牌；
- 固定阶段码和进度分母；
- 拆分进度事件和性能诊断事件；
- 删除页面层重复的高频通知。

### 第三阶段：Same-key

- 为 Same-key 编排增加阶段事件和取消令牌；
- 在计划、Unit 重建、写出、回读验证和报告阶段上报聚合进度；
- 单项修复接入统一任务桥接器。

### 第四阶段：批量修复

- 增加父子操作关联；
- 增加逐 Mod 阶段和总体进度；
- 完善取消终态、恢复日志和总是落盘的审计 manifest；
- 让最终 UI 统计来自明确终态计数。

### 第五阶段：收敛与清理

- 删除页面层重复的自由文本日志和阶段通知；
- 统一失败/跳过原因码；
- 验证大型真实样本的日志大小、UI 响应和取消一致性；
- 后续再评估是否将跨护甲旧进度类型完全替换为统一契约。

## 9. 测试和验收标准

### 单元测试

- 事件顺序和序号单调递增；
- 阶段完成后才进入下一阶段；
- 已知进度的 `completed/total` 正确；
- 未知总量不伪造百分比；
- 进度节流和阶段切换立即发送；
- 失败、跳过、警告、取消均带原因码。

### 服务测试

- Same-key 成功、预检失败、验证失败、写出失败和取消；
- 跨护甲成功、部分跳过、映射失败、验证失败和取消；
- 批量在首项、中间项、提交前和提交中收到取消请求；
- 提交失败后恢复；
- 所有终态都写入 manifest。

### Manager 测试

- 事件正确映射到任务中心阶段和百分比；
- 普通进度不产生消息刷屏；
- 转折通知只发送一次；
- 后台线程事件不会直接修改 WPF 绑定集合；
- 用户消息不包含 AssetKey、绝对路径和堆栈；
- 信息中心缓存、generation 和失效行为不受遥测影响。

## 10. 相关文档和实现文件

- `docs/hd2modadaptation-stable-patch-reconstruction.md`：稳定 Patch 重建技术流程；
- `docs/reconstruction-model-cache-cross-armor-plan.md`：重建和跨护甲业务阶段；
- `docs/manager-ux-automation-discussion.md`：Manager 后台任务和 UX 讨论；
- `HD2ModCore/Infrastructure/ModSameKeyReconstructionService.cs`；
- `HD2ModCore/Infrastructure/ModRepairBatchService.cs`；
- `HD2ModCore/Infrastructure/CrossArmorTransferCandidateService.cs`；
- `HD2ModManager/Services/BackgroundTaskService.cs`。
