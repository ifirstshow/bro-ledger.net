using System.Diagnostics;
using BroLedger.Core.Services;

namespace BroLedger.App;

/// <summary>
/// UI semantics for Core's <see cref="PerkSortMode"/> — ported from Java ui/PerkSortMode.java
/// (label field + next()). Labels are UI copy, so they live in the App layer; the Core enum
/// itself is untouched.
/// </summary>
internal static class PerkSortModeLabels
{
    public static string Label(this PerkSortMode mode) => mode switch
    {
        PerkSortMode.Off => "天赋排序：关",
        PerkSortMode.Commonality => "天赋排序：共同性",
        PerkSortMode.Tier => "天赋排序：品级",
        PerkSortMode.TierThenCommon => "天赋排序：品级+共性",
        _ => throw new UnreachableException($"{mode}"),
    };

    public static PerkSortMode Next(this PerkSortMode mode)
    {
        var values = Enum.GetValues<PerkSortMode>();
        var i = Array.IndexOf(values, mode);
        return values[(i + 1) % values.Length];
    }
}
