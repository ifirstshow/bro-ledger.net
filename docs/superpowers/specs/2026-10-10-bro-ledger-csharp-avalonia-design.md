# bro-ledger.net 设计规格 — Bro Ledger 的 C#/Avalonia 1:1 复刻

日期：2026-10-10
状态：已获用户批准的设计，待实现

## 1. 目标与非目标

**目标**：在 `bro-ledger.net/` 中创建 Java/JavaFX 版 Bro Ledger（源码位于同级目录 `../../../../bro-ledger`，下称「Java 版」或「原版」）的 1:1 C# 复刻。

「1:1」的含义（已与用户确认）：

- **功能 1:1**：主应用（存档解析、总览表、兄弟卡片、角色系统、文件监视、资产提取、升级通知、Bro Developer、快捷键）+ tools/ 三个开发工具 + 测试全部迁移
- **数据格式兼容**：`<save>.keeper.json`、`~/.bro-ledger/roles.json` 与 Java 版**同 schema 同位置**，两版工具可并存互换使用同一份数据；Role 分享码与 Java 版互通（同一个云端 store）
- **UI 尽量贴近原版**：布局结构、配色、字体、交互一致；不要求像素级逐像素还原
- **三平台全部 Native AOT**：win-x64 / linux-x64 / osx-arm64 / osx-x64 发布产物

**非目标**：

- 不复刻 Java 版的构建体系（Maven/jlink 等）
- 不修改云端 Worker 的 API
- 不做安装包（msi/dmg/deb）——AOT 单文件产物即交付物

## 2. 技术栈

| 项 | 选择 | 依据 |
|---|---|---|
| 运行时 | .NET 10 | Avalonia 12 要求 .NET 8+，官方推荐 .NET 10 |
| UI | Avalonia 12 | 用户指定；官方支持 Native AOT；AXAML 对应 FXML |
| MVVM | CommunityToolkit.Mvvm | 纯 source generator，AOT 零障碍，对应 JavaFX Property 绑定 |
| JSON | System.Text.Json + source generator | AOT 硬要求，全部走 `JsonSerializerContext` |
| 测试 | xUnit | 对应原版 JUnit |
| 日志 | Microsoft.Extensions.Logging | 轻量接入 |

## 3. 仓库结构

```
bro-ledger.net/
├── Directory.Build.props        # net10.0、IsAotCompatible、nullable enable、LangVersion latest
├── BroLedger.sln
├── src/
│   ├── BroLedger.Core/          # 模型 + 解析器 + 服务，零 UI 依赖
│   ├── BroLedger.App/           # Avalonia 桌面应用（PublishAot 唯一所在项目）
│   └── BroLedger.Tools/         # 控制台工具（可执行，普通发布即可）
└── tests/
    ├── BroLedger.Core.Tests/
    └── BroLedger.App.Tests/
```

项目引用：App → Core；Tools → Core；Core.Tests → Core + Tools（ReplayAnalyzerTest 需要）；App.Tests → App。

## 4. 模块映射（Java → C#）

### 4.1 model/ → Core/Models/

Brother, BrotherAnnotation, DictionaryEntry, InventorySlot, ItemStats, PerkPlanStatus, Role, RolePack, ShareRecord, Stat, StatModifier, TraitEntry, WeaponStats —— 字段与语义逐一对应。均为纯数据类，**不使用** `[ObservableProperty]`（可变性按原版字段决定：普通 class + 可变属性）。

### 4.2 parser/ → Core/Parsing/

| Java | C# | 要点 |
|---|---|---|
| SaveParser | SaveParser | 入口：解压/定位/解析 .sav |
| BrotherLocator | BrotherLocator | 在解压数据中定位兄弟区块 |
| BrotherParser | BrotherParser | 含 `computeFingerprint`（见 §5.1） |
| ItemParser | ItemParser | 装备/物品解析 |
| HexReader | HexReader | 小端二进制读取原语 |
| ArmorCalculator | ArmorCalculator | 护甲有效值计算 |
| SaveFormat | SaveFormat | 偏移/字节常量表 |
| ItemType | ItemType | 物品类型枚举 |

