namespace Tracker.Core;

/// <summary>
/// Průběžně skládaný stav jedné entity z <c>Power.log</c>. Řádky logu přicházejí v nedeterministickém
/// pořadí, proto se každý atribut doplňuje samostatně a nikdy se nepřepisuje na <c>null</c>.
/// </summary>
public sealed class TrackedEntity(int entityId)
{
    public int EntityId { get; } = entityId;
    public string? Name { get; internal set; }
    public string? CardId { get; internal set; }
    public string? CardType { get; internal set; }
    public string? Race { get; internal set; }
    public string? Zone { get; internal set; }
    public int ZonePosition { get; internal set; }

    /// <summary>Hodnota tagu <c>CONTROLLER</c> / <c>player=</c>. V Battlegrounds jde o lokálního hráče nebo sdíleného soupeře.</summary>
    public int? ControllerId { get; internal set; }

    /// <summary>Hodnota tagu <c>PLAYER_ID</c> na hrdinovi, tedy slot 1 až 8 v lobby.</summary>
    public int? LobbyPlayerId { get; internal set; }

    /// <summary>Tým 1 až 4 z tagu <c>BACON_DUO_TEAM_ID</c>; mimo režim Duos zůstává prázdný.</summary>
    public int? DuoTeamId { get; internal set; }

    /// <summary>
    /// Bojuje tenhle hrdina v příštím souboji jako první ze své dvojice? Tag
    /// <c>BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT</c> hra píše na konci každého souboje na
    /// všechny hrdiny lobby i na entitu lokálního hráče. Mimo Duos nepadá.
    /// </summary>
    public bool? FightsFirstNextCombat { get; internal set; }

    /// <summary>
    /// Karta, která by spoluhráči složila pár, respektive triple (tagy
    /// <c>BACON_DUO_PAIR_CANDIDATE_TEAMMATE</c> a <c>BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE</c>).
    /// Hra ji značí v nabídce Boba i v ruce; mimo Duos tagy nepadají.
    /// </summary>
    public bool IsTeammatePairCandidate { get; internal set; }

    public bool IsTeammateTripleCandidate { get; internal set; }

    public int? Attack { get; internal set; }
    public int? Health { get; internal set; }
    public int? Armor { get; internal set; }
    public int? Damage { get; internal set; }
    public int? Cost { get; internal set; }

    /// <summary>Tavern tier minionu (tag <c>TECH_LEVEL</c>).</summary>
    public int? TechLevel { get; internal set; }

    /// <summary>Tavern tier hráče (tag <c>PLAYER_TECH_LEVEL</c>).</summary>
    public int? TavernTier { get; internal set; }

    public int? LeaderboardPlace { get; internal set; }
    public int? Triples { get; internal set; }
    public bool IsHero { get; internal set; }

    /// <summary>
    /// Slot na trinket: 1 malý, 2 velký. Bere se z karty, se kterou entita vznikla, a už se
    /// nemění — po výběru trinketu se totiž tatáž entita přepíše na vybranou kartu a jinak by
    /// se slot nedal poznat.
    /// </summary>
    public int? TrinketSlot { get; internal set; }

    /// <summary>Odpočet kol do výběru trinketu z tagu <c>BACON_TURNS_LEFT_TO_DISCOVER_TRINKET</c>.</summary>
    public int? TrinketTurnsLeft { get; internal set; }

    /// <summary>
    /// Vlastní počítadla karty (<c>TAG_SCRIPT_DATA_NUM_1</c> a <c>_2</c>). Karty typu
    /// „a zlepši tohle“ si v nich drží hodnotu, kterou právě dávají; v textu karty jí odpovídá
    /// zástupný <c>SDN1</c>.
    /// </summary>
    public int? ScriptDataNum1 { get; internal set; }

    public int? ScriptDataNum2 { get; internal set; }

    /// <summary>
    /// Dočasná kopie hrdiny vyrobená jen pro souboj (tag <c>BACON_COMBAT_PHASE_HERO</c>). Má znovu
    /// plné HP bez nasčítaného poškození, takže se z ní nesmí číst stav hráče v lobby.
    /// </summary>
    public bool IsCombatHero { get; internal set; }
    public bool IsGolden { get; internal set; }

    /// <summary>Minion z nabídkového poolu této lobby (tag <c>IS_BACON_POOL_MINION</c>).</summary>
    public bool IsPoolMinion { get; internal set; }

    /// <summary>Trinket (tag <c>BACON_TRINKET</c>); nese ho i nabídka k výběru.</summary>
    public bool IsTrinket { get; internal set; }
    public bool HasTaunt { get; internal set; }
    public bool HasDivineShield { get; internal set; }
    public bool HasReborn { get; internal set; }
    public bool HasVenomous { get; internal set; }
    public bool HasPoisonous { get; internal set; }
    public bool HasWindfury { get; internal set; }
    public string? PlayState { get; internal set; }

    /// <summary>
    /// Generace desky. Battlegrounds po každém přepnutí fáze souboje vyrobí pro miniony nové
    /// entity a ty staré už nikdy nezmíní, takže bez tohoto razítka by deska jen narůstala.
    /// </summary>
    public int Epoch { get; internal set; }

    /// <summary>
    /// Vznikla entita během souboje? Nabídka Boba se plní jen mimo souboj, a tenhle příznak se
    /// na rozdíl od zóny už nikdy nezmění, takže ho pozdější opožděné tagy nerozbijí.
    /// </summary>
    public bool CreatedDuringCombat { get; internal set; }

    /// <summary>
    /// Controller a zóna z okamžiku, kdy entita vznikla. Karta vyrobená efektem se rodí
    /// u svého hráče v SETASIDE a teprve pak se přesune, kdežto nabídka Boba vzniká rovnou
    /// v PLAY na soupeřově straně. Pozdější stav už to nerozliší.
    /// </summary>
    public int? InitialControllerId { get; internal set; }

    public string? InitialZone { get; internal set; }

    public bool IsMinion => string.Equals(CardType, "MINION", StringComparison.OrdinalIgnoreCase);
    public bool IsInPlay => string.Equals(Zone, "PLAY", StringComparison.OrdinalIgnoreCase);
    public bool IsInHand => string.Equals(Zone, "HAND", StringComparison.OrdinalIgnoreCase);
    public int? EffectiveHealth => Health is null ? null : Health - (Damage ?? 0);
}
