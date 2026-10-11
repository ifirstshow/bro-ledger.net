# 计划 ③/⑤：Tools 控制台工具 + Role 分享 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 移植 RoleShareService（同一云端 Worker 端点，分享码与 Java 版互通）与 3 个控制台工具，迁移 2 个测试类（Java 侧 39 个 @Test：RoleShareServiceTest 19 + ReplayAnalyzerTest 20）。

**Architecture:** 新增 `src/BroLedger.Tools`（单一控制台项目、动词式分发 `replay|csv|savefile`——见 Global Constraints 裁决），三个工具类各自暴露 `public static int Run(string[] args)`。RoleShareService 归 `BroLedger.Core/Services/`，HTTP 用 BCL `HttpClient`，测试用自写 `HttpTestServer`（TcpListener 最小 HTTP stub，零新依赖）。Core.Tests 已引用 Tools（计划①配置），需补 Tools 的 InternalsVisibleTo。

**Tech Stack:** 同前两计划；无新 NuGet 包。

**Spec:** `docs/superpowers/specs/2026-10-10-bro-ledger-csharp-avalonia-design.md`（§4.5 tools、§5.5 Role 分享、附录 A 测试清单）

**Java 真源根**（简写 `JAVA` = `C:\Users\afly\Documents\Projects\BattleBrothers\bro-ledger\src\main\java\se\niclas\broledger`；`JAVATEST` = 同仓 `src\test\java\se\niclas\broledger`）。

**既有接口**（计划①②产出，勿改）：`SaveParser.Parse/Warnings`、`BrotherLocator/HexReader/ItemParser/HexUtils`、`SaveReplayService`（`Instance/ReplayDir/AnnotationsFor/ListAll/Snapshots/Prune`、`ReplayEntry`）、`AnnotationService`（`Instance/PreviewReconcile/ReadAnnotationMaps`、`LevelUpEvent`——其 `StatDeltas/ConsumedIncreases` 为 `List<KeyValuePair<Stat,int>>` 按 Stat.Values 序）、`DictionaryService/RoleService/AppConfig/StatModifierService/StatPotentialCalculator`、`OverviewCalc`（含 `TierOrMax/DecoratePerkName`）、`Role/RolePack`（**常量 `RolePack.SchemaVersion = 1`、实例属性 `PackSchemaVersion`**——计划②产出名，wire 名 `schemaVersion`；`ForSharing`）、`LevelUpEvent`（8 字段：`Name/Adjusted/LevelsAssigned/StatDeltas/AdjustedStats/ConsumedIncreases/AddedPerkIds/Post11`）、`DataJsonContext`（internal partial，追加 [JsonSerializable]；JSON 一律泛型重载——计划②裁决）。

## Global Constraints

- 沿用前两计划全部 Global Constraints（net10.0、零警告、1:1 移植规则、source-gen JSON、测试逐断言对照 Java 转录）
- **Tools 结构裁决**：单一控制台项目 `src/BroLedger.Tools`（`OutputType=Exe`），`Program.cs` 按首参数动词分发（`replay`→ReplayAnalyzer、`csv`→BrotherCsvExporter、`savefile`→BrotherSavefileExporter；无动词→打印用法返回 1），三个工具类各 `public static int Run(string[] args)`，工具内部参数风格与 Java main 逐字一致（动词后的参数即 Java 的 args）。偏离说明：Java 用 Maven exec:java 选 mainClass，C# 单 exe 动词分发是等价最小结构
- **NoSuchElementException → `KeyNotFoundException`**、**IllegalArgumentException → `ArgumentException`**、**IOException → `System.IO.IOException`**（测试断言相应类型）
- RoleShare 基址 override：Java 系统属性 `broledger.roleShareBaseUrl` → 环境变量 `BROLEDGER_ROLESHARE_BASEURL`（对齐 AppPaths 裁决）；单例 + `internal CreateForTest(baseUrl)` / `internal ResetInstance()`
- 分享码正则：`[GeneratedRegex("^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{8}$")]`（Java matches() 全串匹配 = C# 锚定）；字母表无 0/O/1/I
- HttpClient：`SocketsHttpHandler.ConnectTimeout = 10s`、`HttpClient.Timeout = 20s`（Java REQUEST_TIMEOUT）；请求头/方法/URL 路径逐字（`/packs`、`/packs/{code}`、`X-Owner-Token`）
- 测试 HTTP stub：自写 `HttpTestServer`（`TcpListener(127.0.0.1, 0)` 随机端口，读请求行+头，回调 `(method, path, ownerToken) => (status, body)`，~60 行）——替代 Java 内嵌 HttpServer；**不用** HttpListener（Windows URL ACL 权限坑）与 Kestrel（重）
- 异常消息文本逐字（"Publish failed: HTTP "、"No role pack found for code: " 等——测试断言 contains）
- 新增测试仅限移植缺口钉子（注明）；无新 NuGet 包
- 派单不预写测试数（计划②教训）：只给文件路径与「逐断言转录」

