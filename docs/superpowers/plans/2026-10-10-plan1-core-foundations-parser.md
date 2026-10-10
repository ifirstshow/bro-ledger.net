# 计划 ①/⑤：BroLedger.Core 基础 + 存档解析管线 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭建 bro-ledger.net 解决方案骨架，并将 Java 版解析层（HexUtils → 模型 → 字典 → 解析器）1:1 移植为 C#，对真实 `TestSavefile.sav` 的集成测试全绿。

**Architecture:** 单库 `BroLedger.Core`（零 UI 依赖）+ `BroLedger.Core.Tests`（xUnit）。解析策略与 Java 版逐字节一致：整个 .sav 读为**小写 hex 字符串**，`HexReader` 以 hex 字符为游标单位在其上读取。`DictionaryService`/`GameDataService` 用 System.Text.Json source generator（AOT 要求）。

**Tech Stack:** .NET 10、C#（latest）、xUnit、System.Text.Json（source-gen）、JsonSchema.Net（仅测试）、Microsoft.Extensions.Logging.Abstractions。

**Spec:** `docs/superpowers/specs/2026-10-10-bro-ledger-csharp-avalonia-design.md`（§3 仓库结构、§4 模块映射、§5.1 fingerprint、§6 AOT、§9 测试）

**本计划是 5 个顺序计划中的第 1 个**：①Core基础+解析（本计划）→ ②服务层+数据兼容 → ③Tools+Role分享 → ④App UI → ⑤AOT+CI收尾。后续计划在前序真实代码上编写。

**Java 真源根目录**（下文简写 `JAVA/src/main/java/se/niclas/broledger` = `C:\Users\afly\Documents\Projects\BattleBrothers\bro-ledger\src\main\java\se\niclas\broledger`）。

## Global Constraints

- TargetFramework `net10.0`；`Nullable=enable`；`ImplicitUsings=enable`；`LangVersion=latest`（Directory.Build.props 统一）
- 所有项目 `IsAotCompatible=true`；本计划**不**设 `PublishAot`（App 项目专属，计划⑤）
- 本地构建须零警告；`TreatWarningsAsErrors` 只在 CI（计划⑤）
- **1:1 移植规则**：对应 Java 文件是唯一真源。逐方法移植，不改算法、不改舍入、不改字符串常量（含 `—` en-dash U+2013、`" – "` 分隔符）。命名换为 C# PascalCase；Java 可变公共字段 → C# 公共字段（不用属性，保持直改直读）
- fingerprint 与全部面向数据的 hex 输出为**大写**（`Convert.ToHexString` 默认大写）；`ToHexLower` 为**小写**
- JSON 反序列化一律走 `JsonSerializerContext` source generator；禁止运行时反射序列化
- Java `StringBuilder.append((String)null)` 追加字面量 `"null"`；C# `Append((string?)null)` 追加空串 —— 移植时必须 `?? "null"`（见 Task 8 钉子测试）
- 测试 1:1 迁移；新增测试仅限移植缺口钉子（fingerprint 向量、BrotherLocator 合成 hex、DictionaryService 最小测试），每个都在任务中注明缺口原因
- 新 NuGet 包仅限：`Microsoft.Extensions.Logging.Abstractions`（Core）、`xunit`/`xunit.runner.visualstudio`/`Microsoft.NET.Test.Sdk`/`Json.Schema.Net`（Tests）。用 `dotnet add package` 取最新稳定版，不手写版本号

## Review Focus

规格隐含但单测覆盖不到的 5 个输入类别，实现者最容易踩：

1. **游戏写档中途的截断 .sav**——期望：单兄弟解析异常被捕获、其余兄弟照常返回，进程不崩。→ Task 9 的每兄弟独立 `try/catch (Exception)`；Task 10 手工验证：截断副本 `TestSavefile.sav` 后 `Parse` 不抛且兄弟数 > 0。
2. **存档含未知物品 ID**——期望：该兄弟停止解析（`ParseInventory` 返回 false）、warning 收集进 `Warnings`、其余兄弟不受影响。→ Task 9 `printRoster` 手验 + Task 10 手工验证（用 game-data.json 中不存在的物品 ID 改造合成存档可选；若无合成条件则代码走查确认 return-false 路径）。
3. **非 Battle Brothers 文件（无签名）**——期望：`FindOffsets` 返回空、`ParseHex` 返回空列表、不抛异常。→ Task 6 钉子测试覆盖。
4. **大小写混用 hex 输入**——期望：`ReadHexBytes` 输出统一大写、字典键查找大写归一后仍命中。→ Task 2 `toHexLower`/`toHex` 大小写钉子 + Task 5 DictionaryService 大写归一测试。
5. **backgroundHexId 为 null 的兄弟**（存档损坏/无背景条目）——期望 fingerprint 输入含字面量 `"null"`（Java 语义）。→ Task 8 向量 `97B3FA4BF310CB3C9CE33EE03ED25182` 钉死。

---

### Task 1: 解决方案脚手架

**Files:**
- Create: `Directory.Build.props`
- Create: `BroLedger.sln`
- Create: `src/BroLedger.Core/BroLedger.Core.csproj`
- Create: `tests/BroLedger.Core.Tests/BroLedger.Core.Tests.csproj`
- Create: `.gitignore`

**Interfaces:**
- Consumes: 无
- Produces: `BroLedger.Core`（classlib，RootNamespace `BroLedger.Core`）、`BroLedger.Core.Tests`（xUnit，引用 Core），`InternalsVisibleTo("BroLedger.Core.Tests")` 已配置，后续所有任务在此结构上加文件

- [ ] **Step 1: 创建解决方案与项目**

```bash
cd bro-ledger.net
dotnet new sln -n BroLedger
dotnet new classlib -n BroLedger.Core -o src/BroLedger.Core
dotnet new xunit -n BroLedger.Core.Tests -o tests/BroLedger.Core.Tests
dotnet sln add src/BroLedger.Core tests/BroLedger.Core.Tests
dotnet add tests/BroLedger.Core.Tests reference src/BroLedger.Core
```

- [ ] **Step 2: 写 Directory.Build.props（覆盖两个 csproj 各自的 TargetFramework 等重复项）**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
</Project>
```

从两个 csproj 中删除被 props 覆盖的 `<TargetFramework>` 等属性。`BroLedger.Core.csproj` 增加：

```xml
  <PropertyGroup>
    <RootNamespace>BroLedger.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="BroLedger.Core.Tests" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
```

`BroLedger.Core.Tests.csproj` 增加 `Json.Schema.Net`：

```bash
dotnet add src/BroLedger.Core package Microsoft.Extensions.Logging.Abstractions
dotnet add tests/BroLedger.Core.Tests package Json.Schema.Net
```

- [ ] **Step 3: .gitignore**

```gitignore
bin/
obj/
*.user
.vs/
[Bb]uild/
```

- [ ] **Step 4: 删除模板文件、验证构建**

删除 `src/BroLedger.Core/Class1.cs` 与模板的 `UnitTest1.cs`、`GlobalUsings.cs`（xUnit 模板自带）。

Run: `dotnet build -warnaserror`
Expected: Build succeeded，0 警告 0 错误

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: solution scaffold (Core + Core.Tests, net10.0, AOT-compatible)"
```

---

### Task 2: HexUtils（TDD）

**Files:**
- Create: `src/BroLedger.Core/Utils/HexUtils.cs`
- Test: `tests/BroLedger.Core.Tests/Utils/HexUtilsTests.cs`

**Interfaces:**
- Consumes: 无
- Produces: `BroLedger.Core.Utils.HexUtils` 静态类，方法签名（后续解析层全部依赖）：
  `byte[] FromHex(string)`、`string ToHex(byte[])`（大写）、`string ToHexLower(byte[])`（小写）、
  `short Int16LE(string)`、`int UInt16LE(string)`、`float FloatLE(string)`、`uint UInt32LE(string)`、
  `int UInt8(string)`、`int Int8(string)`、`string UInt16ToHexLE(int)`、`int HexCharSpan(int)`、`string Sha256Hex(string)`（小写）

- [ ] **Step 1: 写失败测试**（迁移 `JAVA/src/test/java/se/niclas/broledger/util/HexUtilsTest.java` 全部 13 个用例）

