# 计划 ②/⑤：BroLedger.Core 服务层 + 数据兼容 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Java 版 13 个服务/模型移植为 C#（keeper/roles/config 三种 JSON 数据格式与 Java 版字节兼容），并迁移对应的 12 个测试类（约 3,400 行）。

**Architecture:** 延续计划①的 `BroLedger.Core`（零 UI 依赖）。JSON 全部走 `DataJsonContext` source-gen（扩展既有 internal partial 上下文，不新建上下文类）。Java 包级私有测试缝 → C# `internal` + 既有 `InternalsVisibleTo("BroLedger.Core.Tests")`。单例 + 静态测试缝镜像 Java（`CreateForTest`/`ResetForTest`）。FileWatcher 的 FX 线程调度改为注入 `Action<Action>` 委托。

**Tech Stack:** 同计划①（.NET 10、xUnit、System.Text.Json source-gen）；新用 `System.IO.Compression`（AssetExtractor 的 ZipFile，BCL 自带）。

**Spec:** `docs/superpowers/specs/2026-10-10-bro-ledger-csharp-avalonia-design.md`（§4.3 服务映射、§5.1-5.3 数据兼容、§8 服务行为、附录 A 测试清单）

**Java 真源根目录**（简写 `JAVA` = `C:\Users\afly\Documents\Projects\BattleBrothers\bro-ledger\src\main\java\se\niclas\broledger`；测试根 `JAVATEST` = `...\bro-ledger\src\test\java\se\niclas\broledger`）。

**计划①既有接口**（直接使用，勿改）：`HexUtils.Sha256Hex/UInt16ToHexLE`、`Stat`（8 实例 + `Values` + `StatIndex/StarIndex/JsonKey/Abbrev/DisplayName`）、`Brother`（`Stats/Stars/PerkIds/Traits/LevelTotal/LevelPoints/Fingerprint` 等公共字段）、`GameDataService`（`Instance`/`LoadFromEmbeddedResource`/`GetEntries`/`GetImageSlots`/`GetGameArtVersion`）、`DictionaryService`（`Instance`/`Has/Get/GetType/GetName`）、`DataJsonContext`（internal，现有 `[JsonSerializable(typeof(DictionaryEntry))]`）。

## Global Constraints

- 沿用计划①全部 Global Constraints（net10.0、零警告、1:1 移植规则、大写 hex、source-gen JSON、`?? "null"` 陷阱）
- **数据格式即契约**：`.broledger.json`/`roles.json`/`config.json` 的字段名、嵌套结构、版本号必须与 Java 侧逐字一致（本计划 §各任务给出精确 schema）；未知字段容忍（STJ 默认跳过 ≡ Java FAIL_ON_UNKNOWN_PROPERTIES 禁用）
- **文件名裁决**：注解文件为 `<save完整文件名>.broledger.json`（如 `save.sav.broledger.json`）；fallback 为 `~/.bro-ledger/annotations-<sha256hex(存档绝对路径)>.json`——README/规格早期 "keeper.json" 是历史名（规格 §5.2 已订正）
- Java `writerWithDefaultPrettyPrinter()` → STJ `WriteIndented = true`（缩进风格不要求逐字节一致，但必须 human-readable 且两版可互读）
- Jackson `readerForUpdating(this)`（AppConfig 原地更新）→ C# 无对象复用反序列化：读入 DTO 后逐字段拷贝到单例（语义：缺失字段保留现值）
- 测试接缝镜像：Java 包级私有 → C# `internal`；`@TempDir` → `Path.Combine(Path.GetTempPath(), "broledger-test-" + Guid.NewGuid().ToString("N"))`，测试结束删除整目录
- Java `EnumMap<Stat,…>` 迭代序 = Stat 声明序 → C# 用 `Stat.Values` 序的 List/数组承载，禁用 Dictionary 迭代（无序）
- 日志：`ILogger? = null` → `NullLogger.Instance` 模式（同计划①）；Java `log.fine` → `LogTrace`，`warning` → `LogWarning`，`severe` → `LogError`
- 新增测试仅限移植缺口钉子（各任务注明）；新 NuGet 包：无
- **ImageCache 裁决**：整体推迟到计划④（App 层，Avalonia Bitmap）——Java 类本质是 JavaFX Image 缓存，"Core 字节半区"无消费者，纯投机代码；规格 §4.3 的拆分表述按此执行（无测试损失：Java 亦无 ImageCacheTest）
- **派发顺序**（接口依赖决定，非编号序）：Task 1 → 2 → 3 → **7** → **6** → **4** → 5 → 8 → 9 → 10 → 11 → 12 → 13 → 14（T4 依赖 T3+T6；T6 依赖 T7；T9 依赖 T5+T6+T7）

## Review Focus

规格隐含但单测覆盖不到的输入类别：

