using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Bonusy pro celou hru. Tvary řádků i chování tagů jsou odpozorované ve skutečném
/// <c>Power.log</c> (zápas match-20260902-204226-180, Red Chromadrake v kole 12).
/// </summary>
public sealed class GlobalBuffsTests
{
    private const string GameState = "D 18:24 GameState.DebugPrintPower() - ";

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
        GameState + "    tag=ZONE value=PLAY",
        GameState + "TAG_CHANGE Entity=Hráč#21600 tag=HERO_ENTITY value=116"
    ];

    /// <summary>
    /// Útok undeadů není na entitě hráče: tag <c>UNDEAD_ATTACK_BUFF</c> z enumu hry se v logu
    /// nevyskytuje a hodnotu nese enchantment hráče <c>BG25_011pe</c>
    /// v <c>TAG_SCRIPT_DATA_NUM_1</c>. Naměřeno na kartě Nerubian Deathswarmer.
    /// </summary>
    [Fact]
    public void TracksUndeadAttackFromThePlayerEnchantment()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=4123 CardID=BG25_011pe",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "TAG_CHANGE Entity=[entityName=Undead Bonus Attack Player Enchant [DNT] id=4123 zone=PLAY zonePos=0 cardId=BG25_011pe player=7] tag=TAG_SCRIPT_DATA_NUM_1 value=11"
        ]);

        Assert.Equal(11, tracker.State.Buffs.UndeadAttack);
        Assert.True(tracker.State.Buffs.HasUndead);
        Assert.False(tracker.State.Buffs.IsEmpty);
    }

    /// <summary>
    /// Enchantment se každým soubojem přegeneruje a ten odcházející dostane nulu, ačkoli bonus
    /// platí dál. Zahazuje se proto stejně jako nulování tagů.
    /// </summary>
    [Fact]
    public void KeepsUndeadAttackWhenTheOldEnchantmentIsZeroed()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=4123 CardID=BG25_011pe",
            GameState + "    tag=CONTROLLER value=7",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=TAG_SCRIPT_DATA_NUM_1 value=11",
            GameState + "TAG_CHANGE Entity=[entityName=Undead Bonus Attack Player Enchant [DNT] id=4123 zone=PLAY zonePos=0 cardId=BG25_011pe player=7] tag=TAG_SCRIPT_DATA_NUM_1 value=0"
        ]);

        Assert.Equal(11, tracker.State.Buffs.UndeadAttack);
    }

    /// <summary>Enchantment má každý hráč vlastní; do panelu patří jen ten lokální.</summary>
    [Fact]
    public void IgnoresUndeadAttackOfAnotherPlayer()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "FULL_ENTITY - Creating ID=11579 CardID=BG25_011pe",
            GameState + "    tag=CONTROLLER value=15",
            GameState + "    tag=ZONE value=PLAY",
            GameState + "    tag=TAG_SCRIPT_DATA_NUM_1 value=42"
        ]);

        Assert.Equal(0, tracker.State.Buffs.UndeadAttack);
        Assert.False(tracker.State.Buffs.HasUndead);
    }

    [Fact]
    public void TracksSpellBloodGemElementalAndPirateBuffs()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=1",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=2",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_HEALTH_INCREASE value=3",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_BLOODGEMBUFFATKVALUE value=1",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_BLOODGEMBUFFHEALTHVALUE value=2",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_ELEMENTAL_BUFFATKVALUE value=4",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_ELEMENTAL_BUFFHEALTHVALUE value=14",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_PIRATE_BUFFATKVALUE value=5",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_PIRATE_BUFFHEALTHVALUE value=5"
        ]);

        var buffs = tracker.State.Buffs;
        Assert.Equal((2, 3), (buffs.SpellAttack, buffs.SpellHealth));
        Assert.Equal((1, 2), (buffs.BloodGemAttack, buffs.BloodGemHealth));
        Assert.Equal((4, 14), (buffs.ElementalAttack, buffs.ElementalHealth));
        Assert.Equal((5, 5), (buffs.PirateAttack, buffs.PirateHealth));
        Assert.False(buffs.IsEmpty);
    }

    [Fact]
    public void KeepsBuffsWhenGameZeroesThemDuringCombat()
    {
        // Hra na začátku souboje tagy vynuluje a po chvíli je vrátí. Kdyby se nula zrcadlila,
        // počítadlo by v každém souboji na několik sekund spadlo na +0/+0.
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=3",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_HEALTH_INCREASE value=5",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=0",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_HEALTH_INCREASE value=0"
        ]);

        Assert.Equal(3, tracker.State.Buffs.SpellAttack);
        Assert.Equal(5, tracker.State.Buffs.SpellHealth);

        var restored = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=3",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=0",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=3",
            GameState + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=0",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=TAVERN_SPELL_ATTACK_INCREASE value=4"
        ]);

        Assert.Equal(4, restored.State.Buffs.SpellAttack);
    }

    [Fact]
    public void StaysEmptyWhenNothingBuffsAnything()
    {
        var tracker = Replay([
            .. Opening(),
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=RESOURCES value=6"
        ]);

        Assert.True(tracker.State.Buffs.IsEmpty);
    }

    [Fact]
    public void ResetsBuffsWithNewGame()
    {
        var tracker = Replay([
            .. Opening(),
            // Bez rozehraného kola tracker druhou game konstrukci považuje za opakovanou,
            // ne za novou hru, a lobby ani bonusy nemaže.
            GameState + "TAG_CHANGE Entity=GameEntity tag=TURN value=9",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_BLOODGEMBUFFATKVALUE value=2",
            GameState + "TAG_CHANGE Entity=Hráč#21600 tag=BACON_BLOODGEMBUFFHEALTHVALUE value=4",
            .. Opening()
        ]);

        Assert.True(tracker.State.Buffs.IsEmpty);
        Assert.Equal(0, tracker.State.Buffs.BloodGemAttack);
    }
}