```csharp
using BroLedger.Core.Utils;
using Xunit;

namespace BroLedger.Core.Tests.Utils;

public class HexUtilsTests
{
    // ---- ToHexLower --------------------------------------------------------

    [Fact]
    public void ToHexLower_EmptyArray() =>
        Assert.Equal("", HexUtils.ToHexLower([]));

    [Fact]
    public void ToHexLower_SingleByte() =>
        Assert.Equal("0a", HexUtils.ToHexLower([0x0A]));

    [Fact]
    public void ToHexLower_MultipleBytes() =>
        Assert.Equal("deadbeef", HexUtils.ToHexLower([0xDE, 0xAD, 0xBE, 0xEF]));

    [Fact]
    public void ToHexLower_ProducesLowercase()
    {
        var result = HexUtils.ToHexLower([0xFF]);
        Assert.Equal("ff", result);
        Assert.Equal(result, result.ToLowerInvariant());
    }

    [Fact]
    public void ToHexLower_ConsistentWithToHex()
    {
        byte[] bytes = [0x12, 0xAB, 0x7F];
        Assert.Equal(HexUtils.ToHex(bytes).ToLowerInvariant(), HexUtils.ToHexLower(bytes));
    }

    // ---- UInt16ToHexLE ------------------------------------------------------

    [Fact]
    public void UInt16ToHexLE_Zero() =>
        Assert.Equal("0000", HexUtils.UInt16ToHexLE(0));

    [Fact]
    public void UInt16ToHexLE_RoundTrip()
    {
        int[] values = [1, 127, 255, 256, 1000, 65535];
        foreach (var v in values)
        {
            var hex = HexUtils.UInt16ToHexLE(v);
            Assert.True(hex.Length == 4, $"Must be 4 hex chars for value {v}");
            Assert.Equal(v, HexUtils.UInt16LE(hex));
        }
    }

    [Fact]
    public void UInt16ToHexLE_KnownValue()
    {
        // 0x0001 in LE should be "0100"
        Assert.Equal("0100", HexUtils.UInt16ToHexLE(1));
    }

    // ---- HexCharSpan --------------------------------------------------------

    [Fact]
    public void HexCharSpan_Zero() => Assert.Equal(0, HexUtils.HexCharSpan(0));

    [Fact]
    public void HexCharSpan_One() => Assert.Equal(2, HexUtils.HexCharSpan(1));

    [Fact]
    public void HexCharSpan_Four() => Assert.Equal(8, HexUtils.HexCharSpan(4));

    [Fact]
    public void HexCharSpan_AlwaysDoubles()
    {
        for (var n = 0; n <= 10; n++)
            Assert.Equal(n * 2, HexUtils.HexCharSpan(n));
    }

    // ---- Sha256Hex ----------------------------------------------------------

    [Fact]
    public void Sha256Hex_ReturnsLowercaseHex()
    {
        var result = HexUtils.Sha256Hex("hello");
        Assert.NotNull(result);
        Assert.Equal(result, result.ToLowerInvariant());
        Assert.True(result.Length == 64, "SHA-256 produces 32 bytes = 64 hex chars");
    }

    [Fact]
    public void Sha256Hex_KnownValue() =>
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            HexUtils.Sha256Hex("hello"));

    [Fact]
    public void Sha256Hex_DifferentInputsDifferentOutputs() =>
        Assert.NotEqual(HexUtils.Sha256Hex("a"), HexUtils.Sha256Hex("b"));

    [Fact]
    public void Sha256Hex_SameInputSameOutput() =>
        Assert.Equal(HexUtils.Sha256Hex("test"), HexUtils.Sha256Hex("test"));
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 编译错误 `HexUtils` 不存在

- [ ] **Step 3: 实现 HexUtils.cs**

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BroLedger.Core.Utils;

public static class HexUtils
{
    public static byte[] FromHex(string hex)
    {
        var len = hex.Length;
        var data = new byte[len / 2];
        for (var i = 0; i < len; i += 2)
            data[i / 2] = (byte)((Uri.FromHex(hex[i]) << 4) + Uri.FromHex(hex[i + 1]));
        return data;
    }

    public static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    public static short Int16LE(string hex2bytes) =>
        BinaryPrimitives.ReadInt16LittleEndian(FromHex(hex2bytes));

    public static int UInt16LE(string hex2bytes) =>
        BinaryPrimitives.ReadUInt16LittleEndian(FromHex(hex2bytes));

    public static float FloatLE(string hex4bytes) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(FromHex(hex4bytes)));

    public static uint UInt32LE(string hex4bytes) =>
        BinaryPrimitives.ReadUInt32LittleEndian(FromHex(hex4bytes));

    public static int UInt8(string hex1byte) => FromHex(hex1byte)[0];

    public static int Int8(string hex1byte) => (sbyte)FromHex(hex1byte)[0];

    public static string ToHexLower(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    public static string UInt16ToHexLE(int value) =>
        $"{value & 0xFF:X2}{(value >> 8) & 0xFF:X2}";

    public static int HexCharSpan(int byteCount) => byteCount * 2;

    public static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
```

> 移植注意：Java `Integer.toUnsignedLong(uint32)` 返回 `long`，C# 侧用 `uint`（调用方 `BrotherParser` 做 `(int)` 截断，与 Java `(int)` 语义位型一致）。`Uri.FromHex` 等价 `Character.digit(c,16)`。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 13 个测试全部 PASS

- [ ] **Step 5: Commit**

```bash
git add src/BroLedger.Core/Utils/HexUtils.cs tests/BroLedger.Core.Tests/Utils/HexUtilsTests.cs
git commit -m "feat(core): HexUtils port with full test parity (HexUtilsTest)"
```

---

### Task 3: 模型类 + Stat + ArmorCalculator（TDD）

**Files:**
- Create: `src/BroLedger.Core/Models/Brother.cs`
- Create: `src/BroLedger.Core/Models/InventorySlot.cs`
- Create: `src/BroLedger.Core/Models/ItemStats.cs`
- Create: `src/BroLedger.Core/Models/DictionaryEntry.cs`
- Create: `src/BroLedger.Core/Models/TraitEntry.cs`
- Create: `src/BroLedger.Core/Models/Stat.cs`
- Create: `src/BroLedger.Core/Parsing/ArmorCalculator.cs`
- Test: `tests/BroLedger.Core.Tests/Parsing/ArmorCalculatorTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `BroLedger.Core.Models.Brother`（公共字段 `VisualLayerMap/Header/Stats/Stars/TalentBytes/TalentPoints/ActionPoints/EquippedSlots/ExtraPouches/PouchCount/BackgroundHexId/PerkIds/Traits/Name/Title/Description/DescriptionTemplate/Experience/LevelTotal/PerkPoints/PerkUsed/LevelPoints/Morale/LightWound/SalaryMultiplier/GreedAndGluttony/Unknown/HumanBytes/MoraleModifierHex/Tattoo/Fingerprint`；嵌套 `record VisualLayer(string Value, string Separator)`；构造器初始化 7 个空槽与 8 个空 `TalentBytes`）
  - `InventorySlot(int SlotIndex)` + `static InventorySlot Empty(int)`，字段 `Empty(bool)/ItemId/ItemType/Stats`
  - `ItemStats`（全公共字段，`bool HasAttachment()`）
  - `DictionaryEntry`（17 个可空属性 + `[JsonPropertyName]`，供 Task 5 JSON 反序列化）
  - `TraitEntry(string Id, string? EventData)` record
  - `Stat` record：静态实例 `Health/Resolve/Fatigue/Initiative/MeleeSkill/RangedSkill/MeleeDefense/RangedDefense` + `Stat[] Values`，成员 `StatIndex/StarIndex/JsonKey/Abbrev/DisplayName`
  - `ArmorCalculator`：`static int EffectiveArmor(Brother)`、`static int TotalFatiguePenalty(Brother)`、`static string WeaponDamageRange(Brother)`、internal `HasStats/IsUsableSlot/HasNoDamage/FormatDamageRange`

- [ ] **Step 1: 写失败测试**（迁移 `parser/ArmorCalculatorTest.java` 全部 17 个用例）

```csharp
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using Xunit;

namespace BroLedger.Core.Tests.Parsing;

public class ArmorCalculatorTests
{
    private static Brother EmptyBrother() => new();

    private static void Equip(Brother b, int slot, float durability, float baseFatigue)
    {
        var s = b.EquippedSlots[slot];
        s.Empty = false;
        s.ItemId = "DEADBEEF";
        s.Stats = new ItemStats { Durability = durability, BaseFatigue = baseFatigue };
    }

    private static void EquipWeapon(Brother b, int dmgMin, int dmgMax, int fatigueUse)
    {
        var s = b.EquippedSlots[0];
        s.Empty = false;
        s.ItemId = "DEADBEEF";
        s.Stats = new ItemStats { DamageMin = dmgMin, DamageMax = dmgMax, FatigueUse = fatigueUse };
    }

    [Fact]
    public void EffectiveArmor_ZeroWhenNothingEquipped() =>
        Assert.Equal(0, ArmorCalculator.EffectiveArmor(EmptyBrother()));

    [Fact]
    public void EffectiveArmor_SumsBodyAndHelmet()
    {
        var b = EmptyBrother();
        Equip(b, 2, 150f, 0f);
        Equip(b, 3, 60f, 0f);
        Assert.Equal(210, ArmorCalculator.EffectiveArmor(b));
    }

    [Fact]
    public void EffectiveArmor_IgnoresWeaponSlot()
    {
        var b = EmptyBrother();
        EquipWeapon(b, 10, 20, 5);
        Equip(b, 2, 100f, 0f);
        Assert.Equal(100, ArmorCalculator.EffectiveArmor(b));
    }

    [Fact]
    public void FatiguePenalty_ZeroWhenEmpty() =>
        Assert.Equal(0, ArmorCalculator.TotalFatiguePenalty(EmptyBrother()));

    [Fact]
    public void FatiguePenalty_SumsArmorAndWeapon()
    {
        var b = EmptyBrother();
        Equip(b, 2, 150f, 12f);
        Equip(b, 3, 60f, 6f);
        EquipWeapon(b, 10, 20, 8);
        Assert.Equal(26, ArmorCalculator.TotalFatiguePenalty(b));
    }

    [Fact]
    public void DamageRange_DashWhenNoWeapon() =>
        Assert.Equal("—", ArmorCalculator.WeaponDamageRange(EmptyBrother()));

    [Fact]
    public void DamageRange_DashWhenZeroDamage()
    {
        var b = EmptyBrother();
        EquipWeapon(b, 0, 0, 5);
        Assert.Equal("—", ArmorCalculator.WeaponDamageRange(b));
    }

    [Fact]
    public void DamageRange_FormattedCorrectly()
    {
        var b = EmptyBrother();
        EquipWeapon(b, 30, 50, 8);
        Assert.Equal("30 – 50", ArmorCalculator.WeaponDamageRange(b));
    }

    [Fact]
    public void HasStats_NullSlot() => Assert.False(ArmorCalculator.HasStats(null));

    [Fact]
    public void HasStats_EmptySlot() =>
        Assert.False(ArmorCalculator.HasStats(InventorySlot.Empty(0)));

    [Fact]
    public void HasStats_SlotWithoutStats()
    {
        var s = InventorySlot.Empty(0);
        s.Empty = false;
        Assert.False(ArmorCalculator.HasStats(s));
    }

    [Fact]
    public void HasStats_SlotWithStats()
    {
        var s = InventorySlot.Empty(0);
        s.Empty = false;
        s.Stats = new ItemStats();
        Assert.True(ArmorCalculator.HasStats(s));
    }

    [Fact]
    public void IsUsableSlot_Null() => Assert.False(ArmorCalculator.IsUsableSlot(null));

    [Fact]
    public void IsUsableSlot_EmptySlot() =>
        Assert.False(ArmorCalculator.IsUsableSlot(InventorySlot.Empty(0)));

    [Fact]
    public void IsUsableSlot_NonEmpty()
    {
        var s = InventorySlot.Empty(0);
        s.Empty = false;
        Assert.True(ArmorCalculator.IsUsableSlot(s));
    }

    [Fact]
    public void HasNoDamage_TrueWhenBothZero()
    {
        var s = new ItemStats();
        Assert.True(ArmorCalculator.HasNoDamage(s));
    }

    [Fact]
    public void HasNoDamage_FalseWhenMinNonZero()
    {
        var s = new ItemStats { DamageMin = 5 };
        Assert.False(ArmorCalculator.HasNoDamage(s));
    }

    [Fact]
    public void FormatDamageRange_DashWhenBothZero() =>
        Assert.Equal("—", ArmorCalculator.FormatDamageRange(0, 0));

