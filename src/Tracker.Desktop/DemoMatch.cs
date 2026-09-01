using System.Text;

namespace Tracker.Desktop;

/// <summary>
/// Syntetický log ve stejném tvaru, jaký píše hra. Slouží k předvedení overlaye bez běžícího
/// Hearthstonu, proto obsahuje lobby, desku, nabídku Boba i souboj.
/// </summary>
internal static class DemoMatch
{
    private const string Prefix = "D 18:00 GameState.DebugPrintPower() - ";

    private static readonly (int Slot, int Entity, string Hero, string Card, string? BattleTag, int Armor, int Tier)[] Lobby =
    [
        (1, 101, "Y'Shaarj", "TB_BaconShop_HERO_92", "Ty#1234", 8, 4),
        (2, 102, "Ragnaros the Firelord", "TB_BaconShop_HERO_11", "Ragna", 6, 4),
        (3, 103, "Millhouse Manastorm", "TB_BaconShop_HERO_49", "Milly", 10, 3),
        (4, 104, "A. F. Kay", "TB_BaconShop_HERO_16", null, 12, 3),
        (5, 105, "Forest Warden Omu", "TB_BaconShop_HERO_74", "Omu", 4, 5),
        (6, 106, "Murloc Holmes", "BG23_HERO_303", "Holmes", 0, 2),
        (7, 107, "Ambassador Faelin", "BG22_HERO_201", "Faelin", 2, 3),
        (8, 108, "Kel'Thuzad", "TB_BaconShop_HERO_KelThuzad", null, 0, 2)
    ];

    private static readonly (int Entity, string Name, string Card, int Position, int Attack, int Health, int Tier)[] Board =
    [
        (201, "Bristleback Brute", "BGS_017", 1, 12, 9, 3),
        (202, "Cave Hydra", "LOOT_078", 2, 8, 6, 3),
        (203, "Deflect-o-Bot", "BOT_312", 3, 6, 8, 4),
        (204, "Kangor's Apprentice", "BGS_075", 4, 3, 6, 5)
    ];

    private static readonly (int Entity, string Name, string Card, int Position, int Attack, int Health, int Tier)[] Shop =
    [
        (301, "Southsea Busker", "BG26_135", 1, 3, 1, 1),
        (302, "Crackling Cyclone", "BGS_119", 2, 2, 1, 1),
        (303, "Molten Rock", "BGS_127", 3, 3, 3, 1),
        (304, "Them Apples", "BG28_966", 4, 3, 2, 2)
    ];

    public static IReadOnlyList<string> Lines { get; } = Build();

