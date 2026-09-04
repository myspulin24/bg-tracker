namespace Tracker.Core;

public sealed class TrackerState
{
    private const int MaxRecentEvents = 6;

    private readonly Dictionary<string, ObservedParticipant> participants = new(StringComparer.Ordinal);
    private readonly Queue<string> recentEvents = new();
    private readonly Dictionary<int, LobbyParticipant> lobbyParticipants = [];
    private readonly Dictionary<int, TrackedEntity> entities = [];
    private readonly List<CombatRound> combatHistory = [];
    private readonly SortedSet<string> availableRaces = new(StringComparer.Ordinal);

    public bool IsGameActive { get; internal set; }
    public bool BattlegroundsSignalSeen { get; internal set; }
    public int GamesSeen { get; internal set; }
    public int ParsedLines { get; internal set; }
    public int RecognizedEvents { get; internal set; }

    /// <summary>Interní číslo tahu z tagu <c>TURN</c>; na jedno Battlegrounds kolo připadají dva tahy.</summary>
    public int? Turn { get; internal set; }

    /// <summary>Battlegrounds kolo tak, jak je ukazuje hra.</summary>
    public int? Round => Turn is null ? null : (Turn.Value + 1) / 2;

    public string Phase { get; internal set; } = "čekání";
    public string? Result { get; internal set; }
    public string? LocalPlayerEntity { get; internal set; }
    public string? LastEvent { get; internal set; }

    /// <summary>Hodnota <c>CONTROLLER</c> lokálního hráče; v Battlegrounds nejde o slot v lobby.</summary>
    public int? LocalControllerId { get; internal set; }

    /// <summary>Hodnota <c>CONTROLLER</c> sdílené soupeřovy strany, která nese i nabídku Boba.</summary>
    public int? OpponentControllerId { get; internal set; }

    /// <summary>Slot lokálního hráče v lobby, tedy 1 až 8.</summary>
    public int? LocalPlayerSlot { get; internal set; }

    /// <summary>Tag <c>BACON_IN_COMBAT_PHASE</c> na <c>GameEntity</c>.</summary>
    public bool IsCombatPhase { get; internal set; }

    /// <summary>Aktuální generace entit desky; zvyšuje se s každým přepnutím fáze souboje.</summary>
    public int Epoch { get; internal set; }

    public int? Gold { get; internal set; }
    public int? GoldSpent { get; internal set; }
    public int? TempGold { get; internal set; }
    public int? MaxGold { get; internal set; }

    /// <summary>Zlato, které lokální hráč právě může utratit.</summary>
    public int? AvailableGold =>
        Gold is null ? null : Math.Max(0, Gold.Value + (TempGold ?? 0) - (GoldSpent ?? 0));

    /// <summary>Cena upgradu na další tavern tier z tlačítka <c>TB_BaconShopTechUp*</c>.</summary>
    public int? TavernUpgradeCost { get; internal set; }

    /// <summary>
    /// Cena rerollu z tagu <c>COST</c> na tlačítku <c>TB_BaconShop_8p_Reroll_Button</c>. Nemusí
    /// být jedna: efekty ji zlevňují a při volném rerollu je nula.
    /// </summary>
    public int? RefreshCost { get; internal set; }

    /// <summary>
    /// Kolik rerollů zbývá zdarma (tag <c>BACON_FREE_REFRESH_COUNT</c> na témže tlačítku).
    /// Naměřený průběh 2 → 4 → 3 → 2 → 1 ukazuje, že jde o živé počítadlo, ne o součet za hru.
    /// </summary>
    public int? FreeRefreshes { get; internal set; }

    /// <summary>
    /// Zlato, které hráč dostane navíc příští kolo (tag
    /// <c>BACON_PLAYER_EXTRA_GOLD_NEXT_TURN</c> na entitě hráče). Na rozdíl od bonusů pro celou
    /// hru se po utracení skutečně vrací na nulu.
    /// </summary>
    public int? ExtraGoldNextTurn { get; internal set; }

