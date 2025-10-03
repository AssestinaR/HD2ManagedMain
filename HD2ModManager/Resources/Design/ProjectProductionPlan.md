# HD2 Mod Manager - 项目制作文档（草案）

## 愿景
- 构建稳定、便携、可扩展的 HD2 Mod 管理器，核心逻辑与界面解耦，先可用后增强。
- 以 Core 类库为唯一业务来源，CLI 与两类 WPF UI 作为壳层复用核心能力。
- 所有配置与库采用相对路径，存放于程序根目录，避免 AppData 依赖。

## 分层与项目结构
- `HD2ModCore`（类库，.NET 8）
  - Domain/Models：`ModEntity`、`FileGroup`、`Profile`、`Result`/`ErrorCode`。
  - Infrastructure：文件 IO、CSV 解析（System.Text.Json + CSV）、压缩（SharpCompress）、链接（软链接优先）、路径解析（相对/绝对转换）。
  - Application 用例：`ImportMods`、`TagSuggest`/`TagDerive`、`ModLibrary` CRUD/索引、`ProfileActivate`/`ProfileOrder`（marker/before/after 稳定拓扑）、`ApplyLinks`、`SettingsService`、`EnvironmentService`。
- `HD2ModCLI`（控制台，.NET 8）
  - System.CommandLine，命令入口用于验证与自动化；无复杂队列，支持批量同步执行。
- `HD2ModUI.Basic`（WPF，.NET 8）
  - 极简单页：库 + 启用列表 + 启动游戏按钮。绑定 Core 用例，验证基础流程。
- `HD2ModUI.Native`（WPF，.NET 8/10）
  - 原生风格（FileZilla/Everything）：顶部工具栏、底部状态栏、倒立品字布局；虚拟化与懒加载；批量操作、标签编辑、分屏排序。
- `HD2ModUI.Styled`（WPF，.NET 10）
  - 同功能，风格化样式：Tab 系统替代弹窗、过渡动画；共用 ViewModel/Commands。
- `HD2ModLauncher`（WPF）
  - 统一入口：版本选择（CLI/Basic/Native/Styled）；环境预检（可写性/软链接/游戏路径探测）；记住默认；单实例。

## 目录与数据（程序根相对）
- `config/`
  - `settings.json`（`gameDirRelative`、`libraryDirRelative`、`linkMode`、`importPolicy`）
  - `profiles/active.json`（唯一启用的配置文件）及其他配置文件
- `library/`
  - `mods.json`（库清单）
  - `files/`（Mod 内容根，相对路径）
- `logs/`（运行与导入日志）
- `cache/`（临时与缩略图，可重建）
- `exports/`（导出包）

## 核心用例与规则（Core）
- 导入展平
  - 递归扫描目录/压缩包（zip/rar/7z），识别含 patch 的有效子目录；每个有效目录导入为单体 Mod；不保留 Options 层级。
  - 若存在 `manifest.json`，仅读取名称/描述/封面等元数据，最终仍按目录拆分为单体。
  - 不含 patch 的目录视为“元数据目录”，按设置：丢弃/导入/询问。
  - 解压与校验：流式拷贝后按 entry.Size 校验；0KB 或不一致标记失败并跳过。
- 标签建议与派生
  - 数据源：`ArmorList.csv`、`WeaponsList.csv`、`SupportList.csv`、`ArmorPassives.csv`。
  - 从名称解析编号（如 `FS-55`、`AR-23` 等），派生顶层与子类标签；仅打顶层（如 `护甲/轻甲/中甲/重甲`）视为替换该类全部。
- 库管理
  - CRUD 与索引：`guid→实体`、`code→实体列表`；JSON 持久化；`updatedAt/createdAt`。
  - Mod JSON 结构遵循设计文档（名称/备注/图片/标签/`fileGroups`/`sourcePath` 等）。
- 配置与激活
  - 唯一启用；首次创建/导入自动启用。
  - 排序：`marker(-1/0/1)` 段位优先，段内基于列表顺序 + `before/after` 稳定拓扑；环/跨段冲突记录并回退。
  - 链接应用：软链接优先，失败回退（复制或提示）；幂等与回 rollback。
