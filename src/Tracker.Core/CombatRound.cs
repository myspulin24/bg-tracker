namespace Tracker.Core;

/// <summary>Jeden souboj: proti komu, s jakým výsledkem a za kolik poškození.</summary>
public sealed class CombatRound(int? round, int? opponentPlayerId)
{
    public int? Round { get; } = round;
    public int? OpponentPlayerId { get; } = opponentPlayerId;
    public string? OpponentHeroName { get; internal set; }
    public string? OpponentBattleTag { get; internal set; }

    /// <summary>Druhý ze soupeřící dvojice v režimu Duos.</summary>
    public int? OpponentTeammatePlayerId { get; internal set; }
    public string? OpponentTeammateHeroName { get; internal set; }
    public string? OpponentTeammateBattleTag { get; internal set; }

    /// <summary><c>WON</c>, <c>LOST</c> nebo <c>TIED</c>; <c>null</c>, dokud hra výsledek neoznámí.</summary>
    public string? Outcome { get; internal set; }

    /// <summary>Poškození, které v tomto souboji utrpěl lokální hrdina.</summary>
    public int? DamageTaken { get; internal set; }

    /// <summary>
    /// Poškození, které souboj uštědřil soupeři. Log ho nehlásí zvlášť, počítá se z přírůstku
    /// tagu <c>DAMAGE</c> na soupeřově hrdinovi za dobu souboje.
    /// </summary>
    public int? DamageDealt { get; internal set; }

    /// <summary>Stav soupeřova poškození na začátku souboje, ze kterého se přírůstek počítá.</summary>
    internal int OpponentDamageAtStart { get; set; }
}