1. **Java 版写出的 .broledger.json / roles.json / config.json 被 C# 读**——字段顺序、pretty-print 细节、Java 侧 null 省略都不得破坏解析。→ 每个持久化任务的测试夹具用**Java 形状的手写 JSON 字符串**（含缺字段、null、未知字段三种变体）钉死互读。
2. **存档目录不可写**——期望注解走 fallback `annotations-<hash>.json`，且 fallback 名与 Java 逐字节一致（同一 SHA-256 输入）。→ Task 3 钉子测试：只读目录下 `LoadFor`+`SetRole` 后断言 fallback 文件存在且名含正确 hash。（计划① `HexUtils.Sha256Hex` 已钉）
3. **损坏的 config.json**——期望备份为 `config.json.bad-<timestamp>` 后以默认值继续，绝不静默覆盖。→ Task 2 测试覆盖（Java AppConfigTest 对应用例）。
4. **ExpectedStats 边界**：`levelTotal ≥ 11` → remaining 0；Gifted perk（`9899E380`）预算；`sum(rolls) ≠ 3×remaining` → ArgumentException；灵活属性 >6 → `TooManyFlexibleStatsException`。→ Task 9 测试迁移覆盖（Java 用例即边界集）。
5. **FileWatcher 事件风暴/OVERFLOW**——OVERFLOW 跳过 + 250ms×6 settle 轮询 + 2s 去抖，以 Java 真源为准；C# 侧 OVERFLOW 由 `FileSystemWatcher.Error`/`InternalBufferOverflowException` 承接。无自动化测试（Java 亦无）——Task 12 代码走查 + 计划④ UI 联动手验。

---

### Task 1: AppPaths + AppInfo（Core 版）+ Stat.Ordinal

**Files:**
- Create: `src/BroLedger.Core/Utils/AppPaths.cs`
- Create: `src/BroLedger.Core/AppInfo.cs`
- Modify: `src/BroLedger.Core/Models/Stat.cs`（加 `Ordinal`）
- Test: `tests/BroLedger.Core.Tests/Utils/AppPathsTests.cs`

**Stat.Ordinal 移植缺口**：Java 全代码库用 `Stat.ordinal()`（0–7，声明序）作为注解/角色数组的下标契约；计划①的 C# `Stat` record 无此成员。在 `Stat` record 内加：`public int Ordinal { get; }`——8 个静态实例构造时按 `Values` 序传 0–7（直接在静态字段初始化器里写死数字，与 Java 枚举声明序逐一核对）。计划①的 62 个测试不得受影响。

**Interfaces:**
- Consumes: 无
- Produces:
  - `AppPaths.DataDir()`：`string`。环境变量 `BROLEDGER_DATADIR` 非空 → 返回其值；否则 `Path.Combine(Environment.GetFolderPath(UserProfile), ".bro-ledger")`（Java 系统属性 `broledger.dataDir` → 环境变量，测试缝）
  - `AppInfo.Version()` / `AppInfo.IsDebug()`：读嵌入资源 `BroLedger.Core.Resources.version.properties`（key `version`/`debug.enabled`；缺省 `""` 与 `true`）。**布局裁决**：AppInfo 归 Core 而非规格 §4.5b 的 App 层——SaveReplayService（Core）依赖 `IsDebug()`，Core 不能反向引用 App（Task 10 落实后记台账）
- Create: `src/BroLedger.Core/Resources/version.properties`（自 `JAVA/../resources/se/niclas/broledger/version.properties` 原样复制）+ csproj EmbeddedResource（LogicalName `BroLedger.Core.Resources.version.properties`）

- [ ] **Step 1: 写失败测试**

> **移植缺口说明**：Java 无 AppPathsTest/AppInfoTest；路径解析是数据兼容地基（fallback 文件名、roles/config 位置），钉 3 个最小用例。

```csharp
using BroLedger.Core.Utils;
using Xunit;

namespace BroLedger.Core.Tests.Utils;

public class AppPathsTests : IDisposable
{
    private readonly string? _saved = Environment.GetEnvironmentVariable("BROLEDGER_DATADIR");

    [Fact]
    public void DataDir_OverrideEnvVar_Wins()
    {
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", @"D:\custom-dir");
        Assert.Equal(@"D:\custom-dir", AppPaths.DataDir());
    }

    [Fact]
    public void DataDir_EmptyOverride_FallsBackToHome()
    {
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", "");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".bro-ledger"), AppPaths.DataDir());
    }

    [Fact]
    public void AppInfo_IsDebug_DefaultsTrue() => Assert.True(AppInfo.IsDebug());

    public void Dispose() =>
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _saved);
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 编译错误 `AppPaths` 不存在

- [ ] **Step 3: 实现**（真源：`JAVA/util/AppPaths.java`、`JAVA/AppInfo.java`；AppInfo 解析 properties 格式——逐行 `key=value`，跳过 `#` 注释与空行，`Properties.load` 语义）

```csharp
namespace BroLedger.Core.Utils;

public static class AppPaths
{
    public static string DataDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("BROLEDGER_DATADIR");
        if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bro-ledger");
    }
}
```

AppInfo：读嵌入资源为字符串，解析 `version=`/`debug.enabled=` 两个 key（`Dictionary<string,string>`；Java Properties 默认值语义：`version`→`""`、`debug.enabled` 缺省→true，`"false"`（忽略大小写）→false）。资源缺失不抛（Java 侧仅 log warning）。

- [ ] **Step 4: 运行确认通过；Step 5: Commit**

```bash
dotnet test tests/BroLedger.Core.Tests   # 65/65
git add -A && git commit -m "feat(core): AppPaths + AppInfo (Core placement ruling)"
```

---

### Task 2: AppConfig + ShareRecord 模型

