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