### 4.2b util/ → Core/Utils/

| Java | C# | 要点 |
|---|---|---|
| AppPaths | AppPaths | 桌面路径解析（`~/.bro-ledger/` 等），用 `Environment.GetFolderPath` |
| HexUtils | HexUtils | 十六进制工具（含 `HexUtilsTest` 覆盖的行为） |

### 4.3 service/ → Core/Services/

AnnotationService, AppConfig, AssetExtractor, DictionaryService, ExpectedStatsCalculator, FileWatcherService, GameDataService, ImageCache, ImageMapService, RoleService, RoleShareService, SaveReplayService, StatModifierService, StatPotentialCalculator, WeaponStatsService —— 一对一迁移。

平台相关替换：

- `WatchService` → `FileSystemWatcher`。**行为真源：`service/FileWatcherService.java`**——包括游戏写档期间的 Changed 事件风暴（去抖/重试策略）与 `Error` 事件（内部缓冲溢出）的处理，均以该文件为准逐一对齐
- `java.net.http.HttpClient` → `System.Net.Http.HttpClient`
- `ImageCache` **整体归 App 层**（计划④）：Java 实现直接缓存 `javafx.scene.image.Image`（无字节半区可迁），C# 对应为 Avalonia `Bitmap` 的 LRU 缓存（max 300 + 1×1 灰占位 PNG 字节常量照搬）——Core 不引用 Avalonia，早期「Core 字节缓存半区」表述作废
- 桌面路径：`~/.bro-ledger/` → `Environment.GetFolderPath(UserProfile)` 拼接

### 4.4 ui/ → App/Views + App/ViewModels

JavaFX Controller 模式 → MVVM：

- 8 个 FXML → 8 个 AXAML：main, brother-card, level-up-modal, preferences, role-manager, role-share-dialog, role-import-dialog, setup
- Controller → ViewModel（MainViewModel, BrotherCardViewModel, LevelUpModalViewModel, PreferencesViewModel, RoleManagerViewModel, RoleShareViewModel, RoleImportViewModel, SetupViewModel）
- 纯自定义面板 → Avalonia 自定义控件，**各控件行为真源标注如下**：
  - **BrotherOverviewPane**（总览表，Java 真源 `ui/BrotherOverviewPane.java`）：列重排（同类目内拖拽）、按角色排序、四个增强开关、列顺序联动卡片、**Level-up 指南粘性面板**（点击 level 单元格弹出/ESC/再点击/焦点外关闭——粘性面板宿主即此控件内部弹出层）
  - **PerkPlanPane**（Java 真源 `ui/PerkPlanPane.java`）：50 perk 网格、拥有/未拥有透明度、tooltip
  - **StarsWidget**（Java 真源 `ui/StarsWidget.java`）：天赋星级显示（纯显示控件，无交互）
  - **BrotherListCell**（Java 真源 `ui/BrotherListCell.java`）：总览行渲染
  - TooltipFactory（→ ToolTip 模板工厂）, Modals（→ 窗口服务）, OverviewCalc（→ 纯计算静态类，定 Core）, HotkeyManager（→ KeyBindings + 全局快捷键）, UiContext（→ App 服务容器/静态上下文）, PerkSortMode（→ 枚举）
- **TableView 列交互验收判据**（真源 `ui/BrotherOverviewPane.java` + `ui/MainController.java`）：①列在所属类目（stats/equipment）内可拖拽重排且顺序持久化 ②跨类目不可拖入 ③重排后兄弟卡片属性排列同步变化 ④排序增强开关四态切换即时生效

### 4.5 tools/ → Tools/

保留原子包结构：`tools/ReplayAnalyzer`、`tools/BrotherCsvExporter`、`tools/parser/BrotherSavefileExporter` —— 控制台程序，`System.CommandLine` 不引入，保持与原版相同的 main 参数风格。