**Files:**
- Create: `src/BroLedger.Core/Models/ShareRecord.cs`
- Create: `src/BroLedger.Core/Services/AppConfig.cs`
- Modify: `src/BroLedger.Core/Services/DataJsonContext.cs`（追加 `[JsonSerializable]`）
- Test: `tests/BroLedger.Core.Tests/Services/AppConfigTests.cs`（迁移 `JAVATEST/service/AppConfigTest.java` 全部 320 行用例）

**Interfaces:**
- Consumes: `AppPaths`（Task 1）、`DataJsonContext`（计划①）
- Produces:
  - `ShareRecord`：公共属性 `Code/OwnerToken/Title/UploadedAt(string)/RoleCount(int)` + `[JsonPropertyName]`（`code/ownerToken/title/uploadedAt/roleCount`）+ `bool Updatable()` + 双构造器
  - `AppConfig` 单例：公共可变属性（PascalCase + JsonPropertyName camelCase）`LastSaveFilePath/GameArtDirectory/WindowWidth=1800/WindowHeight=1320/WindowX(int?)/WindowY(int?)/OverviewColumnOrder(List<string>?)/RoleOrder(List<string>?)/ExpectedStatsMode="NAIVE"/LevelUpModalMode="MODAL"/ReplayCaptureEnabled=false/RecentShares(List<ShareRecord>?)`；`const int RecentSharesMax = 5`
  - 方法：`Instance`、`Load()`、`LoadFrom(string path)`、`Save()`、`SaveTo(string path)`、`RecordShare(ShareRecord)`、`bool HasGameArtDirectory()`、`string? GameArtRoot()`、`Stat[] OrderedStats()`、`internal static string? StatAbbrevFromColumnId(string?)`、`internal static void ResetForTest()`、`internal static string? ConfigFileForTest`
  - JSON 形状（config.json，pretty）：顶层即 AppConfig 字段（camelCase），无包装对象

- [ ] **Step 1: 写失败测试**——迁移 Java 用例，接缝替换：`configFileForTest` → `AppConfig.ConfigFileForTest`（internal static 属性）、`resetForTest` → `ResetForTest()`、`@TempDir` → GUID 临时目录。覆盖：默认值、SaveTo/LoadFrom 往返、损坏文件备份为 `.bad-<timestamp>`、RecordShare 去重+置顶+截断到 5、OrderedStats（含 `stat-<abbrev>` 列 ID 解析与补尾）、HasGameArtDirectory。**每断言对照 Java 源转录，不凭记忆**
- [ ] **Step 2: 运行确认编译失败**
- [ ] **Step 3: 实现**（真源 `JAVA/service/AppConfig.java` 202 行逐方法移植）
  - SaveTo：`<file>.tmp` 写入（`WriteIndented`）→ `File.Move(tmp, file, overwrite: true)`（.NET 无 ATOMIC_MOVE 跨平台常量——File.Move 即同卷移动；Java 的 AtomicMoveNotSupportedException 回退分支消失，记录偏差）+ 失败删 tmp
  - LoadFrom：文件不存在→默认值返回（LogTrace）；反序列化失败→`File.Move(file, file + ".bad-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())` + LogError/LogWarning（对齐 Java 备份语义）。DTO：私有 record `AppConfigData`（13 个可空属性，JsonPropertyName 同上）→ 读入后逐字段拷贝到单例（null 字段保留单例现值——与 Jackson readerForUpdating 一致）
  - OrderedStats：`Stat.Values` 序遍历、`stat-` 前缀解析、未见追加——逐句对照
  - DataJsonContext 追加：`[JsonSerializable(typeof(AppConfigData))]`、`[JsonSerializable(typeof(ShareRecord))]`
- [ ] **Step 4: 运行确认通过（62+4=66 起，以实际用例数为准）；Step 5: Commit** `feat(core): AppConfig + ShareRecord with Java-shaped config.json`

---

### Task 3: AnnotationService 核心（存储 + keeper 读写）

**Files:**
- Create: `src/BroLedger.Core/Models/BrotherAnnotation.cs`
- Create: `src/BroLedger.Core/Services/AnnotationService.cs`（本任务只做：单例/LoadFor/Get/Set 系/UiState/SetOrder/Flush/ResolveDestination/Write/Read + 内嵌 DTO）
- Modify: `src/BroLedger.Core/Services/DataJsonContext.cs`
- Test: `tests/BroLedger.Core.Tests/Services/AnnotationServiceTests.cs`（迁移 `JAVATEST/service/AnnotationServiceTest.java`，548 行）