    [Fact]
    public void FormatDamageRange_RangeString() =>
        Assert.Equal("15 – 25", ArmorCalculator.FormatDamageRange(15, 25));
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 编译错误（类型不存在）

- [ ] **Step 3: 实现模型类**（真源：`JAVA/.../model/*.java`，逐字段对照）

`InventorySlot.cs`：

```csharp
namespace BroLedger.Core.Models;

public class InventorySlot(int slotIndex)
{
    public readonly int SlotIndex = slotIndex;
    public bool Empty = true;
    public string? ItemId;    // 大写 8 位 hex
    public string? ItemType;  // 字典类型：genericWeapon、namedArmor 等
    public ItemStats? Stats;

    public static InventorySlot Empty(int slotIndex) => new(slotIndex);
}
```

`ItemStats.cs`：

```csharp
namespace BroLedger.Core.Models;

public class ItemStats
{
    // Common
    public float Durability;
    public float MaxDurability;
    public int Icon;
    public string? IconSet;
    public string? Repair;      // 原始 1 字节 hex
    public int MagicNumber;

    // Armor / helmet
    public float Fatigue;
    public float BaseFatigue;
    public float BaseDurability;
    public string? Attachment;      // 4 字节 hex，"00000000" = 无
    public int AttachmentIcon;
    public string? House;           // 1 字节 hex（noble 护甲/盾）

    // Named item
    public string? Name;

    // Weapon
    public int DamageMin;
    public int DamageMax;
    public float DamageArmor;
    public int HeadChance;
    public int DamageShield;
    public int HitBonus;
    public float Penetration;
    public int FatigueUse;
    public int Ammo;
    public int AmmoMax;

    // Shield
    public int MDef;
    public int RDef;

    // Auxiliary
    public float Amount;    // provisions
    public float Expiry;    // provisions
    public int Bought;      // provisions / commodity

    // Special
    public string? Description; // davkul armor

    public bool HasAttachment() => Attachment is not null && Attachment != "00000000";
}
```

`TraitEntry.cs`：

```csharp
namespace BroLedger.Core.Models;

public sealed record TraitEntry(string Id, string? EventData);
```

`DictionaryEntry.cs`（JSON 键为 camelCase，属性 PascalCase + `[JsonPropertyName]` 一一对应 Java 字段名）：

```csharp
using System.Text.Json.Serialization;

namespace BroLedger.Core.Models;

public class DictionaryEntry
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subType")] public string? SubType { get; set; }
    [JsonPropertyName("slot")] public string? Slot { get; set; }
    [JsonPropertyName("icon")] public int? Icon { get; set; }
    [JsonPropertyName("iconSet")] public string? IconSet { get; set; }

    [JsonPropertyName("durability")] public int? Durability { get; set; }
    [JsonPropertyName("fatigue")] public int? Fatigue { get; set; }
    [JsonPropertyName("fatigueUse")] public int? FatigueUse { get; set; }
    [JsonPropertyName("damageMin")] public int? DamageMin { get; set; }
    [JsonPropertyName("damageMax")] public int? DamageMax { get; set; }
    [JsonPropertyName("damageArmor")] public double? DamageArmor { get; set; }
    [JsonPropertyName("headChance")] public int? HeadChance { get; set; }
    [JsonPropertyName("damageShield")] public int? DamageShield { get; set; }
    [JsonPropertyName("hitBonus")] public int? HitBonus { get; set; }
    [JsonPropertyName("penetration")] public double? Penetration { get; set; }
    [JsonPropertyName("ammoMax")] public int? AmmoMax { get; set; }
}
```

`Stat.cs`：

```csharp
namespace BroLedger.Core.Models;

// 双下标约定：StatIndex = Brother.Stats[] 读/显示序；StarIndex = Brother.Stars[] 存档/天赋序（Initiative 在 3）
public sealed record Stat(int StatIndex, int StarIndex, string JsonKey, string Abbrev, string DisplayName)
{
    public static readonly Stat Health = new(0, 0, "health", "HP", "Health");
    public static readonly Stat Resolve = new(1, 1, "resolve", "Res", "Resolve");
    public static readonly Stat Fatigue = new(2, 2, "fatigue", "Fat", "Fatigue");
    public static readonly Stat Initiative = new(7, 3, "initiative", "Init", "Initiative");
    public static readonly Stat MeleeSkill = new(3, 4, "melee_skill", "MS", "Melee Skill");
    public static readonly Stat RangedSkill = new(4, 5, "ranged_skill", "RS", "Ranged Skill");
    public static readonly Stat MeleeDefense = new(5, 6, "melee_defense", "MD", "Melee Def");
    public static readonly Stat RangedDefense = new(6, 7, "ranged_defense", "RD", "Ranged Def");

    public static readonly Stat[] Values =
    [
        Health, Resolve, Fatigue, Initiative, MeleeSkill, RangedSkill, MeleeDefense, RangedDefense
    ];
}
```

`Brother.cs`：

```csharp
namespace BroLedger.Core.Models;

public class Brother
{
    // Stats 下标（读/显示序）：[0]HP [1]Res [2]Fat [3]MS [4]RS [5]MD [6]RD [7]Init
    // Stars 下标（天赋序）：[0]HP [1]Res [2]Fat [3]Init [4]MS [5]RS [6]MD [7]RD
    public Dictionary<string, VisualLayer> VisualLayerMap { get; } = new();

    public string? Header;

    public int[] Stats { get; } = new int[8];
    public int[] Stars { get; } = new int[8];
    public string[] TalentBytes { get; } = ["", "", "", "", "", "", "", ""];

    public int TalentPoints;
    public int ActionPoints;

    // 7 固定槽：[0]武器 [1]盾 [2]身甲 [3]盔 [4]饰品 [5]箭袋 [6]囊
    public InventorySlot[] EquippedSlots { get; } = new InventorySlot[7];
    public List<InventorySlot> ExtraPouches { get; } = [];

    public int PouchCount; // 2 或 4

    public string? BackgroundHexId; // 大写 8 位 hex

    public List<string> PerkIds { get; } = [];
    public List<TraitEntry> Traits { get; } = [];

    public string? Name;
    public string? Title;
    public string? Description;
    public string? DescriptionTemplate;

    public int Experience;
    public int LevelTotal;
    public int PerkPoints;
    public int PerkUsed;
    public int LevelPoints;

    public float Morale;
    public float LightWound;
    public float SalaryMultiplier;

    // 不透明数据块
    public string? GreedAndGluttony;    // 6 字节 raw hex
    public string? Unknown;             // 2 字节 raw hex
    public string? HumanBytes;          // 6 字节 raw hex
    public string? MoraleModifierHex;
    public string? Tattoo;              // 1 字节 raw hex，仅 wildman/barbarian

    public string? Fingerprint;

    public sealed record VisualLayer(string Value, string Separator);

    public Brother()
    {
        for (var i = 0; i < 7; i++) EquippedSlots[i] = InventorySlot.Empty(i);
    }
}
```

- [ ] **Step 4: 实现 ArmorCalculator.cs**（真源：`parser/ArmorCalculator.java`；`—` 与 ` – ` 中的破折号是 U+2013 EN DASH，逐字符照抄）

```csharp
using BroLedger.Core.Models;

namespace BroLedger.Core.Parsing;

public static class ArmorCalculator
{
    private const int SlotWeapon = 0;
    private const int SlotShield = 1;
    private const int SlotBody = 2;
    private const int SlotHelmet = 3;

    public static int EffectiveArmor(Brother b) =>
        (int)Durability(b, SlotBody) + (int)Durability(b, SlotHelmet);

    public static int TotalFatiguePenalty(Brother b) =>
        (int)BaseFatigue(b, SlotBody)
        + (int)BaseFatigue(b, SlotHelmet)
        + WeaponFatigue(b);

    public static string WeaponDamageRange(Brother b)
    {
        var slot = Slot(b, SlotWeapon);
        if (!HasStats(slot)) return "—";
        var s = slot.Stats!;
        return FormatDamageRange(s.DamageMin, s.DamageMax);
    }

    internal static bool HasStats(InventorySlot? s) => IsUsableSlot(s) && s!.Stats != null;

    internal static bool IsUsableSlot(InventorySlot? s) => s is not null && !s.Empty;

    internal static bool HasNoDamage(ItemStats s) => s.DamageMin == 0 && s.DamageMax == 0;

    internal static string FormatDamageRange(int min, int max) =>
        min == 0 && max == 0 ? "—" : $"{min} – {max}";

    private static float Durability(Brother b, int idx)
    {
        var s = Slot(b, idx);
        return HasStats(s) ? s!.Stats!.Durability : 0f;
    }

    private static float BaseFatigue(Brother b, int idx)
    {
        var s = Slot(b, idx);
        return HasStats(s) ? s!.Stats!.BaseFatigue : 0f;
    }

    private static int WeaponFatigue(Brother b)
    {
        var s = Slot(b, SlotWeapon);
        return HasStats(s) ? s!.Stats!.FatigueUse : 0;
    }

    private static InventorySlot? Slot(Brother b, int idx)
    {
        if (b.EquippedSlots == null || idx >= b.EquippedSlots.Length) return null;
        var s = b.EquippedSlots[idx];
        return IsUsableSlot(s) ? s : null;
    }
}
```

- [ ] **Step 5: 运行确认通过**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 全部 PASS（累计 13 + 19 = 32 个）

- [ ] **Step 6: Commit**

```bash
git add src/BroLedger.Core/Models src/BroLedger.Core/Parsing/ArmorCalculator.cs tests/BroLedger.Core.Tests/Parsing
git commit -m "feat(core): model classes + Stat + ArmorCalculator with full test parity"
```

---

### Task 4: 嵌入资源 + GameDataService + GameDataSchemaTest

**Files:**
- Create: `src/BroLedger.Core/Resources/data/game-data.json`（自 `JAVA/src/main/resources/se/niclas/broledger/data/game-data.json` 原样复制）
- Create: `src/BroLedger.Core/Resources/data/game-data.schema.json`（同上原样复制）
- Create: `src/BroLedger.Core/Services/GameDataService.cs`
- Modify: `src/BroLedger.Core/BroLedger.Core.csproj`（EmbeddedResource）
- Test: `tests/BroLedger.Core.Tests/Services/GameDataSchemaTests.cs`

**Interfaces:**
- Consumes: Task 1 的 csproj 结构
- Produces: `GameDataService.Instance` 单例，方法 `void LoadFromEmbeddedResource()`、`JsonNode? GetEntries()`、`JsonNode? GetImageSlots()`、`int GetGameArtVersion()`；嵌入资源逻辑名 `BroLedger.Core.Resources.data.game-data.json` / `...game-data.schema.json`

- [ ] **Step 1: 复制资源文件并声明嵌入**

```bash
mkdir -p src/BroLedger.Core/Resources/data
cp "../bro-ledger/src/main/resources/se/niclas/broledger/data/game-data.json" src/BroLedger.Core/Resources/data/
cp "../bro-ledger/src/main/resources/se/niclas/broledger/data/game-data.schema.json" src/BroLedger.Core/Resources/data/
```

`BroLedger.Core.csproj` 增加：

```xml
  <ItemGroup>
    <EmbeddedResource Include="Resources\data\game-data.json" LogicalName="BroLedger.Core.Resources.data.game-data.json" />
    <EmbeddedResource Include="Resources\data\game-data.schema.json" LogicalName="BroLedger.Core.Resources.data.game-data.schema.json" />
  </ItemGroup>
```

- [ ] **Step 2: 写失败测试**（迁移 `service/GameDataSchemaTest.java`；`networknt` → `JsonSchema.Net`）

```csharp
using System.Text.Json.Nodes;
using BroLedger.Core.Services;
using Json.Schema;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// 每次构建校验 game-data.json 与 schema 一致，捕获宽容解析器会吞掉的坏条目
public class GameDataSchemaTests
{
    private const string DataBase = "BroLedger.Core.Resources.data.";

    [Fact]
    public void GameData_ConformsToSchema()
    {
        var asm = typeof(GameDataService).Assembly;

        JsonNode schemaNode, dataNode;
        using (var s = asm.GetManifestResourceStream(DataBase + "game-data.schema.json"))
        {
            Assert.NotNull(s);
            schemaNode = JsonNode.Parse(s);
        }
        using (var s = asm.GetManifestResourceStream(DataBase + "game-data.json"))
        {
            Assert.NotNull(s);
            dataNode = JsonNode.Parse(s);
        }

        var schema = JsonSchema.FromText(schemaNode!.ToJsonString());
        var results = schema.Evaluate(dataNode, new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (!results.IsValid)
        {
            var details = results.Details
                .Where(d => d.Errors != null)
                .SelectMany(d => d.Errors!.Values)
                .Take(20);
            Assert.Fail($"{results.Details.Count(d => d.Errors != null)} schema violation(s):\n  {string.Join("\n  ", details)}");
        }
    }
}
```

- [ ] **Step 3: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: FAIL — 嵌入资源未找到（`GetManifestResourceStream` 返回 null）

- [ ] **Step 4: 实现 GameDataService.cs**（真源：`service/GameDataService.java`；`JsonNode?` 空值承担 Java `MissingNode` 语义）

```csharp
using System.Text.Json.Nodes;

namespace BroLedger.Core.Services;

public sealed class GameDataService
{
    private static GameDataService? _instance;
    public static GameDataService Instance => _instance ??= new();

    private JsonNode? _root;

    private GameDataService() { }

    // 幂等：已加载则后续调用为空操作
    public void LoadFromEmbeddedResource()
    {
        if (_root != null) return;
        using var s = typeof(GameDataService).Assembly
            .GetManifestResourceStream("BroLedger.Core.Resources.data.game-data.json")
            ?? throw new IOException("game-data.json not found in embedded resources");
        _root = JsonNode.Parse(s);
    }

    public JsonNode? GetEntries() => SafeGet("entries");

    public JsonNode? GetImageSlots() => SafeGet("imageSlots");

    public int GetGameArtVersion()
    {
        var node = SafeGet("gameArtVersion");
        return node is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
    }

    private JsonNode? SafeGet(string field) => _root?[field];
}
```

- [ ] **Step 5: 运行确认通过**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 全部 PASS（schema 校验绿——复制来的 JSON 本就符合 schema）

- [ ] **Step 6: Commit**

```bash
git add src/BroLedger.Core/Resources src/BroLedger.Core/BroLedger.Core.csproj src/BroLedger.Core/Services/GameDataService.cs tests/BroLedger.Core.Tests/Services
git commit -m "feat(core): embed game-data(+schema), GameDataService port, schema validation test"
```

---

### Task 5: DictionaryService（含 JSON source-gen 上下文）

**Files:**
- Create: `src/BroLedger.Core/Services/DataJsonContext.cs`
- Create: `src/BroLedger.Core/Services/DictionaryService.cs`
- Test: `tests/BroLedger.Core.Tests/Services/DictionaryServiceTests.cs`

**Interfaces:**
- Consumes: `GameDataService`（Task 4）、`DictionaryEntry`（Task 3）
- Produces: `DictionaryService.Instance` 单例：
  `void Load(Stream json)`（legacy `[[hexId,{fields}],…]` 测试缝）、`void LoadFromEntries(JsonNode?)`、
  `void LoadFromEmbeddedResource()`、`bool Has(string)`、`DictionaryEntry? Get(string)`、
  `string? GetType(string)`、`string GetName(string)`、`List<KeyValuePair<string,DictionaryEntry>> GetAllByType(string)`、
  `IReadOnlyDictionary<string,DictionaryEntry> GetAll()`
  （`internal DataJsonContext` 供后续计划的 keeper/roles JSON 上下文并列扩展）

- [ ] **Step 1: 写失败测试**

> **移植缺口说明**：Java 版无 DictionaryService 专项测试（`loadFromEntries` 的 flatten/merge 逻辑仅被集成测试间接覆盖）。移植时为这个非平凡逻辑补最小钉子测试，属 Global Constraints 允许的移植缺口测试。

```csharp
using System.Text;
using System.Text.Json.Nodes;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

public class DictionaryServiceTests
{
    // 与 Java 一致：测试共享 getInstance()/Instance 单例，用已知条目 Load 填充后断言。
    private static DictionaryService NewService()
    {
        var s = DictionaryService.Instance;
        s.Load(LegacyJson());
        return s;
    }

    private static MemoryStream LegacyJson() => new(Encoding.UTF8.GetBytes("""
        [
          ["80DE01B7", {"name":"Additional Fur Padding","type":"attachment","slot":"attachment","fatigue":-2}],
          ["AB12CD34", {"name":"Test Sword","type":"genericWeapon","slot":"hand","durability":40,"damageMin":20,"damageMax":30,"damageArmor":15.5}]
        ]
        """));

    [Fact]
    public void Load_LegacyPairs_PopulatesUppercaseKeys()
    {
        var svc = NewService();
        Assert.True(svc.Has("80de01b7"));          // 查找键大写归一
        Assert.True(svc.Has("80DE01B7"));
        Assert.False(svc.Has("FFFFFFFF"));
    }

    [Fact]
    public void Get_ReturnsEntryWithFields()
    {
        var svc = NewService();
        var e = svc.Get("AB12CD34");
        Assert.NotNull(e);
        Assert.Equal("Test Sword", e!.Name);
        Assert.Equal("genericWeapon", e.Type);
        Assert.Equal(40, e.Durability);
        Assert.Equal(15.5, e.DamageArmor);
    }

    [Fact]
    public void GetType_ReturnsNullForUnknown()
    {
        var svc = NewService();
        Assert.Equal("attachment", svc.GetType("80DE01B7"));
        Assert.Null(svc.GetType("FFFFFFFF"));
    }

    [Fact]
    public void GetName_FallsBackToHexId() =>
        Assert.Equal("FFFFFFFF", NewService().GetName("FFFFFFFF"));

    [Fact]
    public void LoadFromEntries_FlattensBaseSubObject()
    {
        var svc = NewService();
        var entries = JsonNode.Parse("""
        {
          "80DE01B7": {"name":"Fur Padding","type":"attachment","slot":"attachment","base":{"durability":0,"fatigue":-2},"weapon":{},"modifier":{},"assets":[]},
          "ZZZZZZZZ": {"name":"Plain Armor","type":"genericArmor","slot":"body","fatigue":9}
        }
        """);
        svc.LoadFromEntries(entries);

        var flat = svc.Get("80DE01B7");
        Assert.NotNull(flat);
        Assert.Equal("Fur Padding", flat!.Name);
        Assert.Equal(-2, flat.Fatigue);          // 来自 base 子对象
        Assert.Equal(0, flat.Durability);

        var plain = svc.Get("zzzzzzzz");
        Assert.NotNull(plain);
        Assert.Equal(9, plain!.Fatigue);
    }

    [Fact]
    public void LoadFromEntries_NullOrEmpty_NoOp() =>
        NewService().LoadFromEntries(null); // 不抛即通过
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 编译错误 `DictionaryService` 不存在

- [ ] **Step 3: 实现 DataJsonContext.cs**（AOT：所有 JSON 走 source-gen）

```csharp
using System.Text.Json.Serialization;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DictionaryEntry))]
internal partial class DataJsonContext : JsonSerializerContext;
```

- [ ] **Step 4: 实现 DictionaryService.cs**（真源：`service/DictionaryService.java`；单例延迟初始化与 Java 一致——非线程安全）

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

public sealed class DictionaryService
{
    private static DictionaryService? _instance;
    public static DictionaryService Instance => _instance ??= new();

    private readonly Dictionary<string, DictionaryEntry> _entries = new(1200);

    private DictionaryService() { }

    // legacy [[hexId, {fields}], ...] 格式（测试缝）
    public void Load(Stream json)
    {
        var root = JsonNode.Parse(json) ?? throw new IOException("empty json");
        foreach (var pair in root.AsArray())
        {
            var id = pair![0]!.GetValue<string>().ToUpperInvariant();
            var entry = pair[1]!.Deserialize(DataJsonContext.Default.DictionaryEntry)
                ?? throw new IOException($"Failed to parse entry {id}");
            _entries[id] = entry;
        }
    }

    public void LoadFromEmbeddedResource()
    {
        GameDataService.Instance.LoadFromEmbeddedResource();
        LoadFromEntries(GameDataService.Instance.GetEntries());
    }

    // 顶层字段（去掉 base/weapon/modifier/assets）+ base 子对象合并 → DictionaryEntry
    public void LoadFromEntries(JsonNode? entriesNode)
    {
        if (entriesNode == null) return;
        foreach (var (key, node) in entriesNode.AsObject())
        {
            var hexId = key.ToUpperInvariant();
            var flat = new JsonObject();
            if (node is JsonObject obj)
                foreach (var (k, v) in obj)
                    if (k is not ("base" or "weapon" or "modifier" or "assets"))
                        flat[k] = v?.DeepClone();
            if (node?["base"] is JsonObject baseObj)
                foreach (var (k, v) in baseObj)
                    flat[k] = v?.DeepClone();
            try
            {
                var entry = flat.Deserialize(DataJsonContext.Default.DictionaryEntry)
                    ?? throw new JsonException("null entry");
                _entries[hexId] = entry;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse entry {hexId}", ex);
            }
        }
    }

    public bool Has(string hexId) => _entries.ContainsKey(hexId.ToUpperInvariant());

    public DictionaryEntry? Get(string hexId) => _entries.GetValueOrDefault(hexId.ToUpperInvariant());

    public string? GetType(string hexId) => Get(hexId)?.Type;

    public string GetName(string hexId) => Get(hexId)?.Name ?? hexId;

    // 按类型过滤、按名称排序（null 排最后）
    public List<KeyValuePair<string, DictionaryEntry>> GetAllByType(string type) =>
        _entries
            .Where(e => type == e.Value.Type)
            .OrderBy(e => e.Value.Name is null ? 1 : 0)
            .ThenBy(e => e.Value.Name, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyDictionary<string, DictionaryEntry> GetAll() => _entries;
}
```

