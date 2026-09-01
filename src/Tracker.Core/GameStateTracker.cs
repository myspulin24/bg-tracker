using System.Globalization;
using System.Text.RegularExpressions;

namespace Tracker.Core;

public sealed partial class GameStateTracker
{
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
    private readonly HashSet<int> eliminatedSlots = [];
    private int? localPlayerId;
    private int? localPlayerEntityId;
    private int? announcedRound;
    private bool combatBoardsCaptured;

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
        // okamžik, kdy se dá zachytit soupeřova deska tak, jak do souboje nastoupil.
        if (tag.Equals("PROPOSED_ATTACKER", StringComparison.OrdinalIgnoreCase) &&
            State.IsCombatPhase && !combatBoardsCaptured)
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
            return BindHeroOwner(entity, value) || changed;
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
                    if (State.LocalPlayerEntity is not null &&
                        entity.Equals(State.LocalPlayerEntity, StringComparison.Ordinal))
                    {
                        State.Result = value;
                    }

                    State.AddEvent($"{entity}: {ResultName(value)}.");
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
                return true;
            case "COST" when TryInt(value, out var cost):
                entity.Cost = cost;
                RegisterTavernUpgradeCost(entity, cost);
                return false;
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
            default:
                return false;
        }
    }

    /// <summary>Tagy, které hra publikuje na entitě lokálního hráče: zlato a výsledek posledního souboje.</summary>
    private bool ApplyLocalPlayerTag(TrackedEntity entity, string tag, string value)
    {
        if (entity.EntityId != localPlayerEntityId || !TryInt(value, out var numeric))
        {
            return false;
        }

        switch (tag.ToUpperInvariant())
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
            case "BACON_WON_LAST_COMBAT" when numeric != 0:
                return RecordCombatWin();
            case "DAMAGE_DEALT_TO_HERO_LAST_TURN":
                return RecordCombatDamage(numeric);
            default:
                return false;
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
        State.AddEvent($"Souboj s {OpponentLabel(combat)}: výhra.");
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

        combat.DamageTaken = damage;
        combat.Outcome ??= "LOST";
        State.AddEvent($"Souboj s {OpponentLabel(combat)}: {ResultName(combat.Outcome)}, −{damage} HP.");
        return true;
    }

    private bool SetCombatPhase(bool inCombat)
    {
        if (State.IsCombatPhase == inCombat)
        {
            return false;
        }

        if (!inCombat)
        {
            // Souboj bez jediného útoku se zachytí aspoň takhle, byť už po odklizení desek.
            if (!combatBoardsCaptured)
            {
                CaptureCombatBoards();
            }

            State.IsCombatPhase = false;
            State.Epoch++;
            State.CurrentCombat = null;
            return true;
        }

        State.IsCombatPhase = true;
        State.Epoch++;
        combatBoardsCaptured = false;
        FinishPreviousCombat();
        var combat = new CombatRound(State.Round, State.NextOpponentPlayerId);
        if (State.NextOpponent is { } opponent)
        {
            combat.OpponentHeroName = opponent.HeroName;
            combat.OpponentBattleTag = opponent.BattleTag;
        }

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
        State.AddEvent($"Souboj s {OpponentLabel(previous)}: remíza.");
    }

    /// <summary>
    /// Uloží obě desky k příslušným slotům v lobby. Cizí desku log ukáže jen během souboje proti
    /// ní, takže tohle je jediná příležitost, jak se k ní vůbec dostat.
    /// </summary>
    private void CaptureCombatBoards()
    {
        combatBoardsCaptured = true;
        if (CombatOpponentSlot() is { } opponentSlot)
        {
            StoreBoard(opponentSlot, State.OpponentBoard);
        }

        if (State.LocalPlayerSlot is { } localSlot)
        {
            StoreBoard(localSlot, State.PlayerBoard);
        }
    }

    private void StoreBoard(int lobbyPlayerId, IReadOnlyList<BoardMinion> board)
    {
        if (board.Count == 0)
        {
            return;
        }

        var participant = State.LobbyParticipant(lobbyPlayerId);
        participant.LastBoard = board;
        participant.LastBoardRound = State.Round;
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
        if (State.LocalParticipant?.LeaderboardPlace is { } place)
        {
            State.FinalPlace = place;
            State.AddEvent($"Konec hry: {place}. místo.");
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

    private static string OpponentLabel(CombatRound combat) =>
        combat.OpponentBattleTag ?? combat.OpponentHeroName ??
        (combat.OpponentPlayerId is { } slot ? $"hráčem #{slot}" : "neznámým soupeřem");

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
        eliminatedSlots.Clear();
        localPlayerId = null;
        localPlayerEntityId = null;
        announcedRound = null;
        combatBoardsCaptured = false;
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
        if (OwnsLobbyStats(entity, lobbyPlayerId))
        {
            participant.Health = entity.Health ?? participant.Health;
            participant.Armor = entity.Armor ?? participant.Armor;
            participant.Damage = entity.Damage ?? participant.Damage;
            participant.TavernTier = entity.TavernTier ?? participant.TavernTier;
            participant.LeaderboardPlace = entity.LeaderboardPlace ?? participant.LeaderboardPlace;
            participant.Triples = entity.Triples ?? participant.Triples;
            participant.PlayState = entity.PlayState ?? participant.PlayState;
        }

        if (heroOwners.TryGetValue(entity.EntityId, out var battleTag))
        {
            participant.BattleTag = battleTag;
            battleTagLobbyPlayers[battleTag] = lobbyPlayerId;
            participant.IsLocal = State.LocalPlayerEntity is not null &&
                                  battleTag.Equals(State.LocalPlayerEntity, StringComparison.Ordinal);
        }
        else if (localPlayerId == entity.ControllerId && localPlayerId == lobbyPlayerId &&
                 State.LocalPlayerEntity is { } localBattleTag)
        {
            participant.BattleTag = localBattleTag;
            participant.IsLocal = true;
            battleTagLobbyPlayers[localBattleTag] = lobbyPlayerId;
        }

        if (participant.IsLocal)
        {
            State.LocalPlayerSlot = lobbyPlayerId;
            RefreshTavernUpgradeCost();
        }

        AnnounceElimination(participant);
    }

    private void AnnounceElimination(LobbyParticipant participant)
    {
        if (!participant.IsEliminated || !eliminatedSlots.Add(participant.PlayerId))
        {
            return;
        }

        var label = participant.BattleTag ?? participant.HeroName ?? $"hráč #{participant.PlayerId}";
        State.AddEvent(participant.LeaderboardPlace is { } place
            ? $"{label} vypadl na {place}. místě."
            : $"{label} vypadl.");
    }

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