**Interfaces:**
- Consumes: `AppPaths`、`HexUtils.Sha256Hex`、`BrotherAnnotation`
- Produces:
  - `BrotherAnnotation`：公共字段 `Fingerprint/RoleId(string?)/SortIndex(int?)/StatIncreases(int[]?)/Post11Increases(int[]?)/PerkPlanStatus(Dictionary<string,string>?)` + 双构造器（无参/带 fp）
  - `AnnotationService` 单例：`LoadFor(string savePath)`（主路径 `<save完整文件名>.broledger.json`、fallback `AppPaths.DataDir()/annotations-<Sha256Hex(绝对路径)>.json`——**Path 对比用绝对路径字符串**）、`Get(fp)`（缺失即建，LinkedHashMap→**插入序 Dictionary 语义用普通 Dictionary 即可**——Java 侧从不依赖注解迭代序写盘以外的行为；写盘 JSON 对象序不作契约）、`SetRole/SetStatIncreases/SetPost11Increases/SetPerkPlanStatus`（各自动 Flush）、`SetUiState/GetUiState`、`SetOrder(List<string>)`（sortIndex 0..n-1）、`UiState` 类（4 个 string? 字段：`PotentialMode/ArmorMode/WeaponTierMode/PerkSortMode` + JsonPropertyName）
  - JSON 形状：`{"version":1,"uiState":{...},"annotations":{"<fp>":{"roleId":..,"sortIndex":..,"statIncreases":[..],"post11Increases":[..],"perkPlanStatus":{..}}}}`，pretty 缩进；`version` 恒 1；空 perkPlanStatus 写 null
  - DTO：`AnnotationsFile`（`Version/UiState/Annotations`）+ `AnnotationEntry`（5 字段），source-gen
  - internal 测试缝：`internal void LoadForTest(string primaryPath, string fallbackPath)`？——**否**：Java 测试直接 `getInstance()` + `loadFor(tempPath)`，C# 同样只用公共 `LoadFor`，无新增缝
- [ ] **Step 1: 写失败测试**（对照 Java 548 行转录；临时目录缝；覆盖：无文件→空库、主路径读、fallback 回退读、写后重读往返、SetX 落盘、SetOrder、UiState 往返、**fallback 钉子**：主目录只读时（`File.SetAttributes ReadOnly` 或建不存在的父目录不可行——用「主路径父目录指向一个文件路径」制造 createDirectories 失败）注解落到 fallback 且文件名含 `HexUtils.Sha256Hex(绝对路径)`——**移植缺口钉子**，Java 侧该路径仅手测）
- [ ] **Step 2: 运行确认编译失败**
- [ ] **Step 3: 实现**（真源 `JAVA/service/AnnotationService.java` L32-241 逐方法移植；resolveDestination 的双 try/catch 目录创建语义保留；write 直接写目标（Java 无 tmp+move，保持）；`Map.of()` 不可变语义在后续任务用到时再定）
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): AnnotationService core — .broledger.json store with Java-shaped schema`

---

### Task 4: AnnotationService 对账（reconcile + preview）

**Files:**
- Modify: `src/BroLedger.Core/Services/AnnotationService.cs`（追加对账半区）
- Test:
  - `tests/BroLedger.Core.Tests/Services/AnnotationServiceHelpersTests.cs`（迁移 `JAVATEST/service/AnnotationServiceHelpersTest.java` 322 行）
  - `tests/BroLedger.Core.Tests/Services/PreviewReconcileTests.cs`（迁移 `JAVATEST/service/PreviewReconcileTest.java` 322 行）

**Interfaces:**
- Consumes: Task 3 全部、`StatPotentialCalculator.Range/RangeForStars`（Task 6——**实现顺序上 Task 6 先行**，本任务派单时注明依赖）
- Produces:
  - `record LevelUpEvent(string Name, bool Adjusted, int LevelsAssigned, IReadOnlyDictionary<Stat,int> StatDeltas, IReadOnlyList<Stat> AdjustedStats, IReadOnlyDictionary<Stat,int> ConsumedIncreases, IReadOnlyList<string> AddedPerkIds, bool Post11)`——StatDeltas/ConsumedIncreases 用 `List<KeyValuePair<Stat,int>>` 或按 Stat.Values 序的数组承载（EnumMap 序 = 声明序，**禁用 Dictionary**）
  - `ReconcileOnReload(List<Brother>, List<Brother>)`、`PreviewReconcile(old, new, Dictionary<string,int[]> plannedByFp, Dictionary<string,int[]> post11ByFp)`（就地更新两 map）、`static ReadAnnotationMaps(string? path, planned, post11)`
  - internal 静态缝（Java 包级私有→internal）：`StatDeltas/RaisesLowerBound/AddedPerks/IsPost11Reconcile/FindMatch/LevelsAssigned/ReconcilePost11/ConsumePlannedIncreases`
  - 常量 `GiftedPerkId = "9899E380"`
- [ ] **Step 1: 写失败测试**（两个 Java 测试文件逐用例转录——Helpers 测试全部打在 internal 静态上；PreviewReconcileTest 打在 `PreviewReconcile` 上，构造 Brother 用对象初始化器）
- [ ] **Step 2: 确认编译失败**
- [ ] **Step 3: 实现**（真源 L243-520：`Reconciliation` record、`reconcileOnReload`/`reconcileBrother` 的日志行 → LogTrace 保留、两个静态 helper、preview 半区零 I/O 语义）
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): AnnotationService level-up reconciliation + preview (Gifted=9899E380)`

---

### Task 5: RoleService + Role + RolePack

**Files:**
- Create: `src/BroLedger.Core/Models/Role.cs`
- Create: `src/BroLedger.Core/Models/RolePack.cs`
- Create: `src/BroLedger.Core/Services/RoleService.cs`
- Modify: `src/BroLedger.Core/Services/DataJsonContext.cs`
- Test:
  - `tests/BroLedger.Core.Tests/Services/RoleServiceTests.cs`（迁移 `JAVATEST/service/RoleServiceTest.java` 359 行）
  - `tests/BroLedger.Core.Tests/Models/RolePackTests.cs`（迁移 `JAVATEST/model/RolePackTest.java` 116 行）

