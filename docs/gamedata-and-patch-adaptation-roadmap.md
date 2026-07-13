# Game Data 索引与 Patch 适配落地路线

**日期：** 2026-07-13  
**状态：** 规划基线  
**适用项目：** `HD2ModAdaptation`、`HD2ModCore`、`HD2ModManager`

---

## 1. 文档目标

本文记录 Game Data 资源索引、Item 资源关系、部件复用检测，以及 patch 内部分析迁移到 `HD2ModAdaptation` 的统一落地方案。

核心目标：

1. 将 patch 内部语义读取逐步迁移到 `HD2ModAdaptation`；
2. 为 Game Data 建立可增量更新的资源索引缓存；
3. 建立 Item → Unit/Material/Texture 的资源关系表；
4. 检测多个 Item 之间的 Unit、Composite、Mesh、Material 和 Texture 复用；
5. 让 Core/Manager 消费已经生成的事实和关系，避免重复扫描 patch 或 Game Data；
6. 在新链路成熟前保留 Core 的旧 patch 实现作为 fallback，之后再清理重复代码。

---

## 2. 最终职责边界

```text
HD2ModAdaptation
  ├─ Patch TOC、payload、Unit、Material、Texture 的语义读取
  ├─ Game Data archive 读取
  ├─ Unit/Composite/Bone/Material/Texture 关系提取
  ├─ Patch group 分析
  ├─ Game Data 资源事实索引生成
  └─ 只读分析、依赖诊断和重建/写出能力

HD2ModCore
  ├─ Mod 库、manifest、文件完整性和 patch group fingerprint
  ├─ Game Data 索引 fingerprint、缓存生命周期和持久化
  ├─ archivehashes.json 的业务元数据投影
  ├─ AssetKey → archive/Item 的查询适配
  ├─ Mod 级聚合、标签、兼容性规则和派生缓存
  └─ 迁移完成前保留旧 patch 操作作为 fallback

HD2ModManager
  ├─ 编排索引构建和刷新
  ├─ 展示进度、状态、问题和复用组提示
  └─ 不直接解析 patch 或 Game Data 内部格式
```

`HD2ModAdaptation` 不引用 `HD2ModCore`。两者之间使用中立 DTO 或由 Core/Manager 提供适配器连接。

---

## 3. 已确认可复用的 Adaptation 能力

### 3.1 Patch 读取链路

以下实现继续作为唯一的 patch 内部读取基础，不新增第二套格式解析：

- `PatchTocScanner`
- `PatchEntryPayloadReader`
- `PatchUnitMeshReader`
- `GameDataUnitMeshReader`
- `MaterialDependencyResolver`
- `StingrayMaterialReferenceReader`
- `TemporaryMaterialCombiner`
- `UnitMeshReader`

### 3.2 使用边界

- `PatchMaterialDetector` 只能作为启发式 `MaterialModeHint`，不能作为完整性证明；
- `TemporaryMaterialCombiner` 只处理已经明确选择的模型 patch 和材料 patch；
- `MaterialDependencyResolver` 用于依赖闭包和缺失诊断，不保证所有真实样本都能闭合；
- `UnitMeshWriter`、重建器、transfer pipeline 只用于修复/重建，不在普通资产分析中调用；
- “能够读取”不等于“当前游戏能够接受”；旧 Unit/Composite 不能原样输出。

---

## 4. 总体数据层次

```text
Game Data archive 基础索引
        ↓
Item 资源关系表
        ↓
Unit/Composite/Mesh/Material/Texture 复用关系
        ↓
Patch group 语义分析
        ↓
Core Mod 聚合、完整性检查、兼容性提示
        ↓
Manager UI 展示和后续修复操作
```

所有层次都应保存 `SchemaVersion` 和 `SourceFingerprint`，避免不同解析器版本生成的数据混用。

---

## 5. 第一阶段：中立契约和测试夹具

### 目标

先定义跨项目传递的小型 DTO，验证边界，而不是立即改动整个 Core 派生链路。

### Adaptation 侧计划

新增只读分析契约，建议包含：

- `PatchGroupInput`
- `PatchGroupAnalysis`
- `GameDataArchiveInput`
- `GameDataArchiveIndex`
- `GameItemResourceInfo`
- `ResourceDependencyFact`
- `ResourceReuseGroup`
- `AdaptationAnalysisIssue`

DTO 只表达结构事实，不包含 Core 的 `ModNode`、SQLite 类型或 Manager UI 类型。

### 测试

优先使用现有合成 patch 测试夹具覆盖：

1. 单 patch 内嵌 Material；
2. 模型 patch + 明确材料 patch；
3. 缺少 Composite；
4. 缺少 Material/Texture；
5. `.stream` 或 `.gpu_resources` 缺失/越界；
6. 重复 AssetKey；
7. 多个 Item 共享 Unit/Material。

### 完成标准

- 不新增重复 TOC、payload 或 Unit 解析器；
- 所有失败都返回结构化 issue；
- 取消操作能够及时结束；
- DTO 不依赖 Core 和 Manager。

---

## 6. 第二阶段：Game Data 基础索引

### 目标