- [ ] **Step 5: 运行确认通过**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 全部 PASS

- [ ] **Step 6: Commit**

```bash
git add src/BroLedger.Core/Services tests/BroLedger.Core.Tests/Services/DictionaryServiceTests.cs
git commit -m "feat(core): DictionaryService port (flatten/merge base, legacy load seam, JSON source-gen)"
```

---

### Task 6: SaveFormat + HexReader + ItemType + BrotherLocator

**Files:**
- Create: `src/BroLedger.Core/Parsing/SaveFormat.cs`
- Create: `src/BroLedger.Core/Parsing/HexReader.cs`
- Create: `src/BroLedger.Core/Parsing/ItemType.cs`
- Create: `src/BroLedger.Core/Parsing/BrotherLocator.cs`
- Test: `tests/BroLedger.Core.Tests/Parsing/BrotherLocatorTests.cs`

**Interfaces:**
- Consumes: `HexUtils`（Task 2）
- Produces:
  - `SaveFormat` internal static class：全部字节常量（值见真源 `parser/SaveFormat.java`，Task Step 3 列全）
  - `HexReader(string data, int startOffset)`：`int Cursor {get;set;}`、`Skip(int)`、`ReadHexBytes(int)`（返回大写）、`PeekAt(int,int)`、`ReadUInt8/ReadInt8/ReadInt16LE/ReadUInt16LE/ReadFloatLE/ReadUInt32LE/ReadString`
  - `ItemType` enum + `static ItemType FromKey(string?)`（UNKNOWN 兜底）
  - `BrotherLocator`：`const int SigCountBack=38, SigDataAhead=58`、`static int CountBrothers(string)`、`static List<int> FindOffsets(string)`、`internal static int ParseBrotherCount(string,int)`