    /// <summary>
    /// Strop tavern tieru z tagu <c>BACON_MAX_PLAYER_TECH_LEVEL</c>. Na něm se cena upgradu
    /// přestane hlásit, takže bez téhle hodnoty nejde poznat, jestli chybí, nebo je zbytečná.
    /// </summary>
    public int? MaxTavernTier { get; internal set; }

    /// <summary>Malý trinket lokálního hráče, nebo odpočet, dokud si ho nevybere.</summary>
    public Trinket? LesserTrinket => TrinketSlot(1);

    /// <summary>Velký trinket lokálního hráče, nebo odpočet, dokud si ho nevybere.</summary>
    public Trinket? GreaterTrinket => TrinketSlot(2);

    /// <summary>Bonusy platné pro celou hru: kouzla, blood gemy, elementálové, piráti.</summary>
    public GlobalBuffs Buffs { get; } = new();

    /// <summary>Slot dalšího soupeře z tagu <c>NEXT_OPPONENT_PLAYER_ID</c>.</summary>
    public int? NextOpponentPlayerId { get; internal set; }

    /// <summary>Slot spoluhráče dalšího soupeře z tagu <c>NEXT_OPPONENT_TEAMMATE_PLAYER_ID</c>.</summary>
    public int? NextOpponentTeammatePlayerId { get; internal set; }

    /// <summary>Režim Duos, poznaný podle tagů <c>BACON_DUO_*</c>.</summary>
    public bool IsDuos { get; internal set; }

    /// <summary>Slot spoluhráče lokálního hráče z tagu <c>BACON_DUO_TEAMMATE_PLAYER_ID</c>.</summary>
    public int? TeammatePlayerId { get; internal set; }

    /// <summary>Kolik míst se v této lobby rozdává: osm v sólu, čtyři v Duos.</summary>
    public int PlaceCount => IsDuos ? 4 : 8;

    /// <summary>Konečné umístění lokálního hráče; v Duos jde o umístění týmu.</summary>
    public int? FinalPlace { get; internal set; }

    /// <summary>
    /// Typy minionů, které se objevily v nabídce Boba. Hra seznam nikde nevypíše dopředu,
    /// skládá se proto z ras minionů, které Bob v této lobby skutečně nabídl.
    /// Během prvních kol je tedy ještě neúplný.
    /// </summary>
    public IReadOnlyList<string> AvailableRaces => [.. availableRaces];

    public IReadOnlyCollection<ObservedParticipant> Participants => participants.Values;
    public IReadOnlyCollection<string> RecentEvents => recentEvents;
    public IReadOnlyCollection<LobbyParticipant> LobbyParticipants => lobbyParticipants.Values;
    public IReadOnlyCollection<TrackedEntity> Entities => entities.Values;
    public IReadOnlyList<CombatRound> CombatHistory => combatHistory;
    public CombatRound? CurrentCombat { get; internal set; }

    /// <summary>Minioni lokálního hráče na desce, seřazení podle pozice.</summary>
    public IReadOnlyList<BoardMinion> PlayerBoard => Board(LocalControllerId);

    /// <summary>Soupeřova deska; naplněná jen během fáze souboje.</summary>
    public IReadOnlyList<BoardMinion> OpponentBoard =>
        IsCombatPhase ? Board(OpponentControllerId) : [];

    /// <summary>Nabídka Boba; naplněná jen mimo fázi souboje.</summary>
    public IReadOnlyList<BoardMinion> Shop =>
        IsCombatPhase ? [] : Board(OpponentControllerId);

    /// <summary>Karty v ruce lokálního hráče včetně tavern kouzel.</summary>
    public IReadOnlyList<BoardMinion> Hand => LocalControllerId is not { } controller
        ? []
        : entities.Values
            .Where(entity => entity.Epoch == Epoch && entity.IsInHand &&
                             entity.ControllerId == controller && entity.Name is not null)
            .OrderBy(entity => entity.ZonePosition)
            .Select(BoardMinion.From)
            .ToArray();