- 环境检测
  - 根与库可写、游戏目录存在与可写；软链接能力（管理员/开发者模式）检查；问题清单与修复建议。
- 路径与设置
  - 便携相对路径：所有内部引用保存为相对路径，运行时用程序根解析为绝对路径。
  - `PathResolver`：统一绝对/相对转换、规范化与可写性检查。

## 控制台（CLI）详细设计

### 目标与范围
- 用途：
  - 批量与自动化：递归拆分导入文件夹、批量导入压缩包（包内递归），批量导出与激活。
  - 可用性测试：设置、路径、标签派生、排序与链接应用等基础能力的快速验证。
- 不支持（或不推荐）在 CLI 中进行的高精细度操作：
  - 针对单个 Mod 的图片路径、备注等细节编辑（建议直接修改 JSON 或用 UI 处理）。
  - 复杂的交互式选择流程（在 UI 中实现）。

### 输入输出与退出码
- 标准输出：简洁文本为默认；`--json` 切换为 JSON 结果（便于脚本解析）。
- 日志：写入 `logs/cli.log`；错误写入 `logs/error.log`。
- 退出码：`0=成功`，`1=参数错误`，`2=路径不可用`，`3=导入失败`，`4=链接失败`，`5=排序约束错误`。

### 命令与参数
- settings
  - `settings get`：输出当前设置（相对路径）。
  - `settings set --gameDir ../Games/HD2 --libraryDir ./library --linkMode soft`。
  - `detect-game`：自动探测 Steam 安装与库，写入相对路径。
- import
  - `import [paths...] [--include-metadata-dirs=ask|yes|no] [--json]`：
    - 支持多个目录与压缩包；目录与包内递归拆分导入；不含 patch 的目录按策略处理。
    - 输出导入结果列表（成功/失败、原因）。
- tags
  - `tags suggest [name]`：基于名称的编号识别与建议标签。
  - `tags derive [tags...]`：输入标签集合，输出派生后的完整集合。
- profile
  - `profile activate [profilePath]`：设置唯一启用的配置文件。
  - `profile order --marker {-1|0|1} --after guidA,guidB --before guidC`：写入排序约束并执行稳定拓扑。
- apply-links
  - `apply-links`：按 `active.json` 与 `settings.json` 部署到游戏目录，软链接优先，失败回退。
- export
  - `export [guid] --out ./exports/name.zip`：标准 zip 与 `manifest.json`，用于分享与备份。

### 批量与递归策略
- 目录与压缩包递归：
  - 对每个路径：若为目录，深度优先遍历；若为压缩包，用 SharpCompress 枚举条目并模拟目录结构。
  - 任意层级子目录只要包含合法 patch 即导入为单体 Mod；名称以“主包-选项[-子选项]”净化与唯一化。
- 并发与节流：
  - 默认串行导入以简化实现；`--parallel N` 可启用小并发（N<=2）避免 I/O 饱和。
- 失败处理：
  - 输出失败列表与原因；不中断其他项；最终以退出码汇总严重错误。

### 配置与路径
- 所有路径参数均可相对程序根；CLI 启动时加载 `config/settings.json`。
- `settings set` 写入相对路径；`detect-game` 计算相对值并保存。

### 用例示例
- 批量导入并自动应用：
  - `hd2mm import ./downloads/*.zip ./mods-folder --include-metadata-dirs=no`
  - `hd2mm apply-links`
- 排序与前置：
  - `hd2mm profile order --marker -1 --after a1,b2`
- 标签调试：
  - `hd2mm tags suggest "AR-23 Liberator high-res"`
  - `hd2mm tags derive AR-23 武器 主要武器`

## UI 模式
- 基础模式（WPF）
  - 单页：库列表 + 启用列表 + 启动游戏按钮；导入自动派生标签与自动应用；右键移除/导出；设置项（路径/链接模式/导入策略）。
- 原生 UI（WPF）
  - 布局：顶部工具栏、底部状态栏，中间倒立品字（左启用列表、右库列表、下信息栏）。
  - 能力：批量操作、标签编辑、分屏联动排序（`marker/after/before`）、虚拟化与图片懒加载、进度显示。
- 风格化 UI（WPF）
  - Tab 替代弹窗；统一导航与状态；过渡动画；与原生共用 ViewModel/Commands，仅替换样式与模板。

