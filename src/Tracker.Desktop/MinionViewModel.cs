using Tracker.Core;

namespace Tracker.Desktop;

public sealed record MinionViewModel(
    string Position,
    string Name,
    string Attack,
    string Health,
    string Tier,
    string Keywords,
    bool IsGolden,
    string Detail,
    CardArt? Art)
{
    /// <summary>Řádek klíčových slov na kartičce zabírá místo, tak se u karty bez nich vynechá.</summary>
    public bool HasKeywords => Keywords.Length > 0;

    public static MinionViewModel From(BoardMinion minion) => new(
        minion.ZonePosition.ToString(),
        minion.Name,
        minion.Attack?.ToString() ?? "—",
        minion.Health?.ToString() ?? "—",
        minion.TechLevel?.ToString() ?? "—",
        minion.Keywords,
        minion.IsGolden,
        Describe(minion),
        CardArtCache.Shared.Get(minion.CardId));

    /// <summary>
    /// Doplněk pod velkou kartou: typ minionu, pozice a klíčová slova celá. Na kartičce se
    /// klíčová slova vejdou jen oříznutá, protože její šířka je pevná.
    /// </summary>
    private static string Describe(BoardMinion minion)
    {
        var parts = new List<string>(4);
        if (minion.IsGolden)
        {
            parts.Add("Zlatá");
        }

        if (minion.Race is { } race)
        {
            parts.Add(MinionRace.Display(race));
        }

        parts.Add($"Pozice {minion.ZonePosition}");
        if (minion.Keywords.Length > 0)
        {
            parts.Add(minion.Keywords);
        }

        return string.Join(" · ", parts);
    }
}