## Review Focus

1. **分享码互通**（规格 §5.5 验收 5）：Java 版发布的码 C# 能 fetch、normalize 两侧一致——字母表逐字、trim+大写归一、非法输入（0/O/1/I、长度、内嵌空格）双向拒绝 → Task 4 测试迁移逐字覆盖
2. **sanitize 敌意包防御**：store 内容不可信——null 角色、null 名、错长数组、越界值、>100 角色截断+flag、schemaVersion 越界拒绝 → Task 4 逐断言覆盖
3. **HTTP 状态码映射**：404→"expired"/not found 异常、403→friendly 文案、其余非 200→IOException；坏码不发起网络请求（不经 URI 构造）→ Task 4
4. **ReplayAnalyzer crossCheck 语义**：null 与全零等价（无计划增长）、mismatch 消息含兄弟名与字段标签 → Task 2
5. **CSV/TSV 字段分隔符**：CsvExporter 用 `;` 分隔 + sanitize（`;`→空格、换行剥离）；SavefileExporter 用 `\t`——逐字，错一个字符输出即不可比 → Task 1/3

---

### Task 1: Tools 项目脚手架 + BrotherSavefileExporter

**Files:**
- Create: `src/BroLedger.Tools/BroLedger.Tools.csproj`（Exe；**不设 PublishAot**（规格 §3——仅 App 项目 AOT）；`<InternalsVisibleTo Include="BroLedger.Core.Tests" />`；ProjectReference Core）
- Create: `src/BroLedger.Tools/Program.cs`（动词分发）
- Create: `src/BroLedger.Tools/Tools/Parser/BrotherSavefileExporter.cs`（Java `tools/parser/` 子包结构保留）
- Modify: `BroLedger.sln`（add project）

**Interfaces:**
- Consumes: `BrotherLocator.FindOffsets`、`HexReader`、`ItemParser`、`HexUtils.ToHex`（**大写**——与 SaveParser 的 ToHexLower 不同，Java 即如此）、`DictionaryService`
- Produces: `BrotherSavefileExporter(DictionaryService)`：`void Write(string savePath, string outputPath)` + `public static int Run(string[] args)`；`Program.cs` 动词分发骨架（`savefile` 路由本任务，`replay`/`csv` 路由占位到 Task 2/3——占位处 `return 1` + 打印 "not yet available" 并注释任务号）