## 启动器
- 统一入口：选择启动 CLI/Basic/Native/Styled；记住上次选择。
- 预检环境：程序根与库可写、游戏目录存在、软链接能力；显示报告与修复建议。
- 单实例控制与参数传递（根路径、配置位置）。

## 启动器详细设计
- 目标：提供统一、小型入口，记忆上次选择，进行环境预检与设置管理。

### 窗口与交互
- 小窗口样式：类似战网/QQ/微信登录窗体，居中显示。
- 主要控件：
  - 下拉菜单（`UI 选择`）：列出设置文件中声明的可用 UI（Basic/Native/Styled/CLI 等）。
  - `启动`按钮：按所选 UI 启动对应 EXE 或入口。
  - `设置`按钮：向下扩展——点击后拉长启动器高度，在下方显示设置区域（类似安装向导“更多设置”）。
  - 记忆上次选择：`settings.json` 中保存 `lastUi`，启动器加载时自动选中；提供“下次自动启动所选 UI”开关。

### 可用 UI 配置（不自动发现）
- 由 `config/ui-discovery.json` 或 `config/settings.json` 中的 `uis[]` 显式声明：
  - 示例：`[{ "name": "Basic", "path": "HD2ModUI.Basic.exe", "args": "" }, ...]`
  - 启动器仅读取此列表并校验存在性；不做目录扫描与自动发现。
- 后续新增 UI：只需在设置文件中添加条目，无需改动启动器逻辑。

### 启动流程
- 预检：
  - 读取 `config/settings.json`；调用 `EnvironmentService.CheckAll()`：根与库可写、游戏目录存在、软链接能力。
  - 若检测失败，显示简要问题与修复建议（移动到可写目录、设置游戏路径、启用开发者模式等）。
- 参数传递：将程序根、`settings.json` 路径作为参数传给目标 UI/CLI；保持相对路径模式一致。
- 单实例（Mutex）：
  - 若已运行某 UI，启动器直接退出并请求聚焦已运行的 UI（通过 NamedPipe/窗口句柄定位）；无法聚焦时给出提示后退出。

### 设置内容（由启动器统一承载）
- 基础项：游戏目录（相对）、库目录（相对）、链接模式（软/硬/复制回退）、导入策略（含/不含 patch 目录处理）。
- 高级项：并发度（导入时）、日志级别、语言、`uis[]` 列表管理（新增/删除 UI 条目）。
- 显示方式：扩展面板（非弹窗）；设置修改后写回 `config/settings.json`。
- 约束：其他 UI 项目不再提供设置入口，避免臃肿与分散；所有设置集中在启动器。

### 视觉与体验
- 轻量主题；最少交互路径：选择 UI → 启动。
- 错误提示非阻塞；提供“查看详细报告”入口。
- 记住窗口位置与尺寸（含扩展高度状态）。

### 非目标与约束
- 启动器不执行业务操作（如导入/激活），仅做入口与设置；业务由各 UI/CLI 调用 Core 完成。
- 不强制管理员运行；按需提升由目标 UI 在具体操作时处理。

## 非目标与取舍
- 不实现 Explorer 缩略图 Shell 扩展与自定义包格式；导出使用标准 zip 与 `manifest.json`。
- 不强制管理员运行；按需提升与回退策略为原则。
- 首版不做跨进程 IPC；各壳同进程引用 Core。

## 质量与测试
- CLI 驱动回归：批量导入、排序、激活、导出。
- 校验：长度/0KB/路径规范；错误码与日志统一。
- 性能：受限并发（内部串行或小并发）、列表虚拟化、懒加载、索引加速。

## 迭代计划
- M1：Core 用例 + CLI（settings/import/activate/apply-links），相对路径与软链接优先，日志。
- M2：基础 UI（单页），验证绑定与自动应用。
- M3：原生 UI（全功能），批量/分屏/排序/标签编辑。
- M4：风格化 UI（样式与动画），Tab 体系。
- M5：启动器与环境预检，统一入口。

---

## Core 详细小节（标签系统优先）

