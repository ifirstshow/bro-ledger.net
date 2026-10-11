using BroLedger.Core.Models;
using BroLedger.Core.Services;
using BroLedger.Core.Tests.Helpers;

namespace BroLedger.Core.Tests.Services;

// Transcribed test-by-test from JAVATEST/service/RoleShareServiceTest.java.
// Seams: Java com.sun.net.httpserver.HttpServer → HttpTestServer (route per
// method+path, one HttpTestServer per test); createForTest/resetInstance →
// CreateForTest/ResetInstance; NoSuchElementException → KeyNotFoundException;
// IllegalArgumentException → ArgumentException.
// Collection: CreateForTest/ResetInstance mutate the static RoleShareService
// singleton — tests here must run sequentially.
[Collection("RoleShareService")]
public class RoleShareServiceTests : IDisposable
{
    public void Dispose() => RoleShareService.ResetInstance();

    [Fact]
    public void PublishReturnsCodeAndOwnerTokenFromServerResponse()
    {
        using var server = new HttpTestServer();
        server.Route("POST", "/packs", _ => (200, "{\"code\":\"AB3D9F2K\",\"ownerToken\":\"cafe0123\"}"));
        var service = RoleShareService.CreateForTest(server.Url);
        var role = new Role { Id = "x", Name = "Tank" };

        var result = service.Publish(new RolePack("Pack", [role]));

        Assert.Equal("AB3D9F2K", result.Code);
        Assert.Equal("cafe0123", result.OwnerToken);
    }

    [Fact]
    public void PublishToleratesMissingOwnerToken()
    {
        // An older server that predates updates returns only the code.
        using var server = new HttpTestServer();
        server.Route("POST", "/packs", _ => (200, "{\"code\":\"AB3D9F2K\"}"));
        var service = RoleShareService.CreateForTest(server.Url);

        var result = service.Publish(new RolePack("Pack", []));

        Assert.Equal("AB3D9F2K", result.Code);
        Assert.Null(result.OwnerToken);
    }

    [Fact]
    public void UpdateSendsPutWithOwnerTokenHeader()
    {
        string? seenMethod = null, seenToken = null;
        using var server = new HttpTestServer();
        server.Route("PUT", "/packs/AB3D9F2K", r =>
        {
            seenMethod = r.Method;
            seenToken = r.OwnerToken;
            return (200, "{\"code\":\"AB3D9F2K\"}");
        });
        var service = RoleShareService.CreateForTest(server.Url);

        service.Update("ab3d9f2k", "cafe0123", new RolePack("Pack", []));

        Assert.Equal("PUT", seenMethod);
        Assert.Equal("cafe0123", seenToken);
    }

    [Fact]
    public void UpdateMapsForbiddenToFriendlyError()
    {
        using var server = new HttpTestServer();
        server.Route("PUT", "/packs/AB3D9F2K", _ => (403, ""));
        var service = RoleShareService.CreateForTest(server.Url);

        var ex = Assert.Throws<IOException>(
            () => service.Update("AB3D9F2K", "wrong", new RolePack(null, [])));
        Assert.Contains("can't be updated", ex.Message);
    }