- [ ] **Step 1: 移植**（真源 `JAVA/tools/parser/BrotherSavefileExporter.java` 277 行；main→Run，`System.exit(1)`→`return 1`，usage 文案逐字；traceParse 逐行——statNames 数组 `{"hp","fat","res","mel","rng","def","ini","mor"}`、`String.format("%.4f")`→`value.ToString("0.0000", InvariantCulture)`、`trunc` 的 `…` 字符、`PARSE_EXCEPTION` 步骤记录 `e.GetType().Name`、`" | "` 合并语义→`Dictionary merge`→C# 手写 tryGetValue/赋值）
- [ ] **Step 2: `dotnet build -warnaserror` 零警告**
- [ ] **Step 3: 冒烟手验**（不入库产物）：`dotnet run --project src/BroLedger.Tools -- savefile ../bro-ledger/src/test/example/TestSavefile.sav %TEMP%\bsf.tsv`，确认输出行数=兄弟数+1、含 `name`/`stars` 列
- [ ] **Step 4: Commit** `feat(tools): project scaffold + BrotherSavefileExporter (TSV trace)`

> **移植缺口说明**：Java 无 BrotherSavefileExporter 测试；冒烟手验即门。

---

### Task 2: ReplayAnalyzer + ReplayAnalyzerTests

**Files:**
- Create: `src/BroLedger.Tools/Tools/ReplayAnalyzer.cs`
- Modify: `src/BroLedger.Tools/Program.cs`（`replay` 路由替换 Task 1 的占位）
- Test: `tests/BroLedger.Core.Tests/Tools/ReplayAnalyzerTests.cs`（迁移 `JAVATEST/tools/ReplayAnalyzerTest.java` 全部用例）

**Interfaces:**
- Consumes: Task 1 项目结构；`SaveParser`、`SaveReplayService`（`Instance/ReplayDir/AnnotationsFor/ListAll`、`ReplayEntry`）、`AnnotationService.Instance.PreviewReconcile` + 静态 `ReadAnnotationMaps`、`LevelUpEvent`（`Name/Adjusted/LevelsAssigned/StatDeltas/AddedPerkIds/ConsumedIncreases/Post11`）、`DictionaryService`
- Produces: `ReplayAnalyzer` 静态类：`public static int Run(string[] args)`（交互式 Scanner→`Console.ReadLine` 循环；`--dir` 参数、1-based transition 或 "all"、可选输出文件）+ internal 静态缝（Java 包级私有）：`ReportRosterChanges(prev, next, TextWriter, List<string>)`、`ReportLevelUpEvent(LevelUpEvent, TextWriter, DictionaryService)`、`DetectAnomalies(prev, next, List<string>)`、`CrossCheck(brothers, computedPlanned, computedPost11, actualPlanned, actualPost11)`、`IsValid(Brother)`、`DisplayTs(string?)`、`StemOf(string)`；`record Transition(int Idx, string Prev, string Next)` internal

- [ ] **Step 1: 写失败测试**（Java 20 个 @Test 逐断言转录；`ByteArrayOutputStream+PrintStream` → `StringWriter`；`fakeEvent` helper 构造 LevelUpEvent——C# 侧 StatDeltas/AdjustedStats/ConsumedIncreases/AddedPerkIds 传空集合、Post11=false；isValid 五例、detectAnomalies 三例、roster 四例、crossCheck 三例、displayTs/stemOf 五例）
- [ ] **Step 2: 运行确认编译失败**
- [ ] **Step 3: 实现**（真源 541 行逐方法。要点：① `DISPLAY_FMT "yyyy-MM-dd HH:mm:ss.SSS 'UTC'"` + `PARSE_FMT` 同 SaveReplayService；`displayTs` 用 `DateTimeOffset.TryParseExact(stem, "yyyyMMdd'T'HHmmss'_'fff'Z'", Invariant, ...)` 失败返回原串、null 安全；② `analyzeTransitions` 的 carried 状态三 map 深拷贝推进（`deepCopy` 值 clone）；③ crossCheck 的 `isNullOrZero/nullToZero` null≡全零等价；④ isValid 的 60 字长/name 空白/背景类型校验（`DictionaryService.Instance.Get`）；⑤ `"=".repeat(70)`→`new string('=', 70)`；⑥ 输出 `TextWriter`（Console.Out 或 StreamWriter））
- [ ] **Step 4: 运行确认通过；Step 5: Commit** `feat(tools): ReplayAnalyzer (transition replay + cross-check)`

---