### 模型与约束
- `Tag`
  - 字段：`Type`(`Armor|Weapon|Support|Other`)、`Code`（如 `FS-55`、`AR-23`）、`EnglishName`、`ChineseName`、`Meta`（字典：`Passive`、`Source`、`Armor`、`Speed`、`Stamina` 等）。
  - 约束：`Code` 对于精确标签唯一；系统标签集合只读，用户自定义标签可增删。
- `ModEntity`
  - 字段：`guid`、`name`、`description`、`image`、`iconPath`、`tags:string[]`、`fileGroups:FileGroup[]`、`sourcePath`、`createdAt/updatedAt`。
  - 约束：`tags` 同时保存精确编号与派生标签（如 `护甲/轻甲` 等）。`fileGroups` 仅含有效 patch 文件组。
- `FileGroup`
  - 字段：`hexPrefix`、`patchN`、`files:string[]`、`relativePath`（相对程序根）。
  - 约束：导入时按包含合法 patch 的目录生成；非 patch 目录按设置处理。
- `Profile`
  - 字段：`entries[{ guid, marker, after[], before[] }]`。
  - 约束：唯一启用；排序遵循段位优先与稳定拓扑；环与跨段冲突需记录并回退。
- `Result`/`ErrorCode`
  - 统一错误模型：`Ok`、`InvalidPath`、`NotWritable`、`PatchMissing`、`ExtractFailed`、`LinkFailed`、`TopoCycle`、`ConstraintCrossSegment`、`TagUnknown` 等。

### 标签数据源与服务
- `TagCatalogService`
  - 输入：`ArmorList.csv`、`WeaponsList.csv`、`SupportList.csv`、`ArmorPassives.csv`。
  - 输出：内存字典与只读集合：`code→Tag`、`type→List<Tag>`、`passive→描述`。
  - 行为：加载与刷新、索引构建（`code→tag`、`group/subcategory` 映射）。
- `TagCsvSuggestionService`
  - 输入：`modName:string`。
  - 输出：`IEnumerable<Tag>` 建议列表（通过识别编号、关键字）。
  - 行为：正则识别编号（`FS-\d+`、`AR-\d+` 等）、清洗与去重、权重排序。
- `TagDerivationService`
  - 输入：`IEnumerable<Tag>`（包含精确编号或顶层类别）。
  - 输出：补全后的标签集合（添加 `护甲/轻甲`、`武器/主要武器/...`、`支援/...`）。
  - 规则：
    - 护甲编号 → `护甲` + 重量类别（由 `ArmorList.csv` 映射）。
    - 武器编号 → `武器` + 分组/子类（由 `WeaponsList.csv` 映射）。
    - 支援编号 → `支援` + 分类（由 `SupportList.csv` 映射）。
    - 仅顶层标签（如 `护甲`、`轻甲/中甲/重甲`）视为替换该类全部；在冲突分组中隐含所有子标签。
- `TagTooltipService`
  - 输入：标签 `Code`。
  - 输出：名称（中英）、被动（中英）、来源、护甲/速度/耐力；用于 UI 悬停提示。

### 冲突分组与排序（标签驱动）
- `ConflictGroupingService`
  - 输入：`ModEntity.tags`。
  - 输出：冲突组键（精确编号或类别广度）用于分段与冲突判断。
  - 规则：
    - 精确编号为最细粒度，同编号直接冲突；顶层与子类标签形成“广度组”（如 `护甲` 或 `轻甲` 表示广泛覆盖）。
    - 隐含规则：仅打顶层标签的 Mod 在对应类别的所有精确标签组中冲突。
- `ProfileOrderService`
  - 行为：
    - 段位切分：Top(marker=-1)/Middle(marker=0)/Bottom(marker=1)。
    - 段内稳定拓扑排序：以 JSON 列表为基础顺序，对 `before/after` 做有向边，零入度优先取基础靠前者；检测环并回退最近添加边。
    - 跨段约束与段位冲突忽略并记录（段位优先）。

### 导入与库
- `ImportService`
  - 输入：目录或压缩包路径（支持 zip/rar/7z）；设置：是否导入元数据目录。
  - 行为：递归扫描；凡包含合法 patch 的任意层级子目录均导入为单体 Mod；读取 `manifest.json` 仅作元数据；名字净化与唯一化（`主包-选项[-子选项]`）；提取校验（长度、0KB）。
  - 输出：`ModEntity[]` 写入库；失败记录与日志。