    private static IReadOnlyList<string> Build()
    {
        var lines = new List<string>
        {
            Prefix + "CREATE_GAME",
            Prefix + "    GameEntity EntityID=1",
            Prefix + "        tag=CARDTYPE value=GAME",
            Prefix + "    Player EntityID=20 PlayerID=7 GameAccountId=[hi=1 lo=42]",
            Prefix + "    Player EntityID=21 PlayerID=15 GameAccountId=[hi=0 lo=0]",
            "D 18:00 GameState.DebugPrintGame() - PlayerID=7, PlayerName=Ty#1234",
            "D 18:00 GameState.DebugPrintGame() - PlayerID=15, PlayerName=Soupeř",
            Prefix + "TAG_CHANGE Entity=GameEntity tag=BACON_MAX_PLAYER_TECH_LEVEL value=6"
        };

        foreach (var (slot, entity, hero, card, battleTag, armor, tier) in Lobby)
        {
            var controller = slot == 1 ? 7 : 15;
            lines.Add($"{Prefix}FULL_ENTITY - Creating ID={entity} CardID={card}");
            lines.Add($"{Prefix}    tag=CONTROLLER value={controller}");
            lines.Add($"{Prefix}    tag=CARDTYPE value=HERO");
            lines.Add($"{Prefix}    tag=PLAYER_ID value={slot}");
            lines.Add($"{Prefix}    tag=HEALTH value=30");
            lines.Add($"{Prefix}    tag=ARMOR value={armor}");
            lines.Add($"{Prefix}    tag=ZONE value=PLAY");
            lines.Add($"{Prefix}    tag=PLAYER_LEADERBOARD_PLACE value={slot}");
            lines.Add($"{Prefix}FULL_ENTITY - Updating {Descriptor(hero, entity, card, "PLAY", 0, controller)} CardID={card}");
            lines.Add($"{Prefix}TAG_CHANGE Entity={Descriptor(hero, entity, card, "PLAY", 0, controller)} tag=PLAYER_TECH_LEVEL value={tier}");
            if (battleTag is not null)
            {
                lines.Add($"{Prefix}TAG_CHANGE Entity={battleTag} tag=HERO_ENTITY value={entity}");
            }
        }

        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=TURN value=9");
        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_ACTION");
        lines.Add(Prefix + "TAG_CHANGE Entity=Ty#1234 tag=RESOURCES value=6");
        lines.Add(Prefix + "TAG_CHANGE Entity=Ty#1234 tag=RESOURCES_USED value=2");
        lines.Add(Prefix + $"TAG_CHANGE Entity={Descriptor("Y'Shaarj", 101, "TB_BaconShop_HERO_92", "PLAY", 0, 7)} tag=NEXT_OPPONENT_PLAYER_ID value=5");

        lines.Add($"{Prefix}FULL_ENTITY - Creating ID=401 CardID=TB_BaconShopTechUp05_Button");
        lines.Add($"{Prefix}    tag=CONTROLLER value=7");
        lines.Add($"{Prefix}    tag=COST value=5");

        AddMinions(lines, Board, controller: 7, golden: 203);
        AddMinions(lines, Shop, controller: 15, golden: 302);

        lines.Add(Prefix + "TAG_CHANGE Entity=Ty#1234 tag=PLAYER_TRIPLES value=2");
        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_COMBAT");
        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=1");
        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=BACON_IN_COMBAT_PHASE value=0");
        lines.Add(Prefix + "TAG_CHANGE Entity=Ty#1234 tag=BACON_WON_LAST_COMBAT value=1");
        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=TURN value=10");
        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=STEP value=MAIN_ACTION");
        lines.Add(Prefix + $"TAG_CHANGE Entity={Descriptor("Murloc Holmes", 106, "BG23_HERO_303", "PLAY", 0, 15)} tag=DAMAGE value=30");
        lines.Add(Prefix + $"TAG_CHANGE Entity={Descriptor("Y'Shaarj", 101, "TB_BaconShop_HERO_92", "PLAY", 0, 7)} tag=PLAYER_LEADERBOARD_PLACE value=1");
        lines.Add(Prefix + "TAG_CHANGE Entity=Ty#1234 tag=PLAYSTATE value=WON");
        lines.Add(Prefix + "TAG_CHANGE Entity=GameEntity tag=STEP value=FINAL_GAMEOVER");
        return lines;
    }

    private static void AddMinions(
        List<string> lines,
        (int Entity, string Name, string Card, int Position, int Attack, int Health, int Tier)[] minions,
        int controller,
        int golden)
    {
        foreach (var (entity, name, card, position, attack, health, tier) in minions)
        {
            lines.Add($"{Prefix}FULL_ENTITY - Creating ID={entity} CardID={card}");
            lines.Add($"{Prefix}    tag=CONTROLLER value={controller}");
            lines.Add($"{Prefix}    tag=CARDTYPE value=MINION");
            lines.Add($"{Prefix}    tag=ZONE value=PLAY");
            lines.Add($"{Prefix}    tag=ZONE_POSITION value={position}");
            lines.Add($"{Prefix}    tag=ATK value={attack}");
            lines.Add($"{Prefix}    tag=HEALTH value={health}");
            lines.Add($"{Prefix}    tag=TECH_LEVEL value={tier}");
            if (entity == golden)
            {
                lines.Add($"{Prefix}    tag=PREMIUM value=1");
                lines.Add($"{Prefix}    tag=DIVINE_SHIELD value=1");
            }

            if (position == 1)
            {
                lines.Add($"{Prefix}    tag=TAUNT value=1");
            }

            lines.Add($"{Prefix}FULL_ENTITY - Updating {Descriptor(name, entity, card, "PLAY", position, controller)} CardID={card}");
        }
    }

    private static string Descriptor(string name, int entity, string card, string zone, int position, int controller) =>
        new StringBuilder("[entityName=").Append(name).Append(" id=").Append(entity)
            .Append(" zone=").Append(zone).Append(" zonePos=").Append(position)
            .Append(" cardId=").Append(card).Append(" player=").Append(controller).Append(']')
            .ToString();
}