    /// <summary>
    /// Žebříček tak, jak ho ukazuje hra: živí hráči podle zbývajících životů včetně armoru,
    /// pod nimi vyřazení podle svého konečného umístění. Tag <c>PLAYER_LEADERBOARD_PLACE</c> se
    /// v logu obnovuje po dávkách, takže by sám o sobě pořadí chvílemi ukazoval zastaralé.
    /// </summary>
    public IReadOnlyList<LobbyParticipant> Standings => IsDuos ? DuoStandings : SoloStandings;

    /// <summary>
    /// Žebříček po týmech. V Duos nesou oba spoluhráči stejné <c>PLAYER_LEADERBOARD_PLACE</c>,
    /// takže řadit hráče jednotlivě by dvojice roztrhalo. Týmy se proto řadí podle součtu
    /// zbývajících životů a uvnitř týmu jde první lokální hráč.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<LobbyParticipant>> Teams =>
    [
        .. lobbyParticipants.Values
            .GroupBy(participant => participant.TeamId ?? -participant.PlayerId)
            .Select(team => (IReadOnlyList<LobbyParticipant>)
            [
                .. team
                    .OrderByDescending(participant => participant.IsLocal)
                    .ThenByDescending(participant => participant.IsTeammate)
                    .ThenBy(participant => participant.PlayerId)
            ])
            .OrderBy(team => team.All(participant => participant.IsEliminated))
            // Vyřazené týmy řadí dosažené umístění. Zbývající životy jsou u nich záporné podle
            // toho, jak velkou ranou tým padl, což s pořadím nesouvisí.
            .ThenBy(team => team.All(participant => participant.IsEliminated)
                ? team.Min(participant => participant.LeaderboardPlace ?? int.MaxValue)
                : 0)
            .ThenByDescending(team => team.Sum(participant => participant.RemainingHealth ?? 0))
            .ThenBy(team => team.Min(participant => participant.PlayerId))
    ];

    /// <summary>
    /// Pořadí lokálního hráče, respektive jeho týmu. Počítá se ze stejného řazení, jaké vidí
    /// uživatel v tabulce, aby si číslo v hlavičce a pozice v seznamu neodporovaly.
    /// </summary>
    public int? LocalPlace
    {
        get
        {
            if (IsDuos)
            {
                var teams = Teams;
                for (var index = 0; index < teams.Count; index++)
                {
                    if (teams[index].Any(participant => participant.IsLocal))
                    {
                        return index + 1;
                    }
                }

                return null;
            }

            var standings = SoloStandings;
            for (var index = 0; index < standings.Count; index++)
            {
                if (standings[index].IsLocal)
                {
                    return index + 1;
                }
            }

            return null;
        }
    }

    private IReadOnlyList<LobbyParticipant> DuoStandings => [.. Teams.SelectMany(team => team)];

    private IReadOnlyList<LobbyParticipant> SoloStandings =>
    [
        .. lobbyParticipants.Values
            .Where(participant => !participant.IsEliminated)
            .OrderByDescending(participant => participant.RemainingHealth ?? -1)
            .ThenByDescending(participant => participant.TavernTier ?? 0)
            .ThenBy(participant => participant.PlayerId),
        .. lobbyParticipants.Values
            .Where(participant => participant.IsEliminated)
            .OrderBy(participant => participant.LeaderboardPlace ?? int.MaxValue)
            .ThenBy(participant => participant.PlayerId)
    ];

    public LobbyParticipant? LocalParticipant => LocalPlayerSlot is { } slot &&
        lobbyParticipants.TryGetValue(slot, out var participant)
            ? participant
            : lobbyParticipants.Values.FirstOrDefault(candidate => candidate.IsLocal);

