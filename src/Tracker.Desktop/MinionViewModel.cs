using Tracker.Core;

namespace Tracker.Desktop;

public sealed record MinionViewModel(
    string Position,
    string Name,
    string Stats,
    string Tier,
    string Keywords,
    bool IsGolden)
{
    public static MinionViewModel From(BoardMinion minion) => new(
        minion.ZonePosition.ToString(),
        minion.Name,
        minion.Stats,
        minion.TechLevel is { } tier ? tier.ToString() : "—",
        minion.Keywords,
        minion.IsGolden);
}