- [ ] **Step 1: 写失败测试**（BrotherLocator 合成 hex 钉子——**移植缺口说明**：Java 无 BrotherLocatorTest；签名/偏移是全解析链的地基，用合成 hex 钉死正则与偏移语义）

```csharp
using BroLedger.Core.Parsing;
using Xunit;

namespace BroLedger.Core.Tests.Parsing;

public class BrotherLocatorTests
{
    private const string Sig = "000000000000007d2c10000000000000d4c4a7e9000100000102000000";

    private static string TwoBrotherHex() =>
        new string('F', 36) + "02" + Sig + Sig + new string('A', 20);

    [Fact]
    public void CountBrothers_ReadsCountByteBeforeSignature()
    {
        // 匹配点前 38 hex 字符处 = "02"（1 字节兄弟数）
        Assert.Equal(2, BrotherLocator.CountBrothers(TwoBrotherHex()));
    }

    [Fact]
    public void FindOffsets_ReturnsDataAheadForEachMatch()
    {
        // 第一个签名起于 38，第二个起于 38+58=96；各 +58 → [96, 154]
        Assert.Equal(new List<int> { 96, 154 }, BrotherLocator.FindOffsets(TwoBrotherHex()));
    }

    [Fact]
    public void FindOffsets_EmptyWhenSignatureAbsent()
    {
        Assert.Empty(BrotherLocator.FindOffsets("DEADBEEF"));
        Assert.Equal(0, BrotherLocator.CountBrothers("DEADBEEF"));
    }

    [Fact]
    public void FindOffsets_IsCaseInsensitive()
    {
        // 签名小写化后仍应命中（正则 IgnoreCase 对齐 Java Pattern.CASE_INSENSITIVE）
        var hex = new string('F', 36) + "01" + Sig.ToLowerInvariant();
        Assert.Equal(new List<int> { 96 }, BrotherLocator.FindOffsets(hex));
    }

    [Fact]
    public void FindOffsets_CountExceedingMatches_StopsAtMatches()
    {
        // 计数=5 但只有 1 个签名 → 只返回 1 个偏移
        var hex = new string('F', 36) + "05" + Sig;
        Assert.Single(BrotherLocator.FindOffsets(hex));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 编译错误（类型不存在）

- [ ] **Step 3: 实现**（真源：`parser/SaveFormat.java`、`parser/HexReader.java`、`parser/ItemType.java`、`parser/BrotherLocator.java`）

`SaveFormat.cs`（Java 的 `private static final int` → `internal const int`，值逐个照抄）：

```csharp
namespace BroLedger.Core.Parsing;

// Battle Brothers 存档二进制格式的字节常量，全部单位为字节
internal static class SaveFormat
{
    // ---- 视觉层 (BrotherParser.ParseVisualLayers) --------------------------
    public const int VisualLayerValueBytes = 12;
    public const int VisualLayerSepBytes = 53;

    // ---- 属性区 (BrotherParser.ParseActionPointsAndStats) ------------------
    public const int GreedGluttonyBytes = 6;

    // ---- 特性事件数据 (BrotherParser.ParseTraits) --------------------------
    public const int InjuryEventBytes = 14;
    public const int TrainingEventBytes = 37;
    public const int KnowledgeEventBytes = 12;
    public const int LearningEventBytes = 2;

    // ---- 名字后区域 (BrotherParser.ParseNameAndLevelData) ------------------
    public const int HumanBytes = 6;

    // ---- 士气修正 (BrotherParser.ParseMoraleModifiers) ----------------------
    public const int MoraleEntryTailBytes = 4;
    public const int MoraleBlockTailBytes = 8;

    // ---- 通用物品块 (ItemParser.Generic) -----------------------------------
    public const int QuantityHexBytes = 4;
    public const int GenericPaddingBytes = 2;

    // ---- 非身体部位挂件 (ItemParser.Attachment else 分支) -------------------
    public const int AttachmentDataBytes = 15;
    public const int AttachmentPaddingBytes = 2;

    // ---- 挂件块 (ItemParser.ReadAttachmentBlock) ---------------------------
    public const int AttStandalonePreIcon = 1;
    public const int AttStandalonePostIcon = 9;
    public const int AttStandalonePadding = 2;
    public const int AttInlinePreIcon = 2;
    public const int AttInlinePostIcon = 10;

    // ---- 命名武器 (ItemParser.NamedWeapon) ---------------------------------
    public const int NamedWeaponZeroFloatBytes = 4;
}
```

`HexReader.cs`：

```csharp
using System.Text;
using BroLedger.Core.Utils;

namespace BroLedger.Core.Parsing;

// 基于 hex 字符串的游标读取器；每次读取前进 byteCount*2 个字符
public class HexReader(string data, int startOffset)
{
    public int Cursor { get; set; } = startOffset;

    public void Skip(int byteCount) => Cursor += HexUtils.HexCharSpan(byteCount);

    // 读取 byteCount 字节，返回大写 hex 字符串
    public string ReadHexBytes(int byteCount)
    {
        var span = HexUtils.HexCharSpan(byteCount);
        var result = data.Substring(Cursor, span).ToUpperInvariant();
        Cursor += span;
        return result;
    }

    // 在绝对字符偏移处窥视，不移动游标
    public string PeekAt(int absoluteCharOffset, int byteCount) =>
        data.Substring(absoluteCharOffset, HexUtils.HexCharSpan(byteCount)).ToUpperInvariant();

    public int ReadUInt8() => HexUtils.UInt8(ReadHexBytes(1));

    public int ReadInt8() => HexUtils.Int8(ReadHexBytes(1));

    public short ReadInt16LE() => HexUtils.Int16LE(ReadHexBytes(2));

    public int ReadUInt16LE() => HexUtils.UInt16LE(ReadHexBytes(2));

    public float ReadFloatLE() => HexUtils.FloatLE(ReadHexBytes(4));

    public uint ReadUInt32LE() => HexUtils.UInt32LE(ReadHexBytes(4));

    // 长度前缀 UTF-8 字符串：UInt16LE 长度 + 该长度的 UTF-8 字节
    public string ReadString()
    {
        var len = ReadUInt16LE();
        if (len == 0) return "";
        var bytes = HexUtils.FromHex(ReadHexBytes(len));
        return Encoding.UTF8.GetString(bytes);
    }
}
```

`ItemType.cs`：

```csharp
namespace BroLedger.Core.Parsing;

public enum ItemType
{
    GenericWeapon,
    NamedWeapon,
    GenericArmor,
    NamedArmor,
    GenericHelmet,
    NamedHelmet,
    GenericShield,
    NamedShield,
    Auxiliary,
    Attachment,
    Unknown,
}

// Java ItemType.fromString 的等价静态工厂；null/未知 → Unknown
public static class ItemTypeKeys
{
    public static ItemType From(string? key) => key switch
    {
        "genericWeapon" => ItemType.GenericWeapon,
        "namedWeapon" => ItemType.NamedWeapon,
        "genericArmor" => ItemType.GenericArmor,
        "namedArmor" => ItemType.NamedArmor,
        "genericHelmet" => ItemType.GenericHelmet,
        "namedHelmet" => ItemType.NamedHelmet,
        "genericShield" => ItemType.GenericShield,
        "namedShield" => ItemType.NamedShield,
        "auxiliary" => ItemType.Auxiliary,
        "attachment" => ItemType.Attachment,
        _ => ItemType.Unknown,
    };
}
```

`BrotherLocator.cs`（**必须**用 `[GeneratedRegex]`——AOT 兼容的编译期正则；对齐 Java `Matcher.find()` 非重叠语义）：

```csharp
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BroLedger.Core.Parsing;

public static partial class BrotherLocator
{
    [GeneratedRegex("000000000000007d2c10000000000000d4c4a7e9000100000102000000",
        RegexOptions.IgnoreCase)]
    private static partial Regex SigRegex();

    // 相对签名匹配起点的字节距离（hex 字符单位）
    public const int SigCountBack = 38;
    public const int SigDataAhead = 58;

