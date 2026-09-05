using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Testy režimu Duos podle tvarů řádků odpozorovaných v pěti skutečných čtyřtýmových lozích.
/// Dvojice nejsou tvořené sousedními sloty, sdílí jednu zásobu životů a
/// <c>PLAYER_LEADERBOARD_PLACE</c> nese umístění týmu. V souboji se na téže straně desky
/// vystřídají oba hrdinové dvojice: první bojuje, druhý se přidá, až padne některá z desek.
/// </summary>
public sealed class DuosTests
{
    private const string GameState = "D 23:32 GameState.DebugPrintPower() - ";

    private static GameStateTracker Replay(params string[] lines)
    {
        var parser = new PowerLogParser();
        var tracker = new GameStateTracker();
        Apply(tracker, lines);
        return tracker;
    }

    private static void Apply(GameStateTracker tracker, params string[] lines)
    {
        var parser = new PowerLogParser();
        foreach (var line in lines)
        {
            tracker.Apply(parser.Parse(line));
        }
    }

    /// <summary>
    /// Soubojová kopie hrdiny, jak ji hra vyrobí na začátku souboje nebo ve chvíli, kdy se
    /// hrdina do souboje přidá. Slot nese v <c>PLAYER_ID</c>, kopii značí
    /// <c>BACON_COMBAT_PHASE_HERO</c>.
    /// </summary>
    private static string[] CombatHero(int entityId, int playerId, string name, int controller) =>
    [
        GameState + $"FULL_ENTITY - Creating ID={entityId} CardID=BG31_HERO_10{playerId}",
        GameState + $"    tag=CONTROLLER value={controller}",
        GameState + "    tag=CARDTYPE value=HERO",
        GameState + $"    tag=PLAYER_ID value={playerId}",
        GameState + "    tag=ZONE value=PLAY",
        GameState + "    tag=BACON_COMBAT_PHASE_HERO value=1",
        GameState + $"FULL_ENTITY - Updating [entityName={name} id={entityId} zone=PLAY zonePos=0 cardId=BG31_HERO_10{playerId} player={controller}] CardID=BG31_HERO_10{playerId}"
    ];

    /// <summary>Minion na první pozici desky dané strany.</summary>
    private static string[] Minion(int entityId, string name, int controller) =>
    [
        GameState + $"FULL_ENTITY - Creating ID={entityId} CardID=BGS_{entityId}",
        GameState + $"    tag=CONTROLLER value={controller}",
        GameState + "    tag=CARDTYPE value=MINION",
        GameState + "    tag=ZONE value=PLAY",
        GameState + "    tag=ZONE_POSITION value=1",
        GameState + "    tag=ATK value=3",
        GameState + "    tag=HEALTH value=3",
        GameState + $"FULL_ENTITY - Updating [entityName={name} id={entityId} zone=PLAY zonePos=1 cardId=BGS_{entityId} player={controller}] CardID=BGS_{entityId}"
    ];

    private static string Died(int entityId, string name, int controller) =>
        GameState + $"TAG_CHANGE Entity=[entityName={name} id={entityId} zone=PLAY zonePos=1 cardId=BGS_{entityId} player={controller}] tag=ZONE value=GRAVEYARD";

    /// <summary>Hrdina jednoho slotu i s jeho týmem, jak ho hra vypíše v <c>FULL_ENTITY</c>.</summary>
    private static string[] Hero(int entityId, int playerId, int teamId, string name, int health) =>
    [
        GameState + $"FULL_ENTITY - Creating ID={entityId} CardID=BG3{teamId}_HERO_00{playerId}",
        GameState + "    tag=CONTROLLER value=15",
        GameState + "    tag=CARDTYPE value=HERO",
        GameState + $"    tag=PLAYER_ID value={playerId}",
        GameState + $"    tag=HEALTH value={health}",
        GameState + "    tag=ZONE value=PLAY",
        GameState + $"    tag=BACON_DUO_TEAM_ID value={teamId}",
        GameState + $"FULL_ENTITY - Updating [entityName={name} id={entityId} zone=PLAY zonePos=0 cardId=BG3{teamId}_HERO_00{playerId} player=15] CardID=BG3{teamId}_HERO_00{playerId}"
    ];

