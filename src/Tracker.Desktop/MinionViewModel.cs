using Tracker.Core;

namespace Tracker.Desktop;

public sealed record MinionViewModel(
    string Position,
    string Name,
    string Attack,
    string Health,
    string Tier,
    string Keywords,
    string Race,
    bool IsGolden,
    string Detail,
    CardInfo? Card,
    string TeammateHint = "")
{
    /// <summary>Klíčová slova se na kartičce ukazují jen tehdy, když nějaká jsou.</summary>
    public bool HasKeywords => Keywords.Length > 0;

    /// <summary>Typ minionu se na kartičce ukazuje v pásce dole, jako ve hře.</summary>
    public bool HasRace => Race.Length > 0;

    /// <summary>Duos: karta by spoluhráči složila pár nebo triple; mimo Duos vždy prázdné.</summary>
    public bool HasTeammateHint => TeammateHint.Length > 0;

    public static MinionViewModel From(BoardMinion minion) => new(
        minion.ZonePosition.ToString(),
        minion.Name,
        StatFormat.Compact(minion.Attack),
        StatFormat.Compact(minion.Health),
        minion.TechLevel?.ToString() ?? "—",
        minion.Keywords,
        minion.Race is { } race ? MinionRace.Display(race) : string.Empty,
        minion.IsGolden,
        minion.TeammateHint.Length > 0
            ? $"Pozice {minion.ZonePosition} · {minion.TeammateHint}"
            : $"Pozice {minion.ZonePosition}",
        CardCache.Shared.Get(minion.CardId),
        minion.TeammateHint);
}