**Interfaces:**
- Consumes: `AppConfig`（Task 2：`RoleOrder`/`Save()`）、`DataJsonContext`
- Produces:
  - `Role`：公共字段 `Id/Name=""/Frontline=true/TargetStats=int[8]/Priority=int[8]{3,...}/PerkPlanTemplate(Dictionary<string,string>?)/SourcePackCode/SourceRoleId` + `[JsonPropertyName]`（`id/name/frontline/targetStats/priority/perkPlanTemplate/sourcePackCode/sourceRoleId`）+ `static CopyContentInto(src, dest)`（深拷贝数组与模板）+ `Equals/GetHashCode`（按 Id）
  - `RolePack`：`const int SchemaVersion = 1`、字段 `SchemaVersion/Label/Roles`、`static ForSharing(label, roles)`（拷贝角色、剥 provenance）、`bool Truncated()`、`MarkTruncated()`——**truncated 为私有字段不加 JsonPropertyName**（Jackson 不序列化）
  - `RoleService` 单例：`GetAll()`（AppConfig.RoleOrder 排序 + 缺失追加）、`GetById`、`FindBySource(packCode, sourceRoleId)`、`Add(name, frontline)`（UUID → `Guid.NewGuid().ToString()`）、`ImportPack(pack, selected, sourcePackCode)`（原地更新 vs 新建+provenance）、`Update(role)`、`Delete(id)`、`Move(id, newIndex)`、`internal static CreateForTest(path)`、`internal static ResetInstance()`
  - roles.json 形状：`{"version":1,"roles":[...]}` pretty
- [ ] **Step 1: 写失败测试**（Java 475 行逐用例；RoleServiceTest 的 `AppConfig.resetForTest()`/`configFileForTest` → `AppConfig.ResetForTest()`/`ConfigFileForTest`；**互读钉子**：一个用例的夹具 JSON 用 Java 形状手写——含 `sourcePackCode/sourceRoleId` 字段——读入断言字段齐全）
- [ ] **Step 2: 确认编译失败**
- [ ] **Step 3: 实现**（真源三文件逐方法；`UUID.randomUUID()` → `Guid.NewGuid().ToString()`——格式同为 8-4-4-4-12 小写连字符，与 Java 兼容；Move 只写 AppConfig 不写 roles.json——与 Java 一致）
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): RoleService + Role + RolePack (roles.json compat, provenance import)`

---

### Task 6: StatPotentialCalculator

**Files:**
- Create: `src/BroLedger.Core/Services/StatPotentialCalculator.cs`
- Test: `tests/BroLedger.Core.Tests/Services/StatPotentialCalculatorTests.cs`（迁移 `JAVATEST/service/StatPotentialCalculatorTest.java` 202 行）

**Interfaces:**
- Consumes: `Stat`、`StatModifierService`（Task 7——**先于本任务完成**，调整执行顺序：Task 7 → Task 6；派单时注明）
- Produces: `record Range(int Min, int Max)`、`record Potential(int BasePotential, int FinalPotential, int MinBasePotential, int MaxBasePotential, int RemainingLevels)`；`internal static Range? BaseRange(int)`、`internal static Range? RangeForStars(int, int)`、`internal static Range ApplyStars(Range, int)`、`internal static int ProjectBase(int,int,int)`、`internal static int ProjectExpectedBase(int,int,Range)`、`public static int ExpectedBasePotential(int, Stat, int, int)`、`public static Potential Compute(Brother, Stat)`
- [ ] **Step 1: 写失败测试**（BASE_RANGES 表值逐项断言：HP/Res/Fat (2,4)、MS (1,3)、RS (2,4)、MD (1,3)、RD (2,4)、Init (3,5)；星级规则 1★min+1/2★min+2/3★max+1；用例对照 Java 转录）
- [ ] **Step 2: 确认编译失败**
- [ ] **Step 3: 实现**（真源 120 行逐方法；`Math.round(double)`→`Math.Round`（** banker's rounding 陷阱**：Java Math.round = floor(x+0.5)，C# Math.Round 默认 ToEven——必须 `Math.Round(x, MidpointRounding.AwayFromZero)` 或 `(int)Math.Floor(x + 0.5)`；计划①规格的 "halves up" 即此语义）
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): StatPotentialCalculator (roll ranges, star adjustments, halves-up rounding)`

---

### Task 7: StatModifierService + StatModifier 模型

**Files:**
- Create: `src/BroLedger.Core/Models/StatModifier.cs`
- Create: `src/BroLedger.Core/Services/StatModifierService.cs`
- Modify: `src/BroLedger.Core/Services/DataJsonContext.cs`
- Test: `tests/BroLedger.Core.Tests/Services/StatModifierServiceTests.cs`（迁移 `JAVATEST/service/StatModifierServiceTest.java` 196 行）