- `ModLibraryService`
  - 行为：CRUD、索引（guid→实体、code→实体列表）、持久化到 `library/mods.json`；`updatedAt/createdAt` 维护；图片路径与缩略图生成委托 `ThumbnailService`。
- `ThumbnailService`
  - 输入：`image/iconPath`。
  - 行为：生成与缓存缩略图到 `cache/thumbnails`；懒加载策略。

### 激活与链接
- `ActivationService`
  - 输入：当前启用的 `Profile` 与库实体。
  - 行为：将选中 Mod 的文件映射到游戏 data 目录；软链接优先（检测开发者模式/管理员）；失败回退复制或提示；幂等与失败回滚（清理残留）。

### 设置、路径与环境
- `SettingsService`
  - 存储：`config/settings.json`（相对路径字段）；支持 `settings get/set/reset`。
- `PathResolver`
  - 责任：程序根→绝对/相对转换、规范化（斜杠/大小写）、存在与可写检查、跨卷判定。
- `EnvironmentService`
  - 检测：根与库可写、游戏目录存在与可写、软链接能力（开发者模式/管理员）；Steam 自动探测游戏路径并保存为相对；启动器预检调用。

### 日志与错误
- `LogService`
  - 输出到 `logs/*.log`，分类：导入、激活、运行；支持事件订阅用于 UI 进度。
- `ErrorCode` 与 `Result<T>`
  - 统一返回：成功/错误码/消息；用于 CLI 退出码与 UI 提示。

### 接口约定（示例）
- `TagCatalogService.LoadAsync()` → `Task<TagCatalog>`（包含字典索引）
- `TagCsvSuggestionService.Suggest(string name)` → `IEnumerable<Tag>`
- `TagDerivationService.Derive(IEnumerable<Tag> tags)` → `ISet<Tag>`
- `ImportService.ImportAsync(IEnumerable<string> paths, ImportOptions opts)` → `Task<ImportResult>`
- `ModLibraryService.SaveAsync(ModEntity entity)` / `GetByGuid(Guid id)` / `List()`
- `ProfileService.ActivateAsync(Profile profile)` / `Order(Profile profile)` → `OrderResult`
- `ActivationService.ApplyAsync(Profile profile, LinkMode mode)` → `ActivationResult`
- `SettingsService.Get()` / `Set(Settings s)` / `DetectGameAsync()`
- `EnvironmentService.CheckAll()` → `EnvironmentReport`

以上详细小节用于指导 Core 的实现与测试，确保标签系统成为核心驱动，替代单纯的“排列顺序”策略。

## 简易界面（Basic UI）详细设计
- 目标：极简、低干扰，面向普通用户；仅显示“已加载的 Mod”（即当前配置文件中的条目视图），但兼容库与标签逻辑。

### 布局与交互
- 顶部工具条：
  - `导入` 按钮：选择目录/压缩包；支持拖入窗口直接触发导入；调用 Core `ImportService`（递归展平），导入后自动派生标签，并尝试自动启用到当前配置文件（见下文导入行为）。
  - `启动游戏` 按钮：仅启动游戏进程（部署在启用/禁用时已自动完成链接）。
- 第二层筛选框：
  - 输入文本筛选下方列表（名称/标签匹配）。
  - 空闲提示轮播：每 5 秒切换一条（如“可以将 mod 拖入窗口以导入 mod”“右键 mod 可删除”）。
- 中间列表：
  - 仅一个列表，标题显示：`已加载的 Mod`。
  - 容器使用虚拟化（VirtualizingStackPanel/ItemsRepeater）；每行一个 Mod。
- 底部状态栏：
  - 显示：已安装的 Mod 数量、启用的数量、灰色数量、最后操作提示。

### 列表项结构（虚拟化）
- 左侧缩略图（懒加载，来自 `image/iconPath`；无图使用占位）。
- 右侧名称与悬停信息：
  - 名称文本：两种状态颜色
    - 黑色：已启用（在配置文件 entries 中）
    - 灰色：未启用（仅在库中，或导入时产生冲突未启用）
  - 悬停提示：显示大图预览（如有）、备注、标签摘要与派生信息（调用 `TagTooltipService`）。