### Task 3: BrotherCsvExporter

**Files:**
- Create: `src/BroLedger.Tools/Tools/BrotherCsvExporter.cs`
- Modify: `src/BroLedger.Tools/Program.cs`（`csv` 路由）

**Interfaces:**
- Consumes: Task 1；`SaveParser`、`AnnotationService.Instance.LoadFor/Get`、`AppConfig.Instance.Load/OrderedStats`、`RoleService.Instance.GetById`、`StatModifierService.Instance.GetTier`、`StatPotentialCalculator.Compute`、`OverviewCalc.TierOrMax/DecoratePerkName`、`DictionaryService`
- Produces: `BrotherCsvExporter(DictionaryService, StatModifierService, AnnotationService, RoleService)`：`void Export(AppConfig, string savePath, string outputPath)` + `public static int Run(string[] args)`（main 逻辑逐字：config.Load→lastSaveFilePath 缺失打印提示 return 1；无 args→存档旁同名 `.csv`）

- [ ] **Step 1: 移植**（真源 191 行；`SEP=";"`；TRAIT_TYPE_ORDER 四键 + getOrDefault 99；`PrintWriter(UTF_8)` → `new StreamWriter(outputPath, false, new UTF8Encoding(false))`；header/row 列序逐字（Name/Title/Role/Background/Level/XP + 每属性三列 abbrev/abbrev*/
abbrev+" pot" + Perk N + Traits）；`"*".repeat(stars)`；sanitize（`;`→空格、`\n`→空格、`\r`→删除）；`"--"` en-dash 无；`sortedPerkNames` 的 tier 升序+名序 → OrderBy(TierOrMax).ThenBy(dict.GetName)）
- [ ] **Step 2: `dotnet build -warnaserror` + 冒烟手验**：`dotnet run -- csv ../bro-ledger/src/test/example/TestSavefile.sav %TEMP%\bros.csv`，确认行数与列头
- [ ] **Step 3: Commit** `feat(tools): BrotherCsvExporter (semicolon CSV, stat order from config)`

> **移植缺口说明**：Java 无 BrotherCsvExporter 测试；冒烟手验即门。

---

### Task 4: RoleShareService + HttpTestServer + 测试

**Files:**
- Create: `src/BroLedger.Core/Services/RoleShareService.cs`
- Modify: `src/BroLedger.Core/Services/DataJsonContext.cs`（确认 `[JsonSerializable(typeof(RolePack))]` 在位——publish/fetch 需要；缺则追加）
- Test:
  - Create: `tests/BroLedger.Core.Tests/Helpers/HttpTestServer.cs`（TcpListener 最小 HTTP stub）
  - Create: `tests/BroLedger.Core.Tests/Services/RoleShareServiceTests.cs`（迁移 `JAVATEST/service/RoleShareServiceTest.java` 全部用例）

**Interfaces:**
- Consumes: `Role/RolePack`（计划②）、`DataJsonContext`
- Produces:
  - `record PublishResult(string Code, string? OwnerToken)`
  - 单例：`Instance`（环境变量 `BROLEDGER_ROLESHARE_BASEURL` 覆盖，缺省 `https://bro-ledger-roles.niclas-cf8.workers.dev`）、`internal static CreateForTest(string)`、`internal static ResetInstance()`
  - `static string? NormalizeCode(string?)`；`PublishResult Publish(RolePack)`；`void Update(string rawCode, string ownerToken, RolePack pack)`；`RolePack Fetch(string rawCode)`
  - `internal static RolePack Sanitize(RolePack)`（Java 包级私有，测试直打）+ private NormalizedTargets/NormalizedPerkPlan/NormalizedPriorities
  - schema 校验用 `RolePack.SchemaVersion` 常量（对 Java `RolePack.SCHEMA_VERSION`）
  - 其余常量逐字：DEFAULT_BASE_URL、REQUEST_TIMEOUT=20s、MAX_PACK_ROLES=100、MAX_ROLE_NAME_LENGTH=60、MAX_PERK_PLAN_ENTRIES=100、MAX_TARGET_STAT=999
  - `HttpTestServer`（定稿 API，实现照此）：`class HttpTestServer : IDisposable`——构造时 `TcpListener(IPAddress.Loopback, 0)` 启动 accept 循环（后台线程）；`public string Url { get; }`（`http://127.0.0.1:{port}`）；`public void Route(string method, string path, Func<(string Method, string Path, string OwnerToken), (int Status, string Body)> handler)`（逐路由注册，未匹配路由返回 500）；`Dispose()` 停 listener。请求头解析只需读 request line 与 `X-Owner-Token`；响应写 status+body（Content-Type: application/json）。单线程 accept 循环即可（测试串行）