    public static int CountBrothers(string hex)
    {
        var m = SigRegex().Match(hex);
        return m.Success ? ParseBrotherCount(hex, m.Index) : 0;
    }

    public static List<int> FindOffsets(string hex)
    {
        var m = SigRegex().Match(hex);
        if (!m.Success) return [];
        var count = ParseBrotherCount(hex, m.Index);
        var offsets = new List<int>(count);
        foreach (var match in SigRegex().Matches(hex))
        {
            if (offsets.Count >= count) break;
            offsets.Add(match.Index + SigDataAhead);
        }
        return offsets;
    }

    internal static int ParseBrotherCount(string hex, int matchStart) =>
        Convert.ToInt32(hex.Substring(matchStart - SigCountBack, 2), 16);
}
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 全部 PASS

- [ ] **Step 5: Commit**

```bash
git add src/BroLedger.Core/Parsing tests/BroLedger.Core.Tests/Parsing/BrotherLocatorTests.cs
git commit -m "feat(core): SaveFormat constants, HexReader, ItemType, BrotherLocator (GeneratedRegex)"
```

---

### Task 7: ItemParser

**Files:**
- Create: `src/BroLedger.Core/Parsing/ItemParser.cs`

**Interfaces:**
- Consumes: `HexReader`（Task 6）、`InventorySlot/ItemStats/DictionaryEntry`（Task 3）、`DictionaryService`（Task 5）、`SaveFormat`/`ItemTypeKeys`（Task 6）
- Produces: `ItemParser(DictionaryService dict, List<string> warnings, ILogger? logger = null)`，`void Parse(HexReader, InventorySlot)`；私有方法 `Generic/GenericWeapon/GenericShield/Attachment/BodyArmorStats/GenericArmor/GenericHelmet/NamedWeapon/NamedArmor/NamedHelmet/NamedShield/Auxiliary/ReadAttachmentBlock`（含 `AttachmentMode` 私有枚举）

> 本任务无独立测试——Java 版亦无 ItemParserTest；正确性由 Task 9 的 `SaveParserTests` 集成测试覆盖。步骤为移植 → 构建 → 提交。

- [ ] **Step 1: 移植 ItemParser.cs**

真源：`parser/ItemParser.java`。移植要点（全文件照此移植）：

- 构造器：`public ItemParser(DictionaryService dict, List<string> warnings, ILogger? logger = null)`，`_logger = logger ?? NullLogger.Instance`
- `Parse` 开头 `logger.LogTrace("Parsing item: {ItemId}", slot.ItemId)`；`slot.Stats = new ItemStats()`；switch 用 `ItemTypeKeys.From(slot.ItemType)`；`case ItemType.Unknown: warnings.Add($"Unknown item type '{slot.ItemType}' (id {slot.ItemId})")`；结尾 `slot.Empty = false`
- `Generic`：`s.Repair = reader.ReadHexBytes(1); s.Icon = reader.ReadUInt16LE(); s.Durability = reader.ReadFloatLE(); reader.Skip(SaveFormat.QuantityHexBytes); s.MagicNumber = reader.ReadUInt8(); reader.Skip(SaveFormat.GenericPaddingBytes)`
- `GenericWeapon`：Generic 后 `s.Ammo = reader.ReadUInt16LE()`；`var entry = dict.Get(slot.ItemId); if (entry?.SubType == "masterworkBow") s.Name = reader.ReadString()`
- `GenericShield`：Generic 后 `if (entry?.SubType == "nobleShield") s.House = reader.ReadHexBytes(1)`
- `Attachment`：`entry?.Slot == "body"` → `ReadAttachmentBlock(reader, s, AttachmentMode.Standalone)`；否则 `reader.Skip(1); s.Icon = reader.ReadUInt16LE(); reader.Skip(SaveFormat.AttachmentDataBytes); reader.Skip(SaveFormat.AttachmentPaddingBytes)`
- `BodyArmorStats`：`s.Fatigue = reader.ReadFloatLE()`；若 `s.BaseFatigue == 0 && s.BaseDurability == 0`：`slot.ItemType == "genericArmor" && entry != null` 时 `s.BaseFatigue = entry.Fatigue ?? 0; s.BaseDurability = entry.Durability ?? 0`，否则 `s.BaseFatigue = s.Fatigue; s.BaseDurability = s.MaxDurability`
- `GenericArmor`：`entry?.SubType == "nobleArmor"` → `s.House = reader.ReadHexBytes(1)`；`var isBody = entry?.Slot == "body"` → isBody 时先 `ReadAttachmentBlock(..., BodyArmorInline)`；随后 `Generic`；`s.Durability = reader.ReadFloatLE()`（第二次读、覆盖）；isBody 时 `BodyArmorStats`；`entry?.SubType == "davkul"` → `s.Description = reader.ReadString()`
- `GenericHelmet`：调 `GenericArmor` 后 `if (entry?.Fatigue is not null) slot.Stats!.Fatigue = entry.Fatigue.Value`
- `NamedWeapon`：按 Java 顺序读 `Name/MaxDurability/Fatigue(Int8)/DamageMin/Max(UInt16)/DamageArmor(Float)/HeadChance(UInt8)/DamageShield(UInt16)/HitBonus(UInt16)/Penetration(Float)/FatigueUse(Int16)/AmmoMax(Int16)`，`reader.Skip(SaveFormat.NamedWeaponZeroFloatBytes)`，最后调 `GenericWeapon`
- `NamedArmor`：`Name/MaxDurability(Float)/Fatigue(Int8)` 后调 `GenericArmor`；`NamedHelmet` 调 `NamedArmor`
- `NamedShield`：先 `MaxDurability`，调 `GenericShield`，再读 `Name/Fatigue(Int8)/MDef/RDef(UInt16)/FatigueUse(Int16)`
- `Auxiliary`：Generic 后按 `entry?.SubType` switch：`"ammo"` → `Ammo=ReadUInt16LE()`；`"canine"` → `Name=ReadString()`；`"provisions"` → `Amount=ReadFloatLE(); Expiry=ReadFloatLE(); Bought=ReadInt16LE()`；`"commodity"` → `Bought=ReadInt16LE()`
- `ReadAttachmentBlock`：`s.Attachment = reader.ReadHexBytes(4)`；`"00000000"` → return；Standalone：`Skip(AttStandalonePreIcon); s.AttachmentIcon=ReadUInt16LE(); Skip(AttStandalonePostIcon); Skip(AttStandalonePadding)`；Inline：`Skip(AttInlinePreIcon); s.AttachmentIcon=ReadUInt16LE(); Skip(AttInlinePostIcon)`；两分支共用结尾 `s.BaseDurability = reader.ReadFloatLE(); s.BaseFatigue = reader.ReadInt16LE()`
- 保留 Java 注释中的告诫：两分支的 12 字节偏移差异镜像 JS 源码、未经双物品真实存档验证，**不得合并分支**

- [ ] **Step 2: 构建验证**

Run: `dotnet build -warnaserror`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/BroLedger.Core/Parsing/ItemParser.cs
git commit -m "feat(core): ItemParser port (Forge port, all 10 item types)"
```

---

### Task 8: BrotherParser + fingerprint 钉子测试

**Files:**
- Create: `src/BroLedger.Core/Parsing/BrotherParser.cs`
- Test: `tests/BroLedger.Core.Tests/Parsing/FingerprintTests.cs`

**Interfaces:**
- Consumes: `HexReader/SaveFormat/ItemTypeKeys`（Task 6）、`ItemParser`（Task 7）、模型（Task 3）、`DictionaryService`（Task 5）
- Produces: `BrotherParser(DictionaryService dict, List<string> warnings, ILogger? logger = null)`，`Brother Parse(HexReader, string fullHexData)`；internal `static string FingerprintInput(string?, string?, int[])`、`static bool HasTattooByte(string?)`

- [ ] **Step 1: 写失败测试**（**移植缺口说明**：Java 无 fingerprint 单测；fingerprint 是 keeper.json 数据兼容的根基（规格 §5.1），用离线计算的 SHA-256 已知向量钉死）

```csharp
using System.Security.Cryptography;
using System.Text;
using BroLedger.Core.Parsing;
using Xunit;

namespace BroLedger.Core.Tests.Parsing;

public class FingerprintTests
{
    // 向量离线计算：printf '<input>' | sha256sum 前 32 字符大写
    [Theory]
    [InlineData("test", "AB12CD34", "00000000", "F1DDA98DD8AAE8C8F1A35DC74EA5B5E5")]
    [InlineData("test", "AB12CD34", "10000000", "4425E2D96E03F4BB52DB8B755EB2902C")]
    public void FingerprintInput_MatchesJavaSha256(string name, string bg, string starsStr, string expected)
    {
        var stars = starsStr.Select(c => c - '0').ToArray();
        var input = BrotherParser.FingerprintInput(name, bg, stars);
        Assert.Equal($"{name}|{bg}|{starsStr}", input);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)), 0, 16);
        Assert.Equal(expected, fingerprint);
    }

    [Fact]
    public void FingerprintInput_NullBackground_JavaNullLiteral()
    {
        // Java StringBuilder.append(null) 追加字面量 "null"；C# Append(null) 追加空串——必须 ?? "null"
        Assert.Equal("test|null|00000000", BrotherParser.FingerprintInput("test", null, new int[8]));
    }

    [Theory]
    [InlineData("6DF381C6", true)]   // Wildman
    [InlineData("CB90AA90", true)]   // Barbarian
    [InlineData("12345678", false)]
    [InlineData(null, false)]
    public void HasTattooByte_WildmanAndBarbarianOnly(string? bg, bool expected) =>
        Assert.Equal(expected, BrotherParser.HasTattooByte(bg));
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 编译错误 `BrotherParser` 不存在

- [ ] **Step 3: 实现 BrotherParser.cs**

真源：`parser/BrotherParser.java`。骨架与关键移植点（全方法逐一移植，顺序不得变）：