    /// <summary>
    /// Lobby čtyř dvojic. Týmy záměrně nejdou po sousedních slotech, protože ve skutečném logu
    /// tvořily tým sloty 3 a 8.
    /// </summary>
    private static string[] Lobby() =>
    [
        GameState + "CREATE_GAME",
        GameState + "    Player EntityID=2 PlayerID=1 GameAccountId=[hi=144115198130930503 lo=53370550]",
        GameState + "        tag=BACON_DUO_TEAMMATE_PLAYER_ID value=2",
        GameState + "        tag=BACON_DUO_TEAM_ID value=2",
        GameState + "    Player EntityID=3 PlayerID=9 GameAccountId=[hi=0 lo=0]",
        "D 23:32 GameState.DebugPrintGame() - PlayerID=1, PlayerName=Hráč#21600",
        "D 23:32 GameState.DebugPrintGame() - PlayerID=9, PlayerName=Protihráč",
        .. Hero(107, 1, 2, "Forest Lord Cenarius", 28),
        .. Hero(124, 2, 2, "Nozdormu", 28),
        .. Hero(139, 3, 4, "A. F. Kay", 30),
        .. Hero(154, 8, 4, "Cookie the Cook", 30),
        .. Hero(169, 4, 3, "Murozond", 20),
        .. Hero(184, 5, 3, "Arch-Villain Rafaam", 20),
        .. Hero(199, 6, 1, "Snake Eyes", 10),
        .. Hero(214, 7, 1, "Artanis", 10),
        GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=107"
    ];

    [Fact]
    public void RecognisesDuosAndPairsPlayersIntoTeams()
    {
        var state = Replay(Lobby()).State;

        Assert.True(state.IsDuos);
        Assert.Equal(4, state.PlaceCount);
        Assert.Equal(2, state.TeammatePlayerId);

        // Vlastní tým hra napíše na entitu hráče, ne na hrdinu; musí se doplnit oběma.
        Assert.Equal(2, state.Slot(1)?.TeamId);
        Assert.Equal(2, state.Slot(2)?.TeamId);
        Assert.True(state.Slot(2)?.IsTeammate);
        Assert.False(state.Slot(1)?.IsTeammate);

        // Dvojice nejsou sousední sloty, takže se musí brát z tagu, ne odvozovat z čísel.
        Assert.Equal([3, 8], state.Teams.Single(team => team.Any(m => m.PlayerId == 3))
            .Select(member => member.PlayerId));
    }

    [Fact]
    public void OrdersTeamsByTheirCombinedHealthAndKeepsPairsTogether()
    {
        var state = Replay(Lobby()).State;

        Assert.Equal(
            [[3, 8], [1, 2], [4, 5], [6, 7]],
            state.Teams.Select(team => team.Select(member => member.PlayerId).ToArray()));

        // Lokální hráč jde ve své dvojici první a místo se počítá týmu, ne hráči.
        Assert.Equal(2, state.LocalPlace);
        Assert.True(state.Standings[2].IsLocal);
    }

    [Fact]
    public void GivesOneBattleTagToOnlyOneSlot()
    {
        // V Duos přepíná hra HERO_ENTITY jedné entity hráče mezi oběma hrdiny týmu. Bez obrany
        // by jméno lokálního hráče obsadilo i slot spoluhráče a skutečná jména by se ztratila.
        var state = Replay([
            .. Lobby(),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=124",
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=139",
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=154"
        ]).State;

        Assert.Equal("Hráč#21600", state.Slot(1)?.BattleTag);
        Assert.Null(state.Slot(2)?.BattleTag);
        Assert.Equal("Soupeř", state.Slot(3)?.BattleTag);
        Assert.Null(state.Slot(8)?.BattleTag);
    }

    [Fact]
    public void KeepsTheLocalIdentityOnItsOwnSlotEvenWhenTheTeammateFightsFirst()
    {
        // Když spoluhráč bojuje první, přijde HERO_ENTITY na jeho hrdinu dřív než na vlastního.
        // Bez obrany by spoluhráč zabral jméno, slot i živou desku lokálního hráče.
        var state = Replay([
            .. Lobby()[..^1],
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=124",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=107"
        ]).State;

        Assert.Equal(1, state.LocalPlayerSlot);
        Assert.True(state.Slot(1)?.IsLocal);
        Assert.False(state.Slot(2)?.IsLocal);
        Assert.Equal("Hráč#21600", state.Slot(1)?.BattleTag);
        Assert.Null(state.Slot(2)?.BattleTag);
    }

