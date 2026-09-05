using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Testy postavené na tvarech řádků odpozorovaných ve skutečném Battlegrounds <c>Power.log</c>.
/// </summary>
public sealed class BattlegroundsStateTests
{
    private const string GameState = "D 18:24 GameState.DebugPrintPower() - ";
    private const string TaskList = "D 18:24 PowerTaskList.DebugPrintPower() - ";

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

    private static string[] Opening() =>
    [
        GameState + "CREATE_GAME",
        GameState + "    Player EntityID=20 PlayerID=7 GameAccountId=[hi=144115198130930503 lo=53370550]",
        GameState + "    Player EntityID=21 PlayerID=15 GameAccountId=[hi=0 lo=0]",
        "D 18:24 GameState.DebugPrintGame() - PlayerID=7, PlayerName=Hráč#21600",
        "D 18:24 GameState.DebugPrintGame() - PlayerID=15, PlayerName=Protihráč",
        GameState + "FULL_ENTITY - Creating ID=116 CardID=TB_BaconShop_HERO_92",
        GameState + "    tag=CONTROLLER value=7",
        GameState + "    tag=CARDTYPE value=HERO",
        GameState + "    tag=PLAYER_ID value=7",
        GameState + "    tag=HEALTH value=30",
        GameState + "    tag=ARMOR value=10",
        GameState + "    tag=ZONE value=PLAY",
        GameState + "FULL_ENTITY - Updating [entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] CardID=TB_BaconShop_HERO_92",
        GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=116"
    ];

    /// <summary>Hrdina soupeře v lobby, jak ho hra vypíše při startu hry.</summary>
    private static string[] LobbyHero(int entityId, int slot, string name) =>
    [
        GameState + $"FULL_ENTITY - Creating ID={entityId} CardID=BG20_HERO_{slot:000}",
        GameState + "    tag=CONTROLLER value=15",
        GameState + "    tag=CARDTYPE value=HERO",
        GameState + $"    tag=PLAYER_ID value={slot}",
        GameState + "    tag=HEALTH value=30",
        GameState + "    tag=ZONE value=SETASIDE",
        GameState + $"FULL_ENTITY - Updating [entityName={name} id={entityId} zone=SETASIDE zonePos=0 cardId=BG20_HERO_{slot:000} player=15] CardID=BG20_HERO_{slot:000}"
    ];

    private static string HeroTag(int entityId, int slot, string name, string tag, int value) =>
        GameState + $"TAG_CHANGE Entity=[entityName={name} id={entityId} zone=SETASIDE zonePos=0 cardId=BG20_HERO_{slot:000} player=15] tag={tag} value={value}";

    [Fact]
    public void PlacesEliminatedPlayersByWhoIsLeftAndOrdersSameRoundDeathsByRemainingHealth()
    {
        var tracker = Replay([
            .. Opening(),
            .. LobbyHero(131, 1, "Patches the Pirate"),
            .. LobbyHero(132, 2, "Millificent Manastorm"),
            .. LobbyHero(133, 3, "Kurtrus Ashfallen"),
            .. LobbyHero(134, 4, "Shudderwock"),
            .. LobbyHero(135, 5, "Heistbaron Togwaggle"),
            .. LobbyHero(136, 6, "Rock Master Voone"),
            .. LobbyHero(138, 8, "Murloc Holmes"),
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=19",
            // Tag umístění v okamžiku vyřazení ještě nese živé pořadí z doby, kdy hráč žil;
            // naměřeno na hráči vyřazeném jako pátý s dvojkou. Místo se proto počítá z toho,
            // kolik hráčů zůstalo ve hře.
            HeroTag(133, 3, "Kurtrus Ashfallen", "PLAYER_LEADERBOARD_PLACE", 2),
            HeroTag(133, 3, "Kurtrus Ashfallen", "DAMAGE", 30)
        ]);

        Assert.Contains("Kurtrus Ashfallen vypadl na 8. místě.", tracker.State.RecentEvents);

        // Dva pády v jednom kole: výš končí ten, kdo skončil blíž nule, ať tagy DAMAGE přišly
        // v jakémkoli pořadí. Hláška prvního z nich se přepíše na místě.
        var parser = new PowerLogParser();
        foreach (var line in new[]
        {
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=21",
            HeroTag(135, 5, "Heistbaron Togwaggle", "DAMAGE", 31),
            HeroTag(134, 4, "Shudderwock", "DAMAGE", 44)
        })
        {
            tracker.Apply(parser.Parse(line));
        }

        Assert.Contains("Heistbaron Togwaggle vypadl na 6. místě.", tracker.State.RecentEvents);
        Assert.Contains("Shudderwock vypadl na 7. místě.", tracker.State.RecentEvents);
        Assert.DoesNotContain("Heistbaron Togwaggle vypadl na 7. místě.", tracker.State.RecentEvents);
    }

