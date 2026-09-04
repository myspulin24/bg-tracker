using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Ekonomika krčmy a trinkety. Tvary řádků, nositelé tagů i chování hodnot jsou odpozorované
/// ve skutečném <c>Power.log</c> (session Hearthstone_2026_09_04_16_58_07): cena rerollu
/// a počet volných rerollů sedí na tlačítku <c>TB_BaconShop_8p_Reroll_Button</c>, zlato navíc
/// na entitě hráče, strop tieru na entitách hrdinů a odpočet trinketu na entitě slotu.
/// </summary>
public sealed class TavernEconomyTests
{
    private const string GameState = "D 17:07 GameState.DebugPrintPower() - ";

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
        "D 17:07 GameState.DebugPrintGame() - PlayerID=7, PlayerName=Hráč#21600",
        "D 17:07 GameState.DebugPrintGame() - PlayerID=15, PlayerName=Protihráč",
        GameState + "FULL_ENTITY - Creating ID=116 CardID=TB_BaconShop_HERO_92",
        GameState + "    tag=CONTROLLER value=7",
        GameState + "    tag=CARDTYPE value=HERO",
        GameState + "    tag=PLAYER_ID value=7",
        GameState + "    tag=ZONE value=PLAY",
        GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=116"
    ];

    /// <summary>Tlačítko rerollu lokálního hráče, tak jak ho hra vypisuje.</summary>
    private static string[] RefreshButton(int controller = 7) =>
    [
        GameState + "FULL_ENTITY - Creating ID=8067 CardID=TB_BaconShop_8p_Reroll_Button",
        GameState + $"    tag=CONTROLLER value={controller}",
        GameState + "    tag=ZONE value=PLAY"
    ];

    [Fact]
    public void TracksRefreshCostAndFreeRefreshes()
    {
        var tracker = Replay([
            .. Opening(),
            .. RefreshButton(),
            GameState + "TAG_CHANGE Entity=[entityName=Refresh id=8067 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=7] tag=COST value=1",
            GameState + "TAG_CHANGE Entity=[entityName=Refresh id=8067 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=7] tag=BACON_FREE_REFRESH_COUNT value=2"
        ]);

        Assert.Equal(1, tracker.State.RefreshCost);
        Assert.Equal(2, tracker.State.FreeRefreshes);
    }

    /// <summary>Volný reroll hra hlásí nulovou cenou; po vyčerpání počítadlo spadne na nulu.</summary>
    [Fact]
    public void ReportsFreeRefreshAsZeroCostAndForgetsSpentOnes()
    {
        var tracker = Replay([
            .. Opening(),
            .. RefreshButton(),
            GameState + "TAG_CHANGE Entity=[entityName=Refresh id=8067 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=7] tag=BACON_FREE_REFRESH_COUNT value=4",
            GameState + "TAG_CHANGE Entity=[entityName=Refresh id=8067 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=7] tag=COST value=0",
            GameState + "TAG_CHANGE Entity=[entityName=Refresh id=8067 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=7] tag=BACON_FREE_REFRESH_COUNT value=0"
        ]);

        Assert.Equal(0, tracker.State.RefreshCost);
        Assert.Null(tracker.State.FreeRefreshes);
    }

    /// <summary>
    /// Tlačítka mají všichni hráči, ale panel patří lokálnímu. Bez filtru na controller by
    /// se do něj propsala cizí krčma.
    /// </summary>
    [Fact]
    public void IgnoresRefreshButtonOfAnotherPlayer()
    {
        var tracker = Replay([
            .. Opening(),
            .. RefreshButton(controller: 15),
            GameState + "TAG_CHANGE Entity=[entityName=Refresh id=8067 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=15] tag=COST value=1",
            GameState + "TAG_CHANGE Entity=[entityName=Refresh id=8067 zone=PLAY zonePos=0 cardId=TB_BaconShop_8p_Reroll_Button player=15] tag=BACON_FREE_REFRESH_COUNT value=3"
        ]);

        Assert.Null(tracker.State.RefreshCost);
        Assert.Null(tracker.State.FreeRefreshes);
    }

    /// <summary>
    /// Zlato navíc se po utracení skutečně vrací na nulu, takže se na něj nesmí použít
    /// pravidlo bonusů pro celou hru, které návrat na nulu zahazuje.
    /// </summary>
    [Fact]
    public void TracksExtraGoldNextTurnAndClearsItWhenSpent()
    {
        var lines = new List<string>(Opening())
        {
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_PLAYER_EXTRA_GOLD_NEXT_TURN value=2"
        };
        var tracker = Replay([.. lines]);
        Assert.Equal(2, tracker.State.ExtraGoldNextTurn);

        var parser = new PowerLogParser();
        tracker.Apply(parser.Parse(
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_PLAYER_EXTRA_GOLD_NEXT_TURN value=0"));
        Assert.Null(tracker.State.ExtraGoldNextTurn);
    }

    [Fact]
    public void TracksMaxTavernTier()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=[entityName=Hrdina id=116 zone=PLAY zonePos=0 cardId=TB_BaconShop_HERO_92 player=7] tag=BACON_MAX_PLAYER_TECH_LEVEL value=6"
        ]);

        Assert.Equal(6, tracker.State.MaxTavernTier);
    }

    [Fact]
    public void CountsDownToTrinketAndThenShowsTheChosenOne()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=394 CardID=BG30_Trinket_1st",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=BACON_TURNS_LEFT_TO_DISCOVER_TRINKET value=5"
        ]);

        var waiting = tracker.State.LesserTrinket;
        Assert.NotNull(waiting);
        Assert.False(waiting.Value.IsFilled);
        Assert.Equal(5, waiting.Value.TurnsLeft);

        // Vybraný trinket přepíše tutéž entitu, takže se slot pozná jen podle karty, se kterou vznikl.
        var parser = new PowerLogParser();
        tracker.Apply(parser.Parse(
            GameState + "TAG_CHANGE Entity=[entityName=Lovely Locket id=394 zone=PLAY zonePos=0 cardId=BG36_MagicItem_211 player=7] tag=NUM_TURNS_IN_PLAY value=1"));

        var filled = tracker.State.LesserTrinket;
        Assert.NotNull(filled);
        Assert.True(filled.Value.IsFilled);
        Assert.Equal("Lovely Locket", filled.Value.Name);
    }

    /// <summary>
    /// Jméno a karta se po výběru nemění zároveň: naměřeno, že jméno se z descriptorů obnoví
    /// hned, kdežto karta až tehdy, když entitu zmíní řádek s novým <c>cardId</c>. Kdyby se
    /// obsazenost poznávala po kartě, druhý slot by v panelu zůstal prázdný i po výběru.
    /// </summary>
    [Fact]
    public void ShowsTheChosenTrinketEvenWhenTheCardIdLagsBehind()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=395 CardID=BG30_Trinket_2nd",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=BACON_TURNS_LEFT_TO_DISCOVER_TRINKET value=0",
            GameState + "TAG_CHANGE Entity=[entityName=Beatboxer Portrait id=395 zone=PLAY zonePos=0 cardId=BG30_Trinket_2nd player=7] tag=NUM_TURNS_IN_PLAY value=1"
        ]);

        var slot = tracker.State.GreaterTrinket;
        Assert.NotNull(slot);
        Assert.True(slot.Value.IsFilled);
        Assert.Equal("Beatboxer Portrait", slot.Value.Name);
    }

    /// <summary>
    /// Prázdných slotů vyrobí hra za zápas desítky a všechny nesou odpočet ze začátku hry.
    /// Platí jen ten v <c>PLAY</c>; bez filtru panel ukazoval odpočet i po výběru trinketu.
    /// </summary>
    [Fact]
    public void PrefersTheTrinketSlotInPlay()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=395 CardID=BG30_Trinket_2nd",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=BACON_TURNS_LEFT_TO_DISCOVER_TRINKET value=2",
            GameState + "FULL_ENTITY - Creating ID=6883 CardID=BG30_Trinket_2nd",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=SETASIDE",
            GameState + "    tag=BACON_TURNS_LEFT_TO_DISCOVER_TRINKET value=8"
        ]);

        var slot = tracker.State.GreaterTrinket;
        Assert.NotNull(slot);
        Assert.Equal(2, slot.Value.TurnsLeft);
    }

    /// <summary>
    /// Popis efektu se dohledává podle karty, takže ji vybraný trinket musí nést. Dokud v entitě
    /// leží prázdný slot, karta se neposílá — popis prázdného slotu by byl k ničemu.
    /// </summary>
    [Fact]
    public void CarriesTheCardOfTheChosenTrinketOnly()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=394 CardID=BG30_Trinket_1st",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=BACON_TURNS_LEFT_TO_DISCOVER_TRINKET value=3"
        ]);

        Assert.Null(tracker.State.LesserTrinket?.CardId);

        var parser = new PowerLogParser();
        tracker.Apply(parser.Parse(
            GameState + "SHOW_ENTITY - Updating Entity=[entityName=Lovely Locket id=394 zone=PLAY zonePos=0 cardId=BG30_Trinket_1st player=7] CardID=BG36_MagicItem_211"));

        Assert.Equal("BG36_MagicItem_211", tracker.State.LesserTrinket?.CardId);
    }

    /// <summary>
    /// U velkého trinketu hra kartu v entitě slotu nevymění vůbec, takže se musí dohledat podle
    /// jména mezi nabídkami — ty kartu nesou vždycky a poznají se po tagu <c>BACON_TRINKET</c>.
    /// Bez toho by u druhého slotu chyběl popis efektu.
    /// </summary>
    [Fact]
    public void FindsTheTrinketCardByNameWhenTheSlotKeepsThePlaceholder()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=395 CardID=BG30_Trinket_2nd",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "FULL_ENTITY - Creating ID=3265 CardID=BG35_MagicItem_741",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=SETASIDE",
            GameState + "    tag=BACON_TRINKET value=1",
            GameState + "TAG_CHANGE Entity=[entityName=Beatboxer Portrait id=3265 zone=SETASIDE zonePos=0 cardId=BG35_MagicItem_741 player=7] tag=BACON_TRINKET value=1",
            GameState + "TAG_CHANGE Entity=[entityName=Beatboxer Portrait id=395 zone=PLAY zonePos=0 cardId=BG30_Trinket_2nd player=7] tag=NUM_TURNS_IN_PLAY value=1"
        ]);

        var slot = tracker.State.GreaterTrinket;
        Assert.NotNull(slot);
        Assert.Equal("Beatboxer Portrait", slot.Value.Name);
        Assert.Equal("BG35_MagicItem_741", slot.Value.CardId);
    }

    /// <summary>
    /// Karty typu „a zlepši tohle“ si hodnotu drží samy v <c>TAG_SCRIPT_DATA_NUM_1</c> a <c>_2</c>.
    /// Naměřeno na Spark Snapperovi, kterému během zápasu vyrostla z 2/2 na 26/28.
    /// </summary>
    [Fact]
    public void TracksTheCardsOwnCounters()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=18157 CardID=BG36_851",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=CARDTYPE value=MINION",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=ZONE_POSITION value=3",
            GameState + "    tag=TAG_SCRIPT_DATA_NUM_1 value=26",
            GameState + "    tag=TAG_SCRIPT_DATA_NUM_2 value=28"
        ]);

        var minion = Assert.Single(tracker.State.PlayerBoard);
        Assert.Equal((26, 28), (minion.ScriptDataNum1, minion.ScriptDataNum2));
    }

    [Fact]
    public void HasNoTrinketBeforeTheGameCreatesTheSlots()
    {
        var tracker = Replay(Opening());

        Assert.Null(tracker.State.LesserTrinket);
        Assert.Null(tracker.State.GreaterTrinket);
    }
}
