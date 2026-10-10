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
        foreach (Match match in SigRegex().Matches(hex))
        {
            if (offsets.Count >= count) break;
            offsets.Add(match.Index + SigDataAhead);
        }
        return offsets;
    }

    internal static int ParseBrotherCount(string hex, int matchStart) =>
        Convert.ToInt32(hex.Substring(matchStart - SigCountBack, 2), 16);
}