**Interfaces:**
- Consumes: `GameDataService`、`DictionaryService`、`DataJsonContext`
- Produces:
  - `StatModifier`：`HexId/Tier(int?)/Effects(List<Effect>)/Description`；嵌套 `Effect(Stat/Percentage(int?)/Points(int?)，JsonPropertyName: stat/percentage/points)`
  - `record Breakdown(int Base, int Flat, int SumPct, int FinalValue, IReadOnlyList<Contribution> Contributions)`、`record Contribution(string HexId, string? Name, int? Percentage, int? Points)`
  - 单例：`LoadFromEmbeddedResource()`、`internal LoadFromEntries(JsonNode?)`（entries 中带 `modifier` 子对象的项）、`internal LoadFromStream(Stream)`（legacy `{modifiers:[...]}` 测试缝）、`StatModifier? ByHexId(string?)`、`int? GetTier(string?)`、`Breakdown Compute(Brother, Stat)`、`Breakdown ComputeWithBase(Brother, Stat, int)`、`internal static int ApplyModifiers(int, int, int)`
- [ ] **Step 1: 写失败测试**（Java 196 行转录；`loadFromStream` → `LoadFromStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))`）
- [ ] **Step 2: 确认编译失败**
- [ ] **Step 3: 实现**（真源 160 行逐方法；公式 `(base + flat) × (1 + sumPct/100.0)` 后 `(int)` 截断——C# `(int)` 同为向零截断；perks 先于 traits 累计——顺序影响 Contribution 列表序，保留）
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): StatModifierService (flat→pct pipeline, legacy load seam)`

---

### Task 8: WeaponStatsService + WeaponStats 模型

**Files:**
- Create: `src/BroLedger.Core/Models/WeaponStats.cs`
- Create: `src/BroLedger.Core/Services/WeaponStatsService.cs`
- Modify: `src/BroLedger.Core/Services/DataJsonContext.cs`

**Interfaces:**
- Consumes: `GameDataService`、`DataJsonContext`
- Produces: `WeaponStats`（`WeaponClass(string?)/Tier(int?)/Worth(int?)/DamageMin(int?)/DamageMax(int?)/IgnoresArmor(int?)/VsArmor(int?)`，JsonPropertyName camelCase）；单例 `Load(Stream)`（`{hexId:{...}}` map，键大写归一）、`LoadFromEmbeddedResource()`、`internal LoadFromEntries(JsonNode?)`（带 `weapon` 子对象的项）、`Get/GetTier/GetWeaponClass/GetAll`
- **移植缺口说明**：Java 无 WeaponStatsServiceTest——无独立测试；由 Task 9 的 greedy 链路与计划③ ReplayAnalyzer 间接覆盖；步骤 = 移植 → `dotnet build -warnaserror` 零警告 → 提交

- [ ] **Step 1: 实现**（真源 `JAVA/service/WeaponStatsService.java` 87 行 + 模型；`StreamReadFeature.INCLUDE_SOURCE_IN_LOCATION` 仅影响错误定位信息，C# 无对应必要——省略并记录）
- [ ] **Step 2: 构建 + Commit** `feat(core): WeaponStatsService (weapon sub-object map)`

---

### Task 9: ExpectedStatsCalculator

**Files:**
- Create: `src/BroLedger.Core/Services/ExpectedStatsCalculator.cs`
- Test:
  - `tests/BroLedger.Core.Tests/Services/ExpectedStatsCalculatorTests.cs`（迁移 `JAVATEST/service/ExpectedStatsCalculatorTest.java` 426 行）
  - `tests/BroLedger.Core.Tests/Services/BattleBrothersLevelPredictorTests.cs`（迁移 `JAVATEST/service/BattleBrothersLevelPredictorTest.java` 213 行）

**Interfaces:**
- Consumes: `StatPotentialCalculator`（Task 6）、`StatModifierService`（Task 7）、`Role`（Task 5）、`Stat`
- Produces:
  - `enum Mode { Naive, Greedy }` + `static Mode Parse(string?)`（"GREEDY" 忽略大小写→Greedy，其余→Naive）
  - `record Expected(int BaseExpected, int FinalExpected, int Count, int RemainingLevels)`
  - `record PriorityEntry(Stat Stat, bool Fixed, int RollValue, double Deviation)`
  - `static double[][] ExpectedGains(int levelsRemaining, int[] stars, int[] rolls, int[] priority)`（返回 `[greedy, naive]`，按 Stat.Ordinal 序）＋ `sealed class TooManyFlexibleStatsException : Exception`
  - `static List<PriorityEntry> RollPriorityGuideEntries(...)`、`static List<string> RollPriorityGuide(...)`（格式串逐字：`"Always pick %-18s  -"` → `"Always pick {0,-18}  -"`、`"%-18s  %d   %+.1f"` → `string.Format` 等价 `{1}   {2:+0.0;-0.0;+0.0}`——**正负号格式对照 Java `%+.1f` 逐字核对**）
  - `static int RemainingLevels(Brother)` / `static int RemainingLevels(int levelTotal, int levelPoints)`
  - `static Expected Compute(Brother, Stat, int[]?, Mode)` / 5 参重载（post11Increases）
  - `static int[] AutoAssignByRole(Brother, Role)` / `(Role, int)` / `AutoAssignPost11ByRole(Brother, Role)`
  - internal：`AllocateByRoleTiers/StatsAtTier/MeanRoll/RollDeviation/IsFixed/IsFlexible/FlexibleSlots/IsFullyAllocated`（private `WithinRemaining/NaiveBase/GreedyBase` 不需要缝）
- [ ] **Step 1: 写失败测试**（两个 Java 文件 639 行逐用例转录；greedy 枚举与 memo 行为由 Java 期望值钉死——**不得**改断言迁就实现）
- [ ] **Step 2: 确认编译失败**
- [ ] **Step 3: 实现**（真源 557 行逐方法。关键移植点：① memo 键 `Arrays.toString(remaining)` → `string.Join(",", remaining)`——键格式不影响语义只影响命中；② 枚举递归 `enumerate` 逐行移植；③ 平局打破链：deviation 降序 → rolls 降序 → priority 升序 → DisplayName Ordinal 升序；④ `allocateByRoleTiers` 的 equal-split + 字母序 remainder；⑤ `(int)Math.ceil` → `(int)Math.Ceiling`（double 精度行为一致））
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): ExpectedStatsCalculator (greedy enumeration + role tier allocation)`

