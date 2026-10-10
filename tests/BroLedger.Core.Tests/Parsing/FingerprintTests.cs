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
