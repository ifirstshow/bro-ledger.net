using BroLedger.Core.Parsing;
using Xunit;

namespace BroLedger.Core.Tests.Parsing;

public class BrotherLocatorTests
{
    private const string Sig = "000000000000007d2c10000000000000d4c4a7e9000100000102000000";

    // 兄弟数字节位于签名匹配起点前 38 hex 字符处（对齐 Java parseBrotherCount 语义）
    private static string TwoBrotherHex() =>
        "02" + new string('F', 36) + Sig + Sig + new string('A', 20);

    [Fact]
    public void CountBrothers_ReadsCountByteBeforeSignature()
    {
        // 匹配点（38）前 38 hex 字符处 = "02"（1 字节兄弟数）
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
        var hex = "01" + new string('F', 36) + Sig.ToLowerInvariant();
        Assert.Equal(new List<int> { 96 }, BrotherLocator.FindOffsets(hex));
    }

    [Fact]
    public void FindOffsets_CountExceedingMatches_StopsAtMatches()
    {
        // 计数=5 但只有 1 个签名 → 只返回 1 个偏移
        var hex = "05" + new string('F', 36) + Sig;
        Assert.Single(BrotherLocator.FindOffsets(hex));
    }
}