### 4.5b 根包入口 → App/

| Java | C# | 要点 |
|---|---|---|
| App.java | Program.cs + App.axaml(.cs) | JavaFX Application 生命周期 → Avalonia AppBuilder 启动 |
| Launcher.java | Program.cs | 启动入口合并 |
| AppInfo.java | **Core/AppInfo.cs**（放置调整：SaveReplayService 等 Core 服务依赖 `IsDebug()`，Core 不能反向引用 App；version.properties 随行嵌入 Core） | 版本/名称常量（对齐 version.properties） |

### 4.6 资源

| 原资源 | 处理 |
|---|---|
| game-data.json | 原样复制为嵌入资源 |
| game-data.schema.json | 原样复制为嵌入资源（`GameDataSchemaTest` 校验其存在与有效性） |
| keeper.css | → App/Styles.axaml（原值搬运颜色/字号/间距） |
| Cinzel-Regular/Bold.otf | 嵌入资源，Avalonia FontManager 加载 |
| icon-*.png, unknown-trait.png, BroLedger.png/ico | 嵌入资源 |
| version.properties | → csproj 版本属性 + AssemblyInfo |

## 5. 数据兼容规范（硬性要求）

### 5.1 Fingerprint

```
input  = UTF8(brother.name + brother.backgroundHexId + starsString)
hash   = SHA-256(input)
fingerprint = hex(hash[0..16])   // 32 个十六进制字符
```

`starsString` 与 Java 版 `Brother` 中构造 fingerprint 输入的拼接方式完全一致（实现时以 Java 源码 `BrotherParser.computeFingerprint` + `fingerprintInput` 为准逐字符核对，含分隔符与大小写）。验收：对同一 `TestSavefile.sav`，两版产出的兄弟 fingerprint 集合完全一致。

### 5.2 keeper.json（实际文件名 `.broledger.json`）

- 路径：与存档同目录，`<save文件名完整名>.broledger.json`（如 `save.sav.broledger.json`）——**以 Java 代码 `AnnotationService.loadFor` 为准；README 与本规格早期的 "keeper.json" 称呼是历史名**
- Schema：与 Java 版 `AnnotationService` 序列化格式一致（`version`、`annotations` map：fingerprint → roleId / statIncreases / post11Increases / perkPlanStatus / sortIndex）
- **fallback 路径**（存档目录不可写时）：`~/.bro-ledger/annotations-<sha256hex(存档绝对路径)>.json`，哈希输入与 Java 版一致（存档绝对路径字符串的 UTF-8）
- 读写须兼容 Java 版已写出的文件；字段缺省、null 语义逐一对齐

### 5.3 roles.json

- 路径：`~/.bro-ledger/roles.json`
- Schema 与 UUID 语义与 Java 版一致；角色顺序、frontline/backline、每属性 target/priority(P1/P2/P3) 字段名一致
- 两版可交替读写同一文件无丢失

### 5.4 game-data.json

嵌入资源原样复用；`GameDataService` 与 `DictionaryService` 的查找语义一致（key 为 8 位大写十六进制 ID）。

### 5.5 Role 分享

- 端点：`https://bro-ledger-roles.niclas-cf8.workers.dev`（可经环境变量覆盖，对齐原版 override 机制）
- 分享码：8 字符、32 字母表（无 0/O/1/I）、大小写不敏感（读入时 trim + 大写归一）
- HTTP 消息体 JSON 结构与 Java 版 `RoleShareService` 的请求/响应完全一致；90 天过期、取用重置时钟的语义由 Worker 负责，客户端不重复实现

## 6. Native AOT 规范

