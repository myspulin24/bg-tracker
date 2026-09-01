using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tracker.Core;

namespace Tracker.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string gameStatus = "Čekání";
    private string turn = "—";
    private string phase = "čekání";
    private string gold = "—";
    private string place = "—";
    private string tavernUpgrade = "Upgrade: —";
    private string nextOpponent = "Další soupeř: —";
    private string opposingBoardTitle = "NABÍDKA BOBA";
    private string availableRaces = "—";
    private string result = "—";
    private string diagnostics = $"{TrackerVersion.Display} • {TrackerVersion.Copyright} • 0 řádků / 0 událostí";
    private string pauseButtonText = "Pozastavit";
    private string modeLabel = "DEMO";
    private string gameMode = "SÓLO";
    private string sourceDescription = "syntetická data";
    private bool isPauseEnabled = true;
    private string updateStatus = string.Empty;
    private bool hasUpdate;
    private bool isUpdateReady;
    private readonly Dictionary<int, MinionViewModel[]> boardCache = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ParticipantViewModel> Participants { get; } = [];
    public ObservableCollection<MinionViewModel> PlayerBoard { get; } = [];
    public ObservableCollection<MinionViewModel> OpposingBoard { get; } = [];
    public ObservableCollection<string> Events { get; } = [];

    public string GameStatus { get => gameStatus; private set => Set(ref gameStatus, value); }
    public string Turn { get => turn; private set => Set(ref turn, value); }
    public string Phase { get => phase; private set => Set(ref phase, value); }
    public string Gold { get => gold; private set => Set(ref gold, value); }
    public string Place { get => place; private set => Set(ref place, value); }
    public string TavernUpgrade { get => tavernUpgrade; private set => Set(ref tavernUpgrade, value); }
    public string NextOpponent { get => nextOpponent; private set => Set(ref nextOpponent, value); }
    public string OpposingBoardTitle { get => opposingBoardTitle; private set => Set(ref opposingBoardTitle, value); }
    public string AvailableRaces { get => availableRaces; private set => Set(ref availableRaces, value); }
    public string Result { get => result; private set => Set(ref result, value); }
    public string Diagnostics { get => diagnostics; private set => Set(ref diagnostics, value); }
    public string PauseButtonText { get => pauseButtonText; set => Set(ref pauseButtonText, value); }
    public string ModeLabel { get => modeLabel; set => Set(ref modeLabel, value); }

    /// <summary>Herní režim lobby: <c>SÓLO</c>, nebo <c>DUOS</c>.</summary>
    public string GameMode { get => gameMode; private set => Set(ref gameMode, value); }
    public string SourceDescription { get => sourceDescription; set => Set(ref sourceDescription, value); }
    public bool IsPauseEnabled { get => isPauseEnabled; set => Set(ref isPauseEnabled, value); }

    /// <summary>Text pruhu s aktualizací; pruh se zobrazuje jen podle <see cref="HasUpdate"/>.</summary>
    public string UpdateStatus { get => updateStatus; set => Set(ref updateStatus, value); }
    public bool HasUpdate { get => hasUpdate; set => Set(ref hasUpdate, value); }

    /// <summary>Stažená verze čeká na výměnu, takže má smysl nabídnout restart.</summary>
    public bool IsUpdateReady { get => isUpdateReady; set => Set(ref isUpdateReady, value); }

    public void Update(TrackerState state)
    {
        var standings = state.Standings;

        GameStatus = state.IsGameActive ? "Hra probíhá" : state.GamesSeen == 0 ? "Čekání" : "Hra skončila";
        Turn = state.Round?.ToString() ?? "—";
        Phase = FirstUpper(state.Phase);
        Gold = state.AvailableGold is { } available ? $"{available}/{state.Gold ?? available}" : "—";
        Place = state.LocalPlace is { } localPlace ? $"{localPlace}/{state.PlaceCount}" : "—";
        TavernUpgrade = $"Upgrade tavernu: {Value(state.TavernUpgradeCost)}";
        NextOpponent = $"Další soupeř: {OpponentLabel(state)}";
        AvailableRaces = state.AvailableRaces.Count == 0
            ? "Typy v nabídce: —"
            : "Typy: " + string.Join(" · ", state.AvailableRaces.Select(MinionRace.Display));
        OpposingBoardTitle = state.IsCombatPhase ? "DESKA SOUPEŘE" : "NABÍDKA BOBA";
        GameMode = state.IsDuos ? "DUOS" : "SÓLO";
        Result = state.FinalPlace is { } finalPlace
            ? $"{finalPlace}. místo"
            : TranslateResult(state.Result);
        Diagnostics =
            $"{TrackerVersion.Display} • {TrackerVersion.Copyright} • {state.ParsedLines} řádků / {state.RecognizedEvents} událostí";

        Sync(Participants, [.. Rank(state, standings).Select(row => new ParticipantViewModel(
            row.Place,
            row.Participant.HeroName ?? "Čekám na hrdinu",
            row.Participant.BattleTag ?? "Skrytý hráč",
            row.Participant.IsEliminated ? "†" : Value(row.Participant.EffectiveHealth),
            Value(row.Participant.Armor),
            Value(row.Participant.TavernTier),
            Value(row.Participant.Triples),
            TranslateStatus(row.Participant.PlayState),
            row.Participant.IsLocal,
            row.Participant.IsTeammate,
            row.Participant.PlayerId == state.NextOpponentPlayerId ||
                (state.IsDuos && row.Participant.PlayerId == state.NextOpponentTeammatePlayerId),
            row.Participant.IsEliminated,
            row.IsTeamStart,
            StableBoard(row.Participant, state),
            BoardCaption(row.Participant, state)))]);

        Sync(PlayerBoard, [.. state.PlayerBoard.Select(MinionViewModel.From)]);
        Sync(OpposingBoard, [.. (state.IsCombatPhase ? state.OpponentBoard : state.Shop).Select(MinionViewModel.From)]);
        Sync(Events, [.. state.RecentEvents.Reverse()]);
    }

    /// <summary>
    /// Aktualizuje kolekci na místě. Kompletní <c>Clear</c> a znovunaplnění při každém ticku by
    /// resetovalo posun v panelu událostí a způsobilo problikávání seznamů.
    /// </summary>
    private static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (index >= target.Count)
            {
                target.Add(source[index]);
            }
            else if (!EqualityComparer<T>.Default.Equals(target[index], source[index]))
            {
                target[index] = source[index];
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    /// <summary>Jeden řádek tabulky lobby i s tím, kolikáté místo a začátek kterého týmu značí.</summary>
    private readonly record struct Row(LobbyParticipant Participant, string Place, bool IsTeamStart);

    /// <summary>
    /// Očísluje řádky tabulky. V Duos se místo rozdává týmu, ne hráči, takže se číslo píše
    /// jen k prvnímu z dvojice; u druhého by tvrdilo, že je o příčku horší.
    /// </summary>
    private static IReadOnlyList<Row> Rank(TrackerState state, IReadOnlyList<LobbyParticipant> standings)
    {
        if (!state.IsDuos)
        {
            return [.. standings.Select((participant, index) => new Row(participant, $"{index + 1}", false))];
        }

        var rows = new List<Row>(standings.Count);
        var teams = state.Teams;
        for (var index = 0; index < teams.Count; index++)
        {
            for (var member = 0; member < teams[index].Count; member++)
            {
                rows.Add(new Row(teams[index][member], member == 0 ? $"{index + 1}" : string.Empty, member == 0));
            }
        }

        return rows;
    }

    /// <summary>
    /// U lokálního hráče je zajímavá živá deska, u soupeřů poslední, kterou log ukázal.
    /// </summary>
    private static IReadOnlyList<BoardMinion> BoardOf(LobbyParticipant participant, TrackerState state) =>
        IsMe(participant, state) && state.PlayerBoard.Count > 0 ? state.PlayerBoard : participant.LastBoard;

    /// <summary>
    /// Živá deska patří jedinému slotu. V Duos sdílí spoluhráč tutéž stranu desky jako lokální
    /// hráč, takže sám příznak <c>IsLocal</c> by mu mohl podstrčit cizí sestavu.
    /// </summary>
    private static bool IsMe(LobbyParticipant participant, TrackerState state) =>
        state.LocalPlayerSlot is { } slot ? participant.PlayerId == slot : participant.IsLocal;

    private static string BoardCaption(LobbyParticipant participant, TrackerState state)
    {
        if (IsMe(participant, state) && state.PlayerBoard.Count > 0)
        {
            return "Aktuální deska";
        }

        if (participant.LastBoard.Count > 0)
        {
            return $"Deska z kola {participant.LastBoardRound?.ToString() ?? "—"}";
        }

        // Proti spoluhráči se nikdy nenastupuje, takže by slib, že se deska ukáže, nikdy neplatil.
        return participant.IsTeammate
            ? "Desku spoluhráče log neukazuje"
            : "Desku uvidíte, až proti tomuto hráči nastoupíte";
    }

    /// <summary>
    /// Vrací stejnou instanci, dokud se obsah desky nezmění. Nová instance při každém ticku by
    /// nahradila celý řádek lobby a zavřela právě otevřené podokno s deskou.
    /// </summary>
    private IReadOnlyList<MinionViewModel> StableBoard(LobbyParticipant participant, TrackerState state)
    {
        var next = BoardOf(participant, state).Select(MinionViewModel.From).ToArray();
        if (boardCache.TryGetValue(participant.PlayerId, out var previous) && previous.SequenceEqual(next))
        {
            return previous;
        }

        boardCache[participant.PlayerId] = next;
        return next;
    }

    private static string OpponentLabel(TrackerState state)
    {
        // Nastupuje se proti jednomu soupeři; druhého z dvojice bere spoluhráč. Uvádí se proto
        // jako doplněk, ne jako druhý protivník.
        var first = SlotLabel(state.NextOpponent, state.NextOpponentPlayerId);
        return state.IsDuos && (state.NextOpponentTeammate is not null || state.NextOpponentTeammatePlayerId is not null)
            ? $"{first} — v týmu s {SlotLabel(state.NextOpponentTeammate, state.NextOpponentTeammatePlayerId)}"
            : first;
    }

    private static string SlotLabel(LobbyParticipant? participant, int? playerId) => participant is not null
        ? $"{participant.HeroName ?? "—"} · {participant.BattleTag ?? "Skrytý hráč"}"
        : playerId is { } slot
            ? $"hráč #{slot}"
            : "—";

    private static string TranslateResult(string? result) => result switch
    {
        "WON" => "Výhra",
        "LOST" => "Prohra",
        "TIED" => "Remíza",
        _ => "—"
    };

    private static string TranslateStatus(string? status) => status switch
    {
        "WON" => "výhra",
        "LOST" => "prohra",
        "TIED" => "remíza",
        null => "—",
        _ => status.ToLowerInvariant()
    };

    private static string Value(int? value) => value?.ToString() ?? "—";

    private static string FirstUpper(string value) => string.IsNullOrEmpty(value)
        ? value
        : char.ToUpperInvariant(value[0]) + value[1..];

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
