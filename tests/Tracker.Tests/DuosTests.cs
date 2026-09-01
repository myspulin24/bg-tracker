using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Testy režimu Duos podle tvarů řádků odpozorovaných ve skutečném čtyřtýmovém logu.
/// Dvojice nejsou tvořené sousedními sloty a <c>PLAYER_LEADERBOARD_PLACE</c> nese umístění týmu.
/// </summary>
public sealed class DuosTests
{
    private const string GameState = "D 23:32 GameState.DebugPrintPower() - ";

    private static GameStateTracker Replay(params string[] lines)
    {
        var parser = new PowerLogParser();
        var tracker = new GameStateTracker();
        foreach (var line in lines)
        {
            tracker.Apply(parser.Parse(line));
        }

        return tracker;
    }

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
    public void NamesTheFoughtOpponentAndReportsOneCombatOnce()
    {
        var tracker = Replay([
            .. Lobby(),
            GameState + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=139",
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=7",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=NEXT_OPPONENT_PLAYER_ID value=3",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=NEXT_OPPONENT_TEAMMATE_PLAYER_ID value=8",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            // Poškození dorazí na dvakrát, jak dobojuje každý ze spoluhráčů.
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=DAMAGE_DEALT_TO_HERO_LAST_TURN value=6",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=DAMAGE_DEALT_TO_HERO_LAST_TURN value=14"
        ]);

        // Nastupuje se proti jedinému soupeři, druhého z dvojice si bere spoluhráč. V hlášce
        // je proto jen ten, proti komu se doopravdy bojovalo, a číslo kola kvůli opožděnému výsledku.
        var events = tracker.State.RecentEvents.ToArray();
        Assert.Equal("Kolo 4 · souboj s Soupeř: prohra, −14 HP.", events[^1]);
        Assert.DoesNotContain(events[..^1], message => message.Contains("Souboj", StringComparison.Ordinal));
    }

    [Fact]
    public void AnnouncesTheTeamOnlyWhenBothOfItsHeroesAreGone()
    {
        var hero = "[entityName=Snake Eyes id=199 zone=PLAY zonePos=0 cardId=BG31_HERO_006 player=15]";
        var mate = "[entityName=Artanis id=214 zone=PLAY zonePos=0 cardId=BG31_HERO_007 player=15]";
        var tracker = Replay([
            .. Lobby(),
            GameState + $"TAG_CHANGE Entity={hero} tag=PLAYER_LEADERBOARD_PLACE value=4",
            GameState + $"TAG_CHANGE Entity={mate} tag=PLAYER_LEADERBOARD_PLACE value=4",
            GameState + $"TAG_CHANGE Entity={hero} tag=DAMAGE value=10"
        ]);

        Assert.Contains("Snake Eyes vypadl, tým hraje dál.", tracker.State.RecentEvents);

        tracker.Apply(new PowerLogParser().Parse(
            GameState + $"TAG_CHANGE Entity={mate} tag=DAMAGE value=10"));

        Assert.Contains("Tým Snake Eyes + Artanis vypadl na 4. místě.", tracker.State.RecentEvents);
    }
}
