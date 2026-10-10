using BroLedger.Core.Models;
using BroLedger.Core.Services;
using BroLedger.Core.Utils;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/AnnotationServiceTest.java
// (reconcileOnReload half excluded — next task). Seams: @TempDir → per-test GUID dir;
// reflection singleton reset → ResetForTest(); fallback dir → BROLEDGER_DATADIR override
// (same seam as AppPathsTests).
public class AnnotationServiceTests : IDisposable
{
    private readonly string Tmp;
    private AnnotationService Service;
    private string SavePath;

    public AnnotationServiceTests()
    {
        Tmp = Path.Combine(Path.GetTempPath(), "broledger-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Tmp);
        SavePath = Path.Combine(Tmp, "test.sav");
        File.WriteAllText(SavePath, "dummy");
        AnnotationService.ResetForTest();
        Service = AnnotationService.Instance;
        Service.LoadFor(SavePath);
    }

    public void Dispose()
    {
        AnnotationService.ResetForTest();
        if (Directory.Exists(Tmp)) Directory.Delete(Tmp, recursive: true);
    }

    // Java: savePath.resolveSibling(savePath.getFileName() + ".broledger.json")
    private string PrimaryFile =>
        Path.Combine(Path.GetDirectoryName(SavePath)!, Path.GetFileName(SavePath) + ".broledger.json");

    // Java: FALLBACK_DIR.resolve("annotations-" + sha256hex(save.toAbsolutePath()) + ".json")
    private static string FallbackFile(string dataDir, string save) =>
        Path.Combine(dataDir, "annotations-" + HexUtils.Sha256Hex(Path.GetFullPath(save)) + ".json");

    /// <summary>Scoped BROLEDGER_DATADIR override so fallback paths land in the test dir.</summary>
    private void WithDataDir(Action<string> run)
    {
        var saved = Environment.GetEnvironmentVariable("BROLEDGER_DATADIR");
        var dataDir = Path.Combine(Tmp, "data");
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", dataDir);
        try { run(dataDir); }
        finally
        {
            Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", saved);
            AnnotationService.ResetForTest();
        }
    }

    // ---- transcribed: get/setRole -------------------------------------------

    [Fact]
    public void GetReturnsFreshAnnotationForUnknownFingerprint()
    {
        var a = Service.Get("ABCD1234");
        Assert.NotNull(a);
        Assert.Null(a.RoleId);
    }

    [Fact]
    public void SetRolePersistsAcrossReload()
    {
        Service.SetRole("FP001", "uuid-tank");

        Service.LoadFor(SavePath);
        Assert.Equal("uuid-tank", Service.Get("FP001").RoleId);
    }

    [Fact]
    public void NullRoleIdPersistsAsNull()
    {
        Service.SetRole("FP001", "uuid-tank");
        Service.SetRole("FP001", null);

        Service.LoadFor(SavePath);
        Assert.Null(Service.Get("FP001").RoleId);
    }

    [Fact]
    public void MultipleBrothersPersistedIndependently()
    {
        Service.SetRole("A", "uuid-archer");
        Service.SetRole("B", "uuid-warrior");

        Service.LoadFor(SavePath);
        Assert.Equal("uuid-archer", Service.Get("A").RoleId);
        Assert.Equal("uuid-warrior", Service.Get("B").RoleId);
    }

    [Fact]
    public void ClearRoleRefsClearsMatchingEntriesAndPersists()
    {
        Service.SetRole("A", "uuid-tank");
        Service.SetRole("B", "uuid-tank");
        Service.SetRole("C", "uuid-archer");

        var affected = Service.ClearRoleRefs("uuid-tank");

        Assert.Equal(new List<string> { "A", "B" }, affected);
        Assert.Null(Service.Get("A").RoleId);
        Assert.Null(Service.Get("B").RoleId);
        Assert.Equal("uuid-archer", Service.Get("C").RoleId);

        Service.LoadFor(SavePath);
        Assert.Null(Service.Get("A").RoleId);
        Assert.Equal("uuid-archer", Service.Get("C").RoleId);
    }

    [Fact]
    public void ClearRoleRefsNoOpForNullOrUnusedRole()
    {
        Service.SetRole("A", "uuid-tank");
        Assert.Empty(Service.ClearRoleRefs(null));
        Assert.Empty(Service.ClearRoleRefs("uuid-unused"));
        Assert.Equal("uuid-tank", Service.Get("A").RoleId);
    }

    [Fact]
    public void ClearDanglingRoleRefsClearsUnknownKeepsValid()
    {
        Service.SetRole("A", "uuid-tank");
        Service.SetRole("B", "uuid-deleted");
        Service.SetRole("C", "uuid-also-deleted");

        var affected = Service.ClearDanglingRoleRefs(new HashSet<string> { "uuid-tank" });

        Assert.Equal(new List<string> { "B", "C" }, affected);
        Assert.Equal("uuid-tank", Service.Get("A").RoleId);
        Assert.Null(Service.Get("B").RoleId);
        Assert.Null(Service.Get("C").RoleId);

        Service.LoadFor(SavePath);
        Assert.Null(Service.Get("B").RoleId);
    }

    [Fact]
    public void ClearDanglingRoleRefsEmptyWhenAllValid()
    {
        Service.SetRole("A", "uuid-tank");
        Assert.Empty(Service.ClearDanglingRoleRefs(new HashSet<string> { "uuid-tank" }));
        Assert.Equal("uuid-tank", Service.Get("A").RoleId);
    }

    // ---- transcribed: file layout -------------------------------------------

    [Fact]
    public void KeeperJsonCreatedAdjacentToSave()
    {
        Service.SetRole("FP003", "uuid-scout");
        Assert.True(File.Exists(PrimaryFile), "broledger.json not created adjacent to save");
    }

    [Fact]
    public void OverwritingRoleUpdatesFile()
    {
        Service.SetRole("FP004", "uuid-first");
        Service.SetRole("FP004", "uuid-second");

        Service.LoadFor(SavePath);
        Assert.Equal("uuid-second", Service.Get("FP004").RoleId);
    }

    // ---- transcribed: statIncreases ------------------------------------------

    [Fact]
    public void StatIncreasesPersistAcrossReload()
    {
        var inc = new int[Stat.Values.Length];
        inc[Stat.Health.Ordinal] = 3;
        inc[Stat.MeleeSkill.Ordinal] = 2;
        Service.SetStatIncreases("FP005", inc);

        Service.LoadFor(SavePath);
        var loaded = Service.Get("FP005").StatIncreases;
        Assert.NotNull(loaded);
        Assert.Equal(3, loaded[Stat.Health.Ordinal]);
        Assert.Equal(2, loaded[Stat.MeleeSkill.Ordinal]);
        Assert.Equal(0, loaded[Stat.Initiative.Ordinal]);
    }

    [Fact]
    public void StatIncreases_NullForBrotherWithNoIncreases()
    {
        Service.SetRole("FP006", "uuid-x");
        Service.LoadFor(SavePath);
        Assert.Null(Service.Get("FP006").StatIncreases);  // must be null when never set
    }

    [Fact]
    public void StatIncreases_OldFileWithoutFieldLoadsWithoutError()
    {
        // Write a broledger.json that has no statIncreases field (legacy format)
        File.WriteAllText(PrimaryFile,
            "{\"version\":1,\"annotations\":{\"FP007\":{\"roleId\":\"uuid-y\"}}}");

        Service.LoadFor(SavePath);
        var a = Service.Get("FP007");
        Assert.Equal("uuid-y", a.RoleId);  // roleId from legacy file must load
        Assert.Null(a.StatIncreases);      // must default to null in legacy file
    }

    // ---- ports-gap coverage (brief: 无文件→空库 / fallback 读 / UiState / SetOrder /
    //      perkPlanStatus 语义 / JSON 形状 / fallback 钉子) ---------------------

    [Fact]
    public void LoadForWithoutAnyAnnotationFileClearsStore()
    {
        Service.SetRole("STALE", "uuid-old");
        Service.LoadFor(Path.Combine(Tmp, "fresh.sav"));  // no annotation files for it
        Assert.Null(Service.Get("STALE").RoleId);
    }

    [Fact]
    public void LoadFallsBackToFallbackFileWhenPrimaryMissing()
    {
        WithDataDir(dataDir =>
        {
            Directory.CreateDirectory(dataDir);
            var save = Path.Combine(Tmp, "never-selected.sav");
            File.WriteAllText(FallbackFile(dataDir, save),
                "{\"version\":1,\"annotations\":{\"FB1\":{\"roleId\":\"uuid-fb\"}}}");

            Service.LoadFor(save);

            Assert.Equal("uuid-fb", Service.Get("FB1").RoleId);
        });
    }

    [Fact]
    public void UiStateRoundTripsAcrossReload()
    {
        Service.SetUiState(new AnnotationService.UiState
        {
            PotentialMode = "ALL",
            ArmorMode = "BEST",
            WeaponTierMode = "MAX",
            PerkSortMode = "NAME",
        });

        Service.LoadFor(SavePath);
        var loaded = Service.GetUiState();
        Assert.Equal("ALL", loaded.PotentialMode);
        Assert.Equal("BEST", loaded.ArmorMode);
        Assert.Equal("MAX", loaded.WeaponTierMode);
        Assert.Equal("NAME", loaded.PerkSortMode);
    }

    [Fact]
    public void SetOrderAssignsSortIndexesAndPersists()
    {
        Service.SetOrder(new List<string> { "B", "A", "C" });

        Assert.Equal(0, Service.Get("B").SortIndex);
        Assert.Equal(1, Service.Get("A").SortIndex);
        Assert.Equal(2, Service.Get("C").SortIndex);

        Service.LoadFor(SavePath);
        Assert.Equal(0, Service.Get("B").SortIndex);
        Assert.Equal(1, Service.Get("A").SortIndex);
        Assert.Equal(2, Service.Get("C").SortIndex);
    }

    [Fact]
    public void PerkPlanStatusRoundTripsAndEmptyClears()
    {
        Service.SetPerkPlanStatus("PP1", new Dictionary<string, string> { ["ABCD0001"] = "PLANNED" });
        Service.LoadFor(SavePath);
        var map = Service.Get("PP1").PerkPlanStatus;
        Assert.NotNull(map);
        Assert.Equal("PLANNED", map["ABCD0001"]);

        // null or empty = clear plan → persisted as null (Java LinkedHashMap 语义照抄)
        Service.SetPerkPlanStatus("PP1", new Dictionary<string, string>());
        Service.LoadFor(SavePath);
        Assert.Null(Service.Get("PP1").PerkPlanStatus);
    }

    [Fact]
    public void WrittenJsonKeepsJavaShape()
    {
        // 数据兼容格式之首：camelCase 字段名 + version 恒 1 + pretty 缩进
        Service.SetRole("FP001", "uuid-tank");
        var json = File.ReadAllText(PrimaryFile);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"annotations\": {", json);
        Assert.Contains("\"FP001\": {", json);
        Assert.Contains("\"roleId\": \"uuid-tank\"", json);
    }

    /// <summary>
    /// 移植缺口钉子（Java 侧该路径仅手测）：主路径父目录是一个已存在的**文件**，
    /// createDirectories 失败 → 注解必须落到 fallback 且文件名含存档绝对路径的 sha256。
    /// </summary>
    [Fact]
    public void FlushFallsBackToFallbackFileWhenPrimaryDirectoryBlocked()
    {
        WithDataDir(dataDir =>
        {
            var blocker = Path.Combine(Tmp, "blocker");
            File.WriteAllText(blocker, "i am a file, not a directory");
            var save = Path.Combine(blocker, "save.sav");

            AnnotationService.ResetForTest();
            var svc = AnnotationService.Instance;
            svc.LoadFor(save);
            svc.SetRole("FPX", "uuid-x");

            Assert.True(File.Exists(FallbackFile(dataDir, save)),
                "annotation must land in fallback annotations-<sha256>.json when primary dir is uncreatable");
        });
    }
}