---

### Task 10: SaveReplayService

**Files:**
- Create: `src/BroLedger.Core/Services/SaveReplayService.cs`
- Test: `tests/BroLedger.Core.Tests/Services/SaveReplayServiceTests.cs`（迁移 `JAVATEST/service/SaveReplayServiceTest.java` 349 行）

**Interfaces:**
- Consumes: `AppPaths`、`HexUtils.Sha256Hex`、`AppConfig.ReplayCaptureEnabled`、`AppInfo.IsDebug`
- Produces:
  - `const int MaxSnapshots = 5`；`record ReplayEntry(string Dir, string SourceSavePath, int SnapshotCount)`
  - 单例：`ReplayDir(string savePath)`（`baseDir/Sha256Hex(绝对路径)`）、`Capture(string savePath)`（disabled 即返回；注解字节同步读、复制在 `Task.Run` 后台线程——Java daemon Thread 等价；**时间戳格式** `yyyyMMdd'T'HHmmss'_'SSS'Z'` UTC 逐字）、`Snapshots(savePath)`（.sav 升序列表）、`AnnotationsFor(snapshot)`（同名 `.broledger.json` 对）、`ListAll()`（按源路径排序）、`Prune(dir, keep)`（删旧+配对注解）
  - `internal SaveReplayService(string baseDir)`（测试缝）+ `internal static ResetInstance()`——Java 包私有构造器 + 单例，C# 镜像