建立低成本的 archive 和 AssetKey 索引，使后续查询不必反复扫描 Game Data TOC。

### 第一版索引内容

每个 archive 保存：

- archive 名称和相对路径；
- archive hex；
- 显示名称和分类；
- TOC 格式/版本信息；
- `AssetKey`；
- type ID、file ID；
- TOC、stream、gpu resource 的偏移和长度；
- 资源所在 archive；
- 解析状态和问题。

第一版不读取所有 mesh、material 和 texture payload，只保存定位信息。

### 建议查询

```text
FindArchivesByAsset(AssetKey)
FindEntriesByType(TypeId)
FindEntry(ArchiveName, AssetKey)
GetArchiveStatus(ArchiveName)
```

### 缓存策略

Core 负责缓存生命周期，Adaptation 负责生成事实：

```text
GameDataIndexManifest
  ├─ SchemaVersion
  ├─ ParserVersion
  ├─ GameDataRoot
  ├─ ArchiveHashesFingerprint
  ├─ ArchiveCount
  ├─ IndexedArchiveCount
  ├─ SourceFingerprint
  └─ BuiltUtc
```

启动时先执行低成本检查：

- 相对路径；
- 文件长度；
- LastWriteTimeUtc；
- archivehashes.json fingerprint；
- parser/schema 版本。

发现变化后，仅重建变化 archive；结构版本变化时执行全量重建。需要时再对单个 archive 使用 SHA-256。

### 重要规则

`archivehashes.json` 主要提供：

```text
ArchiveHex → 显示名称/分类
```

它不替代 TOC 索引，也不包含完整的 Item 资源关系。

---

## 7. 第三阶段：Item 资源关系表

### 目标

回答“某个 Item 内部包含哪些 Unit、Composite、Material 和 Texture”。

### 输入

- `archivehashes.json` 中的分类、archive hex 和名称；
- Game Data 基础索引；
- Adaptation 的 Unit/Material/Texture 读取能力。

### 输出内容

每个 Item 建议保存：

- Item 名称、分类、archive hex；
- 直接资源和依赖资源；
- Unit、Composite、Bone、Mesh、Material、Texture 的 AssetKey；
- 每个资源的来源 archive；
- 引用方向；
- 解析状态；
- 缺失依赖；
- `SourceFingerprint`。

必须区分：

```text
DirectAssets       Item 直接拥有/定位到的资源
DependencyAssets   由 Unit/Material 引用的资源
SharedCandidates   可能被其他 Item 共享的资源
```

### 读取策略

1. 根据 archivehashes 找到 Item 对应 archive；
2. 从基础索引确定候选 Unit；
3. 读取 Unit 的结构和依赖；
4. 解析 Composite/Bone；
5. 提取 Material 引用；
6. 解析 Material 的 Texture 引用；
7. 记录完整闭包或缺失 issue；
8. 写入 Item 关系表。

如果一个 Item 不能唯一确定 Unit，不允许静默猜测，应保存候选列表和歧义 issue。

---

## 8. 第四阶段：部件复用检测

### 目标

发现多个 Item 是否共享实际资源，并为 Mod 替换完整性检查提供依据。

### 复用级别

```text
ExactUnitReuse       直接共享同一 Unit
CompositeReuse       共享 Composite Unit
MeshReuse            共享可识别 mesh/部件
MaterialReuse        共享 Material
TextureOnlyReuse     仅共享 Texture
```

建议输出：

- `GroupId`；
- Item 成员；
- 共享 AssetKey；
- 复用级别；
- 置信度；
- 解释文本；
- 发现该关系的 source fingerprint。

不要把所有共享 Texture 的 Item 都视为同一个模型复用组。UI 风险提示建议：

- 共享 Unit：高风险；
- 共享 Composite/Mesh：中风险；
- 共享 Material：低风险或材质影响提示；
- 仅共享 Texture：信息提示。

第一批重点验证：

- Armor；
- Helmet；
- Cape；
- Weapon。

后续根据实际索引结果再扩展到 Vehicle、NPC、Automaton、Terminid、Backpack 等类型。

---

## 9. 第五阶段：Patch group 分析迁移

### 输入

Core 先根据已完成的文件级索引提供：

```text
PatchGroupInput
  ├─ ArchiveHex16
  ├─ PatchIndex
  ├─ BasePatchPath
  ├─ StreamPath
  ├─ GpuResourcesPath
  ├─ ContentHash
  └─ 明确选定的相关材料 patch
```

### Adaptation 负责

1. 扫描 patch TOC；
2. 收集 AssetKey 和类型计数；
3. 读取 Unit；
4. 解析 Composite/Bone；
5. 提取 Material/Texture 引用；
6. 执行依赖闭包检查；
7. 返回结构事实和 issue；
8. 对分离式 patch 保留模型/材料关系，不盲目合并全部候选。

### Core 负责

1. 将 Adaptation AssetKey 映射到 metadata；
2. 生成用户可见标签；
3. 按 patch group 保存 summary；
4. 聚合到 Mod 级 summary；
5. 使用已生成的 group facts 进行兼容性分析；
6. 按 group fingerprint 缓存结果。

