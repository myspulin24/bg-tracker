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

    /// <summary>Slot dalšího soupeře z tagu <c>NEXT_OPPONENT_PLAYER_ID</c>.</summary>
    public int? NextOpponentPlayerId { get; internal set; }

    /// <summary>Konečné umístění lokálního hráče, 1 až 8.</summary>
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
    public IReadOnlyList<LobbyParticipant> Standings =>
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

    public LobbyParticipant? NextOpponent => NextOpponentPlayerId is { } slot &&
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
        NextOpponentPlayerId = null;
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

    private IReadOnlyList<BoardMinion> Board(int? controllerId) => controllerId is not { } controller
        ? []
        : entities.Values
            .Where(entity => entity.Epoch == Epoch && entity.IsMinion && entity.IsInPlay &&
                             entity.ControllerId == controller && entity.ZonePosition > 0)
            .OrderBy(entity => entity.ZonePosition)
            .Select(BoardMinion.From)
            .ToArray();
}
