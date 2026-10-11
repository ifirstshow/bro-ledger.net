using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// Transcribed test-by-test from JAVATEST/service/ImageMapServiceTest.java.
// @BeforeAll load → static Svc initialized once (both loads are idempotent).
public class ImageMapServiceTests
{
    private static readonly ImageMapService Svc = Create();

    private static ImageMapService Create()
    {
        var svc = ImageMapService.Instance;
        svc.LoadFromEmbeddedResource();
        return svc;
    }

    // ---- resolveHex (perks / traits / backgrounds) -------------------------

    [Fact]
    public void ResolveHexKnownPerk()
    {
        // 3E7523FA = Fast Adaptation perk
        Assert.Equal("gfx/ui/perks/perk_33.png", Svc.ResolveHex("3E7523FA"));
    }

    [Fact]
    public void ResolveHexKnownTrait()
    {
        // 6FF46EFE = Athletic trait
        Assert.Equal("gfx/ui/traits/trait_icon_21.png", Svc.ResolveHex("6FF46EFE"));
    }

    [Fact]
    public void ResolveHexUnknownReturnsNull()
    {
        Assert.Null(Svc.ResolveHex("00000000"));
    }

    [Fact]
    public void ResolveHexNullInputReturnsNull()
    {
        Assert.Null(Svc.ResolveHex(null));
    }

    [Fact]
    public void ResolveHexCaseInsensitive()
    {
        Assert.Equal(Svc.ResolveHex("3E7523FA"), Svc.ResolveHex("3e7523fa"));
    }

    // ---- resolve (slot + icon, generic items) ------------------------------

    [Fact]
    public void ResolveGenericBodyArmor()
    {
        // slot=body, icon=1 → inventory_body_armor_01.png (archive src path)
        Assert.Equal("gfx/ui/items/armor/inventory_body_armor_01.png",
                Svc.Resolve("body", null, 1, null));
    }

    [Fact]
    public void ResolveUnknownSlotReturnsNull()
    {
        Assert.Null(Svc.Resolve("body", null, 999, null));
    }

    // ---- Resolution priority -----------------------------------------------

    [Fact]
    public void ResolveSlotHexIconBeatsSlotIcon()
    {
        // slot=body, icon=1, hex=BF3413DE (named Southern Mail) overrides generic body:1
        var generic = Svc.Resolve("body", null, 1, null);
        var named = Svc.Resolve("body", "BF3413DE", 1, null);
        Assert.NotNull(named);
        Assert.NotEqual(generic, named);
    }

    [Fact]
    public void ResolveHexDirectBeatsSlotIcon()
    {
        // A hex with a direct mapping returns the archive src path (gfx/ui/perks/…)
        var direct = Svc.ResolveHex("3E7523FA");
        Assert.NotNull(direct);
        Assert.StartsWith("gfx/ui/perks/", direct);
    }

    // ---- resolveAttachment -------------------------------------------------

    [Fact]
    public void ResolveAttachmentKnownEntry()
    {
        // slot=body, attachment=76B57B86 (Bone Platings)
        Assert.Equal("gfx/ui/items/armor_upgrades/inventory_upgrade_06.png",
                Svc.ResolveAttachment("body", "76B57B86"));
    }

    [Fact]
    public void ResolveAttachmentUnknownReturnsNull()
    {
        Assert.Null(Svc.ResolveAttachment("body", "00000000"));
    }

    [Fact]
    public void ResolveAttachmentNullInputReturnsNull()
    {
        Assert.Null(Svc.ResolveAttachment(null, "76B57B86"));
        Assert.Null(Svc.ResolveAttachment("body", null));
    }

    // ---- resolveHexHouse ---------------------------------------------------

    [Fact]
    public void ResolveHexHouseKnownEntry()
    {
        // slot=body, hex=2847F403, house=1 → faction armor variant
        Assert.Equal("gfx/ui/items/armor/inventory_faction_armor_01.png",
                Svc.ResolveHexHouse("body", "2847F403", 1));
    }

    [Fact]
    public void ResolveHexHouseUnknownReturnsNull()
    {
        Assert.Null(Svc.ResolveHexHouse("body", "2847F403", 999));
    }

    // ---- Two hex IDs sharing one image ------------------------------------

    [Fact]
    public void ResolveHexCommaGroupedBothMapped()
    {
        // D6461010 and B6FBBE7B share the same trait icon
        Assert.Equal(Svc.ResolveHex("D6461010"), Svc.ResolveHex("B6FBBE7B"));
        Assert.NotNull(Svc.ResolveHex("D6461010"));
    }
}
