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
        var localIndex = IndexOfLocal(standings);

        GameStatus = state.IsGameActive ? "Hra probíhá" : state.GamesSeen == 0 ? "Čekání" : "Hra skončila";
        Turn = state.Round?.ToString() ?? "—";
        Phase = FirstUpper(state.Phase);
        Gold = state.AvailableGold is { } available ? $"{available}/{state.Gold ?? available}" : "—";
        Place = localIndex >= 0 ? (localIndex + 1).ToString() : "—";
        TavernUpgrade = $"Upgrade tavernu: {Value(state.TavernUpgradeCost)}";
        NextOpponent = $"Další soupeř: {OpponentLabel(state)}";
        AvailableRaces = state.AvailableRaces.Count == 0
            ? "Typy v nabídce: —"
            : "Typy: " + string.Join(" · ", state.AvailableRaces.Select(MinionRace.Display));
        OpposingBoardTitle = state.IsCombatPhase ? "DESKA SOUPEŘE" : "NABÍDKA BOBA";
        Result = state.FinalPlace is { } finalPlace
            ? $"{finalPlace}. místo"
            : TranslateResult(state.Result);
        Diagnostics =
            $"{TrackerVersion.Display} • {TrackerVersion.Copyright} • {state.ParsedLines} řádků / {state.RecognizedEvents} událostí";

        Sync(Participants, [.. standings.Select((participant, index) => new ParticipantViewModel(
            (index + 1).ToString(),
            participant.HeroName ?? "Čekám na hrdinu",
            participant.BattleTag ?? "Skrytý hráč",
            participant.IsEliminated ? "†" : Value(participant.EffectiveHealth),
            Value(participant.Armor),
            Value(participant.TavernTier),
            Value(participant.Triples),
            TranslateStatus(participant.PlayState),
            participant.IsLocal,
            participant.PlayerId == state.NextOpponentPlayerId,
            participant.IsEliminated,
            StableBoard(participant, state),
            BoardCaption(participant, state)))]);

        Sync(PlayerBoard, [.. state.PlayerBoard.Select(MinionViewModel.From)]);
        Sync(OpposingBoard,
            [.. (state.IsCombatPhase ? state.OpponentBoard : state.Shop).Select(MinionViewModel.From)]);
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

    /// <summary>
    /// U lokálního hráče je zajímavá živá deska, u soupeřů poslední, kterou log ukázal.
    /// </summary>
    private static IReadOnlyList<BoardMinion> BoardOf(LobbyParticipant participant, TrackerState state) =>
        participant.IsLocal && state.PlayerBoard.Count > 0 ? state.PlayerBoard : participant.LastBoard;

    private static string BoardCaption(LobbyParticipant participant, TrackerState state)
    {
        if (participant.IsLocal && state.PlayerBoard.Count > 0)
        {
            return "Aktuální deska";
        }

        return participant.LastBoard.Count == 0
            ? "Desku uvidíte, až proti tomuto hráči nastoupíte"
            : $"Deska z kola {participant.LastBoardRound?.ToString() ?? "—"}";
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

    private static int IndexOfLocal(IReadOnlyList<LobbyParticipant> standings)
    {
        for (var index = 0; index < standings.Count; index++)
        {
            if (standings[index].IsLocal)
            {
                return index;
            }
        }

        return -1;
    }

    private static string OpponentLabel(TrackerState state) => state.NextOpponent is { } opponent
        ? $"{opponent.HeroName ?? "—"} · {opponent.BattleTag ?? "Skrytý hráč"}"
        : state.NextOpponentPlayerId is { } slot
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
