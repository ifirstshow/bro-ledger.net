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
        PerkSortMode.Off => "Perk sort: Off",
        PerkSortMode.Commonality => "Perk sort: Commonality",
        PerkSortMode.Tier => "Perk sort: Tier",
        PerkSortMode.TierThenCommon => "Perk sort: Tier+Common",
        _ => throw new UnreachableException($"{mode}"),
    };

    public static PerkSortMode Next(this PerkSortMode mode)
    {
        var values = Enum.GetValues<PerkSortMode>();
        var i = Array.IndexOf(values, mode);
        return values[(i + 1) % values.Length];
    }
}
