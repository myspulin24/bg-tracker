using Tracker.Core;

namespace Tracker.Desktop;

public sealed record MinionViewModel(
    string Position,
    string Name,
    string Attack,
    string Health,
    string Tier,
    string TierTooltip,
    string Keywords,
    bool IsGolden)
{
    public static MinionViewModel From(BoardMinion minion) => new(
        minion.ZonePosition.ToString(),
        minion.Name,
        minion.Attack?.ToString() ?? "—",
        minion.Health?.ToString() ?? "—",
        minion.TechLevel?.ToString() ?? "—",
        minion.TechLevel is { } tier ? $"Tavern Tier {tier}" : "Neznámý tavern tier",
        minion.Keywords,
        minion.IsGolden);
}