### 导入行为（自动启用与自动链接）
- 常规导入：
  - 执行 `ImportService` 展平导入并写入库，随后对每个新导入的 Mod 执行自动启用：
    - 若与当前已启用条目在同一冲突组（由 `ConflictGroupingService` 计算），则不启用并显示为灰色；否则加入配置文件为黑色。
  - 启用/禁用变更触发自动链接部署：调用 `ActivationService.ApplyAsync`，依据设置选择软/硬链接；硬链接失败时自动回退软链接。
- 启动游戏按钮：
  - 不再执行复制部署，仅启动游戏（部署在前述变更时已完成）。
- 复制部署策略：
  - 不提供复制模式（性能与空间占用差）；设置中仅保留软/硬链接模式，默认软链接并在硬链接失败时回退到软链接。

### 极简模式的启用规则与灰化逻辑
- 导入冲突自动灰化：
  - 当新导入的 Mod 与已启用 Mod 的“冲突组键”一致（由 `ConflictGroupingService` 基于精确编号或类别广度计算），新导入项标记为灰色（仅入库，不自动启用）。
- 黑灰状态来源：
  - 黑色列表项来自当前配置文件 `entries`（可附加 `marker` 前置显示符号）。
  - 灰色列表项来自库但不在 `entries` 中；用于提示存在可替换或可并存的选项。

### 右键菜单（列表项）
- 所有项：
  - `删除`：从库和配置文件同步删除（移除链接并清理库实体）。
  - `重命名`：编辑库实体名称（更新 `updatedAt`）。
  - `设置为前置安装`：将该项 `marker = -1` 并保存到配置文件（立即触发自动链接部署）。
- 灰色项专有：
  - `替换掉 xxxx`：将该灰色 Mod 替换当前与其冲突的启用 Mod（`xxxx` 为冲突目标名称），即在配置文件中移除目标并添加此项；保持冲突组唯一性（立即触发自动链接）。
  - `与 xxxx 同时生效`：将灰色 Mod 与其冲突的启用 Mod一同加入配置文件（即并存启用）；若规则不允许并存则提示不可用并记录原因。

### 与 Core 的接口绑定
- 使用 `ImportService`（展平/校验）、`TagDerivationService`、`ConflictGroupingService`、`ProfileService`（写入 entries 与 marker）、`ActivationService`（部署）、`ThumbnailService`（缩略图）。
- 列表数据源：从库 `mods.json` 加载并与配置文件 `active.json` 交叉标注启用状态。

### 非目标
- 不提供标签编辑窗口、不提供复杂排序（before/after）；仅提供前置标记与替换/并存快捷操作。
- 不提供图片与备注的直接编辑（需要使用其他 UI）。

## 设置、路径与环境（更新）
- 链接模式：仅支持软链接与硬链接两种；默认软链接，硬链接失败时自动回退软链接；不提供复制模式。
- 启用/禁用事件：由 Core 在任何更改配置文件 `entries` 时自动调用 `ActivationService.ApplyAsync`，保持部署与配置一致。

## 原生 UI（Native）功能与需求总览

### 顶栏菜单与命令
- 文件
  - 新建 Mod（读取筛选框文本作为名称，名称冲突自动追加序号）
  - 新建配置文件（同上命名逻辑）
  - 导入 Mod（压缩包，多选，递归展平，可选择“仅导入到库”或“导入后自动启用”）
  - 导入 Mod（文件夹，多选，递归展平）
  - 导入配置文件（.json，单选）
  - 为已选择 Mod 导入图片（单选）
  - 为已选择 Mod 导入 patch 文件（多选）
  - 删除（多选；库与配置同步移除，自动取消链接与回滚）
  - 导出无选项的 Mod（单/多选；分别导出多个 zip+manifest）
  - 导出包含选项的 Mod（单/多选；为每个弹出 Options/SubOptions 构建向导，可从库选择或新建空选项）
  - 打开游戏目录、打开 Mod 库目录、打开已选择 Mod 的文件夹（多选）