    [Fact]
    public void TracksRoundGoldAndTavernUpgradeCost()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=PLAYER_TECH_LEVEL value=4",
            GameState + "FULL_ENTITY - Creating ID=408 CardID=TB_BaconShopTechUp05_Button",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=COST value=7",
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=9",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=RESOURCES value=6",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=RESOURCES_USED value=4",
            // Tag TURN na entitě hráče počítá jeho vlastní tahy a nesmí přepsat herní kolo.
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TURN value=5"
        ]);

        Assert.Equal(9, tracker.State.Turn);
        Assert.Equal(5, tracker.State.Round);
        Assert.Equal(2, tracker.State.AvailableGold);
        Assert.Equal(7, tracker.State.TavernUpgradeCost);
    }

    [Fact]
    public void ProjectsBoardAndShopSeparatelyAndDropsRemovedCards()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=417 CardID=BGS_127",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=1",
            GameState + "    tag=ATK value=3",
            GameState + "    tag=HEALTH value=3",
            GameState + "    tag=TECH_LEVEL value=1",
            GameState + "FULL_ENTITY - Updating [entityName=Molten Rock id=417 zone=PLAY zonePos=1 cardId=BGS_127 player=15] CardID=BGS_127",
            GameState + "FULL_ENTITY - Creating ID=419 CardID=BGS_119",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=2",
            GameState + "    tag=ATK value=2",
            GameState + "    tag=HEALTH value=1",
            GameState + "FULL_ENTITY - Creating ID=500 CardID=BG26_135",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=1",
            GameState + "    tag=ATK value=4",
            GameState + "    tag=HEALTH value=6",
            GameState + "    tag=TAUNT value=1",
            GameState + "    tag=PREMIUM value=1",
            GameState + "FULL_ENTITY - Updating [entityName=Southsea Busker id=500 zone=PLAY zonePos=1 cardId=BG26_135 player=7] CardID=BG26_135",
            // Descriptor na tomto řádku pořád tvrdí zone=PLAY, i když karta právě mizí ze hry.
            GameState + "TAG_CHANGE Entity=[entityName=Crackling Cyclone id=419 zone=PLAY zonePos=2 cardId=BGS_119 player=15] tag=ZONE value=REMOVEDFROMGAME"
        ]);

        var shop = Assert.Single(tracker.State.Shop);
        Assert.Equal("Molten Rock", shop.Name);
        Assert.Equal("3/3", shop.Stats);
        Assert.Equal(1, shop.TechLevel);
        Assert.Empty(tracker.State.OpponentBoard);

        var minion = Assert.Single(tracker.State.PlayerBoard);
        Assert.Equal("Southsea Busker", minion.Name);
        Assert.True(minion.IsGolden);
        Assert.Equal("Taunt", minion.Keywords);
    }

    [Fact]
    public void ReadsLobbyPlacementTriplesAndEliminations()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=136 CardID=BG23_HERO_303",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=HERO",
            GameState + "    tag=PLAYER_ID value=8",
            GameState + "    tag=HEALTH value=30",
            GameState + "    tag=ARMOR value=0",
            GameState + "FULL_ENTITY - Updating [entityName=Murloc Holmes id=136 zone=SETASIDE zonePos=0 cardId=BG23_HERO_303 player=15] CardID=BG23_HERO_303",
            GameState + "TAG_CHANGE Entity=[entityName=Murloc Holmes id=136 zone=SETASIDE zonePos=0 cardId=BG23_HERO_303 player=15] tag=PLAYER_LEADERBOARD_PLACE value=8",
            GameState + "TAG_CHANGE Entity=[entityName=Murloc Holmes id=136 zone=SETASIDE zonePos=0 cardId=BG23_HERO_303 player=15] tag=PLAYER_TRIPLES value=3",
            GameState + "TAG_CHANGE Entity=[entityName=Murloc Holmes id=136 zone=SETASIDE zonePos=0 cardId=BG23_HERO_303 player=15] tag=DAMAGE value=30",
            GameState + "TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=PLAYER_LEADERBOARD_PLACE value=1"
        ]);

        var local = Assert.Single(tracker.State.LobbyParticipants, participant => participant.IsLocal);
        Assert.Equal(7, local.PlayerId);
        Assert.Equal("Hráč#21600", local.BattleTag);
        Assert.Equal(1, local.LeaderboardPlace);
        Assert.False(local.IsEliminated);

        var opponent = Assert.Single(tracker.State.LobbyParticipants, participant => participant.PlayerId == 8);
        Assert.Equal("Murloc Holmes", opponent.HeroName);
        Assert.Equal(8, opponent.LeaderboardPlace);
        Assert.Equal(3, opponent.Triples);
        Assert.True(opponent.IsEliminated);
        Assert.Contains(tracker.State.RecentEvents, message => message.Contains("vypadl na 8. místě"));
    }

    [Fact]
    public void PrefersRealBattleTagOverTheBartenderNameFromTheDelayedStream()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=81 CardID=TB_BaconShopBob_SKIN_AO",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "FULL_ENTITY - Updating [entityName=Winter Queen id=81 zone=PLAY zonePos=0 cardId=TB_BaconShopBob_SKIN_AO player=15] CardID=TB_BaconShopBob_SKIN_AO",
            GameState + "FULL_ENTITY - Creating ID=10159 CardID=BG22_HERO_003",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=HERO",
            GameState + "    tag=PLAYER_ID value=5",
            GameState + "FULL_ENTITY - Updating [entityName=Vanndar Stormpike id=10159 zone=PLAY zonePos=0 cardId=BG22_HERO_003 player=15] CardID=BG22_HERO_003",
            // GameState stihne vypsat jen jméno Bobova skinu, skutečný BattleTag přijde až z fronty.
            GameState + "TAG_CHANGE Entity=Winter Queen tag=HERO_ENTITY value=10159",
            TaskList + "TAG_CHANGE Entity=Soupeř tag=HERO_ENTITY value=10159"
        ]);

        var opponent = Assert.Single(tracker.State.LobbyParticipants, participant => participant.PlayerId == 5);
        Assert.Equal("Vanndar Stormpike", opponent.HeroName);
        Assert.Equal("Soupeř", opponent.BattleTag);
    }

    [Fact]
    public void RecordsCombatOutcomesAndFinalPlacement()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=11",
            GameState + "TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=NEXT_OPPONENT_PLAYER_ID value=3",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=0",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_WON_LAST_COMBAT value=1",
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=13",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=0",
            // Nula je jen vynulování tagu na začátku kola, skutečné poškození přijde až po ní.
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=DAMAGE_DEALT_TO_HERO_LAST_TURN value=0",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=DAMAGE_DEALT_TO_HERO_LAST_TURN value=8",
            GameState + "TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=PLAYER_LEADERBOARD_PLACE value=2",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=PLAYSTATE value=LOST",
            GameState + "TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER"
        ]);

        Assert.Equal(2, tracker.State.CombatHistory.Count);
        Assert.Equal("WON", tracker.State.CombatHistory[0].Outcome);
        Assert.Equal(6, tracker.State.CombatHistory[0].Round);
        Assert.Equal(3, tracker.State.CombatHistory[0].OpponentPlayerId);
        Assert.Equal("LOST", tracker.State.CombatHistory[1].Outcome);
        Assert.Equal(8, tracker.State.CombatHistory[1].DamageTaken);
        Assert.False(tracker.State.IsGameActive);
        Assert.Equal("LOST", tracker.State.Result);
        Assert.Equal(2, tracker.State.FinalPlace);
    }

    [Fact]
    public void IgnoresStatsFromTheTemporaryCombatHeroAndRanksByRemainingHealth()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=PLAYER_LEADERBOARD_PLACE value=1",
            GameState + "TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=DAMAGE value=25",
            // Leaderboard entita soupeře: vyřazený hráč se skutečným poškozením.
            GameState + "FULL_ENTITY - Creating ID=136 CardID=BG23_HERO_303",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=HERO",
            GameState + "    tag=PLAYER_ID value=8",
            GameState + "    tag=HEALTH value=30",
            GameState + "    tag=ARMOR value=0",
            GameState + "    tag=PLAYER_LEADERBOARD_PLACE value=8",
            GameState + "FULL_ENTITY - Updating [entityName=Murloc Holmes id=136 zone=SETASIDE zonePos=0 cardId=BG23_HERO_303 player=15] CardID=BG23_HERO_303",
            GameState + "TAG_CHANGE Entity=[entityName=Murloc Holmes id=136 zone=SETASIDE zonePos=0 cardId=BG23_HERO_303 player=15] tag=DAMAGE value=30",
            // Kopie téhož hrdiny pro souboj má znovu plné HP a nesmí ho v lobby vzkřísit.
            GameState + "FULL_ENTITY - Creating ID=8300 CardID=BG23_HERO_303",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=HERO",
            GameState + "    tag=PLAYER_ID value=8",
            GameState + "    tag=HEALTH value=30",
            GameState + "    tag=ARMOR value=12",
            GameState + "    tag=BACON_COMBAT_PHASE_HERO value=1",
            GameState + "FULL_ENTITY - Updating [entityName=Murloc Holmes id=8300 zone=PLAY zonePos=0 cardId=BG23_HERO_303 player=15] CardID=BG23_HERO_303"
        ]);

        var opponent = Assert.Single(tracker.State.LobbyParticipants, participant => participant.PlayerId == 8);
        Assert.True(opponent.IsEliminated);
        Assert.Equal(0, opponent.RemainingHealth);

        // Živý lokální hráč patří nad vyřazeného soupeře bez ohledu na tag z logu.
        Assert.Collection(
            tracker.State.Standings,
            first => Assert.True(first.IsLocal),
            second => Assert.Equal(8, second.PlayerId));
    }

    [Fact]
    public void StartsAFreshGameWhenThePreviousOneNeverReportedGameOver()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=9",
            GameState + "TAG_CHANGE Entity=[entityName=Y'Shaarj id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=PLAYER_TECH_LEVEL value=4",
            // Hra spadla, FINAL_GAMEOVER nikdy nedorazil a rovnou začíná další zápas.
            .. Opening()
        ]);

        Assert.Equal(2, tracker.State.GamesSeen);
        Assert.Null(tracker.State.Turn);
        Assert.Single(tracker.State.LobbyParticipants);
    }

    [Fact]
    public void KeepsTheLobbyWhenGameConstructionRepeatsBeforeTheFirstTurn()
    {
        var tracker = Replay([
            .. Opening(),
            // Opakovaná game konstrukce ještě před prvním kolem nesmí vymazat načtenou lobby.
            GameState + "CREATE_GAME",
            // Echo z animační fronty se ignoruje úplně.
            TaskList + "    CREATE_GAME"
        ]);

        Assert.Equal(1, tracker.State.GamesSeen);
        Assert.Single(tracker.State.LobbyParticipants);
    }

    [Fact]
    public void CollectsAvailableMinionRacesFromPoolMinionsOnly()
    {
        var tracker = Replay([
            .. Opening(),
            // Karta v nabídce Boba vzniká rovnou v PLAY na soupeřově straně.
            GameState + "FULL_ENTITY - Creating ID=417 CardID=BGS_127",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=CARDRACE value=ELEMENTAL",
            GameState + "    tag=ZONE_POSITION value=1",
            GameState + "    tag=IS_BACON_POOL_MINION value=1",
            // Opačné pořadí tagů musí dát stejný výsledek.
            GameState + "FULL_ENTITY - Creating ID=418 CardID=BG26_135",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=2",
            GameState + "    tag=IS_BACON_POOL_MINION value=1",
            GameState + "    tag=CARDRACE value=PIRATE",
            // Amalgám patří ke všem typům, takže žádný nový typ neurčuje.
            GameState + "FULL_ENTITY - Creating ID=419 CardID=BG_TTN_401",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=3",
            GameState + "    tag=CARDRACE value=ALL",
            GameState + "    tag=IS_BACON_POOL_MINION value=1",
            // Karta vyrobená efektem se rodí u svého hráče v SETASIDE. I když se pak objeví
            // v řadě nabídky, typ lobby neurčuje.
            GameState + "FULL_ENTITY - Creating ID=420 CardID=BG31_330",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=SETASIDE",
            GameState + "    tag=CARDRACE value=NAGA",
            GameState + "    tag=IS_BACON_POOL_MINION value=1",
            GameState + "TAG_CHANGE Entity=420 tag=CONTROLLER value=15",
            GameState + "TAG_CHANGE Entity=420 tag=ZONE value=PLAY",
            GameState + "TAG_CHANGE Entity=420 tag=ZONE_POSITION value=4",
            // Token mimo pool nesmí typ přidat.
            GameState + "FULL_ENTITY - Creating ID=421 CardID=BG24_001t",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=5",
            GameState + "    tag=CARDRACE value=UNDEAD"
        ]);

        Assert.Equal(["ELEMENTAL", "PIRATE"], tracker.State.AvailableRaces);
        Assert.Equal("Mech", MinionRace.Display("MECHANICAL"));
    }

    [Fact]
    public void RemembersTheOpponentBoardFromCombatAndFillsInNamesThatArriveLater()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=9",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            // Soubojová kopie soupeřova hrdiny určuje, komu deska patří.
            GameState + "FULL_ENTITY - Creating ID=634 CardID=BG34_HERO_002",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=HERO",
            GameState + "    tag=PLAYER_ID value=3",
            GameState + "    tag=BACON_COMBAT_PHASE_HERO value=1",
            GameState + "FULL_ENTITY - Creating ID=700 CardID=BGS_119",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=1",
            GameState + "    tag=ATK value=6",
            GameState + "    tag=HEALTH value=5",
            // Jméno minionu v tuhle chvíli hra ještě nezveřejnila.
            GameState + "TAG_CHANGE Entity=GameEntity tag=PROPOSED_ATTACKER value=700",
            // Až opožděná fronta jméno doplní; snímek desky se musí dopsat zpětně.
            TaskList + "TAG_CHANGE Entity=[entityName=Crackling Cyclone id=700 zone=PLAY zonePos=1 cardId=BGS_119 player=15] tag=ATTACKING value=1"
        ]);

        var opponent = Assert.Single(tracker.State.LobbyParticipants, participant => participant.PlayerId == 3);
        var minion = Assert.Single(opponent.LastBoard);
        Assert.Equal("Crackling Cyclone", minion.Name);
        Assert.Equal("6/5", minion.Stats);
        Assert.Equal(5, opponent.LastBoardRound);
    }

    [Fact]
    public void LearnsShopCardNamesFromOptionLines()
    {
        var tracker = Replay([
            .. Opening(),
            "D 18:25 GameState.DebugPrintOptions() -   option 4 type=POWER mainEntity=[entityName=Molten Rock id=417 zone=PLAY zonePos=1 cardId=BGS_127 player=15] error=NONE errorParam=",
            GameState + "FULL_ENTITY - Creating ID=417 CardID=BGS_127",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=1"
        ]);

        Assert.Equal("Molten Rock", Assert.Single(tracker.State.Shop).Name);
    }
}