    [Fact]
    public void NeverShowsTheSameBattleTagOnTwoSlots()
    {
        // Slot smí jméno změnit, když se dřív chytlo špatné. Uvolněné jméno se pak musí dát
        // přiřadit jinam a hlavně nesmí zůstat viset na dvou řádcích tabulky zároveň.
        var state = Replay([
            .. Lobby(),
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=139",
            GameState + "TAG_CHANGE Entity=Jiný tag=HERO_ENTITY value=139",
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=154"
        ]).State;

        Assert.Equal("Jiný", state.Slot(3)?.BattleTag);
        Assert.Equal("Soupeř", state.Slot(8)?.BattleTag);

        var tags = state.LobbyParticipants
            .Select(participant => participant.BattleTag)
            .Where(tag => tag is not null)
            .ToArray();
        Assert.Equal(tags.Length, tags.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NamesBothOpponentsAndKeepsTheFirstDamageValue()
    {
        var tracker = Replay([
            .. Lobby(),
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=139",
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=7",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=NEXT_OPPONENT_PLAYER_ID value=3",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=NEXT_OPPONENT_TEAMMATE_PLAYER_ID value=8",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=0",
            // Když lokální hráč bojoval první, zapíše hra tag dvakrát: za soubojovou kopii
            // spoluhráče, která souboj dobojovávala, a znovu za vlastního hrdinu. Druhá hodnota
            // je dvojnásobek; skutečný úbytek životů týmu odpovídá té první.
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=DAMAGE_DEALT_TO_HERO_LAST_TURN value=6",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=DAMAGE_DEALT_TO_HERO_LAST_TURN value=12"
        ]);

        // Bojuje se proti celé dvojici: první z ní nastupuje hned, druhý se přidá, až padne
        // některá z desek. Hláška jmenuje oba hrdinou a nese číslo kola kvůli opožděnému výsledku.
        var events = tracker.State.RecentEvents.ToArray();
        Assert.Equal("Kolo 4 · tým vs A. F. Kay + Cookie the Cook: prohra, dostali jsme 6 dmg.", events[^1]);
        Assert.DoesNotContain(events[..^1], message => message.Contains(" vs ", StringComparison.Ordinal));
        Assert.Equal(6, tracker.State.CombatHistory[^1].DamageTaken);
    }

    [Fact]
    public void AnnouncesTheWholeTeamAtOnceBecauseHealthIsShared()
    {
        var hero = "[entityName=Snake Eyes id=199 zone=PLAY zonePos=0 cardId=BG31_HERO_006 player=15]";
        var mate = "[entityName=Artanis id=214 zone=PLAY zonePos=0 cardId=BG31_HERO_007 player=15]";
        var tracker = Replay([
            .. Lobby(),
            // Tag umístění v okamžiku vyřazení ještě nese živé pořadí z doby, kdy tým žil.
            // Skutečné místo se počítá z týmů, které zůstaly ve hře.
            GameState + $"TAG_CHANGE Entity={hero} tag=PLAYER_LEADERBOARD_PLACE value=2",
            GameState + $"TAG_CHANGE Entity={hero} tag=DAMAGE value=10"
        ]);

        // Dvojice sdílí životy: poškození jednoho hrdiny platí i pro druhého a tým padá naráz.
        Assert.True(tracker.State.Slot(7)?.IsEliminated);
        Assert.Equal(0, tracker.State.Slot(7)?.RemainingHealth);
        Assert.Contains("Tým Snake Eyes + Artanis vypadl na 4. místě.", tracker.State.RecentEvents);

        // Opožděný tag druhého hrdiny už nic dalšího neohlásí.
        Apply(tracker, GameState + $"TAG_CHANGE Entity={mate} tag=DAMAGE value=10");
        Assert.Single(tracker.State.RecentEvents, message => message.Contains("vypadl", StringComparison.Ordinal));
    }

    [Fact]
    public void MirrorsSharedHealthAcrossTheTeam()
    {
        var kay = "[entityName=A. F. Kay id=139 zone=PLAY zonePos=0 cardId=BG34_HERO_003 player=15]";
        var state = Replay([
            .. Lobby(),
            GameState + $"TAG_CHANGE Entity={kay} tag=DAMAGE value=7",
            GameState + $"TAG_CHANGE Entity={kay} tag=ARMOR value=4"
        ]).State;

        // Hra píše stejné hodnoty na oba hrdiny, ale ten druhý je dostane až o kus dál. Do té
        // doby by tabulka ukazovala dva různé stavy jednoho týmu.
        Assert.Equal(23, state.Slot(8)?.EffectiveHealth);
        Assert.Equal(4, state.Slot(8)?.Armor);
    }

    [Fact]
    public void StoresEveryBoardUnderTheHeroWhoFoughtWithIt()
    {
        var tracker = Replay([
            .. Lobby(),
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=5",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=NEXT_OPPONENT_PLAYER_ID value=3",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=NEXT_OPPONENT_TEAMMATE_PLAYER_ID value=8",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            // Spoluhráč bojuje první: hra jeho soubojovou kopii pověsí na entitu lokálního hráče
            // a jeho miniony postaví na lokální stranu desky.
            .. CombatHero(500, 2, "Nozdormu", 1),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=500",
            .. Minion(501, "Sellemental", 1),
            .. CombatHero(600, 3, "A. F. Kay", 9),
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=600",
            .. Minion(601, "Molten Rock", 9),
            GameState + "TAG_CHANGE Entity=GameEntity tag=PROPOSED_ATTACKER value=601"
        ]);

        var state = tracker.State;
        Assert.True(state.IsTeammateFighting);
        Assert.Equal(2, state.CombatLocalSlot);
        Assert.Equal(3, state.CombatOpponentSlot);
        Assert.Equal(["Sellemental"], state.Slot(2)!.LastBoard.Select(minion => minion.Name));
        Assert.Equal(["Molten Rock"], state.Slot(3)!.LastBoard.Select(minion => minion.Name));
        Assert.Empty(state.Slot(1)!.LastBoard);

        // Deska spoluhráče padla a přichází lokální hráč se svou; soupeřova padla a přichází
        // druhý z dvojice. Obě strany se přepnou přes HERO_ENTITY.
        Apply(tracker, [
            Died(501, "Sellemental", 1),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=107",
            .. Minion(502, "Cave Hydra", 1),
            Died(601, "Molten Rock", 9),
            .. CombatHero(700, 8, "Cookie the Cook", 9),
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=700",
            .. Minion(701, "Deflect-o-Bot", 9),
            GameState + "TAG_CHANGE Entity=GameEntity tag=PROPOSED_ATTACKER value=701"
        ]);

        Assert.False(state.IsTeammateFighting);
        Assert.Equal(8, state.CombatOpponentSlot);
        Assert.Equal(["Cave Hydra"], state.Slot(1)!.LastBoard.Select(minion => minion.Name));
        Assert.Equal(["Deflect-o-Bot"], state.Slot(8)!.LastBoard.Select(minion => minion.Name));

        // Desky z první půlky souboje zůstávají tak, jak do něj hrdinové nastoupili.
        Assert.Equal(["Sellemental"], state.Slot(2)!.LastBoard.Select(minion => minion.Name));
        Assert.Equal(["Molten Rock"], state.Slot(3)!.LastBoard.Select(minion => minion.Name));

        Apply(tracker, GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=0");
        Assert.Null(state.CombatLocalSlot);
        Assert.Equal(["Cave Hydra"], state.Slot(1)!.LastBoard.Select(minion => minion.Name));
    }

    [Fact]
    public void TracksWhoFightsFirstAndTheHintsForTheTeammate()
    {
        var state = Replay([
            .. Lobby(),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT value=0",
            GameState + "TAG_CHANGE Entity=[entityName=A. F. Kay id=139 zone=PLAY zonePos=0 cardId=BG34_HERO_003 player=15] tag=BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT value=1",
            .. Minion(417, "Molten Rock", 9),
            // Ikona na kartě v nabídce: tahle by spoluhráči složila pár.
            GameState + "TAG_CHANGE Entity=[entityName=Molten Rock id=417 zone=PLAY zonePos=1 cardId=BGS_417 player=9] tag=BACON_DUO_PAIR_CANDIDATE_TEAMMATE value=1"
        ]).State;

        Assert.False(state.LocalFightsFirst);
        Assert.True(state.Slot(3)?.FightsFirstNextCombat);
        Assert.Equal("pár pro spoluhráče", Assert.Single(state.Shop).TeammateHint);
    }

    [Fact]
    public void AnnouncesACardPassedToTheTeammate()
    {
        var card = "[entityName=Proud Privateer id=14214 zone=HAND zonePos=1 cardId=BG33_825 player=1]";
        var tracker = Replay([
            .. Lobby(),
            GameState + "FULL_ENTITY - Creating ID=14214 CardID=BG33_825",
            GameState + "    tag=CONTROLLER value=1",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=HAND",
            GameState + "    tag=ZONE_POSITION value=1",
            GameState + $"FULL_ENTITY - Updating {card} CardID=BG33_825",
            // Předání spoluhráči: karta dostane IS_USING_PASS_OPTION a odejde z ruky do SETASIDE.
            GameState + $"TAG_CHANGE Entity={card} tag=IS_USING_PASS_OPTION value=1",
            GameState + $"TAG_CHANGE Entity={card} tag=ZONE value=SETASIDE"
        ]);

        Assert.Contains("Předal jsem spoluhráči: Proud Privateer.", tracker.State.RecentEvents);
        Assert.Empty(tracker.State.Hand);
    }
}