    public LobbyParticipant? NextOpponent => Slot(NextOpponentPlayerId);

    /// <summary>Druhý z dvojice, proti které se v Duos nastupuje.</summary>
    public LobbyParticipant? NextOpponentTeammate => Slot(NextOpponentTeammatePlayerId);

    /// <summary>Spoluhráč lokálního hráče v Duos.</summary>
    public LobbyParticipant? Teammate => Slot(TeammatePlayerId);

    /// <summary>Hráč na daném slotu lobby, nebo <c>null</c>, pokud ho log ještě neodhalil.</summary>
    public LobbyParticipant? Slot(int? playerId) => playerId is { } slot &&
        lobbyParticipants.TryGetValue(slot, out var participant)
            ? participant
            : null;

    internal ObservedParticipant Participant(string entity)
    {
        if (!participants.TryGetValue(entity, out var participant))
        {
            participant = new ObservedParticipant(entity);
            participants.Add(entity, participant);
        }

        return participant;
    }

    internal TrackedEntity Entity(int entityId)
    {
        if (!entities.TryGetValue(entityId, out var entity))
        {
            entity = new TrackedEntity(entityId) { Epoch = Epoch, CreatedDuringCombat = IsCombatPhase };
            entities.Add(entityId, entity);
        }

        return entity;
    }

    internal bool TryGetEntity(int entityId, out TrackedEntity entity) =>
        entities.TryGetValue(entityId, out entity!);

    /// <summary>Zaznamená rasu minionu z nabídkového poolu. Vrací <c>true</c>, jde-li o nový typ.</summary>
    internal bool RegisterPoolRace(string? race) =>
        !string.IsNullOrWhiteSpace(race) &&
        !race.Equals("ALL", StringComparison.OrdinalIgnoreCase) &&
        availableRaces.Add(race);

    internal void BeginGame()
    {
        IsGameActive = true;
        BattlegroundsSignalSeen = false;
        GamesSeen++;
        Turn = null;
        Phase = "načítání hry";
        Result = null;
        LocalPlayerEntity = null;
        LocalControllerId = null;
        OpponentControllerId = null;
        LocalPlayerSlot = null;
        IsCombatPhase = false;
        Epoch = 0;
        Gold = null;
        GoldSpent = null;
        TempGold = null;
        MaxGold = null;
        TavernUpgradeCost = null;
        MaxTavernTier = null;
        RefreshCost = null;
        FreeRefreshes = null;
        ExtraGoldNextTurn = null;
        Buffs.Reset();
        NextOpponentPlayerId = null;
        NextOpponentTeammatePlayerId = null;
        IsDuos = false;
        TeammatePlayerId = null;
        FinalPlace = null;
        CurrentCombat = null;
        participants.Clear();
        lobbyParticipants.Clear();
        entities.Clear();
        combatHistory.Clear();
        availableRaces.Clear();
        recentEvents.Clear();
        AddEvent("Začala nová hra.");
    }

    internal LobbyParticipant LobbyParticipant(int playerId)
    {
        if (!lobbyParticipants.TryGetValue(playerId, out var participant))
        {
            participant = new LobbyParticipant(playerId);
            lobbyParticipants.Add(playerId, participant);
        }

        return participant;
    }

    internal void BeginCombat(CombatRound round)
    {
        CurrentCombat = round;
        combatHistory.Add(round);
    }

    internal void RenameParticipant(string oldEntity, string newEntity)
    {
        if (oldEntity.Equals(newEntity, StringComparison.Ordinal) ||
            !participants.Remove(oldEntity, out var participant))
        {
            return;
        }

        if (participants.TryGetValue(newEntity, out var existing))
        {
            existing.TavernTier ??= participant.TavernTier;
            existing.Health ??= participant.Health;
            existing.Armor ??= participant.Armor;
            existing.Damage ??= participant.Damage;
            existing.PlayState ??= participant.PlayState;
            return;
        }

        participant.Entity = newEntity;
        participants.Add(newEntity, participant);
    }