- [ ] **Step 1: 写失败测试**（Java 349 行转录；后台线程 → 测试中 `Capture` 后需等待：Java 测试如何处理就如何移植（读其代码——大概率轮询/直接调用私有 doCapture 语义，若 Java 测试直接构造与同步调用则照搬））
- [ ] **Step 2: 确认编译失败**
- [ ] **Step 3: 实现**（真源 224 行逐方法；`DateTimeFormatter` → `DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'_'SSS'Z'", CultureInfo.InvariantCulture)`；`Files.copy(REPLACE_EXISTING)` → `File.Copy(src, dest, true)`）
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): SaveReplayService (timestamped snapshots + prune)`

---

### Task 11: AssetExtractor

**Files:**
- Create: `src/BroLedger.Core/Services/AssetExtractor.cs`

**Interfaces:**
- Consumes: `GameDataService.GetImageSlots/GetGameArtVersion`
- Produces: `record ExtractionResult(int Total, int Extracted, int Missing, int Fallback)`、`delegate void ProgressListener(int done, int total, string currentFile)`；`ExtractionResult ExtractMapped(datFile, outDir, listener)`、`ExtractWithFallback(...)`、`static bool IsGameArtOutdated(string? gameArtDir)`
- **移植缺口说明**：Java 无 AssetExtractorTest（需真实 .dat）；步骤 = 移植 → 构建零警告 → 提交。**计划⑤验收**用真实 `data_001.dat` 手验。

- [ ] **Step 1: 实现**（真源 `JAVA/service/AssetExtractor.java` 200 行。关键点：① `ZipFile(UTF_8)` → `ZipFile.OpenRead`（.NET ZipArchive 默认 UTF-8 标志处理——**entry 名含非 ASCII 时行为差异记台账**，BB 资产路径全 ASCII 实际无差）；② PATTERN_PREFIXES 18 个前缀 LinkedHashSet → `string[]` 保序；③ fallback 相位：跳过已映射、`.png` 后缀（`ToLowerInvariant`）、`Contains(prefix)` 语义；④ version 文件 `{ "version": N }` pretty 写 + 读 `path("version").asInt(-1)`；⑤ `GameArtVersion` 比较语义）
- [ ] **Step 2: `dotnet build -warnaserror`；Step 3: Commit** `feat(core): AssetExtractor (mapped + fallback extraction from data_001.dat)`

---

### Task 12: FileWatcherService

**Files:**
- Create: `src/BroLedger.Core/Services/FileWatcherService.cs`

**Interfaces:**
- Produces: `class FileWatcherService`：`Watch(string savePath, Action onChange, Action<Action> marshal)`、`Stop()`。
- **调度裁决**：Java `Platform.runLater(onChange)`（FX 线程）→ 注入 `marshal` 委托；App（计划④）传 `Avalonia.Threading.Dispatcher.UIThread.Invoke`，测试传直接调用 `a => a()`。**常量逐字**：`POLL_INTERVAL_MS=250`、`POLL_MAX_ATTEMPTS=6`、`DEBOUNCE_MS=2000`
- **移植缺口说明**：Java 无专项测试（时序型）；步骤 = 移植 → 构建 → 提交；`FileSystemWatcher.Error` 事件（缓冲溢出 ≈ Java OVERFLOW）→ LogTrace + 继续观察（Java OVERFLOW 是 continue 跳过——语义：不触发 onChange，等待下一个事件）

- [ ] **Step 1: 实现**（真源 124 行。结构：`watch()` 先 `Stop()`；`FileSystemWatcher{ Filter=target, NotifyFilter=LastWrite }` + 后台 `Thread`（`IsBackground=true`）跑循环；**Java WatchService.take() 阻塞语义** → C# 无内建阻塞 Take，用 `BlockingCollection<WatcherChangeTypes>` 或轮询 `WaitForChanged`——**推荐**：`FileSystemWatcher.Changed` 事件 → `BlockingCollection.Add`，后台线程 `Take()`（阻塞、`CompleteAdding` 后退出 ≈ ClosedWatchServiceException）；settle 轮询 `File.GetSize` 容忍 IOException（continue，对齐 Java size poll catch）；去抖 `lastFiredAt`（`Environment.TickCount64` 或 Stopwatch））
- [ ] **Step 2: `dotnet build -warnaserror`；Step 3: Commit** `feat(core): FileWatcherService (settle poll + debounce, injected marshaling)`

---

### Task 13: ImageMapService

**Files:**
- Create: `src/BroLedger.Core/Services/ImageMapService.cs`
- Test: `tests/BroLedger.Core.Tests/Services/ImageMapServiceTests.cs`（迁移 `JAVATEST/service/ImageMapServiceTest.java` 122 行）

**Interfaces:**
- Consumes: `GameDataService`
- Produces: 单例：`LoadFromEmbeddedResource()`、`internal LoadFromGameData(JsonNode? entriesNode, JsonNode? imageSlotsNode)`（**注意**：Java 签名收两个 node 但实现只读 imageSlotsNode 的五个子 map——照抄）、`Load(Stream)`（legacy 五键 map JSON）、`Resolve(slot, hexId, icon, iconSet)`（五级优先级逐字）、`ResolveHex(hexId)`（大写归一）、`ResolveAttachment(slot, hexId)`、`ResolveHexHouse(slot, hexId, house)`
- [ ] **Step 1: 写失败测试**（Java 122 行转录——含优先级与 null 链路用例）
- [ ] **Step 2: 确认编译失败**
- [ ] **Step 3: 实现**（真源 125 行；`node.get("hexDirect")` null → 空 map（**非 null 消费方**——计划④派单注记：SafeGet 可能返回 null，此处已内化））
- [ ] **Step 4: 确认通过；Step 5: Commit** `feat(core): ImageMapService (5-key resolution priority)`

---

### Task 14: OverviewCalc 下沉 Core + 计划②收尾

**Files:**
- Create: `src/BroLedger.Core/Parsing/OverviewCalc.cs`（命名空间裁定：纯计算 → `BroLedger.Core.Services`；Java 在 ui/ 包但零 UI 依赖）
- Test: `tests/BroLedger.Core.Tests/Services/OverviewCalcTests.cs`（迁移 `JAVATEST/ui/OverviewCalcTest.java` 458 行）

**Interfaces:**
- Produces: 以 `JAVA/ui/OverviewCalc.java` 的公共/包级私有成员为准，移植时逐成员登记 Produces（实现者读真源后在报告列出全部签名——**本任务先读 Java 源 260 行再定测试转录范围**）
- [ ] **Step 1: 读真源并列出成员清单**（写入报告：public × N、internal × M）
- [ ] **Step 2: 写失败测试**（458 行逐用例）
- [ ] **Step 3: 实现 + 确认通过**
- [ ] **Step 4: Commit** `feat(core): OverviewCalc (sort/compare pure calc)`
- [ ] **Step 5: 计划②收尾验收**：`dotnet build -warnaserror` 零警告；`dotnet test` 全绿（记录总数）；对照附录 A 核对本计划完成的测试类清单（AnnotationServiceHelpers/AnnotationService/AppConfig/BattleBrothersLevelPredictor/ExpectedStatsCalculator/ImageMapService/PreviewReconcile/RolePack/RoleService/SaveReplayService/StatModifierService/StatPotentialCalculator/OverviewCalc = 13 个测试类 + HexUtils/ArmorCalculator/GameDataSchema/SaveParser/BrotherLocator/Fingerprint/DictionaryService 已在计划①）；**互操作手验**：临时目录手工跑一次 C# 写 `.broledger.json` + `roles.json`，肉眼比对 Java 版文件结构（若本机有 Java 版数据文件则直接读它验证）；有修缺提交

---

## 后续计划路线图（不在本计划实施）

- **计划③** Tools + RoleShareService：同一 Worker 端点、8 字符码、`CreateForTest(baseUrl)` 注入缝、内嵌 HttpServer → 本地 stub；3 个控制台工具（ReplayAnalyzer 依赖本计划 SaveReplayService/AnnotationService.PreviewReconcile/ReadAnnotationMaps）
- **计划④** App UI：Avalonia 12 全套 + ImageCache（App 层 Bitmap LRU + 1×1 灰占位 PNG，字节常量在 Java ImageCache.java L69-79）+ FileWatcher marshal 注入 Dispatcher
- **计划⑤** AOT 发布矩阵 + CI + 验收清单（含 Java 并排对照与 AssetExtractor 真实 .dat 手验）