### 迁移策略

```text
新 Adaptation 链路
        ↓ 验证通过
Manager 改用新链路
        ↓
Core 旧 analyzer 仅作为 fallback
        ↓
删除 Core 重复 patch 操作
```

在新链路完成真实样本验证之前，不删除 Core 的旧实现。

---

## 10. 第六阶段：Mod 替换完整性提示

当 Mod 修改某个 AssetKey 时：

1. 查询 Game Data Item 关系表；
2. 查询对应复用组；
3. 比较 Mod patch group 覆盖的 AssetKey；
4. 判断是否只覆盖了共享组的一部分；
5. 生成风险等级和说明。

示例提示逻辑：

```text
修改了共享 Unit：提示可能同时影响多个 Item。
只覆盖共享组部分资源：提示可能出现模型/材质不一致。
完整覆盖复用组：提示已覆盖关联资源，但仍需兼容性验证。
```

这只是分析和提示，不自动扩展输出范围。

---

## 11. 暂不实现的内容

以下内容不进入第一轮 Game Data/Patch 分析：

- 自动 patch 重建；
- 自动 Material/Texture 传播；
- 自动选择未知目标 archive；
- 把旧 Unit 原样复制到新 patch；
- 以“能够回读”证明游戏兼容；
- 仅依靠 `PatchMaterialDetector` 判定 patch 完整性；
- 为所有 Item 自动推导业务语义；
- 直接把所有共享资源合并为一个复用组；
- “应用到整个护甲组”的批量重建功能。

这些功能必须在索引、关系图和真实样本验证完成后单独设计。

---

## 12. 推荐实施顺序

### P0：契约和夹具

- 定义 Adaptation 只读分析 DTO；
- 确认 patch group 输入边界；
- 补齐缺失/重复/越界测试。

### P1：Game Data 基础索引

- 复用现有 Game Data package resolver 和 TOC scanner；
- 生成 archive/AssetKey 定位索引；
- 加入 fingerprint、schema 和增量重建；
- 先验证 `CW-22 Kodiak` 等已知 archive。

### P2：Item 关系表

- 建立 Item → Unit/Material/Texture graph；
- 记录直接资源、依赖资源和问题；
- 接入现有 `archivehashes.json` 元数据。

### P3：复用检测

- 优先实现 ExactUnitReuse、CompositeReuse；
- 再验证 MeshReuse、MaterialReuse；
- 生成分级风险提示。

### P4：Patch group Adaptation 分析

- 新分析器只读 patch group；
- 复用现有 scanner/reader/resolver；
- Core 做语义投影和 Mod 聚合；
- 旧 Core analyzer 保留 fallback。

### P5：Manager 接入

- Manager 调用索引构建服务；
- 展示脏状态、构建进度和问题；
- 使用 group summary 和 reuse group 提示；
- 不让 UI 直接依赖 Adaptation 内部模型。

### P6：清理重复实现

仅当满足以下条件后执行：

- 新链路通过 Adaptation 单元测试；
- 真实 Game Data 验证通过；
- 真实内嵌/分离 patch 验证通过；
- Manager 已完成依赖迁移；
- Core fallback 至少经过一个版本周期验证；
- 已确认没有其他 Core 服务依赖旧 patch 实现。

然后再删除 Core 中重复的 patch scanner、payload reader、Unit/Material 操作，并保留必要的 Core 适配器和文件级索引能力。

---

## 13. 验收标准

### Game Data

- 未变化时启动不重复全量扫描；
- 变化 archive 可增量重建；
- parser/schema 改变可触发全量重建；
- 索引失败不会破坏已有有效缓存；
- 查询可以返回资源来源和问题。

### Item

- 能查询 Item 对应 archive；
- 能返回 Unit、Composite、Material、Texture 关系；
- 缺失闭包有明确原因；
- 不能唯一确定时返回歧义，不静默猜测。

### 复用组

- 至少能识别直接共享 Unit；
- 能区分 Unit 复用和仅 Material/Texture 复用；
- 结果包含共享资源和解释；
- Mod 覆盖不完整时能生成提示。

### Patch

- patch 内部读取来自 Adaptation；
- Core 只负责文件组、索引、映射、聚合和缓存；
- 读分析不调用 writer/reconstructor；
- 旧 Core 实现仍可作为 fallback；
- 所有真实失败保留为 issue，不静默丢弃。

---

## 14. 当前下一步

下一步先实现 P0，不直接修改 Manager 派生链路：

1. 在 `HD2ModAdaptation` 中定义 Game Data 基础索引的中立 DTO 和接口；
2. 复用现有 `GameDataPackageResolver`、`PatchTocScanner`、`PatchEntryPayloadReader`；
3. 为 archive/AssetKey 索引编写合成测试；
4. 再以 `CW-22 Kodiak` 所在 archive 做一次真实读取验证；
5. 确认索引输出后，再实现 Item 关系表；
6. 最后接入 Core 的 fingerprint、缓存和 Manager UI。

在 P0/P1 验证完成前，不进行大范围 domain 重构，也不删除 Core 旧 patch 实现。
