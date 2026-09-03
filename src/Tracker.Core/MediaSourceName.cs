namespace Tracker.Core;

/// <summary>
/// Jméno přehrávače pro proužek s hudbou. Systémové rozhraní pro média hlásí jen
/// identifikátor aplikace, a ten není na pohled k ničemu: Edge se hlásí jako <c>MSEdge</c>,
/// Spotify jako <c>Spotify.exe</c> a aplikace z Microsoft Storu jako
/// <c>Balíček_kód!Aplikace</c>.
/// </summary>
public static class MediaSourceName
{
    /// <summary>
    /// Známé přehrávače. Klíč se hledá v očištěném identifikátoru jako podřetězec, takže
    /// stačí jednou a pokryje i varianty jako <c>Spotify.exe</c> nebo balíček ze Storu.
    /// Pořadí rozhoduje: „youtubemusic“ musí padnout dřív než „youtube“.
    /// </summary>
    private static readonly (string Key, string Name)[] KnownPlayers =
    [
        ("youtubemusic", "YouTube Music"),
        ("ytmdesktop", "YouTube Music"),
        ("youtube", "YouTube"),
        ("spotify", "Spotify"),
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("firefox", "Firefox"),
        ("opera", "Opera"),
        ("brave", "Brave"),
        ("zunemusic", "Media Player"),
        ("zunevideo", "Media Player"),
        ("wmplayer", "Windows Media Player"),
        ("vlc", "VLC"),
        ("foobar", "foobar2000"),
        ("musicbee", "MusicBee"),
        ("aimp", "AIMP"),
        ("winamp", "Winamp"),
        ("tidal", "Tidal"),
        ("deezer", "Deezer"),
        ("applemusic", "Apple Music"),
        ("itunes", "iTunes"),
    ];

    /// <summary>
    /// Přeloží identifikátor aplikace na čitelné jméno. Neznámý identifikátor se jen očistí,
    /// protože i „PowerDVD“ je pro uživatele lepší než prázdno.
    /// </summary>
    public static string Friendly(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return string.Empty;
        }

        // U aplikací ze Storu je za vykřičníkem jméno aplikace v balíčku, které je čitelnější
        // než jméno balíčku s podpisem vydavatele.
        var raw = appUserModelId.Trim();
        var bang = raw.LastIndexOf('!');
        var candidate = bang >= 0 && bang < raw.Length - 1 ? raw[(bang + 1)..] : raw;

        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4];
        }

        var haystack = new string(candidate.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        foreach (var (key, name) in KnownPlayers)
        {
            if (haystack.Contains(key, StringComparison.Ordinal))
            {
                return name;
            }
        }

        // Neznámé jméno: poslední část za tečkou bývá to podstatné (App.Media.Player).
        var dot = candidate.LastIndexOf('.');
        return dot >= 0 && dot < candidate.Length - 1 ? candidate[(dot + 1)..] : candidate;
    }
}