- 编辑
  - 重命名 Mod（统一表格编辑窗口，支持批量填充与逐行编辑）
  - 编辑备注（统一表格编辑窗口）
  - 新增标签/移除标签（统一在标签库窗口进行批量或单独操作）
  - 全选、反选
  - 标签库操作：新增标签（库）、移除标签（库）、查看标签库、从 CSV 批量新增标签
  - 重命名配置文件（弹窗）
- 设置（UI相关）
  - 排序方式：按时间/按名称/按大小（三选一）
  - 分组与视图：按标签分组（开/关）、显示日志栏（开/关）
  - 显示模式：大图标/小图标/列表/详细信息（四选一，控制两列表模板）
- 启动游戏（仅启动进程；启用/禁用变更已自动链接部署）
- 检测启用状态（弹窗表格：软/硬链接目标校验或哈希比对，列出异常项）

### 第二行工具条
- 左侧：配置文件选择下拉（切换唯一启用配置）
- 中部：筛选框（筛选名称与标签；空闲每5秒轮播提示）
- 右侧：新建 Mod / 新建配置文件（优先使用筛选框文本命名）

### 主布局（倒立品字）
- 左上：已启用列表（显示当前配置 entries；单/多选）
- 右上：Mod 库列表（按标签分组与设置排序；单/多选）
- 中间：四个按钮（上下排列）：向左/全部向左/向右/全部向右（启用选中/启用全部/禁用选中/禁用全部）
- 下方：日志栏（操作日志、错误提示、进度）
- 底部状态栏：Mod 总数、已启用 Mod 数、已启用 patch 组数、最近操作提示

### 列表与右键菜单
- 虚拟化与图片懒加载；每行缩略图 + 名称 + 悬停大图与备注（ToolTip）
- 右键菜单（两列表共用，少数项仅库列表可用）：
  - 导入 Mod 压缩包（仅库列表或仅导入到库，不自动启用）
  - 导入 Mod 文件夹（仅库列表或仅导入到库，不自动启用）
  - 导入 patch 文件（需已选择 Mod；多选逐个添加）
  - 导入图片（需已选择 Mod；单选逐个替换）
  - 重命名（统一表格编辑窗口）
  - 编辑备注（统一表格编辑窗口）
  - 新增标签、移除标签（统一在标签库窗口）
  - 启用 Mod、禁用 Mod、移除 Mod、导出 Mod
- 启用/禁用不允许拖拽排序；启用列表显示顺序不等于最终部署顺序，部署由排序规则决定（marker/相对约束/设置排序方式）
- 左键点击已选中项（或双击未选中项）打开详细属性窗口，可直接编辑单个 Mod 的图像、名称、备注、标签

### 标签系统与冲突
- 标签库窗口：
  - 查看/筛选全部标签；批量或单独添加/移除标签到选中 Mod；从 CSV 批量新增；删除不使用的自定义标签（系统标签只读）
  - 变更预览与差异显示；应用/撤销
- 标签派生：导入后自动派生顶层与子类；用于冲突分组与分段视图
- 冲突自动化：导入时与已启用冲突组一致则不启用（灰色）；提供“替换掉 xxxx”或“与 xxxx 同时生效”（并存检测）

### 启用、排序与部署
- 唯一启用配置文件的创建/切换
- 段位标记：前置(-1)/正常(0)/后置(1)
- 相对约束：设置为某 Mod 的前置/后置（对话框选择目标）；稳定拓扑排序 + 段位优先；环/跨段冲突提示与回退
- 启用/禁用/排序变更自动触发链接部署（软/硬链接；硬失败回退软）；部署预览（可选）

### 导入/导出与文件操作
- 递归导入目录与压缩包（展平为单体）；元数据目录策略（丢弃/导入/询问）
- 导出 zip + manifest；包选项编辑向导（构建 Options/SubOptions）
- 打开对应文件夹；为 Mod 添加/替换图片或追加 patch 文件

### 检验与工具
- 启用状态检测窗口：列出链接目标与游戏目录实际文件差异、哈希校验、异常项修复建议
- 索引管理：guid→实体、code→实体列表；提升筛选与渲染
- 统计面板（可选）：按标签分布、冲突组数量、最近更新等

### 设置与环境
- 相对路径：游戏目录、库目录；软/硬链接模式；导入策略；语言
- 环境预检与错误提示；日志级别与输出位置
- 单实例与版本信息显示