```csharp
using System.Security.Cryptography;
using System.Text;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Microsoft.Extensions.Logging;

namespace BroLedger.Core.Parsing;

// 移植 classes.js 的 battleBrother 构造器
public class BrotherParser
{
    // Wildman / Barbarian 背景 ID——多一个 tattoo 字节
    private const string Wildman = "6DF381C6";
    private const string Barbarian = "CB90AA90";

    private readonly DictionaryService _dict;
    private readonly ItemParser _itemParser;
    private readonly ILogger _logger;

    public BrotherParser(DictionaryService dict, List<string> warnings, ILogger? logger = null)
    {
        _dict = dict;
        _itemParser = new ItemParser(dict, warnings, logger);
        _logger = logger ?? NullLogger.Instance;
    }

    public Brother Parse(HexReader reader, string fullHexData)
    {
        var b = new Brother();
        ParseVisualLayers(reader, b);
        b.Header = ParseHeader(reader, fullHexData);
        ParseActionPointsAndStats(reader, b);
        if (!ParseInventory(reader, b)) return b; // 未知物品 → 部分解析
        var circleCount = ParseCircles(reader, b);
        ParseDescriptions(reader, b);
        ParseTraits(reader, b, circleCount);
        ParseNameAndLevelData(reader, b);
        ParseMoraleModifiers(reader, b);
        ParseStarsAndTalents(reader, b);
        b.Fingerprint = ComputeFingerprint(b);
        return b;
    }
    // ...以下私有方法全部照 Java 移植...
}
```

各私有方法的移植要点（**逐条核对 Java 源码后落码**）：

- `ParseVisualLayers`：`layerCount = reader.ReadUInt8()`，循环 `key=ReadString(); value=ReadHexBytes(VisualLayerValueBytes); sep=ReadHexBytes(VisualLayerSepBytes); b.VisualLayerMap[key] = new Brother.VisualLayer(value, sep)`
- `ParseHeader`：`start=reader.Cursor`；`memberCount=ReadInt16LE()`；循环内 `nameLen=ReadInt16LE(); reader.Skip(nameLen); memberType=ReadHexBytes(1)`；`"02"`→`Skip(1)`，`"03"`→`Skip(ReadUInt16LE())`，否则 `Skip(4)`；返回 `fullHexData.Substring(start, reader.Cursor - start).ToUpperInvariant()`
- `ParseActionPointsAndStats`：`b.ActionPoints=ReadUInt8()`；`for 0..8: b.Stats[i]=ReadInt16LE()`；`b.GreedAndGluttony=ReadHexBytes(GreedGluttonyBytes)`；`b.PouchCount=ReadUInt8()`
- `ParseInventory`：返回 bool。`itemCount=ReadUInt8()`；循环 `slotIndex=ReadUInt8(); itemId=ReadHexBytes(4)`；`!dict.Has(itemId)` → `_logger.LogWarning("BrotherParser: unknown item id {ItemId} in slot {Slot} — skipping brother remainder", itemId, slotIndex)` 并 `return false`；`slot.ItemType=dict.GetType(itemId); _itemParser.Parse(reader, slot)`；`slotIndex==6` → `b.ExtraPouches.Add(slot)`，`slotIndex<7` → `b.EquippedSlots[slotIndex]=slot`
- `ParseCircles`：返回 circleCount。`ReadUInt16LE()`；循环 `hexId=ReadHexBytes(4); reader.Skip(1)`；未知 id → log warning + `continue`；`dict.GetType(hexId)=="background"` → `b.BackgroundHexId=hexId; break`；否则 `b.PerkIds.Add(hexId)`
- `ParseDescriptions`：`Description/DescriptionTemplate=ReadString()`；`Unknown=ReadHexBytes(2)`；`SalaryMultiplier=ReadFloatLE()`；`HasTattooByte(b.BackgroundHexId)` → `Tattoo=ReadHexBytes(1)`
- `ParseTraits`：`for (var i = b.PerkIds.Count + 1; i < circleCount; i++)`；`traitId=ReadHexBytes(4)`；未知 → log + `b.Traits.Add(new TraitEntry(traitId, null)); continue`；`!"injury".Equals(type)` → `Skip(1)`；eventData switch：injury→`ReadHexBytes(InjuryEventBytes)`、training→TrainingEventBytes、knowledge→KnowledgeEventBytes、learning→LearningEventBytes、default→null；`b.Traits.Add(new TraitEntry(traitId, eventData))`。循环后 perk 误标提升：`b.Traits.RemoveAll(trait => { if (_dict.GetType(trait.Id) == "perk") { b.PerkIds.Add(trait.Id); return true; } return false; })`
- `ParseNameAndLevelData`：`Name/Title=ReadString()`；`LightWound=ReadFloatLE()`；`Experience=(int)ReadUInt32LE()`；`HumanBytes=ReadHexBytes(HumanBytes)`；`LevelTotal/PerkPoints/PerkUsed/LevelPoints=ReadUInt8()`；`Morale=ReadFloatLE()`
- `ParseMoraleModifiers`：`modCountHex=ReadHexBytes(1); modCount=Convert.ToInt32(modCountHex, 16)`；`sb` 从 modCountHex 起；循环 `sb.Append(ReadHexBytes(1))`；`textLength=ReadUInt16LE()`；`sb.Append(HexUtils.UInt16ToHexLE(textLength))`；`sb.Append(ReadHexBytes(textLength + MoraleEntryTailBytes))`；结尾 `sb.Append(ReadHexBytes(MoraleBlockTailBytes))`；`b.MoraleModifierHex=sb.ToString()`
- `ParseStarsAndTalents`：`for 0..8: b.Stars[i]=ReadUInt8()`；再 `for 0..8: b.TalentPoints=ReadUInt8(); b.TalentBytes[i]=ReadHexBytes(b.TalentPoints)`（ReadHexBytes(0) 合法返回 ""）
- `ComputeFingerprint`：

```csharp
private static string ComputeFingerprint(Brother b)
{
    var input = FingerprintInput(b.Name, b.BackgroundHexId, b.Stars);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(hash, 0, 16); // 前 16 字节 → 32 大写 hex 字符
}

internal static bool HasTattooByte(string? backgroundHexId) =>
    backgroundHexId is Wildman or Barbarian;

internal static string FingerprintInput(string? name, string? backgroundHexId, int[] stars)
{
    // Java StringBuilder.append((String)null) 追加字面量 "null"——C# 必须 ?? "null"
    var sb = new StringBuilder(name).Append('|').Append(backgroundHexId ?? "null").Append('|');
    foreach (var s in stars) sb.Append(s);
    return sb.ToString();
}
```

> 注意：Java `new StringBuilder(name)` 的 name 在此调用点必非 null（ParseNameAndLevelData 已赋值）；C# `new StringBuilder(name)` 对 null 亦安全，无需额外处理。

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 全部 PASS（含 3 个新 fingerprint 测试）

- [ ] **Step 5: Commit**

```bash
git add src/BroLedger.Core/Parsing/BrotherParser.cs tests/BroLedger.Core.Tests/Parsing/FingerprintTests.cs
git commit -m "feat(core): BrotherParser port with Java-pinned fingerprint vectors"
```

---

### Task 9: SaveParser + HexDumpSaveParser + SaveParserTest 集成

**Files:**
- Create: `src/BroLedger.Core/Parsing/SaveParser.cs`
- Create: `tests/BroLedger.Core.Tests/TestData/TestSavefile.sav`（自 Java 仓库复制，7.3MB）
- Create: `tests/BroLedger.Core.Tests/Helpers/HexDumpSaveParser.cs`（测试辅助）
- Modify: `tests/BroLedger.Core.Tests/BroLedger.Core.Tests.csproj`（复制 sav 到输出目录）
- Test: `tests/BroLedger.Core.Tests/Parsing/SaveParserTests.cs`

**Interfaces:**
- Consumes: 前序全部
- Produces: `SaveParser(DictionaryService dict, ILogger? logger = null)`：`IReadOnlyList<string> Warnings`、`List<Brother> Parse(string savePath)`、`List<Brother> ParseHex(string hex)`。测试辅助 `HexDumpSaveParser(DictionaryService)`：`List<Brother> Parse(string savePath)`、`internal static string FormatHexDump(string hex)`

- [ ] **Step 1: 复制测试存档并配置输出复制**

```bash
mkdir -p tests/BroLedger.Core.Tests/TestData
cp "../bro-ledger/src/test/example/TestSavefile.sav" tests/BroLedger.Core.Tests/TestData/
```

测试 csproj 增加：

```xml
  <ItemGroup>
    <Content Include="TestData\TestSavefile.sav" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: 写失败测试**（迁移 `parser/SaveParserTest.java` 全部用例；`-Dsav.file` → 环境变量 `BRO_LEDGER_SAV`；`hex.dump` → `BRO_LEDGER_HEX_DUMP`）

```csharp
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Parsing;

// 对真实 .sav 的集成测试。BRO_LEDGER_SAV 指定真实存档路径，缺省用 TestSavefile.sav。
// IClassFixture 保证存档只解析一次（Java @BeforeAll 等价物）。
public class SaveParserTests(SaveParserTests.Fixture fixture)
    : IClassFixture<SaveParserTests.Fixture>
{
    public class Fixture
    {
        public List<Brother> Brothers { get; }
        public DictionaryService Dict { get; } = DictionaryService.Instance;

        public Fixture()
        {
            Dict.LoadFromEmbeddedResource();
            var savPath = Environment.GetEnvironmentVariable("BRO_LEDGER_SAV")
                ?? Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.sav");
            var parser = Environment.GetEnvironmentVariable("BRO_LEDGER_HEX_DUMP") is not null
                ? new Helpers.HexDumpSaveParser(Dict)
                : new SaveParser(Dict);
            Brothers = parser.Parse(savPath);
            foreach (var b in Brothers) Console.WriteLine(b.Name + "found");
        }
    }

    private readonly Fixture _f = fixture;

    [Fact]
    public void BrotherCountIsPositive()
    {
        Assert.False(_f.Brothers.Count == 0, "Expected at least one brother in the save");
    }

    [Fact]
    public void FirstBrotherHasName()
    {
        var first = _f.Brothers[0];
        Assert.NotNull(first.Name);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
    }

    [Fact]
    public void FirstBrotherHasValidStats()
    {
        var first = _f.Brothers[0];
        Assert.True(first.Stats[Stat.Health.StatIndex] > 0,
            $"Health stat should be positive, got: {first.Stats[Stat.Health.StatIndex]}");
        for (var i = 0; i < 8; i++)
            Assert.True(first.Stats[i] is >= 0 and <= 300,
                $"Stat[{i}] out of range: {first.Stats[i]}");
    }

    [Fact]
    public void FirstBrotherHasBackground()
    {
        var first = _f.Brothers[0];
        Assert.NotNull(first.BackgroundHexId);
        Assert.Equal(8, first.BackgroundHexId!.Length);
    }

    [Fact]
    public void FirstBrotherStarsInRange()
    {
        var first = _f.Brothers[0];
        for (var i = 0; i < 8; i++)
            Assert.True(first.Stars[i] is >= 0 and <= 3,
                $"Star[{i}] out of range: {first.Stars[i]}");
    }

    [Fact]
    public void FirstBrotherHasFingerprint()
    {
        var first = _f.Brothers[0];
        Assert.NotNull(first.Fingerprint);
        Assert.Equal(32, first.Fingerprint!.Length);
    }

    [Fact]
    public void PerksAreKnownIds()
    {
        foreach (var b in _f.Brothers)
        foreach (var perkId in b.PerkIds)
            Assert.True(_f.Dict.Has(perkId),
                $"Perk id not in dictionary: {perkId} (brother: {b.Name})");
    }

    [Fact]
    public void PrintRoster()
    {
        // 非断言——人工核对用（对齐 Java printRoster/debugBrother）
        foreach (var b in _f.Brothers)
            Console.WriteLine(
                $"{b.Name,-25} {b.Title,-20}  HP={b.Stats[Stat.Health.StatIndex],-4} " +
                $"MS={b.Stats[Stat.MeleeSkill.StatIndex],-4}  perks=({string.Join(",", b.PerkIds)})  " +
                $"traits=({string.Join(",", b.Traits.Select(t => t.Id))})");
    }
}
```

- [ ] **Step 3: 运行确认失败**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 编译错误 `SaveParser` 不存在

- [ ] **Step 4: 实现 SaveParser.cs**（真源：`parser/SaveParser.java`）

```csharp
using BroLedger.Core.Services;
using BroLedger.Core.Utils;
using Microsoft.Extensions.Logging;

