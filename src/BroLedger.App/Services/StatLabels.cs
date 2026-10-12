using BroLedger.Core.Models;

namespace BroLedger.App.Services;

/// <summary>
/// 中文显示标签 — 仿 TooltipFactory.StatIcons 的 <see cref="Stat"/> 映射表。<see cref="Stat.DisplayName"/>
/// 保持英文（ExpectedStatsCalculator 的字母序破平局键 + validate 消息属数据逻辑）；UI 显示一律走这里。
/// </summary>
internal static class StatLabels
{
    private static readonly IReadOnlyDictionary<Stat, string> Labels = new Dictionary<Stat, string>
    {
        [Stat.Health] = "生命",
        [Stat.Resolve] = "决心",
        [Stat.Fatigue] = "疲劳",
        [Stat.Initiative] = "主动",
        [Stat.MeleeSkill] = "近攻",
        [Stat.RangedSkill] = "远攻",
        [Stat.MeleeDefense] = "近防",
        [Stat.RangedDefense] = "远防",
    };

    public static string Label(this Stat s) => Labels[s];
}