- `Directory.Build.props`：`<IsAotCompatible>true</IsAotCompatible>`（含所有库）
- 仅 `BroLedger.App.csproj`：`<PublishAot>true</PublishAot>`
- Avalonia 12 无需 `BuiltInComInteropSupport=false`（12.0 之前才需要）
- 所有 JSON 序列化/反序列化走 `JsonSerializerContext` source generator；禁止运行时反射序列化
- 禁用：`Activator.CreateInstance`(字符串)、`Assembly.Load`、`Expression.Compile`、`dynamic`
- 发布矩阵：`win-x64`、`linux-x64`、`osx-arm64`、`osx-x64`，`dotnet publish -c Release -r <rid>`，单文件自包含
- CI：GitHub Actions 三平台 matrix。构建/publish 均带 `-p:TreatWarningsAsErrors=true`——AOT/trimmer 分析警告（IL2000–IL2099、IL3000–IL3007、IL3050–IL3056）零容忍，一旦出现即构建失败。**注意**：TreatWarningsAsErrors 为全集，nullable 与 xUnit 分析器警告同样升为错误（正是所需；首次 CI 若爆量，逐条修复而非放宽）
- `dotnet test` 全绿 + `dotnet publish -c Release -r <rid>` 三平台成功为验收门

## 7. UI 迁移规范

- 布局结构 1:1：FXML 层级 → AXAML 层级；keeper.css 选择器 → Avalonia 样式（类名/伪类按 Avalonia 习惯命名，视觉值不变）
- JavaFX `Property` → `[ObservableProperty]`；`ObservableList` → `ObservableCollection<T>`
- TableView 行为：列重排（同类目内拖拽）、按角色排序、四个增强开关（Weapon Tier / Armor Stats / Perk Sorting / **Potential**——5 态：Off / Current+Pot. / Pot. Only / Current+Exp. / Exp. Only）交互一致；列顺序变化联动兄弟卡片属性排列
- 拖放：Avalonia `DragDrop`；兄弟卡片排序拖放持久化到 keeper.json `sortIndex`
- 快捷键语义一致：Ctrl+O 打开、Ctrl+W 停止监视、Enter 开卡片、ESC 逐层返回（卡片→总览；引导面板；各模态）
  - 注：Java `SHORTCUT_DOWN` 语义——macOS 为 Cmd，其余为 Ctrl（平台加速器键）
- 升级通知弹窗三态（Modal / Auto-close 15s / Off）由 Preferences 控制，持久化于 AppConfig
- Level-up 指南粘性面板：总览中点击 level 单元格弹出，ESC/再点击/焦点外关闭逻辑一致
- Setup 对话框：指向 `data/data_001.dat`、输出目录（默认 `~/game-art`）、Extract / Use existing folder 两按钮
- 状态栏/工具栏、窗口标题、图标沿用原版文案与资产

## 8. 服务行为要点（与 Java 版逐一对齐的算法）

- **StatPotentialCalculator**：potential = current + 剩余升级次数 × 平均骰（星级修正：0星+2 / 1星+2.5 / 2星+3 / 3星+3.5），halves up 舍入
- **ExpectedStatsCalculator**：Naive = `ceil(currentBase + mean_gain × allottedCount)` 后叠 trait/perk 修正；Greedy 探索式分配（要求全部分配完毕，否则回落 Naive）；偏好持久化
- **Auto-assign by role**：P1 均分（>3 个 P1 时字母序打破平局）→ 溢出级联 P2 → P3
- **ArmorCalculator / WeaponStatsService / StatModifierService**：公式与原实现一致
- **SaveReplayService / ReplayAnalyzer**：重放存档历史 diff 的逻辑一致
- **AssetExtractor**：从 `data_001.dat` 提取资产的格式逻辑一致（该格式常量表在 SaveFormat/GameDataService 内）
- 实现各服务时以对应 Java 文件为唯一真源，不凭记忆重写公式

## 9. 测试与验收