- [ ] **Step 1: 写失败测试**（Java 19 个 @Test 逐断言转录——stub 路由逐例对应 Java HttpServer context；`assertThrows(KeyNotFoundException)`/`ArgumentException`/`IOException` 对应 Java 异常类型映射；`sanitizeNormalizesStatArrays` 起共七例直打 internal Sanitize）
- [ ] **Step 2: 运行确认编译失败**
- [ ] **Step 3: 实现**（真源 261 行逐方法。要点：① HttpClient via SocketsHttpHandler{ConnectTimeout=10s}、client.Timeout=20s；② publish POST `/packs` JSON body（`Serialize(pack, DataJsonContext.Default.RolePack)` 泛型重载）、200→解析 `{code, ownerToken}`（ownerToken 缺失→null）；③ update PUT `/packs/{code}` + `X-Owner-Token` 头；404→`KeyNotFoundException("Code {code} has expired — publish as a new code instead.")`、403→`IOException("Code {code} can't be updated from this computer.")`、其余非 200→IOException；④ fetch GET；404→KeyNotFoundException("No role pack found for code: {code}")；坏码 ArgumentException 不联网（normalizeCode 先行）；⑤ sanitize 逐句：null pack→IOException("Fetch failed: empty pack")、schemaVersion>SCHEMA_VERSION→IOException("...newer version...")、roles null→空、removeIf null、>100 截断+MarkTruncated、name null→""/60 截断、三组 normalize；⑥ NormalizedPerkPlan：仅收 OPTIONAL/PLANNED 值、null 键跳过、100 上限 break、空→null）
- [ ] **Step 4: 运行确认通过；Step 5: Commit** `feat(core): RoleShareService (worker API, hostile-pack sanitize, test HttpServer)`

---

### Task 5: 计划③收尾验收

**Files:**
- Modify: 无新文件（验证与修缺）

- [ ] **Step 1: `dotnet build -warnaserror` 零警告（全解决方案）**
- [ ] **Step 2: `dotnet test` 全绿，记录总数（计划②末 396 + 本计划新增）**
- [ ] **Step 3: 三工具冒烟**（临时目录，不入库）：savefile（Task 1 已验，复跑确认）、csv（Task 3 已验）、replay：无 replay 数据时的 stderr 提示路径（"Need at least 2 snapshots..."）
- [ ] **Step 4: 对照附录 A 核对本计划测试类**（RoleShareServiceTest、ReplayAnalyzerTest 两个；全库累计 15 个测试类 + 计划① 7 个）
- [ ] **Step 5: 有修缺提交并汇报**

---

## 后续计划路线图（不在本计划实施）

- **计划④** App UI：Avalonia 12 全套 + ImageCache（App 层）+ PerkSortMode.label/next 补齐 + FileWatcher marshal 用 `Dispatcher.UIThread.InvokeAsync` + PerkComparator 备忘已修 + visualLayerMap 消费序 + attachment icon 并排核对 + 单例 Collection 纪律
- **计划⑤** AOT 发布矩阵 + GitHub Actions 三平台 CI + TreatWarningsAsErrors 门 + 验收清单（Java 并排对照、AssetExtractor 真实 .dat、版本号构建期替换）