namespace BroLedger.Core.Parsing;

public class SaveParser
{
    private readonly DictionaryService _dict;
    private readonly ILogger _logger;
    private readonly List<string> _warnings = [];

    public SaveParser(DictionaryService dict, ILogger? logger = null)
    {
        _dict = dict;
        _logger = logger ?? NullLogger.Instance;
    }

    // 最近一次解析收集的警告（未知物品类型等）
    public IReadOnlyList<string> Warnings => _warnings;

    public List<Brother> Parse(string savePath)
    {
        var bytes = File.ReadAllBytes(savePath);
        // 转小写 hex 字符串——与 Node fs.readFile(path, "hex") 一致
        var hex = HexUtils.ToHexLower(bytes);
        return ParseHex(hex);
    }

    public List<Brother> ParseHex(string hex)
    {
        _warnings.Clear();
        var brotherParser = new BrotherParser(_dict, _warnings, _logger);
        var brothers = new List<Brother>();

        var offsets = BrotherLocator.FindOffsets(hex);
        if (offsets.Count == 0)
        {
            _logger.LogWarning("SaveParser: brother signature not found");
            return brothers;
        }

        foreach (var offset in offsets)
        {
            try
            {
                var reader = new HexReader(hex, offset);
                var b = brotherParser.Parse(reader, hex);
                brothers.Add(b);
            }
            catch (Exception e)
            {
                _logger.LogWarning("SaveParser: failed to parse brother at offset {Offset}: {Message}",
                    offset, e.Message);
            }
        }

        return brothers;
    }
}
```

- [ ] **Step 5: 移植测试辅助 HexDumpSaveParser.cs**（真源：`test/tools/parser/HexDumpSaveParser.java`，132 行逐一移植；命名空间 `BroLedger.Core.Tests.Helpers`；`Files.writeString` → `File.WriteAllText`；工作目录 `Environment.CurrentDirectory`；`System.err` → `Console.Error`；`Files.readAllBytes` 后用 `HexUtils.ToHex`（大写）而非 ToHexLower——与 Java 一致）

```csharp
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;
using BroLedger.Core.Utils;

namespace BroLedger.Core.Tests.Helpers;

// 包装 SaveParser：解析存档并把每个兄弟的原始 hex 段写到工作目录 <savename>_brother_<i>.hex
// （128 字节/行 + 下一行 ASCII 对照；不可打印字节显示 '.'）
public class HexDumpSaveParser
{
    private const int BytesPerLine = 128;

    private readonly DictionaryService _dict;

    public HexDumpSaveParser(DictionaryService dict) => _dict = dict;

    public List<Brother> Parse(string savePath)
    {
        var bytes = File.ReadAllBytes(savePath);
        var hex = HexUtils.ToHex(bytes);

        var fileName = Path.GetFileName(savePath);
        var dot = fileName.LastIndexOf('.');
        var baseName = dot > 0 ? fileName[..dot] : fileName;

        return ParseAndDump(hex, baseName);
    }

    internal static string FormatHexDump(string hex)
    {
        var charsPerLine = BytesPerLine * 2;
        var sb = new System.Text.StringBuilder(hex.Length * 2);

        for (var pos = 0; pos < hex.Length; pos += charsPerLine)
        {
            var lineEnd = Math.Min(pos + charsPerLine, hex.Length);
            var hexLine = hex[pos..lineEnd];
            sb.Append(hexLine).Append('\n');

            var byteCount = hexLine.Length / 2;
            for (var i = 0; i < byteCount; i++)
            {
                var b = Convert.ToInt32(hexLine.Substring(i * 2, 2), 16);
                var c = b is >= 0x20 and <= 0x7E ? (char)b : '.';
                sb.Append(' ').Append(c);
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private List<Brother> ParseAndDump(string hex, string baseName)
    {
        var brotherParser = new BrotherParser(_dict, []);
        var brothers = new List<Brother>();

        var parseOffsets = BrotherLocator.FindOffsets(hex);
        if (parseOffsets.Count == 0)
        {
            Console.Error.WriteLine("HexDumpSaveParser: brother signature not found");
            return brothers;
        }

        var sigStarts = parseOffsets.Select(o => o - BrotherLocator.SigDataAhead).ToList();

        // 解析长度转储 (_brother_N.hex)
        for (var i = 0; i < parseOffsets.Count; i++)
        {
            var start = parseOffsets[i];
            var reader = new HexReader(hex, start);
            Brother? b = null;
            try
            {
                b = brotherParser.Parse(reader, hex);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(
                    $"HexDumpSaveParser: failed parsing brother {i} at offset {start}: {e.Message}");
            }
            finally
            {
                var outFile = Path.Combine(Environment.CurrentDirectory, $"{baseName}_brother_{i}.hex");
                File.WriteAllText(outFile, FormatHexDump(hex[start..reader.Cursor]));
                Console.WriteLine($"HexDumpSaveParser: wrote {Path.GetFullPath(outFile)}");
            }
            if (b != null) brothers.Add(b);
        }

        // 签名到签名转储 (_brother_NA.hex)
        var sigCount = sigStarts.Count;
        var maxSigLength = 0;
        for (var i = 0; i < sigCount - 1; i++)
            maxSigLength = Math.Max(maxSigLength, sigStarts[i + 1] - sigStarts[i]);

        for (var i = 0; i < sigCount; i++)
        {
            var start = sigStarts[i];
            var end = i < sigCount - 1 ? sigStarts[i + 1] : Math.Min(start + maxSigLength, hex.Length);

            var outFile = Path.Combine(Environment.CurrentDirectory, $"{baseName}_brother_{i}A.hex");
            File.WriteAllText(outFile, FormatHexDump(hex[start..end]));
            Console.WriteLine($"HexDumpSaveParser: wrote {Path.GetFullPath(outFile)}");
        }

        return brothers;
    }
}
```

- [ ] **Step 6: 运行确认通过**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 全部 PASS——包括 8 个 SaveParserTests。若 `BrotherCountIsPositive` 失败，先用 Java 版跑同一存档对照（`mvn test -Dsav.file=...`），两版必须产出相同兄弟数——**这是两版并排一致（规格 §9 验收 3）的第一次机会**。

- [ ] **Step 7: Commit**

```bash
git add src/BroLedger.Core/Parsing/SaveParser.cs tests/BroLedger.Core.Tests
git commit -m "feat(core): SaveParser + SaveParserTests integration on real TestSavefile.sav"
```

---

### Task 10: 计划①收尾验收

**Files:**
- Modify: 无新文件（仅验证与修缺）

**Interfaces:**
- Consumes: 全部
- Produces: 计划①完成状态——后续计划 ② 起点的绿色基线

- [ ] **Step 1: 全量构建（CI 门禁预演）**

Run: `dotnet build -warnaserror`
Expected: Build succeeded，0 警告

- [ ] **Step 2: 全量测试**

Run: `dotnet test tests/BroLedger.Core.Tests`
Expected: 全绿（HexUtils 16 + ArmorCalculator 19 + Schema 1 + Dictionary 6 + BrotherLocator 5 + Fingerprint 7 + SaveParser 8 = 62 个）

- [ ] **Step 3: Review Focus 手工验证**

1. 截断验证：`head -c 1000000 tests/BroLedger.Core.Tests/TestData/TestSavefile.sav > /tmp/truncated.sav`，临时设 `BRO_LEDGER_SAV=/tmp/truncated.sav` 跑 `PrintRoster`——期望不抛异常、兄弟数 ≥ 0（截断点若在兄弟区块中段，该兄弟被跳过并有警告）
2. 未知物品验证：代码走查 `BrotherParser.ParseInventory` 的 `return false` 路径 + `_warnings` 被填充（Java 行为镜像）
3. 无签名验证：已被 `BrotherLocatorTests.FindOffsets_EmptyWhenSignatureAbsent` 覆盖
4. 大小写验证：已被 HexUtils/DictionaryService 测试覆盖
5. fingerprint null：已被 `FingerprintInput_NullBackground_JavaNullLiteral` 覆盖

- [ ] **Step 4: Commit（若有修缺）并汇报**

```bash
git add -A
git commit -m "chore: plan 1 acceptance — full build + test + review-focus verification"
```

---

## 后续计划路线图（不在本计划实施）

- **计划②** 服务层+数据兼容：AppPaths、AnnotationService（keeper.json + fallback SHA-256 路径）、AppConfig、RoleService（roles.json）、ExpectedStatsCalculator（Naive/Greedy）、StatPotentialCalculator、StatModifierService、WeaponStatsService、SaveReplayService、AssetExtractor、FileWatcherService（FileSystemWatcher + 去抖/OVERFLOW）、ImageMapService、ImageCache(Core 字节半) + 对应测试迁移
- **计划③** Tools + RoleShareService（同一 Worker 端点、`createForTest` 注入缝）+ 3 个控制台工具
- **计划④** App UI：Avalonia 12、8 AXAML、MVVM、自定义控件、样式/字体迁移
- **计划⑤** AOT 发布矩阵 + GitHub Actions 三平台 CI + 验收清单
