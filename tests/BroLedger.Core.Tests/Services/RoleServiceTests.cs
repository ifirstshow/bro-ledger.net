using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// Transcribed test-by-test from JAVATEST/service/RoleServiceTest.java.
// Seams: @TempDir → per-test GUID dir; RoleService.createForTest/resetInstance →
// CreateForTest/ResetInstance; AppConfig.configFileForTest → ConfigFileForTest.
// Collection: shares the static AppConfig singleton with AppConfigTests — must run sequentially.
[Collection("AppConfig")]
public class RoleServiceTests : IDisposable
{
    private readonly string Tmp;
    private readonly string RolesFile;
    private RoleService Service;

    public RoleServiceTests()
    {
        Tmp = Path.Combine(Path.GetTempPath(), "broledger-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Tmp);
        RolesFile = Path.Combine(Tmp, "roles.json");
        AppConfig.ResetForTest();
        AppConfig.ConfigFileForTest = Path.Combine(Tmp, "config.json");
        Service = RoleService.CreateForTest(RolesFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(Tmp)) Directory.Delete(Tmp, recursive: true);
        AppConfig.ResetForTest();
        RoleService.ResetInstance();
    }

    [Fact]
    public void GetAllReturnsEmptyInitially()
    {
        Assert.Empty(Service.GetAll());
    }

    [Fact]
    public void AddCreatesRoleWithUuid()
    {
        var r = Service.Add("Tank", true);
        Assert.NotNull(r.Id);
        Assert.Equal("Tank", r.Name);
        Assert.True(r.Frontline);
    }

    [Fact]
    public void AddedRoleAppearsInGetAll()
    {
        Service.Add("Archer", false);
        var all = Service.GetAll();
        Assert.Single(all);
        Assert.Equal("Archer", all[0].Name);
    }

    [Fact]
    public void GetAllReturnsInRoleOrderWhenSet()
    {
        Service.Add("Warrior", true);
        Service.Add("Archer", false);
        Service.Add("Medic", true);

        // Role order is built by Add(): [warrior, archer, medic]
        var all = Service.GetAll();
        Assert.Equal("Warrior", all[0].Name);
        Assert.Equal("Archer", all[1].Name);
        Assert.Equal("Medic", all[2].Name);
    }

    [Fact]
    public void GetAllReturnsInsertionOrderWhenNoRoleOrder()
    {
        AppConfig.Instance.RoleOrder = null;
        Service.Add("Warrior", true);
        Service.Add("Archer", false);

        var all = Service.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("Warrior", all[0].Name);
        Assert.Equal("Archer", all[1].Name);
    }

    [Fact]
    public void GetByIdReturnsCorrectRole()
    {
        var r = Service.Add("Scout", true);
        var found = Service.GetById(r.Id);
        Assert.NotNull(found);
        Assert.Equal("Scout", found.Name);
    }

    [Fact]
    public void GetByIdReturnsNullForUnknownId()
    {
        Assert.Null(Service.GetById("does-not-exist"));
    }

    [Fact]
    public void UpdateChangesName()
    {
        var r = Service.Add("OldName", true);
        r.Name = "NewName";
        Service.Update(r);
        Assert.Equal("NewName", Service.GetById(r.Id)!.Name);
    }

    [Fact]
    public void UpdatePersistsAcrossReload()
    {
        var r = Service.Add("Healer", false);
        r.Name = "Cleric";
        Service.Update(r);

        Service = RoleService.CreateForTest(RolesFile);

        Assert.Equal("Cleric", Service.GetById(r.Id)!.Name);
    }

    [Fact]
    public void DeleteRemovesRole()
    {
        var r = Service.Add("Temp", true);
        Service.Delete(r.Id);
        Assert.Null(Service.GetById(r.Id));
        Assert.Empty(Service.GetAll());
    }

    [Fact]
    public void DeletePersistsAcrossReload()
    {
        var r = Service.Add("Gone", true);
        Service.Delete(r.Id);

        Service = RoleService.CreateForTest(RolesFile);

        Assert.Empty(Service.GetAll());
    }

    [Fact]
    public void DefaultPriorityIsThreeForAllStats()
    {
        var r = Service.Add("Default", true);
        Assert.Equal(new[] { 3, 3, 3, 3, 3, 3, 3, 3 }, r.Priority);
    }

    [Fact]
    public void RolesFileCreatedInConfigDir()
    {
        Service.Add("X", true);
        Assert.True(File.Exists(Path.Combine(Tmp, "roles.json")));
    }

    [Fact]
    public void MoveUpdatesOrder()
    {
        var a = Service.Add("A", true);
        Service.Add("B", true);
        Service.Add("C", true);

        // Initial: [A, B, C]; move A to end
        Service.Move(a.Id!, 2);
        var all = Service.GetAll();
        Assert.Equal("B", all[0].Name);
        Assert.Equal("C", all[1].Name);
        Assert.Equal("A", all[2].Name);
    }

    [Fact]
    public void MoveClampsToValidRange()
    {
        var a = Service.Add("A", true);
        Service.Add("B", true);

        Service.Move(a.Id!, 100); // clamp to end
        Assert.Equal("B", Service.GetAll()[0].Name);
        Assert.Equal("A", Service.GetAll()[1].Name);

        Service.Move(a.Id!, -5); // clamp to start
        Assert.Equal("A", Service.GetAll()[0].Name);
    }

    [Fact]
    public void DeleteRemovesIdFromRoleOrder()
    {
        var a = Service.Add("A", true);
        var b = Service.Add("B", true);

        Service.Delete(a.Id!);

        var order = AppConfig.Instance.RoleOrder!;
        Assert.DoesNotContain(a.Id, order);
        Assert.Contains(b.Id, order);
    }

    [Fact]
    public void MoveWorksWhenRoleOrderIsEmpty()
    {
        // Simulate roles that exist in the store but roleOrder is empty (e.g. after delete).
        var a = Service.Add("A", true);
        Service.Add("B", true);
        AppConfig.Instance.RoleOrder = []; // force empty

        // GetAll() falls back to insertion order [A, B]; move should still work
        Service.Move(a.Id!, 1);
        var all = Service.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("B", all[0].Name);
        Assert.Equal("A", all[1].Name);
    }

    [Fact]
    public void AddPopulatesRoleOrder()
    {
        var a = Service.Add("A", true);
        var b = Service.Add("B", true);

        var order = AppConfig.Instance.RoleOrder!;
        Assert.Equal(new[] { a.Id, b.Id }, order);
    }

    // ---- importPack ---------------------------------------------------------

    private static Role PackRole(string name, bool frontline)
    {
        return new Role
        {
            Id = "sender-id-" + name,
            Name = name,
            Frontline = frontline,
            TargetStats = [1, 2, 3, 4, 5, 6, 7, 8],
            Priority = [1, 1, 2, 2, 3, 3, 1, 2],
            PerkPlanTemplate = new Dictionary<string, string> { ["00000001"] = "PLANNED" }
        };
    }

    [Fact]
    public void ImportPackMintsFreshUuidsAndPreservesFields()
    {
        var incoming = PackRole("Tank", true);
        var pack = new RolePack("Test Pack", [incoming]);

        var imported = Service.ImportPack(pack, [incoming], "AB3D9F2K");

        Assert.Single(imported);
        var r = imported[0];
        Assert.NotEqual(incoming.Id, r.Id);
        Assert.Equal("Tank", r.Name);
        Assert.True(r.Frontline);
        Assert.Equal(incoming.TargetStats, r.TargetStats);
        Assert.Equal(incoming.Priority, r.Priority);
        Assert.Equal(incoming.PerkPlanTemplate, r.PerkPlanTemplate);
    }

    [Fact]
    public void ImportPackAppendsToRoleOrder()
    {
        var incoming = PackRole("Archer", false);
        var pack = new RolePack(null, [incoming]);

        var imported = Service.ImportPack(pack, [incoming], "AB3D9F2K");

        Assert.Equal(new[] { imported[0].Id }, AppConfig.Instance.RoleOrder);
        Assert.Single(Service.GetAll());
    }

    [Fact]
    public void ImportPackOnlyImportsSelectedSubset()
    {
        var tank = PackRole("Tank", true);
        var archer = PackRole("Archer", false);
        var pack = new RolePack(null, [tank, archer]);

        var imported = Service.ImportPack(pack, [tank], "AB3D9F2K");

        Assert.Single(imported);
        Assert.Equal("Tank", imported[0].Name);
        Assert.Single(Service.GetAll());
    }

    [Fact]
    public void ImportPackToleratesDuplicateNames()
    {
        var existing = Service.Add("Tank", true);
        var incoming = PackRole("Tank", true);
        var pack = new RolePack(null, [incoming]);

        var imported = Service.ImportPack(pack, [incoming], "AB3D9F2K");

        Assert.Equal(2, Service.GetAll().Count);
        Assert.NotEqual(existing.Id, imported[0].Id);
    }

    [Fact]
    public void ImportPackWithEmptySelectionImportsNothing()
    {
        var incoming = PackRole("Tank", true);
        var pack = new RolePack(null, [incoming]);

        var imported = Service.ImportPack(pack, [], "AB3D9F2K");

        Assert.Empty(imported);
        Assert.Empty(Service.GetAll());
    }

    // ---- provenance / update-in-place ----------------------------------------

    [Fact]
    public void ImportPackRecordsProvenance()
    {
        var incoming = PackRole("Tank", true);
        var pack = new RolePack(null, [incoming]);

        var imported = Service.ImportPack(pack, [incoming], "AB3D9F2K")[0];

        Assert.Equal("AB3D9F2K", imported.SourcePackCode);
        Assert.Equal(incoming.Id, imported.SourceRoleId);
    }

    [Fact]
    public void ReimportFromSameCodeUpdatesInPlace()
    {
        var v1 = PackRole("Tank", true);
        var first = Service.ImportPack(new RolePack(null, [v1]), [v1], "AB3D9F2K")[0];

        var v2 = PackRole("Tank v2", false);
        v2.Id = v1.Id; // sender's role id is stable across pack updates
        v2.TargetStats = [9, 9, 9, 9, 9, 9, 9, 9];
        var second = Service.ImportPack(new RolePack(null, [v2]), [v2], "AB3D9F2K")[0];

        // Same local role, updated content, no duplicate, order untouched.
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Tank v2", Service.GetById(first.Id)!.Name);
        Assert.False(Service.GetById(first.Id)!.Frontline);
        Assert.Equal(v2.TargetStats, Service.GetById(first.Id)!.TargetStats);
        Assert.Single(Service.GetAll());
        Assert.Equal(new[] { first.Id }, AppConfig.Instance.RoleOrder);
        // Provenance survives the in-place update.
        Assert.Equal("AB3D9F2K", second.SourcePackCode);
        Assert.Equal(v1.Id, second.SourceRoleId);
    }

    [Fact]
    public void ReimportFromDifferentCodeCreatesDuplicate()
    {
        var incoming = PackRole("Tank", true);
        Service.ImportPack(new RolePack(null, [incoming]), [incoming], "AB3D9F2K");
        Service.ImportPack(new RolePack(null, [incoming]), [incoming], "ZZ3D9F2K");

        Assert.Equal(2, Service.GetAll().Count);
    }

    [Fact]
    public void ImportWithNullCodeNeverMatchesAndRecordsNoPackCode()
    {
        var incoming = PackRole("Tank", true);
        var a = Service.ImportPack(new RolePack(null, [incoming]), [incoming], null)[0];
        var b = Service.ImportPack(new RolePack(null, [incoming]), [incoming], null)[0];

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, Service.GetAll().Count);
        Assert.Null(a.SourcePackCode);
    }