    [Fact]
    public void UpdateMapsNotFoundToNoSuchElement()
    {
        using var server = new HttpTestServer();
        server.Route("PUT", "/packs/AB3D9F2K", _ => (404, ""));
        var service = RoleShareService.CreateForTest(server.Url);

        var ex = Assert.Throws<KeyNotFoundException>(
            () => service.Update("AB3D9F2K", "cafe0123", new RolePack(null, [])));
        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public void UpdateRejectsMissingTokenOrBadCodeWithoutNetworkCall()
    {
        // Deliberately unroutable base URL — a network attempt would fail differently.
        var service = RoleShareService.CreateForTest("http://127.0.0.1:1");
        Assert.Throws<ArgumentException>(
            () => service.Update("AB3D9F2K", null, new RolePack(null, [])));
        Assert.Throws<ArgumentException>(
            () => service.Update("AB3D9F2K", "", new RolePack(null, [])));
        Assert.Throws<ArgumentException>(
            () => service.Update("bad code", "cafe0123", new RolePack(null, [])));
    }

    [Fact]
    public void FetchReturnsDeserializedPack()
    {
        using var server = new HttpTestServer();
        server.Route("GET", "/packs/AB3D9F2K", _ => (200,
            "{\"schemaVersion\":1,\"label\":\"Pack\",\"roles\":[{\"id\":\"x\",\"name\":\"Tank\",\"frontline\":true," +
            "\"targetStats\":[0,0,0,0,0,0,0,0],\"priority\":[3,3,3,3,3,3,3,3]}]}"));
        var service = RoleShareService.CreateForTest(server.Url);

        var pack = service.Fetch("AB3D9F2K");

        Assert.Equal("Pack", pack.Label);
        Assert.Single(pack.Roles);
        Assert.Equal("Tank", pack.Roles[0].Name);
    }

    [Fact]
    public void FetchThrowsOnNotFound()
    {
        using var server = new HttpTestServer();
        server.Route("GET", "/packs/ZZZZZZZZ", _ => (404, ""));
        var service = RoleShareService.CreateForTest(server.Url);

        Assert.Throws<KeyNotFoundException>(() => service.Fetch("ZZZZZZZZ"));
    }

    [Fact]
    public void FetchNormalizesCaseAndWhitespace()
    {
        using var server = new HttpTestServer();
        server.Route("GET", "/packs/AB3D9F2K", _ => (200, "{\"schemaVersion\":1,\"roles\":[]}"));
        var service = RoleShareService.CreateForTest(server.Url);

        var pack = service.Fetch("  ab3d9f2k  ");
        Assert.NotNull(pack);
    }

    [Fact]
    public void FetchRejectsMalformedCodeWithoutNetworkCall()
    {
        // Deliberately unroutable base URL — a network attempt would fail differently.
        var service = RoleShareService.CreateForTest("http://127.0.0.1:1");
        Assert.Throws<ArgumentException>(() => service.Fetch("bad code/../x"));
        Assert.Throws<ArgumentException>(() => service.Fetch(""));
        Assert.Throws<ArgumentException>(() => service.Fetch(null));
    }

    [Fact]
    public void NormalizeCodeAcceptsValidRejectsInvalid()
    {
        Assert.Equal("AB3D9F2K", RoleShareService.NormalizeCode(" ab3d9f2k "));
        Assert.Equal("ZZZZZZZZ", RoleShareService.NormalizeCode("zzzzzzzz"));
        Assert.Null(RoleShareService.NormalizeCode(null));
        Assert.Null(RoleShareService.NormalizeCode(""));
        Assert.Null(RoleShareService.NormalizeCode("AB3D9F2"));      // too short
        Assert.Null(RoleShareService.NormalizeCode("AB3D9F2KX"));    // too long
        Assert.Null(RoleShareService.NormalizeCode("AB3D9F0K"));     // 0 not in alphabet
        Assert.Null(RoleShareService.NormalizeCode("AB3D9FIK"));     // I not in alphabet
        Assert.Null(RoleShareService.NormalizeCode("AB3D 9F2K"));    // inner whitespace
    }

    [Fact]
    public void FetchSanitizesHostilePackContents()
    {
        using var server = new HttpTestServer();
        server.Route("GET", "/packs/AB3D9F2K", _ => (200,
            "{\"schemaVersion\":1,\"roles\":[null,{\"id\":\"x\",\"name\":null}]}"));
        server.Route("GET", "/packs/CD3D9F2K", _ => (200, "{\"schemaVersion\":1,\"roles\":null}"));
        var service = RoleShareService.CreateForTest(server.Url);

        var withNullEntry = service.Fetch("AB3D9F2K");
        Assert.Single(withNullEntry.Roles);                        // null element dropped
        Assert.Equal("", withNullEntry.Roles[0].Name);             // missing name defaulted

        var withNullList = service.Fetch("CD3D9F2K");
        Assert.NotNull(withNullList.Roles);
        Assert.Empty(withNullList.Roles);
    }

    // ---- sanitize (direct) --------------------------------------------------

    [Fact]
    public void SanitizeNormalizesStatArrays()
    {
        var wrongLengths = new Role { Name = "A", TargetStats = [1, 2], Priority = [1] };
        var badValues = new Role
        {
            Name = "B",
            TargetStats = [-5, 0, 1, 2, 3, 4, 5, 1000],   // negative + oversized thresholds
            Priority = [0, 9, 2, 3, 1, -1, 3, 3],         // out-of-range priorities
        };

        var pack = SanitizedPackOf(wrongLengths, badValues);

        Assert.Equal(new int[8], pack.Roles[0].TargetStats);
        Assert.Equal(new[] { 3, 3, 3, 3, 3, 3, 3, 3 }, pack.Roles[0].Priority);

        Assert.Equal(new[] { 0, 0, 1, 2, 3, 4, 5, 999 }, pack.Roles[1].TargetStats);
        Assert.Equal(new[] { 1, 3, 2, 3, 1, 1, 3, 3 }, pack.Roles[1].Priority);
    }

    [Fact]
    public void SanitizeTruncatesOverLongName()
    {
        var r = new Role { Name = new string('X', 200) };
        var pack = SanitizedPackOf(r);
        Assert.Equal(60, pack.Roles[0].Name!.Length);
    }

    [Fact]
    public void SanitizeFiltersPerkPlanToValidEntriesOnly()
    {
        var r = new Role { Name = "A" };
        // Java also inserts a null key — impossible in a .NET Dictionary, and STJ can't
        // produce one from JSON either; the null-key guard still lives in the service.
        r.PerkPlanTemplate = new Dictionary<string, string>
        {
            ["AAAA1111"] = "PLANNED",
            ["BBBB2222"] = "OPTIONAL",
            ["CCCC3333"] = "NOT_A_VALID_STATUS",
        };

        var pack = SanitizedPackOf(r);
        var cleaned = pack.Roles[0].PerkPlanTemplate;
        Assert.NotNull(cleaned);
        Assert.Equal(
            new Dictionary<string, string> { ["AAAA1111"] = "PLANNED", ["BBBB2222"] = "OPTIONAL" },
            cleaned);
    }

    [Fact]
    public void SanitizeDropsPerkPlanEntirelyWhenNothingValidSurvives()
    {
        var r = new Role { Name = "A" };
        r.PerkPlanTemplate = new Dictionary<string, string> { ["AAAA1111"] = "BOGUS" };

        var pack = SanitizedPackOf(r);
        Assert.Null(pack.Roles[0].PerkPlanTemplate);
    }

    [Fact]
    public void SanitizeCapsPerkPlanEntryCount()
    {
        var r = new Role { Name = "A" };
        var plan = new Dictionary<string, string>();
        for (var i = 0; i < 150; i++)
        {
            plan["PERK" + i] = i % 2 == 0 ? "PLANNED" : "OPTIONAL";
        }
        r.PerkPlanTemplate = plan;

        var pack = SanitizedPackOf(r);
        var cleaned = pack.Roles[0].PerkPlanTemplate;
        Assert.NotNull(cleaned);
        Assert.Equal(100, cleaned.Count);
    }

    [Fact]
    public void SanitizeRejectsNewerSchemaVersion()
    {
        var pack = new RolePack("Future", []) { PackSchemaVersion = RolePack.SchemaVersion + 1 };
        var ex = Assert.Throws<IOException>(() => RoleShareService.Sanitize(pack));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void SanitizeTruncatesOversizedPackAndFlagsIt()
    {
        var many = new List<Role>();
        for (var i = 0; i < 150; i++)
        {
            many.Add(new Role { Name = "Role " + i });
        }
        var pack = RoleShareService.Sanitize(new RolePack(null, many));

        Assert.Equal(100, pack.Roles.Count);
        Assert.True(pack.Truncated());

        var small = SanitizedPackOf(new Role());
        Assert.False(small.Truncated());
    }

    private static RolePack SanitizedPackOf(params Role[] roles)
    {
        return RoleShareService.Sanitize(new RolePack(null, [.. roles]));
    }
}
