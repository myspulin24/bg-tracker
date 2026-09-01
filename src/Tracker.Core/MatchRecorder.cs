namespace Tracker.Core;

/// <summary>
/// Spojuje parser, stavový model a archiv zápasů do jednoho kroku nad řádkem živého logu.
/// Drží pravidlo, kdy zápasový soubor vzniká a kdy se uzavírá.
/// </summary>
public static class MatchRecorder
{
    /// <summary>
    /// Zpracuje jeden řádek `Power.log`. Vrací <c>true</c>, pokud šlo o uživatelsky viditelnou
    /// změnu stavu.
    /// </summary>
    public static bool Handle(
        PowerLogParser parser,
        GameStateTracker tracker,
        MatchLogArchive? archive,
        string line,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(tracker);

        var gamesBefore = tracker.State.GamesSeen;
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
            archive?.CompleteMatch();
        }

        return changed;
    }
}
