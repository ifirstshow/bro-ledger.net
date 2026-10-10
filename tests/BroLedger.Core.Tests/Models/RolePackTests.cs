using System.Text.Json;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Models;

// Transcribed test-by-test from JAVATEST/model/RolePackTest.java.
// Jackson ObjectMapper here → DataJsonContext source-gen (same shapes on the wire).
public class RolePackTests
{
    [Fact]
    public void RoundTripsThroughStj()
    {
        var role = new Role
        {
            Id = "abc-123",
            Name = "Tank",
            Frontline = true,
            TargetStats = [1, 2, 3, 4, 5, 6, 7, 8],
            Priority = [1, 1, 2, 2, 3, 3, 1, 2],
            PerkPlanTemplate = new Dictionary<string, string> { ["00000001"] = "PLANNED" }
        };

        var pack = new RolePack("My Pack", [role]);

        var json = JsonSerializer.Serialize(pack, DataJsonContext.Default.RolePack);
        var parsed = JsonSerializer.Deserialize(json, DataJsonContext.Default.RolePack);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.PackSchemaVersion);
        Assert.Equal("My Pack", parsed.Label);
        Assert.Single(parsed.Roles);
        var parsedRole = parsed.Roles[0];
        Assert.Equal("Tank", parsedRole.Name);
        Assert.True(parsedRole.Frontline);
        Assert.Equal(role.TargetStats, parsedRole.TargetStats);
        Assert.Equal(role.Priority, parsedRole.Priority);
        Assert.Equal(role.PerkPlanTemplate, parsedRole.PerkPlanTemplate);
    }

    [Fact]
    public void NullLabelIsAllowed()
    {
        var pack = new RolePack(null, []);
        var json = JsonSerializer.Serialize(pack, DataJsonContext.Default.RolePack);
        var parsed = JsonSerializer.Deserialize(json, DataJsonContext.Default.RolePack);

        Assert.NotNull(parsed);
        Assert.Null(parsed.Label);
        Assert.Empty(parsed.Roles);
    }

    [Fact]
    public void TruncatedFlagIsNotSerialized()
    {
        var pack = new RolePack("Pack", []);
        pack.MarkTruncated();
        var json = JsonSerializer.Serialize(pack, DataJsonContext.Default.RolePack);
        Assert.DoesNotContain("truncated", json, StringComparison.Ordinal);
        var parsed = JsonSerializer.Deserialize(json, DataJsonContext.Default.RolePack);
        Assert.NotNull(parsed);
        Assert.False(parsed.Truncated());
    }

    // ---- forSharing ---------------------------------------------------------

    [Fact]
    public void ForSharingCopiesContentAndKeepsId()
    {
        var role = new Role
        {
            Id = "local-uuid",
            Name = "Tank",
            Frontline = false,
            TargetStats = [1, 2, 3, 4, 5, 6, 7, 8],
            Priority = [1, 1, 2, 2, 3, 3, 1, 2],
            PerkPlanTemplate = new Dictionary<string, string> { ["00000001"] = "PLANNED" }
        };

        var pack = RolePack.ForSharing("My Pack", [role]);

        Assert.Equal("My Pack", pack.Label);
        var copy = pack.Roles[0];
        Assert.Equal("local-uuid", copy.Id);
        Assert.Equal("Tank", copy.Name);
        Assert.False(copy.Frontline);
        Assert.Equal(role.TargetStats, copy.TargetStats);
        Assert.Equal(role.Priority, copy.Priority);
        Assert.Equal(role.PerkPlanTemplate, copy.PerkPlanTemplate);
        // Deep copies — mutating the pack never touches the catalogue role.
        Assert.NotSame(role.TargetStats, copy.TargetStats);
        Assert.NotSame(role.PerkPlanTemplate, copy.PerkPlanTemplate);
    }

    [Fact]
    public void ForSharingStripsProvenance()
    {
        var role = new Role
        {
            Id = "local-uuid",
            Name = "Imported Tank",
            SourcePackCode = "AB3D9F2K", // must never leak into a published pack
            SourceRoleId = "sender-uuid"
        };

        var pack = RolePack.ForSharing(null, [role]);

        var copy = pack.Roles[0];
        Assert.Null(copy.SourcePackCode);
        Assert.Null(copy.SourceRoleId);
        var json = JsonSerializer.Serialize(pack, DataJsonContext.Default.RolePack);
        Assert.DoesNotContain("AB3D9F2K", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ForSharingSkipsNullRolesAndToleratesNullList()
    {
        var role = new Role { Id = "x" };
        List<Role?> withNull = [null, role];

        Assert.Single(RolePack.ForSharing(null, withNull).Roles);
        Assert.Empty(RolePack.ForSharing(null, null).Roles);
    }
}