    internal void AddEvent(string message)
    {
        LastEvent = message;
        recentEvents.Enqueue(message);
        while (recentEvents.Count > MaxRecentEvents)
        {
            recentEvents.Dequeue();
        }
    }

    /// <summary>
    /// Přepíše už zveřejněnou událost na jejím místě. Výsledek souboje se dozvídáme po částech:
    /// nejdřív jestli se vyhrálo, potom poškození jedné a druhé strany. Bez přepisu by o jednom
    /// souboji stálo v panelu několik protichůdných vět, a přepis jen poslední položky nestačí,
    /// protože se mezi ně vejde třeba vyřazení hráče.
    /// </summary>
    internal bool UpdateEvent(string oldMessage, string newMessage)
    {
        if (!recentEvents.Contains(oldMessage))
        {
            return false;
        }

        var kept = recentEvents.ToArray();
        recentEvents.Clear();
        foreach (var message in kept)
        {
            recentEvents.Enqueue(message.Equals(oldMessage, StringComparison.Ordinal) ? newMessage : message);
        }

        if (string.Equals(LastEvent, oldMessage, StringComparison.Ordinal))
        {
            LastEvent = newMessage;
        }

        return true;
    }

    /// <summary>
    /// Slot na trinket lokálního hráče. Entita slotu žije celou hru a nepřegeneruje se se
    /// soubojem, takže se tu na rozdíl od desky nefiltruje podle generace. Jméno se bere jen
    /// tehdy, když už v entitě není jméno prázdného slotu — jinak by se v panelu objevilo
    /// „Lesser Trinket“ místo odpočtu.
    ///
    /// Prázdných slotů vyrobí hra za zápas desítky: platí jen ten jeden v <c>PLAY</c>, ostatní
    /// leží v <c>SETASIDE</c> nebo ve hřbitově a nesou odpočet ze začátku hry. Bez filtru na
    /// zónu tak panel ukazoval odpočet i dlouho po tom, co byl trinket vybraný.
    /// </summary>
    private Trinket? TrinketSlot(int slot)
    {
        if (LocalControllerId is not { } controller)
        {
            return null;
        }

        var entity = entities.Values
            .Where(candidate => candidate.TrinketSlot == slot &&
                                candidate.ControllerId == controller &&
                                candidate.IsInPlay)
            .OrderByDescending(candidate => candidate.EntityId)
            .FirstOrDefault();
        if (entity is null)
        {
            return null;
        }

        var chosen = Trinket.IsPlaceholderName(entity.Name) ? null : entity.Name;

        // Karta slouží k dohledání popisu efektu. Prázdný slot vlastní kartu nemá, a když ji
        // hra po výběru v entitě slotu ani nevymění (naměřeno u velkého trinketu), zbývá
        // dohledat ji podle jména mezi nabídkami — ty kartu nesou vždycky.
        var card = Trinket.SlotFromCardId(entity.CardId) is null ? entity.CardId : TrinketCard(chosen);
        return new Trinket(slot, chosen, entity.TrinketTurnsLeft, card);
    }

    /// <summary>Karta trinketu daného jména, hledaná mezi entitami s příznakem trinketu.</summary>
    private string? TrinketCard(string? name) => name is null
        ? null
        : entities.Values
            .FirstOrDefault(candidate => candidate.IsTrinket &&
                                         string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
                                         candidate.CardId is not null)
            ?.CardId;

    private IReadOnlyList<BoardMinion> Board(int? controllerId) => controllerId is not { } controller
        ? []
        : entities.Values
            .Where(entity => entity.Epoch == Epoch && entity.IsMinion && entity.IsInPlay &&
                             entity.ControllerId == controller && entity.ZonePosition > 0)
            .OrderBy(entity => entity.ZonePosition)
            .Select(BoardMinion.From)
            .ToArray();
}