- xUnit 迁移 Java 版全部 25 个测试类 + 1 个测试辅助类（`HexDumpSaveParser`，位于原 `test/tools/parser/`），1:1 对应（清单及宿主去向见附录 A）；`TestSavefile.sav` 一并迁移
- UI Controller 测试（MainControllerTest 等）→ 对应 ViewModel 测试（无 UI 线程依赖）
- 可选集成测试：`-Dsav.file` → 环境变量 `BRO_LEDGER_SAV` 提供真实 .sav 时才运行
- **验收标准**：
  1. 全部测试绿（Core + App）
  2. 三平台 AOT publish 成功且产物可启动
  3. 同一 .sav 下与 Java 版并排：兄弟列表、数值、装备、特性、perk 显示一致
  4. keeper.json / roles.json 与 Java 版互读互写无丢失（手工双向验证）
  5. 分享码：Java 版发布 → C# 版导入成功（或反向）

## 10. 错误处理（与原版一致）

- 解析失败：日志 + 跳过该兄弟，不中断整体
- 游戏写档文件锁：等待重试（对齐原版 WatchService 行为）
- 存档不存在/格式不符：UI 提示，不崩溃
- 网络分享/导入失败：对话框内错误提示
- 存档目录不可写：keeper.json 走 fallback 路径（§5.2）

## 附录 A · 测试迁移清单（25 测试类 + 1 辅助，含宿主去向）

| 测试类 | 宿主（§4 映射） |
|---|---|
| AnnotationServiceHelpersTest / AnnotationServiceTest | Core/Services/AnnotationService |
| AppConfigTest | Core/Services/AppConfig |
| ArmorCalculatorTest | Core/Parsing/ArmorCalculator |
| BattleBrothersLevelPredictorTest | Core/Services/ExpectedStatsCalculator（内含 level predictor） |
| ExpectedStatsCalculatorTest | Core/Services/ExpectedStatsCalculator |
| GameDataSchemaTest | Core 嵌入资源 game-data.schema.json |
| HexUtilsTest | Core/Utils/HexUtils |
| ImageMapServiceTest | Core/Services/ImageMapService |
| MainControllerTest | App/ViewModels/MainViewModel |
| OverviewCalcTest | Core（OverviewCalc 为纯计算，从 ui/ 下沉至 Core，定 Core 无二义） |
| Phase3ResourcesTest / Phase4ResourcesTest | App 嵌入资源存在性/良构性验证（Phase3: CSS/AXAML；Phase4: stat 图标 PNG 等） |
| PreviewReconcileTest | Core/Services/AnnotationService（`previewReconcile`，AnnotationService.java L440） |
| ReplayAnalyzerTest | Tools/ReplayAnalyzer（Core.Tests 额外引用 BroLedger.Tools） |
| RoleImportControllerTest | App/ViewModels/RoleImportViewModel |
| RoleManagerControllerTest | App/ViewModels/RoleManagerViewModel |
| RolePackTest | Core/Models/RolePack |
| RoleServiceTest | Core/Services/RoleService |
| RoleShareControllerTest | App/ViewModels/RoleShareViewModel |
| RoleShareServiceTest | Core/Services/RoleShareService——base URL 注入方式对齐 Java 版 `RoleShareService.createForTest(baseUrl)`；本地 stub 对应 Java 内嵌 HttpServer（绑 127.0.0.1 随机端口） |
| SaveParserTest | Core/Parsing/SaveParser（需 `BRO_LEDGER_SAV` 的集成测试除外） |
| SaveReplayServiceTest | Core/Services/SaveReplayService |
| StatModifierServiceTest | Core/Services/StatModifierService |
| StatPotentialCalculatorTest | Core/Services/StatPotentialCalculator |
| HexDumpSaveParser（辅助类，无 Test 后缀） | tests/ 测试辅助，原样迁移 |

## 附录 B · 原版关键参考文件

- Fingerprint：`src/main/java/.../parser/BrotherParser.java`（computeFingerprint / fingerprintInput）
- keeper.json 格式：`service/AnnotationService.java`（含 fallback 命名）
- 分享协议：`service/RoleShareService.java`（端点、码格式、JSON 体）
- 公式真源：`service/StatPotentialCalculator.java`、`service/ExpectedStatsCalculator.java`、`parser/ArmorCalculator.java`
