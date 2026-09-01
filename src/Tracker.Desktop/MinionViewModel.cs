using Tracker.Core;

namespace Tracker.Desktop;

public sealed record MinionViewModel(
    string Position,
    string Name,
    string Attack,
    string Health,
    string TierStars,
    string TierTooltip,
    string Keywords,
    bool IsGolden)
{
    public static MinionViewModel From(BoardMinion minion) => new(
        minion.ZonePosition.ToString(),
        minion.Name,
        minion.Attack?.ToString() ?? "—",
        minion.Health?.ToString() ?? "—",
        Stars(minion.TechLevel),
        minion.TechLevel is { } tier ? $"Tavern Tier {tier}" : "Neznámý tavern tier",
        minion.Keywords,
        minion.IsGolden);

    /// <summary>Tavern tier se ukazuje hvězdičkami stejně jako na kartě ve hře.</summary>
    private static string Stars(int? tier) =>
        tier is > 0 and <= 10 ? new string('★', tier.Value) : string.Empty;
}
