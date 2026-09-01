namespace Tracker.Core;

public sealed class LobbyParticipant(int playerId)
{
    /// <summary>Slot 1 až 8 podle tagu <c>PLAYER_ID</c> na entitě hrdiny.</summary>
    public int PlayerId { get; } = playerId;

    public bool IsLocal { get; internal set; }
    public string? BattleTag { get; internal set; }
    public string? HeroName { get; internal set; }
    public string? HeroCardId { get; internal set; }
    public int? Health { get; internal set; }
    public int? Armor { get; internal set; }
    public int? Damage { get; internal set; }
    public int? TavernTier { get; internal set; }

    /// <summary>Živé pořadí z tagu <c>PLAYER_LEADERBOARD_PLACE</c>; po konci hry jde o konečné umístění.</summary>
    public int? LeaderboardPlace { get; internal set; }

    /// <summary>Počet dosažených triplů z tagu <c>PLAYER_TRIPLES</c>.</summary>
    public int? Triples { get; internal set; }

    /// <summary>MMR není v <c>Power.log</c> dostupné; zůstává rezervované v modelu.</summary>
    public int? Mmr { get; internal set; }

    public string? PlayState { get; internal set; }

    /// <summary>
    /// Deska, jak ji hra naposledy ukázala. U soupeřů jde o stav ze souboje proti nim, protože
    /// jinam do logu cizí deska nepronikne. Dokud proti hráči nikdo nehrál, je prázdná.
    /// </summary>
    public IReadOnlyList<BoardMinion> LastBoard { get; internal set; } = [];

    /// <summary>Kolo, ve kterém byla <see cref="LastBoard"/> pořízena.</summary>
    public int? LastBoardRound { get; internal set; }

    public int? EffectiveHealth => Health is null ? null : Health - (Damage ?? 0);

    /// <summary>Zbývající životy včetně armoru. Podle nich se pozná vyřazený hráč.</summary>
    public int? RemainingHealth => EffectiveHealth is null ? null : EffectiveHealth + (Armor ?? 0);

    public bool IsEliminated => RemainingHealth is <= 0;
}