    [Fact]
    public void FindBySourceMatchesOnlyExactPair()
    {
        var incoming = PackRole("Tank", true);
        var imported = Service.ImportPack(
            new RolePack(null, [incoming]), [incoming], "AB3D9F2K")[0];

        Assert.Equal(imported, Service.FindBySource("AB3D9F2K", incoming.Id));
        Assert.Null(Service.FindBySource("ZZ3D9F2K", incoming.Id));
        Assert.Null(Service.FindBySource("AB3D9F2K", "other-sender-id"));
        Assert.Null(Service.FindBySource(null, incoming.Id));
        Assert.Null(Service.FindBySource("AB3D9F2K", null));
    }

    [Fact]
    public void ProvenancePersistsAcrossReload()
    {
        var incoming = PackRole("Tank", true);
        var imported = Service.ImportPack(
            new RolePack(null, [incoming]), [incoming], "AB3D9F2K")[0];

        Service = RoleService.CreateForTest(RolesFile);

        var reloaded = Service.GetById(imported.Id)!;
        Assert.Equal("AB3D9F2K", reloaded.SourcePackCode);
        Assert.Equal(incoming.Id, reloaded.SourceRoleId);
    }

    // ---- data-compat nail: a roles.json written by the Java app reads back intact ----

    [Fact]
    public void ReadsJavaShapedRolesJsonWithProvenance()
    {
        // Hand-written in the exact shape the Java side (Jackson pretty printer) produces,
        // including the provenance fields — the C# side must read them all back.
        File.WriteAllText(RolesFile, """
        {
          "version" : 1,
          "roles" : [ {
            "id" : "0d0a1b2c-3d4e-4f50-8192-a3b4c5d6e7f8",
            "name" : "Imported Tank",
            "frontline" : false,
            "targetStats" : [ 80, 0, 11, 0, 30, 0, 0, 0 ],
            "priority" : [ 1, 3, 2, 3, 3, 3, 3, 3 ],
            "perkPlanTemplate" : {
              "00000001" : "PLANNED",
              "00000002" : "OPTIONAL"
            },
            "sourcePackCode" : "AB3D9F2K",
            "sourceRoleId" : "sender-uuid-7"
          } ]
        }
        """);

        var svc = RoleService.CreateForTest(RolesFile);
        var r = svc.GetById("0d0a1b2c-3d4e-4f50-8192-a3b4c5d6e7f8");
        Assert.NotNull(r);
        Assert.Equal("Imported Tank", r.Name);
        Assert.False(r.Frontline);
        Assert.Equal(new[] { 80, 0, 11, 0, 30, 0, 0, 0 }, r.TargetStats);
        Assert.Equal(new[] { 1, 3, 2, 3, 3, 3, 3, 3 }, r.Priority);
        Assert.Equal(
            new Dictionary<string, string> { ["00000001"] = "PLANNED", ["00000002"] = "OPTIONAL" },
            r.PerkPlanTemplate);
        Assert.Equal("AB3D9F2K", r.SourcePackCode);
        Assert.Equal("sender-uuid-7", r.SourceRoleId);
    }
}
