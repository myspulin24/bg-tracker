namespace Tracker.Core;

/// <summary>
/// Spojuje parser, stavový model a archiv zápasů do jednoho kroku nad řádkem živého logu.
/// Drží pravidlo, kdy zápasový soubor vzniká a kdy se uzavírá.
/// </summary>
public static class MatchRecorder
{
    /// <summary>
    /// Zpracuje jeden řádek `Power.log`. Vrací <c>true</c>, pokud šlo o uživatelsky viditelnou
    /// změnu stavu. Dohraný zápas se zapíše do <paramref name="history"/>, je-li zadaná.
    /// </summary>
    public static bool Handle(
        PowerLogParser parser,
        GameStateTracker tracker,
        MatchLogArchive? archive,
        string line,
        DateTimeOffset startedAt,
        MatchHistory? history = null)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(tracker);

        var gamesBefore = tracker.State.GamesSeen;
        var wasActive = tracker.State.IsGameActive;
        var changed = tracker.Apply(parser.Parse(line));

        if (archive is not null && tracker.State.GamesSeen > gamesBefore)
        {
            // Předchozí zápas se uzavře i tehdy, když jeho FINAL_GAMEOVER nikdy nedorazil,
            // třeba po pádu hry. Bez toho by se nová hra připsala do cizího souboru.
            archive.CompleteMatch();
            archive.StartMatch(startedAt);
        }

        archive?.Append(line);

        if (!tracker.State.IsGameActive)
        {
            // Historie se zapisuje před uzavřením archivu, dokud je známé jméno jeho souboru;
            // podle něj se pak zápas v historii páruje se záznamem k přehrání.
            if (wasActive)
            {
                RecordFinished(tracker.State, archive, history, startedAt);
            }

            archive?.CompleteMatch();
        }

        return changed;
    }

    /// <summary>Zapíše dohraný zápas do historie; volá se i při obnovení po restartu trackeru.</summary>
    public static bool RecordFinished(TrackerState state, MatchLogArchive? archive, MatchHistory? history, DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        return history is not null &&
               history.Add(MatchHistory.FromState(state, MatchHistory.IdFor(archive?.ActiveMatchPath, endedAt), endedAt));
    }
}
