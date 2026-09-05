using System.Globalization;
using System.Text.RegularExpressions;

namespace Tracker.Core;

public sealed partial class GameStateTracker
{
    /// <summary>Tlačítko rerollu v krčmě; nese cenu i počet volných rerollů.</summary>
    private const string RefreshButtonCardId = "TB_BaconShop_8p_Reroll_Button";

    /// <summary>
    /// Enchantment hráče, který drží nasčítaný útok undeadů: <c>Undead Bonus Attack Player
    /// Enchant [DNT]</c>. Karta patří Nerubian Deathswarmerovi, ale jméno entity je obecné,
    /// takže do ní hra sype bonus i z dalších karet se stejným efektem.
    /// </summary>
    private const string UndeadAttackEnchantmentCardId = "BG25_011pe";

    private static readonly HashSet<string> ParticipantTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "PLAYER_TECH_LEVEL", "HEALTH", "ARMOR", "DAMAGE", "PLAYSTATE",
        "PLAYER_LEADERBOARD_PLACE", "PLAYER_TRIPLES"
    };

    private readonly Dictionary<int, string> entityNames = [];
    private readonly Dictionary<string, int> entityIdsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> entityAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> playerEntities = [];
    private readonly Dictionary<int, int> entityPlayers = [];
    private readonly Dictionary<string, int> namedPlayers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> heroOwners = [];
    private readonly Dictionary<string, int> battleTagLobbyPlayers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> tavernUpgradeCosts = [];
    private readonly Dictionary<string, string> cardNames = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> leaderboardHeroes = [];

    /// <summary>
    /// Ohlášená vyřazení. Klíčem je slot; v Duos, kde padá celý tým, záporné číslo týmu. Hláška
    /// se pamatuje proto, aby se dala přepsat, až log dodá skutečné umístění.
    /// </summary>
    private readonly Dictionary<int, EliminationNote> eliminations = [];

    /// <summary>Sloty, jejichž deska už se v tomto souboji uložila; brání přepsání zbytky desky.</summary>
    private readonly HashSet<int> capturedSlotsThisCombat = [];

    private CombatRound? announcedCombat;
    private string? announcedCombatMessage;
    private int? localTeamId;
    private int? localPlayerId;
    private int? localPlayerEntityId;
    private int? announcedRound;
    private bool announcedGameOver;

    /// <summary>
    /// Hrdina, který právě stojí na které straně desky, podle <c>HERO_ENTITY</c> obou entit
    /// hráčů. V sólu se na lokální straně nikdy nemění; v Duos hra během souboje přepíná obě
    /// strany mezi oběma hrdiny dvojice, jak se přidávají do boje.
    /// </summary>
    private int? localSideHeroId;

    private int? opponentSideHeroId;

    /// <summary>Čeká deska na dané straně na uložení? Nastavuje se se vstupem hrdiny do souboje.</summary>
    private bool localCapturePending;

    private bool opponentCapturePending;

    /// <summary>Vystřídal se na straně během tohoto souboje hrdina? Pak už se deska na konci neukládá.</summary>
    private bool localSideSwitched;

    private bool opponentSideSwitched;

    /// <summary>
    /// Ohlášené vyřazení: hláška, hodnota tagu umístění v okamžiku ohlášení, kolo a umístění
    /// v hlášce. <paramref name="Computed"/> říká, že umístění vyšlo z počtu zbývajících hráčů,
    /// ne z tagu.
    /// </summary>
    private sealed record EliminationNote(
        string Message,
        int? TagPlace,
        int? Round,
        int? Place,
        bool Computed,
        LobbyParticipant Participant,
        IReadOnlyList<LobbyParticipant> Team);

    public TrackerState State { get; } = new();

    public bool Apply(PowerLogEvent powerEvent)
    {
        State.ParsedLines++;
        if (powerEvent.Kind == PowerLogEventKind.Unknown)
        {
            DetectBattlegroundsSignal(powerEvent.RawLine);
            return false;
        }

        State.RecognizedEvents++;
        DetectBattlegroundsSignal(powerEvent.RawLine);

        if (powerEvent.IsDeferred)
        {
            return ApplyDeferred(powerEvent);
        }

        if (powerEvent.Kind == PowerLogEventKind.GameCreated)
        {
            // Rozehraná hra, která se ještě nedostala do prvního kola, může dostat opakovanou
            // game konstrukci; ta nesmí vymazat už načtenou lobby. Jakmile hra jednou běžela,
            // je další CREATE_GAME nová hra i tehdy, když té předchozí chybí FINAL_GAMEOVER.
            if (State.IsGameActive && State.Turn is null)
            {
                return false;
            }

            ResetIdentities();
            State.BeginGame();
            return true;
        }

        if (powerEvent.Kind == PowerLogEventKind.PlayerDeclared &&
            powerEvent.PlayerId is { } declaredPlayerId && powerEvent.EntityId is { } playerEntityId)
        {
            playerEntities[declaredPlayerId] = playerEntityId;
            entityPlayers[playerEntityId] = declaredPlayerId;
            if (powerEvent.IsLocalPlayer)
            {
                localPlayerId = declaredPlayerId;
                localPlayerEntityId = playerEntityId;
                State.LocalControllerId = declaredPlayerId;
            }
            else
            {
                // V Battlegrounds existuje jediná sdílená soupeřova strana. Nese jak desku
                // aktuálního protivníka, tak nabídku Boba mimo souboj.
                State.OpponentControllerId = declaredPlayerId;
            }

            return false;
        }

        if (powerEvent.Kind == PowerLogEventKind.PlayerNamed &&
            powerEvent.PlayerId is { } namedPlayerId && powerEvent.Entity is { } playerName)
        {
            entityAliases[playerName] = playerName;
            namedPlayers[playerName] = namedPlayerId;
            if (playerEntities.TryGetValue(namedPlayerId, out var namedEntityId))
            {
                RegisterEntity(namedEntityId, playerName);
            }

            if (localPlayerId == namedPlayerId)
            {
                State.LocalPlayerEntity = playerName;
            }

            return false;
        }

        if (powerEvent.Kind is PowerLogEventKind.EntityCreated or PowerLogEventKind.EntityShown
                or PowerLogEventKind.EntityObserved &&
            powerEvent.EntityId is { } revealedEntityId && powerEvent.Entity is { } revealedEntity)
        {
            RegisterEntity(revealedEntityId, revealedEntity);
            var entity = State.Entity(revealedEntityId);
            entity.Epoch = State.Epoch;
            MergeCardId(entity, powerEvent.CardId);
            SyncLobbyHero(entity);
            return false;
        }

        if (powerEvent.Kind != PowerLogEventKind.TagChanged ||
            powerEvent.Tag is null || powerEvent.Value is null || powerEvent.Entity is null)
        {
            return false;
        }

        if (powerEvent.EntityId is { } tagEntityId && !IsNumericEntity(powerEvent.Entity))
        {
            RegisterEntity(tagEntityId, powerEvent.Entity);
        }

        return ApplyTag(
            ResolveEntity(powerEvent.Entity, powerEvent.EntityId),
            powerEvent.Tag,
            powerEvent.Value,
            powerEvent.EntityId ?? ResolveEntityId(powerEvent.Entity));
    }

    /// <summary>
    /// Z opožděné animační fronty se přebírají jen jména entit a vazba BattleTagu na hrdinu.
    /// Právě tam se jméno soupeře často objeví dřív než v <c>GameState</c>, kde v tu chvíli ještě
    /// stojí jméno Bobova skinu.
    /// </summary>
    private bool ApplyDeferred(PowerLogEvent powerEvent)
    {
        // Jméno nese každý descriptor, i ten na řádku TAG_CHANGE. Na rozdíl od zóny a statistik
        // jméno nestárne, takže se dá z odložené fronty přebrat odkudkoli.
        if (powerEvent.EntityId is { } entityId && powerEvent.Entity is { } entity &&
            !IsNumericEntity(entity))
        {
            RegisterEntity(entityId, entity);
        }

        if (powerEvent.Kind == PowerLogEventKind.TagChanged &&
            powerEvent.Tag?.Equals("HERO_ENTITY", StringComparison.OrdinalIgnoreCase) == true &&
            powerEvent.Entity is { } owner && powerEvent.Value is { } heroEntity)
        {
            return BindHeroOwner(owner, heroEntity);
        }

        return false;
    }

    private bool ApplyTag(string entity, string tag, string value, int? entityId)
    {
        // Tag TURN nese i entita hráče, kde jde o počet vlastních tahů. Herní kolo je jen na GameEntity.
        if (tag.Equals("TURN", StringComparison.OrdinalIgnoreCase) && IsGameEntity(entity) &&
            TryInt(value, out var turn))
        {
            State.Turn = turn;
            if (State.Round is { } round && announcedRound != round)
            {
                announcedRound = round;
                State.AddEvent($"Kolo {round}.");
            }

            return true;
        }

        if (tag.Equals("STEP", StringComparison.OrdinalIgnoreCase))
        {
            State.Phase = PhaseName(value);
            if (value.Equals("FINAL_GAMEOVER", StringComparison.OrdinalIgnoreCase))
            {
                CompleteGame();
            }

            return true;
        }

        if (tag.Equals("BACON_IN_COMBAT_PHASE", StringComparison.OrdinalIgnoreCase) &&
            TryInt(value, out var combatFlag))
        {
            return SetCombatPhase(combatFlag != 0);
        }

        // První útok znamená, že jsou obě desky postavené a ještě nikdo neumřel. Je to jediný
        // okamžik, kdy se dá zachytit soupeřova deska tak, jak do souboje nastoupil. V Duos se
        // to opakuje pro každého hrdinu, který se do souboje přidá.
        if (tag.Equals("PROPOSED_ATTACKER", StringComparison.OrdinalIgnoreCase) &&
            State.IsCombatPhase && (localCapturePending || opponentCapturePending))
        {
            CaptureCombatBoards();
            return true;
        }

        var changed = false;
        if (entityId is { } trackedEntityId)
        {
            var tracked = State.Entity(trackedEntityId);
            tracked.Epoch = State.Epoch;
            changed |= ApplyEntityTag(tracked, tag, value);
            SyncLobbyHero(tracked);
            changed |= ApplyLocalPlayerTag(tracked, tag, value);
        }

        if (tag.Equals("HERO_ENTITY", StringComparison.OrdinalIgnoreCase))
        {
            var bound = BindHeroOwner(entity, value);
            var sideChanged = TrackSideHero(entity, value);
            return bound || sideChanged || changed;
        }

        if (!ParticipantTags.Contains(tag) || IsGameEntity(entity))
        {
            return changed;
        }

        var participant = State.Participant(entity);
        switch (tag.ToUpperInvariant())
        {
            case "PLAYER_TECH_LEVEL" when TryInt(value, out var tier):
                participant.TavernTier = tier;
                break;
            case "HEALTH" when TryInt(value, out var health):
                participant.Health = health;
                break;
            case "ARMOR" when TryInt(value, out var armor):
                participant.Armor = armor;
                break;
            case "DAMAGE" when TryInt(value, out var damage):
                participant.Damage = damage;
                break;
            case "PLAYSTATE":
                participant.PlayState = value;
                if (value is "WON" or "LOST" or "TIED")
                {
                    // Jen za sebe. Entita hráče na soupeřově straně mění jméno podle toho, koho
                    // klient zrovna ukazuje, takže by hláška ukazovala na náhodného hráče.
                    if (State.LocalPlayerEntity is not null &&
                        entity.Equals(State.LocalPlayerEntity, StringComparison.Ordinal))
                    {
                        // Konec hry ohlásí CompleteGame; umístění řekne víc než holý výsledek.
                        State.Result = value;
                    }
                }
                break;
            case "PLAYER_LEADERBOARD_PLACE" or "PLAYER_TRIPLES":
                break;
            default:
                return changed;
        }

        UpdateLobbyParticipant(entity, entityId, tag, value);

        return true;
    }

    /// <summary>Zapíše tag do stavu entity. Vrací <c>true</c>, pokud jde o uživatelsky viditelnou změnu.</summary>
    private bool ApplyEntityTag(TrackedEntity entity, string tag, string value)
    {
        switch (tag.ToUpperInvariant())
        {
            case "CONTROLLER" when TryInt(value, out var controller):
                entity.ControllerId = controller;
                entity.InitialControllerId ??= controller;
                TryRegisterOfferedRace(entity);
                return false;
            case "CARDTYPE":
                entity.CardType = value;
                entity.IsHero |= value.Equals("HERO", StringComparison.OrdinalIgnoreCase);
                return false;
            case "CARDRACE":
                entity.Race = value;
                return TryRegisterOfferedRace(entity);
            case "IS_BACON_POOL_MINION":
                entity.IsPoolMinion = value != "0";
                return TryRegisterOfferedRace(entity);
            case "PLAYER_ID" when entity.IsHero && TryInt(value, out var lobbyPlayerId):
                entity.LobbyPlayerId = lobbyPlayerId;
                RefreshCombatSlots(entity);
                return true;
            case "BACON_DUO_TEAM_ID" when TryInt(value, out var teamId) && teamId > 0:
                entity.DuoTeamId = teamId;
                State.IsDuos = true;
                SyncLobbyHero(entity);
                return true;
            case "ZONE":
                entity.Zone = value;
                entity.InitialZone ??= value;
                TryRegisterOfferedRace(entity);
                return true;
            case "ZONE_POSITION" when TryInt(value, out var zonePosition):
                entity.ZonePosition = zonePosition;
                TryRegisterOfferedRace(entity);
                return true;
            case "ATK" when TryInt(value, out var attack):
                entity.Attack = attack;
                return true;
            case "HEALTH" when TryInt(value, out var health) && health > 0:
                entity.Health = health;
                return true;
            case "ARMOR" when TryInt(value, out var armor):
                entity.Armor = armor;
                return true;
            case "DAMAGE" when TryInt(value, out var damage):
                entity.Damage = damage;
                RecordDamageDealt(entity, damage);
                return true;
            case "COST" when TryInt(value, out var cost):
                entity.Cost = cost;
                RegisterTavernUpgradeCost(entity, cost);
                return RegisterRefreshCost(entity, cost);
            case "BACON_FREE_REFRESH_COUNT" when TryInt(value, out var freeRefreshes):
                return RegisterFreeRefreshes(entity, freeRefreshes);
            // Strop tavern tieru platí pro celou lobby a hra ho píše na entity hrdinů, takže
            // se bere od kohokoli. Nula znamená, že ho hra ještě nenastavila.
            case "BACON_MAX_PLAYER_TECH_LEVEL" when TryInt(value, out var maxTier) && maxTier > 0:
                State.MaxTavernTier = maxTier;
                return false;
            case "BACON_TRINKET":
                entity.IsTrinket |= value != "0";
                return false;
            case "TAG_SCRIPT_DATA_NUM_1" when TryInt(value, out var scriptNum1):
                entity.ScriptDataNum1 = scriptNum1;
                return RegisterUndeadBuff(entity, scriptNum1);
            case "TAG_SCRIPT_DATA_NUM_2" when TryInt(value, out var scriptNum2):
                entity.ScriptDataNum2 = scriptNum2;
                return false;
            case "BACON_TURNS_LEFT_TO_DISCOVER_TRINKET" when TryInt(value, out var turnsLeft):
                entity.TrinketTurnsLeft = turnsLeft;
                return entity.ControllerId == State.LocalControllerId;
            case "TECH_LEVEL" when TryInt(value, out var techLevel) && techLevel > 0:
                entity.TechLevel = techLevel;
                return true;
            case "PLAYER_TECH_LEVEL" when TryInt(value, out var tavernTier) && tavernTier > 0:
                entity.TavernTier = tavernTier;
                RefreshTavernUpgradeCost();
                return true;
            case "PLAYER_LEADERBOARD_PLACE" when TryInt(value, out var place) && place > 0:
                entity.LeaderboardPlace = place;
                ClaimLeaderboardHero(entity);
                return true;
            case "BACON_COMBAT_PHASE_HERO":
                entity.IsCombatHero = value != "0";
                return false;
            case "PLAYER_TRIPLES" when TryInt(value, out var triples):
                entity.Triples = triples;
                return true;
            case "PREMIUM":
                entity.IsGolden = value != "0";
                return true;
            case "TAUNT":
                entity.HasTaunt = value != "0";
                return true;
            case "DIVINE_SHIELD":
                entity.HasDivineShield = value != "0";
                return true;
            case "REBORN":
                entity.HasReborn = value != "0";
                return true;
            case "POISONOUS":
                entity.HasPoisonous = value != "0";
                return true;
            case "VENOMOUS":
                entity.HasVenomous = value != "0";
                return true;
            case "WINDFURY" or "MEGA_WINDFURY":
                entity.HasWindfury = value != "0";
                return true;
            case "PLAYSTATE":
                entity.PlayState = value;
                return false;
            case "NEXT_OPPONENT_PLAYER_ID" when TryInt(value, out var nextOpponent) &&
                                                entity.ControllerId == State.LocalControllerId:
                State.NextOpponentPlayerId = nextOpponent > 0 ? nextOpponent : null;
                return true;
            case "NEXT_OPPONENT_TEAMMATE_PLAYER_ID" when TryInt(value, out var opponentMate) &&
                                                        entity.ControllerId == State.LocalControllerId:
                State.NextOpponentTeammatePlayerId = opponentMate > 0 ? opponentMate : null;
                State.IsDuos = true;
                return true;
            // Kdo z dvojice bojuje v příštím souboji první. Hra to na konci souboje napíše na
            // všechny hrdiny lobby; na entitu hráče ho zvlášť čte ApplyLocalPlayerTag.
            case "BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT":
                entity.FightsFirstNextCombat = value != "0";
                State.IsDuos = true;
                return false;
            // Ikona na kartě v nabídce nebo v ruce: tahle karta by spoluhráči složila pár či triple.
            case "BACON_DUO_PAIR_CANDIDATE_TEAMMATE":
                entity.IsTeammatePairCandidate = value != "0";
                State.IsDuos = true;
                return true;
            case "BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE":
                entity.IsTeammateTripleCandidate = value != "0";
                State.IsDuos = true;
                return true;
            case "IS_USING_PASS_OPTION" when value != "0":
                return AnnouncePass(entity);
            default:
                return false;
        }
    }

    /// <summary>
    /// Předání karty spoluhráči. Hra ho v logu značí tagem <c>IS_USING_PASS_OPTION</c> na kartě
    /// v ruce; karta pak odejde do <c>SETASIDE</c> a u spoluhráče vznikne kopie, kterou už log
    /// neukáže. Opačný směr, tedy karta od spoluhráče, v logu žádnou vlastní stopu nemá.
    /// </summary>
    private bool AnnouncePass(TrackedEntity entity)
    {
        if (entity.Name is not { } name)
        {
            return false;
        }

        State.IsDuos = true;
        State.AddEvent($"Předal jsem spoluhráči: {name}.");
        return true;
    }

    /// <summary>Tagy, které hra publikuje na entitě lokálního hráče: zlato a výsledek posledního souboje.</summary>
    private bool ApplyLocalPlayerTag(TrackedEntity entity, string tag, string value)
    {
        if (entity.EntityId != localPlayerEntityId || !TryInt(value, out var numeric))
        {
            return false;
        }

        var upper = tag.ToUpperInvariant();
        switch (upper)
        {
            case "RESOURCES":
                State.Gold = numeric;
                return true;
            case "RESOURCES_USED":
                State.GoldSpent = numeric;
                return true;
            case "TEMP_RESOURCES":
                State.TempGold = numeric;
                return true;
            case "MAXRESOURCES":
                State.MaxGold = numeric;
                return true;
            case "BACON_DUO_TEAMMATE_PLAYER_ID" when numeric > 0:
                State.TeammatePlayerId = numeric;
                State.IsDuos = true;
                MarkTeammate(numeric);
                LinkLocalTeam();
                return true;
            case "BACON_DUO_TEAM_ID" when numeric > 0:
                // Vlastní tým hra napíše na entitu hráče, ne na hrdinu jako u ostatních.
                localTeamId = numeric;
                State.IsDuos = true;
                LinkLocalTeam();
                return true;
            // Entita hráče je pro dvojici soupeřů spolehlivější než entita hrdiny: hrdina se
            // každý souboj přegeneruje a jeho CONTROLLER nemusí být v tu chvíli ještě nastavený.
            case "NEXT_OPPONENT_PLAYER_ID":
                State.NextOpponentPlayerId = numeric > 0 ? numeric : null;
                return true;
            case "NEXT_OPPONENT_TEAMMATE_PLAYER_ID":
                State.NextOpponentTeammatePlayerId = numeric > 0 ? numeric : null;
                State.IsDuos = true;
                return true;
            case "BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT":
                State.LocalFightsFirst = numeric != 0;
                State.IsDuos = true;
                return true;
            case "BACON_WON_LAST_COMBAT" when numeric != 0:
                return RecordCombatWin();
            // Zlato navíc na příští kolo. Nepatří k bonusům pro celou hru: ty se po souboji
            // vracejí na starou hodnotu, kdežto tohle se po utracení skutečně vynuluje.
            case "BACON_PLAYER_EXTRA_GOLD_NEXT_TURN":
                State.ExtraGoldNextTurn = numeric > 0 ? numeric : null;
                return true;
            case "DAMAGE_DEALT_TO_HERO_LAST_TURN":
                return RecordCombatDamage(numeric);
            default:
                return ApplyGlobalBuffTag(upper, numeric);
        }
    }

    /// <summary>
    /// Bonusy platné pro celou hru, které hra drží na entitě hráče: tavern kouzla, blood gemy
    /// a plošné buffy na elementály a piráty. Hodnota je kumulativní součet, takže se jen zrcadlí.
    /// </summary>
    private bool ApplyGlobalBuffTag(string tag, int numeric)
    {
        var buffs = State.Buffs;
        switch (tag)
        {
            case "TAVERN_SPELL_ATTACK_INCREASE":
                return Set(buffs.SpellAttack, numeric, value => buffs.SpellAttack = value);
            case "TAVERN_SPELL_HEALTH_INCREASE":
                return Set(buffs.SpellHealth, numeric, value => buffs.SpellHealth = value);
            case "BACON_BLOODGEMBUFFATKVALUE":
                return Set(buffs.BloodGemAttack, numeric, value => buffs.BloodGemAttack = value);
            case "BACON_BLOODGEMBUFFHEALTHVALUE":
                return Set(buffs.BloodGemHealth, numeric, value => buffs.BloodGemHealth = value);
            case "BACON_ELEMENTAL_BUFFATKVALUE":
                return Set(buffs.ElementalAttack, numeric, value => buffs.ElementalAttack = value);
            case "BACON_ELEMENTAL_BUFFHEALTHVALUE":
                return Set(buffs.ElementalHealth, numeric, value => buffs.ElementalHealth = value);
            case "BACON_PIRATE_BUFFATKVALUE":
                return Set(buffs.PirateAttack, numeric, value => buffs.PirateAttack = value);
            case "BACON_PIRATE_BUFFHEALTHVALUE":
                return Set(buffs.PirateHealth, numeric, value => buffs.PirateHealth = value);
            default:
                return false;
        }

        // Hra na začátku každého souboje všechny tyhle tagy vynuluje a o pár sekund později
        // vrátí zpět. V jedné hře bonusy jen přibývají, takže návrat na nulu je vždycky jen
        // ten průběžný reset a zahodí se; skutečnou nulu nastaví až nová hra.
        static bool Set(int current, int incoming, Action<int> assign)
        {
            if (incoming == current || (incoming == 0 && current > 0))
            {
                return false;
            }

            assign(incoming);
            return true;
        }
    }

    /// <summary>
    /// Naváže BattleTag na entitu hrdiny podle <c>TAG_CHANGE Entity=&lt;BattleTag&gt; tag=HERO_ENTITY</c>.
    /// </summary>
    private bool BindHeroOwner(string owner, string value)
    {
        if (!TryInt(value, out var heroEntityId) || IsNumericEntity(owner))
        {
            return false;
        }

        var battleTag = BaseName(owner);
        if (!IsPlausibleBattleTag(battleTag))
        {
            return false;
        }

        heroOwners[heroEntityId] = battleTag;
        SyncLobbyHero(State.Entity(heroEntityId));
        return true;
    }

    /// <summary>
    /// Než hra jméno soupeře doplní, publikuje na jeho místě jméno karty, typicky Bobův skin.
    /// BattleTag se proto uzná jen tehdy, pokud nejde o zobrazované jméno nějaké nehráčské entity.
    /// </summary>
    private bool IsPlausibleBattleTag(string battleTag) =>
        !entityIdsByName.TryGetValue(battleTag, out var entityId) || entityPlayers.ContainsKey(entityId);

    private bool RecordCombatWin()
    {
        if (State.CombatHistory.Count == 0 || State.CombatHistory[^1].Outcome == "WON")
        {
            return false;
        }

        var combat = State.CombatHistory[^1];
        combat.Outcome = "WON";
        combat.DamageTaken ??= 0;
        return AnnounceCombat(combat);
    }

    /// <summary>
    /// Ohlásí souboj, nebo předchozí hlášku o témže souboji přepíše. Výsledek, utrpěné a dané
    /// poškození dorazí po částech a bez přepisu by v panelu zůstalo několik protichůdných vět
    /// o jednom souboji.
    /// </summary>
    private bool AnnounceCombat(CombatRound combat)
    {
        // Výsledek souboje dorazí až v další nákupní fázi, u remízy dokonce až se začátkem
        // dalšího souboje. Bez čísla kola by hláška visela pod cizím nadpisem.
        var round = combat.Round is { } number ? $"Kolo {number} · " : string.Empty;
        var who = State.IsDuos ? "tým" : "já";
        var message = $"{round}{who} vs {OpponentLabel(combat)}: {ResultName(combat.Outcome)}{DamageSummary(combat)}.";
        if (string.Equals(message, announcedCombatMessage, StringComparison.Ordinal))
        {
            return false;
        }

        if (!ReferenceEquals(announcedCombat, combat) || announcedCombatMessage is null ||
            !State.UpdateEvent(announcedCombatMessage, message))
        {
            State.AddEvent(message);
        }

        announcedCombat = combat;
        announcedCombatMessage = message;
        return true;
    }

    /// <summary>
    /// Tag <c>DAMAGE_DEALT_TO_HERO_LAST_TURN</c> se na začátku kola nejdřív vynuluje a teprve pak
    /// dostane skutečnou hodnotu. Nula proto neznamená souboj bez poškození a musí se ignorovat.
    /// </summary>
    private bool RecordCombatDamage(int damage)
    {
        if (damage <= 0 || State.CombatHistory.Count == 0)
        {
            return false;
        }

        var combat = State.CombatHistory[^1];
        if (combat.DamageTaken == damage)
        {
            return false;
        }

        // V Duos hra tag zapíše dvakrát, když lokální hráč bojoval první: jednou za soubojovou
        // kopii spoluhráče, která souboj dobojovávala, a hned potom znovu za vlastního hrdinu,
        // takže druhá hodnota je dvojnásobek. Ve všech pěti měřených zápasech odpovídal
        // skutečný úbytek životů týmu vždy první hodnotě (4→8, 12→24, 15→30, 9→18, 7→14).
        if (State.IsDuos && combat.DamageTaken is > 0)
        {
            return false;
        }

        combat.DamageTaken = damage;
        combat.Outcome ??= "LOST";
        return AnnounceCombat(combat);
    }

    private bool SetCombatPhase(bool inCombat)
    {
        if (State.IsCombatPhase == inCombat)
        {
            return false;
        }

        if (!inCombat)
        {
            // Souboj bez jediného útoku se zachytí aspoň takhle, byť už po odklizení desek. Jen
            // na straně, kde se hrdina nestřídal: po výměně by se pod odcházejícího hrdinu
            // uložily zbytky desky toho, kdo přišel po něm.
            if (localCapturePending && !localSideSwitched)
            {
                CaptureSide(local: true);
            }

            if (opponentCapturePending && !opponentSideSwitched)
            {
                CaptureSide(local: false);
            }

            localCapturePending = false;
            opponentCapturePending = false;
            localSideHeroId = LocalLobbyHeroId() ?? localSideHeroId;
            opponentSideHeroId = null;
            State.IsCombatPhase = false;
            State.Epoch++;
            State.CurrentCombat = null;
            State.CombatLocalSlot = null;
            State.CombatOpponentSlot = null;
            return true;
        }

        State.IsCombatPhase = true;
        State.Epoch++;
        capturedSlotsThisCombat.Clear();
        localSideSwitched = false;
        opponentSideSwitched = false;
        localCapturePending = true;
        opponentCapturePending = true;

        // Na začátku souboje stojí na lokální straně vlastní hrdina; v Duos ho HERO_ENTITY
        // vymění za spoluhráče ještě před prvním útokem, pokud bojuje první. Soupeřova strana
        // se doplní, až hra na soupeřovu entitu hráče pověsí soubojovou kopii hrdiny.
        localSideHeroId = LocalLobbyHeroId() ?? localSideHeroId;
        opponentSideHeroId = null;
        State.CombatLocalSlot = SlotOf(localSideHeroId) ?? State.LocalPlayerSlot;
        State.CombatOpponentSlot = State.NextOpponentPlayerId;
        FinishPreviousCombat();
        var combat = new CombatRound(State.Round, State.NextOpponentPlayerId);
        if (State.NextOpponent is { } opponent)
        {
            combat.OpponentHeroName = opponent.HeroName;
            combat.OpponentBattleTag = opponent.BattleTag;
        }

        if (State.IsDuos)
        {
            combat.OpponentTeammatePlayerId = State.NextOpponentTeammatePlayerId;
            if (State.NextOpponentTeammate is { } opponentMate)
            {
                combat.OpponentTeammateHeroName = opponentMate.HeroName;
                combat.OpponentTeammateBattleTag = opponentMate.BattleTag;
            }
        }

        combat.OpponentDamageAtStart = State.Slot(combat.OpponentPlayerId)?.Damage ?? 0;
        State.BeginCombat(combat);
        return true;
    }

    /// <summary>
    /// Remíza se v logu nijak neohlásí: hra publikuje jen změny, takže po souboji bez poškození
    /// nepřijde ani <c>BACON_WON_LAST_COMBAT</c>, ani <c>DAMAGE_DEALT_TO_HERO_LAST_TURN</c>.
    /// Souboj bez výsledku se proto uzavře až ve chvíli, kdy začíná další.
    /// </summary>
    private void FinishPreviousCombat()
    {
        if (State.CombatHistory.Count == 0 || State.CombatHistory[^1].Outcome is not null)
        {
            return;
        }

        var previous = State.CombatHistory[^1];
        previous.Outcome = "TIED";
        previous.DamageTaken ??= 0;
        AnnounceCombat(previous);
    }

    /// <summary>
    /// Uloží desky obou stran k hrdinům, kteří na nich právě stojí. Cizí desku log ukáže jen
    /// během souboje proti ní, takže tohle je jediná příležitost, jak se k ní vůbec dostat.
    /// V Duos tak postupně přibude deska spoluhráče i obou soupeřů.
    /// </summary>
    private void CaptureCombatBoards()
    {
        if (localCapturePending)
        {
            CaptureSide(local: true);
        }

        if (opponentCapturePending)
        {
            CaptureSide(local: false);
        }
    }

    /// <summary>
    /// Uloží desku jedné strany k hrdinovi, který na ní stojí. Když hrdina slot nemá, na lokální
    /// straně se bere slot lokálního hráče, na soupeřově soubojová kopie hrdiny se slotem.
    /// </summary>
    private void CaptureSide(bool local)
    {
        var slot = local
            ? SlotOf(localSideHeroId) ?? State.LocalPlayerSlot
            : SlotOf(opponentSideHeroId) ?? CombatOpponentSlot();
        if (local)
        {
            localCapturePending = false;
        }
        else
        {
            opponentCapturePending = false;
        }

        // Za zachycený se slot bere jen s neprázdnou deskou. Hrdina s prázdnou deskou se může do
        // souboje vrátit později s plnou, typicky když spoluhráč bojoval první a vlastní miniony
        // se na desku postaví až s příchodem vlastního hrdiny.
        if (slot is { } known && StoreBoard(known, local ? State.PlayerBoard : State.OpponentBoard))
        {
            capturedSlotsThisCombat.Add(known);
        }
    }

    /// <summary>
    /// Sleduje, kdo stojí na které straně desky, podle <c>HERO_ENTITY</c> entit hráčů. V Duos
    /// hra během souboje přepíná obě strany mezi oběma hrdiny dvojice: první bojuje, a jakmile
    /// padne některá z desek, přijde na tutéž stranu druhý hrdina s vlastní deskou. Bez tohoto
    /// sledování by se deska spoluhráče ukládala pod lokálního hráče a druhý soupeř by neměl
    /// desku vůbec. Bere se jen z <c>GameState</c>; opožděná fronta by strany vracela zpátky.
    /// </summary>
    private bool TrackSideHero(string owner, string value)
    {
        if (IsNumericEntity(owner) || !TryInt(value, out var heroEntityId) ||
            State.LocalPlayerEntity is not { } localName)
        {
            return false;
        }

        var local = BaseName(owner).Equals(localName, StringComparison.Ordinal);
        var previous = local ? localSideHeroId : opponentSideHeroId;
        if (previous == heroEntityId)
        {
            return false;
        }

        if (State.IsCombatPhase && SlotOf(previous) is not null)
        {
            // Hrdinu vystřídal jiný. Deska, která ještě neprošla útokem, se uloží teď; jinak by se
            // pod odcházejícího hrdinu zapsala deska toho, kdo přichází.
            if (local ? localCapturePending : opponentCapturePending)
            {
                CaptureSide(local);
            }

            if (local)
            {
                localSideSwitched = true;
            }
            else
            {
                opponentSideSwitched = true;
            }
        }

        if (local)
        {
            localSideHeroId = heroEntityId;
        }
        else
        {
            opponentSideHeroId = heroEntityId;
        }

        if (!State.IsCombatPhase)
        {
            return false;
        }

        RefreshCombatSlots(State.Entity(heroEntityId));
        return true;
    }

    /// <summary>
    /// Doplní slot strany podle hrdiny, který na ní stojí, a nastaví jí čekání na uložení desky.
    /// Volá se po <c>HERO_ENTITY</c> i po <c>PLAYER_ID</c>, protože slot může na soubojovou kopii
    /// dorazit až po tom, co ji hra pověsí na entitu hráče. Hrdina bez slotu, typicky Bob na
    /// konci souboje, žádnou desku k uložení nemá.
    /// </summary>
    private void RefreshCombatSlots(TrackedEntity hero)
    {
        if (!State.IsCombatPhase)
        {
            return;
        }

        var slot = hero.IsHero ? hero.LobbyPlayerId : null;
        var pending = slot is { } known && !capturedSlotsThisCombat.Contains(known);
        if (hero.EntityId == localSideHeroId)
        {
            localCapturePending = pending;
            State.CombatLocalSlot = slot ?? State.CombatLocalSlot;
        }

        if (hero.EntityId == opponentSideHeroId)
        {
            opponentCapturePending = pending;
            State.CombatOpponentSlot = slot ?? State.CombatOpponentSlot;
        }
    }

    private int? SlotOf(int? entityId) =>
        entityId is { } id && State.TryGetEntity(id, out var hero) && hero.IsHero ? hero.LobbyPlayerId : null;

    /// <summary>Entita hrdiny lokálního hráče v lobby, tedy ta, která drží jeho živé statistiky.</summary>
    private int? LocalLobbyHeroId() =>
        State.LocalPlayerSlot is { } slot && leaderboardHeroes.TryGetValue(slot, out var hero) ? hero : null;

    /// <summary>Uloží desku ke slotu. Prázdnou desku neukládá a vrací <c>false</c>.</summary>
    private bool StoreBoard(int lobbyPlayerId, IReadOnlyList<BoardMinion> board)
    {
        if (board.Count == 0)
        {
            return false;
        }

        var participant = State.LobbyParticipant(lobbyPlayerId);
        participant.LastBoard = board;
        participant.LastBoardRound = State.Round;
        return true;
    }

    /// <summary>
    /// Slot právě přítomného soupeře. Soubojová kopie jeho hrdiny je spolehlivější než
    /// <c>NEXT_OPPONENT_PLAYER_ID</c>, který se v logu mění dřív, než souboj doopravdy začne.
    /// </summary>
    private int? CombatOpponentSlot() => State.Entities
        .FirstOrDefault(entity => entity is { IsHero: true, IsCombatHero: true, LobbyPlayerId: not null } &&
                                  entity.ControllerId == State.OpponentControllerId &&
                                  entity.Epoch == State.Epoch)?.LobbyPlayerId
        ?? State.CurrentCombat?.OpponentPlayerId;

    private void CompleteGame()
    {
        FinishPreviousCombat();
        State.IsGameActive = false;
        State.IsCombatPhase = false;
        State.CurrentCombat = null;
        if (announcedGameOver)
        {
            return;
        }

        announcedGameOver = true;
        if (State.LocalParticipant?.LeaderboardPlace is { } place)
        {
            State.FinalPlace = place;
            var team = State.IsDuos ? " s týmem" : string.Empty;
            State.AddEvent($"Konec hry{team}: {place}. místo z {State.PlaceCount}.");
        }
        else
        {
            State.AddEvent($"Konec hry: {ResultName(State.Result)}.");
        }
    }

    private void DetectBattlegroundsSignal(string line)
    {
        if (line.Contains("BACON", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("BATTLEGROUNDS", StringComparison.OrdinalIgnoreCase))
        {
            State.BattlegroundsSignalSeen = true;
        }
    }

    private static string PhaseName(string value) => value.ToUpperInvariant() switch
    {
        // V Battlegrounds není mulligan výměnou karet, ale výběrem hrdiny.
        "BEGIN_MULLIGAN" => "výběr hrdiny",
        "BEGIN_FIRST" or "BEGIN_SHUFFLE" or "BEGIN_DRAW" => "start hry",
        "MAIN_READY" => "příprava",
        "MAIN_START_TRIGGERS" => "spouštění kola",
        "MAIN_START" => "začátek kola",
        "MAIN_ACTION" => "nákup",
        "MAIN_COMBAT" => "souboj",
        "MAIN_END" => "konec kola",
        "MAIN_CLEANUP" => "úklid kola",
        "MAIN_NEXT" => "přechod do dalšího kola",
        "FINAL_WRAPUP" => "uzavírání hry",
        "FINAL_GAMEOVER" => "konec hry",
        _ => value.ToLowerInvariant()
    };

    private static string ResultName(string? value) => value switch
    {
        "WON" => "výhra",
        "LOST" => "prohra",
        "TIED" => "remíza",
        null => "—",
        _ => value.ToLowerInvariant()
    };

    /// <summary>
    /// Popisek souboje. V Duos se bojuje proti celé dvojici: první z ní nastupuje hned a druhý
    /// se přidá, jakmile padne některá z desek. V logu se soubojové kopie obou soupeřů během
    /// jednoho souboje vystřídají na téže straně desky, takže hláška jmenuje oba.
    /// </summary>
    private string OpponentLabel(CombatRound combat)
    {
        var first = SlotLabel(combat.OpponentPlayerId, combat.OpponentHeroName, combat.OpponentBattleTag);
        if (!State.IsDuos || combat.OpponentTeammatePlayerId is null)
        {
            return first;
        }

        var second = SlotLabel(combat.OpponentTeammatePlayerId, combat.OpponentTeammateHeroName,
            combat.OpponentTeammateBattleTag);
        return $"{first} + {second}";
    }

    private string SlotLabel(int? playerId, string? heroName, string? battleTag)
    {
        var participant = State.Slot(playerId);
        return heroName ?? participant?.HeroName ?? battleTag ?? participant?.BattleTag ??
               (playerId is { } slot ? $"hráč #{slot}" : "neznámý soupeř");
    }

    /// <summary>
    /// Jméno do hlášek. Hrdina se pamatuje lépe než BattleTag a v Duos ho log dokonce zná
    /// i u hráčů, jejichž jméno nikdy neodhalí.
    /// </summary>
    private static string ParticipantLabel(LobbyParticipant participant) =>
        participant.HeroName ?? participant.BattleTag ?? $"hráč #{participant.PlayerId}";

    /// <summary>
    /// Zapíše, kolik souboj uštědřil soupeři. Log dané poškození nehlásí, ale soupeřovu hrdinovi
    /// během souboje roste tag <c>DAMAGE</c>; jeho přírůstek od začátku souboje je právě ono.
    /// Nulování na začátku kola se ignoruje, jinak by přírůstek vyšel záporně.
    /// </summary>
    private void RecordDamageDealt(TrackedEntity entity, int damage)
    {
        // Bere se poslední souboj, ne jen ten právě běžící: poškození soupeře dorazí do logu
        // až po přepnutí fáze zpátky na nákup, stejně jako to vlastní.
        if (!entity.IsHero || State.CombatHistory.Count == 0)
        {
            return;
        }

        // V Duos sdílí dvojice životy, takže poškození roste na obou soupeřích stejně.
        var combat = State.CombatHistory[^1];
        if (entity.LobbyPlayerId != combat.OpponentPlayerId &&
            (combat.OpponentTeammatePlayerId is null || entity.LobbyPlayerId != combat.OpponentTeammatePlayerId))
        {
            return;
        }

        var dealt = damage - combat.OpponentDamageAtStart;
        if (dealt <= (combat.DamageDealt ?? 0))
        {
            return;
        }

        combat.DamageDealt = dealt;
        if (combat.Outcome is not null)
        {
            // Výsledek už byl ohlášený, takže se hláška jen doplní o dané poškození.
            AnnounceCombat(combat);
        }
    }

    /// <summary>
    /// Zapíše slotu BattleTag a udrží tabulku vlastnictví přesnou. Když slot jméno mění, musí
    /// se to staré uvolnit, jinak by zůstalo zapsané na slotu, který ho už nezobrazuje, a nikde
    /// jinde by se pak uchytit nemohlo.
    /// </summary>
    private void AssignBattleTag(LobbyParticipant participant, string battleTag)
    {
        if (participant.BattleTag is { } previous && !previous.Equals(battleTag, StringComparison.Ordinal) &&
            battleTagLobbyPlayers.TryGetValue(previous, out var previousSlot) &&
            previousSlot == participant.PlayerId)
        {
            battleTagLobbyPlayers.Remove(previous);
        }

        participant.BattleTag = battleTag;
        battleTagLobbyPlayers[battleTag] = participant.PlayerId;
    }

    /// <summary>
    /// Vlastní BattleTag smí obsadit jen slot, jehož <c>PLAYER_ID</c> sedí na entitu lokálního
    /// hráče. V Duos totiž hra pověsí <c>HERO_ENTITY</c> lokální entity i na hrdinu spoluhráče,
    /// a pokud spoluhráč bojuje první, stihne slot zabrat dřív než skutečný majitel. Pak by
    /// spoluhráč vystupoval jako lokální hráč včetně cizí desky.
    /// </summary>
    private bool OwnsLocalIdentity(string battleTag, int lobbyPlayerId) =>
        localPlayerId is not { } localSlot ||
        !battleTag.Equals(State.LocalPlayerEntity, StringComparison.Ordinal) ||
        lobbyPlayerId == localSlot;

    /// <summary>Oba hrdiny téhož týmu, lokální hráč první; mimo Duos jen hráče samotného.</summary>
    private IReadOnlyList<LobbyParticipant> TeamOf(LobbyParticipant participant) =>
        participant.TeamId is { } teamId
            ?
            [
                .. State.LobbyParticipants
                    .Where(member => member.TeamId == teamId)
                    .OrderByDescending(member => member.IsLocal)
                    .ThenByDescending(member => member.IsTeammate)
                    .ThenBy(member => member.PlayerId)
            ]
            : [participant];

    /// <summary>
    /// V Duos sdílí dvojice jednu zásobu životů: hra píše <c>HEALTH</c>, <c>ARMOR</c> i
    /// <c>DAMAGE</c> na oba hrdiny se stejnou hodnotou, jen ne ve stejném okamžiku (naměřeno
    /// až 4 400 řádků rozestupu). Bez zrcadlení by tabulka chvíli ukazovala dva různé stavy
    /// jednoho týmu a vyřazení by se hlásilo po hráčích, ačkoli padá celý tým najednou.
    /// </summary>
    private void MirrorTeamHealth(LobbyParticipant participant)
    {
        if (!State.IsDuos || participant.TeamId is not { } teamId)
        {
            return;
        }

        foreach (var mate in State.LobbyParticipants)
        {
            if (mate.TeamId != teamId || ReferenceEquals(mate, participant))
            {
                continue;
            }

            mate.Health = participant.Health ?? mate.Health;
            mate.Armor = participant.Armor ?? mate.Armor;
            mate.Damage = participant.Damage ?? mate.Damage;

            // Zrcadlí se i entita hrdiny spoluhráče v lobby. Její vlastní tag dorazí později
            // a do té doby by ji každá další zmínka v logu vrátila na starou hodnotu, čímž by
            // spoluhráč, a přes zrcadlení i lokální hráč, na chvíli obživl.
            if (leaderboardHeroes.TryGetValue(mate.PlayerId, out var heroId) &&
                State.TryGetEntity(heroId, out var hero))
            {
                hero.Health = participant.Health ?? hero.Health;
                hero.Armor = participant.Armor ?? hero.Armor;
                hero.Damage = participant.Damage ?? hero.Damage;
            }
        }
    }

    /// <summary>
    /// Doplněk hlášky o souboji: kolik poškození padlo na kterou stranu. U remízy nepadlo nic,
    /// takže se nepíše nic. V Duos ho dostává i dává tým, proto množné číslo.
    /// </summary>
    private string DamageSummary(CombatRound combat)
    {
        var (took, dealt) = State.IsDuos ? ("dostali jsme", "dali jsme") : ("dostal jsem", "dal jsem");
        if (combat.DamageTaken is > 0)
        {
            return $", {took} {combat.DamageTaken} dmg";
        }

        return combat.DamageDealt is > 0 ? $", {dealt} {combat.DamageDealt} dmg" : string.Empty;
    }

    private void MarkTeammate(int playerId)
    {
        foreach (var participant in State.LobbyParticipants)
        {
            participant.IsTeammate = participant.PlayerId == playerId;
        }
    }

    /// <summary>
    /// Doplní číslo týmu lokálnímu hráči a jeho spoluhráči. Jeden z nich ho v logu mít nemusí:
    /// vlastní tým hra píše na entitu hráče, kdežto u ostatních na entitu hrdiny.
    /// </summary>
    private void LinkLocalTeam()
    {
        var local = State.Slot(State.LocalPlayerSlot);
        var mate = State.Slot(State.TeammatePlayerId);
        var teamId = localTeamId ?? local?.TeamId ?? mate?.TeamId;
        if (teamId is not { } team)
        {
            return;
        }

        if (local is not null)
        {
            local.TeamId = team;
        }

        if (mate is not null)
        {
            mate.TeamId = team;
        }
    }

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool IsGameEntity(string entity) =>
        entity.Contains("GameEntity", StringComparison.OrdinalIgnoreCase);

    private void ResetIdentities()
    {
        entityNames.Clear();
        entityIdsByName.Clear();
        entityAliases.Clear();
        playerEntities.Clear();
        entityPlayers.Clear();
        namedPlayers.Clear();
        heroOwners.Clear();
        battleTagLobbyPlayers.Clear();
        tavernUpgradeCosts.Clear();
        cardNames.Clear();
        leaderboardHeroes.Clear();
        eliminations.Clear();
        capturedSlotsThisCombat.Clear();
        localPlayerId = null;
        localPlayerEntityId = null;
        announcedCombat = null;
        announcedCombatMessage = null;
        localTeamId = null;
        announcedGameOver = false;
        announcedRound = null;
        localSideHeroId = null;
        opponentSideHeroId = null;
        localCapturePending = false;
        opponentCapturePending = false;
        localSideSwitched = false;
        opponentSideSwitched = false;
    }

    /// <summary>
    /// Z entity descriptoru se přebírá jen Card ID. Zóna, pozice a controller v descriptoru popisují
    /// stav <em>před</em> změnou na témže řádku a hra je po odstranění karty ještě dlouho opakuje,
    /// takže by odstraněné karty trvale zůstávaly na desce. Autoritativní jsou jen tagy
    /// <c>ZONE</c>, <c>ZONE_POSITION</c> a <c>CONTROLLER</c>.
    /// </summary>
    private void MergeCardId(TrackedEntity entity, string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        entity.CardId = cardId;
        entity.IsHero |= IsHeroCard(cardId);

        // Slot na trinket se označí kartou, se kterou entita vznikla. Po výběru trinketu se
        // tatáž entita přepíše na vybranou kartu, takže později už by se slot nedal poznat.
        entity.TrinketSlot ??= Trinket.SlotFromCardId(cardId);
        if (entity.Name is null && LookupCardName(cardId) is { } knownName)
        {
            NameEntity(entity, knownName);
        }
    }

    private void NameEntity(TrackedEntity entity, string name)
    {
        if (string.Equals(entity.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        var wasUnnamed = entity.Name is null;
        entity.Name = name;
        if (wasUnnamed)
        {
            PatchStoredBoards(entity.EntityId, name);
        }
    }

    /// <summary>
    /// Jméno soupeřova minionu dorazí z opožděné fronty až po zachycení desky. Uložený snímek se
    /// proto dodatečně dopíše, aby v přehledu nezůstalo `entity #id`.
    /// </summary>
    private void PatchStoredBoards(int entityId, string name)
    {
        foreach (var participant in State.LobbyParticipants)
        {
            if (participant.LastBoard is not BoardMinion[] board)
            {
                continue;
            }

            for (var index = 0; index < board.Length; index++)
            {
                if (board[index].EntityId == entityId)
                {
                    board[index] = board[index] with { Name = name };
                }
            }
        }
    }

    /// <summary>
    /// Soupeřovi minioni vzniknou v souboji jen s Card ID a jméno hra doplní až se zpožděním.
    /// Pokud stejná karta už někdy pojmenovaná byla, dá se jméno použít okamžitě.
    /// </summary>
    private string? LookupCardName(string cardId)
    {
        if (cardNames.TryGetValue(cardId, out var name))
        {
            return name;
        }

        // Zlatá varianta má stejné jméno, jen Card ID s příponou _G.
        return cardId.EndsWith("_G", StringComparison.Ordinal) &&
               cardNames.TryGetValue(cardId[..^2], out var baseName)
            ? baseName
            : null;
    }

    private void RegisterEntity(int entityId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Equals(entityId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            displayName.Contains("UNKNOWN ENTITY", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        entityNames[entityId] = displayName;
        entityIdsByName[BaseName(displayName)] = entityId;
        var entity = State.Entity(entityId);
        NameEntity(entity, BaseName(displayName));
        if (entity.CardId is { } cardId)
        {
            cardNames[cardId] = entity.Name!;
        }

        entityAliases[BaseName(displayName)] = displayName;
        State.RenameParticipant(entityId.ToString(CultureInfo.InvariantCulture), displayName);
        State.RenameParticipant($"entity #{entityId}", displayName);
        SyncLobbyHero(State.Entity(entityId));
    }

    private string ResolveEntity(string entity, int? entityId)
    {
        if (entityId is { } id && entityNames.TryGetValue(id, out var nameById))
        {
            return nameById;
        }

        return entityAliases.TryGetValue(entity, out var nameByAlias) ? nameByAlias : entity;
    }

    /// <summary>
    /// Tagy lokálního hráče chodí adresované jeho BattleTagem bez entity descriptoru. Bez tohoto
    /// překladu by se zlato ani výsledek souboje nikam nezapsaly.
    /// </summary>
    private int? ResolveEntityId(string entity) =>
        State.LocalPlayerEntity is { } localName && BaseName(entity).Equals(localName, StringComparison.Ordinal)
            ? localPlayerEntityId
            : null;

    private static string BaseName(string displayName)
    {
        var idMarker = displayName.LastIndexOf(" (#", StringComparison.Ordinal);
        return idMarker > 0 ? displayName[..idMarker] : displayName;
    }

    /// <summary>
    /// Zaznamená typ minionu, který se objevil v nabídce Boba. Sám tag <c>IS_BACON_POOL_MINION</c>
    /// nestačí: pool obsahuje i karty typů, které v lobby nejsou a hra je vyrobí až efektem jiné
    /// karty. Do nabídky se ale dostanou jen typy, které lobby skutečně nabízí.
    /// </summary>
    private bool TryRegisterOfferedRace(TrackedEntity entity) =>
        entity is { IsPoolMinion: true, IsMinion: true, CreatedDuringCombat: false } &&
        entity.InitialControllerId == State.OpponentControllerId &&
        string.Equals(entity.InitialZone, "PLAY", StringComparison.OrdinalIgnoreCase) &&
        entity.ZonePosition > 0 &&
        State.RegisterPoolRace(entity.Race);

    /// <summary>
    /// Slot v lobby si nárokuje ta entita hrdiny, která dostala <c>PLAYER_LEADERBOARD_PLACE</c>.
    /// Jen ta drží průběžné HP, armor a tier; soubojové kopie hrdiny je mají resetované.
    /// </summary>
    private void ClaimLeaderboardHero(TrackedEntity entity)
    {
        if (entity.IsHero && !entity.IsCombatHero && entity.LobbyPlayerId is { } lobbyPlayerId)
        {
            leaderboardHeroes[lobbyPlayerId] = entity.EntityId;
        }
    }

    private bool OwnsLobbyStats(TrackedEntity entity, int lobbyPlayerId)
    {
        if (entity.IsCombatHero)
        {
            return false;
        }

        // Dokud slot nemá svého leaderboard hrdinu, bere se cokoli, ať se lobby naplní hned.
        return !leaderboardHeroes.TryGetValue(lobbyPlayerId, out var owner) || owner == entity.EntityId;
    }

    private void SyncLobbyHero(TrackedEntity entity)
    {
        if (!entity.IsHero || entity.LobbyPlayerId is not { } lobbyPlayerId ||
            string.IsNullOrWhiteSpace(entity.Name) ||
            IsBartender(entity.CardId, entity.Name))
        {
            return;
        }

        var participant = State.LobbyParticipant(lobbyPlayerId);
        participant.HeroName = BaseName(entity.Name);
        participant.HeroCardId = entity.CardId ?? participant.HeroCardId;
        participant.TeamId ??= entity.DuoTeamId;
        participant.IsTeammate |= State.TeammatePlayerId == lobbyPlayerId;
        LinkLocalTeam();
        if (OwnsLobbyStats(entity, lobbyPlayerId))
        {
            participant.Health = entity.Health ?? participant.Health;
            participant.Armor = entity.Armor ?? participant.Armor;
            participant.Damage = entity.Damage ?? participant.Damage;
            participant.TavernTier = entity.TavernTier ?? participant.TavernTier;
            participant.LeaderboardPlace = entity.LeaderboardPlace ?? participant.LeaderboardPlace;
            participant.Triples = entity.Triples ?? participant.Triples;
            participant.PlayState = entity.PlayState ?? participant.PlayState;
            participant.FightsFirstNextCombat = entity.FightsFirstNextCombat ?? participant.FightsFirstNextCombat;
            MirrorTeamHealth(participant);
        }

        // Jeden BattleTag může patřit jen jednomu slotu. V Duos hra přepíná HERO_ENTITY jedné
        // entity hráče mezi oběma hrdiny týmu, takže by jinak jedno jméno obsadilo dva sloty
        // a skutečná jména spoluhráčů by se ztratila.
        if (heroOwners.TryGetValue(entity.EntityId, out var battleTag) &&
            (!battleTagLobbyPlayers.TryGetValue(battleTag, out var ownedSlot) || ownedSlot == lobbyPlayerId) &&
            OwnsLocalIdentity(battleTag, lobbyPlayerId))
        {
            AssignBattleTag(participant, battleTag);
            participant.IsLocal = State.LocalPlayerEntity is not null &&
                                  battleTag.Equals(State.LocalPlayerEntity, StringComparison.Ordinal);
        }
        else if (localPlayerId == entity.ControllerId && localPlayerId == lobbyPlayerId &&
                 State.LocalPlayerEntity is { } localBattleTag)
        {
            AssignBattleTag(participant, localBattleTag);
            participant.IsLocal = true;
        }

        if (participant.IsLocal)
        {
            State.LocalPlayerSlot = lobbyPlayerId;
            RefreshTavernUpgradeCost();
        }

        AnnounceElimination(participant);
    }

    /// <summary>
    /// Ohlásí vyřazení hráče, v Duos celého týmu. Hláška se pamatuje, protože umístění, které
    /// tag <c>PLAYER_LEADERBOARD_PLACE</c> nese v okamžiku vyřazení, je ještě živé pořadí z doby,
    /// kdy hráč žil; skutečné dorazí až o kus dál a hláška se pak přepíše na místě.
    /// </summary>
    private void AnnounceElimination(LobbyParticipant participant)
    {
        if (!participant.IsEliminated)
        {
            return;
        }

        // V Duos sdílí dvojice životy, takže se zrcadlením padají oba hrdinové zároveň. Kdyby
        // přece jen padl jen jeden, hlásí se to bez umístění: to patří týmu, a ten hraje dál.
        var team = State.IsDuos ? TeamOf(participant) : [participant];
        var teamGone = team.All(member => member.IsEliminated);
        var key = State.IsDuos && teamGone && participant.TeamId is { } teamId ? -teamId : participant.PlayerId;
        if (eliminations.TryGetValue(key, out var note))
        {
            // Umístění z tagu se po vyřazení ještě několikrát přeskládá, než se usadí; hlášku
            // podle něj přepisujeme jen tam, kde se spočítat nedalo.
            if (teamGone && !note.Computed && participant.LeaderboardPlace is { } place && place != note.TagPlace)
            {
                var updated = EliminationMessage(participant, team, teamGone, place);
                var replaced = State.UpdateEvent(note.Message, updated);
                eliminations[key] = note with { Message = replaced ? updated : note.Message, TagPlace = place, Place = place };
            }

            return;
        }

        var computed = teamGone && CanComputeEliminationPlace();
        var announcedPlace = teamGone ? computed ? ComputedEliminationPlace() : participant.LeaderboardPlace : null;
        var message = EliminationMessage(participant, team, teamGone, announcedPlace);
        eliminations[key] = new EliminationNote(
            message, participant.LeaderboardPlace, State.Round, announcedPlace, computed, participant, team);
        State.AddEvent(message);
        if (computed)
        {
            RerankEliminationsOfRound(State.Round);
        }
    }

    /// <summary>
    /// Padnou-li v jednom kole dva hráči, v Duos dva týmy, rozhoduje mezi nimi hra podle
    /// zbývajících životů: kdo skončil blíž nule, je výš (naměřeno: −1 před −14 v sólu, −3 před
    /// −4 v Duos). Pořadí tagů <c>DAMAGE</c> v logu to sledovat nemusí, takže se skupina po
    /// každém dalším pádu přeřadí a dotčené hlášky přepíšou.
    /// </summary>
    private void RerankEliminationsOfRound(int? round)
    {
        var group = eliminations
            .Where(pair => pair.Value.Computed && pair.Value.Round == round && pair.Value.Place is not null)
            .ToArray();
        if (group.Length < 2)
        {
            return;
        }

        var places = group.Select(pair => pair.Value.Place!.Value).OrderBy(place => place).ToArray();
        var ranked = group
            .OrderByDescending(pair => pair.Value.Participant.RemainingHealth ?? int.MinValue)
            .ThenBy(pair => pair.Value.Participant.PlayerId)
            .ToArray();
        for (var index = 0; index < ranked.Length; index++)
        {
            var (key, note) = ranked[index];
            if (note.Place == places[index])
            {
                continue;
            }

            var updated = EliminationMessage(note.Participant, note.Team, teamGone: true, places[index]);
            var replaced = State.UpdateEvent(note.Message, updated);
            eliminations[key] = note with { Message = replaced ? updated : note.Message, Place = places[index] };
        }
    }

    private string EliminationMessage(LobbyParticipant participant, IReadOnlyList<LobbyParticipant> team,
        bool teamGone, int? place)
    {
        if (!teamGone)
        {
            return $"{ParticipantLabel(participant)} vypadl, tým hraje dál.";
        }

        var who = State.IsDuos
            ? $"Tým {string.Join(" + ", team.Select(ParticipantLabel))}"
            : ParticipantLabel(participant);
        return place is { } number ? $"{who} vypadl na {number}. místě." : $"{who} vypadl.";
    }

    /// <summary>
    /// Umístění vyřazeného hráče se dá spočítat, jen když je lobby kompletní: osm hráčů, v Duos
    /// navíc ve čtyřech známých týmech.
    /// </summary>
    private bool CanComputeEliminationPlace() =>
        State.LobbyParticipants.Count == 8 &&
        (!State.IsDuos || State.LobbyParticipants.Select(candidate => candidate.TeamId).Distinct().Count() == 4 &&
                          State.LobbyParticipants.All(candidate => candidate.TeamId is not null));

    /// <summary>
    /// Umístění právě vyřazeného hráče z počtu těch, kdo zůstali ve hře. Tag umístění v tu chvíli
    /// ještě nese živé pořadí z doby, kdy hráč žil (naměřeno: hráč vyřazený jako pátý měl v tu
    /// chvíli dvojku), a skutečné se po vyřazení ještě několikrát přeskládá, než se usadí.
    /// V Duos se počítají týmy, protože místo dostává tým.
    /// </summary>
    private int ComputedEliminationPlace() => State.IsDuos
        ? 1 + State.LobbyParticipants
            .GroupBy(candidate => candidate.TeamId)
            .Count(candidates => candidates.Any(member => !member.IsEliminated))
        : 1 + State.LobbyParticipants.Count(candidate => !candidate.IsEliminated);

    private int? ResolveLobbySlot(string entity, int? entityId)
    {
        if (battleTagLobbyPlayers.TryGetValue(BaseName(entity), out var playerByName))
        {
            return playerByName;
        }

        return entityId is { } heroId && State.TryGetEntity(heroId, out var hero) &&
               hero is { IsHero: true } && hero.LobbyPlayerId is { } slot && OwnsLobbyStats(hero, slot)
            ? slot
            : null;
    }

    private void UpdateLobbyParticipant(string entity, int? entityId, string tag, string value)
    {
        if (ResolveLobbySlot(entity, entityId) is not { } resolvedPlayerId)
        {
            return;
        }

        var lobbyParticipant = State.LobbyParticipant(resolvedPlayerId);
        switch (tag.ToUpperInvariant())
        {
            case "PLAYER_TECH_LEVEL" when TryInt(value, out var tier) && tier > 0:
                lobbyParticipant.TavernTier = tier;
                break;
            case "HEALTH" when TryInt(value, out var health) && health > 0:
                lobbyParticipant.Health = health;
                break;
            case "ARMOR" when TryInt(value, out var armor):
                lobbyParticipant.Armor = armor;
                break;
            case "DAMAGE" when TryInt(value, out var damage):
                lobbyParticipant.Damage = damage;
                break;
            case "PLAYER_LEADERBOARD_PLACE" when TryInt(value, out var place) && place > 0:
                lobbyParticipant.LeaderboardPlace = place;
                break;
            case "PLAYER_TRIPLES" when TryInt(value, out var triples):
                lobbyParticipant.Triples = triples;
                break;
            case "PLAYSTATE":
                lobbyParticipant.PlayState = value;
                break;
        }

        MirrorTeamHealth(lobbyParticipant);
        AnnounceElimination(lobbyParticipant);
    }

    /// <summary>Cena upgradu se čte z tlačítka <c>TB_BaconShopTechUpNN_Button</c> lokálního hráče.</summary>
    private void RegisterTavernUpgradeCost(TrackedEntity entity, int cost)
    {
        if (entity.ControllerId != State.LocalControllerId || entity.CardId is null)
        {
            return;
        }

        var match = TechUpButtonRegex().Match(entity.CardId);
        if (!match.Success || !TryInt(match.Groups["tier"].Value, out var tier))
        {
            return;
        }

        tavernUpgradeCosts[tier] = cost;
        RefreshTavernUpgradeCost();
    }

    /// <summary>
    /// Nasčítaný útok undeadů. Tag <c>UNDEAD_ATTACK_BUFF</c> z enumu hry se v logu nevyskytuje;
    /// hodnotu nese enchantment hráče <c>BG25_011pe</c> v <c>TAG_SCRIPT_DATA_NUM_1</c>. Každý
    /// hráč má vlastní, proto se filtruje na controller lokálního hráče.
    ///
    /// Nula se zahazuje ze stejného důvodu jako u ostatních bonusů: enchantment se každým
    /// soubojem přegeneruje a ten odcházející dostane nulu, ačkoli bonus platí dál.
    /// </summary>
    private bool RegisterUndeadBuff(TrackedEntity entity, int value)
    {
        if (entity.ControllerId != State.LocalControllerId ||
            !string.Equals(entity.CardId, UndeadAttackEnchantmentCardId, StringComparison.OrdinalIgnoreCase) ||
            value == State.Buffs.UndeadAttack ||
            (value == 0 && State.Buffs.UndeadAttack > 0))
        {
            return false;
        }

        State.Buffs.UndeadAttack = value;
        return true;
    }

    /// <summary>
    /// Cena rerollu a počet volných rerollů se čtou z tlačítka
    /// <c>TB_BaconShop_8p_Reroll_Button</c> lokálního hráče. Volné rerolly hra píše i na
    /// pomocnou enchantment entitu, ale jen tlačítko drží stav, který uživatel opravdu vidí.
    /// </summary>
    private bool RegisterRefreshCost(TrackedEntity entity, int cost)
    {
        if (!IsLocalRefreshButton(entity))
        {
            return false;
        }

        State.RefreshCost = cost;
        return true;
    }

    private bool RegisterFreeRefreshes(TrackedEntity entity, int count)
    {
        if (!IsLocalRefreshButton(entity))
        {
            return false;
        }

        State.FreeRefreshes = count > 0 ? count : null;
        return true;
    }

    private bool IsLocalRefreshButton(TrackedEntity entity) =>
        entity.ControllerId == State.LocalControllerId &&
        string.Equals(entity.CardId, RefreshButtonCardId, StringComparison.OrdinalIgnoreCase);

    private void RefreshTavernUpgradeCost()
    {
        var currentTier = State.LocalParticipant?.TavernTier;
        State.TavernUpgradeCost = currentTier is { } tier && tavernUpgradeCosts.TryGetValue(tier + 1, out var cost)
            ? cost
            : null;
    }

    private static bool IsHeroCard(string? cardId) =>
        !string.IsNullOrWhiteSpace(cardId) &&
        cardId.Contains("HERO", StringComparison.OrdinalIgnoreCase) &&
        !cardId.Contains("HERO_POWER", StringComparison.OrdinalIgnoreCase);

    // Bob má vlastní skiny, takže se jeho jméno ani přesné Card ID nedá napevno očekávat.
    private static bool IsBartender(string? cardId, string displayName) =>
        displayName.Contains("Bartender Bob", StringComparison.OrdinalIgnoreCase) ||
        cardId?.StartsWith("TB_BaconShopBob", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsNumericEntity(string entity) => int.TryParse(entity, out _);

    [GeneratedRegex(@"^TB_BaconShopTechUp(?<tier>\d+)_Button$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TechUpButtonRegex();
}