### 性能与稳定
- 虚拟化列表、图片缓存与懒加载
- 批量操作进度、失败列表与重试
- 并发节流（导入/缩略图生成）；UI 异步，避免长阻塞

### 批量重命名/备注编辑窗口（统一表格编辑）
- 表格结构：行=选中的 Mod；列=原名称/原备注/新名称/新备注
- 批量填充：共享列批量填前缀/后缀/备注；逐行编辑独立差异；规则化追加序号
- 复制辅助：从原名称复制片段到新名称；即刻预览与冲突校验

### 提示与可用性
- 空状态与引导文案（导入、右键操作等，筛选框提示轮播）
- 快捷键：删除、重命名、启用/禁用、搜索聚焦
- 国际化与原生风格主题；低动画优先，保证性能

## 标签系统（优化版：强弱语义分离）

### 问题与目标
- 问题：如果所有标签都用 `string[]` 混在一起，后续很容易把“描述/收藏类标签”（如颜色、主题）误用于冲突判断，导致误冲突、误灰化、误替换。
- 目标：允许跨域“交叉标签”（如“黄色”同时作用于护甲与武器），但不影响冲突判断；冲突判断只由“对象标签（系统标签）”驱动。

### 标签类型（语义约定）
- 系统对象标签（System / Object Tags，强语义，参与冲突与分组）：
  - 精确编号：`FS-55`、`AR-23`、`MG-43`...
  - 派生类别：`护甲`、`轻甲/中甲/重甲`、`武器`、`主要武器/次要武器`、`支援`、以及更细分子类
- 用户自由标签（User Tags，弱语义，不参与冲突，只用于筛选与展示）：
  - 颜色/主题/偏好：如 `黄色`、`搞笑`、`高清`、`收藏` 等

### 核心规则（必须写死）
- 冲突分组（`ConflictGroupingService`）只使用“系统对象标签”（精确编号与派生类别），用户自由标签永远不进入冲突键集合。
- 同一 Mod 允许同时拥有多个自由标签（跨域交叉）且不影响冲突：
  - 例：`黄色头盔`（护甲）与 `黄色枪械`（武器）都能拥有 `黄色` 标签，但不会因此互相冲突。

### 数据结构建议（Core）
- 现阶段（M1/M2）：保持 `ModEntity.Tags:string[]` 作为持久化格式不变，但在 Core 里引入“分类视图”与过滤函数：
  - `TagClassifier`：根据 `TagCatalog` 将字符串标签分为 `ObjectTags` 与 `UserTags`。
  - `ConflictGroupingService` 调用 `TagClassifier.ObjectTags` 再计算冲突键。
- 后续（M3+，可选）：
  - 将库实体结构升级为：
    - `objectTags:string[]`（系统/对象）
    - `userTags:string[]`（用户/自由）
  - 并提供一次性迁移：老 `tags[]` 自动拆分。

### 命名空间建议（降低混淆）
- 推荐对用户自由标签使用命名空间前缀（可选）：
  - `theme:yellow` / `color:yellow` 或 `颜色/黄色`
- 系统对象标签保持固定中文体系（或未来做本地化映射），避免与用户标签同名。

### 可解释性（为 UI 与“检测/冲突解释”准备）
- 冲突服务输出不仅给出“是否冲突”，还应给出原因：
  - `ConflictReason`：
    - 匹配到的精确编号（如 `AR-23`）
    - 或匹配到的广度类别（如 `护甲/轻甲`）
    - 冲突键列表（debug 用）

### 改进时机建议
- 现在（推荐立刻做）：
  - 立刻在 Core 中落实“冲突只看系统对象标签”的规则，并实现 `TagClassifier`。
  - 原因：这是架构层规则，如果后面 UI/导入/自动启用都依赖冲突结果，再改会造成大范围返工。
  - 成本：很低（增加一层过滤与分类），但收益很高（避免误冲突）。
- 之后（M3+再做）：
  - 将 `tags[]` 拆分为 `objectTags/userTags` 的持久化升级。
  - 原因：涉及 JSON 结构变更与迁移，适合在原生 UI阶段功能稳定后做。
