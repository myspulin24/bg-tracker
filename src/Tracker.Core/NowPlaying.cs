namespace Tracker.Core;

/// <summary>
/// Co právě hraje. Čistá data bez WinRT, aby se skládání textu dalo testovat: obsah plní
/// systémové rozhraní pro média, které hlásí jen názvy, stav a to, která tlačítka přehrávač
/// podporuje. Pozice ve skladbě tu schválně není — přehrávače ji do systému průběžně
/// neposílají, takže by ukazatel postupu stál na místě.
/// </summary>
/// <param name="Title">Název skladby, u videa v prohlížeči jeho titulek.</param>
/// <param name="Artist">Interpret; u videa bývá jméno kanálu a někdy chybí.</param>
/// <param name="Source">Přehrávač, ze kterého to hraje, například <c>Spotify</c>.</param>
/// <param name="IsPlaying">Hraje, nebo je pozastavené?</param>
/// <param name="CanPlayPause">Podporuje přehrávač přepnutí přehrávání?</param>
/// <param name="CanSkipNext">Podporuje přeskočení na další skladbu?</param>
/// <param name="CanSkipPrevious">Podporuje návrat na předchozí skladbu?</param>
public sealed record NowPlaying(
    string Title,
    string Artist,
    string Source,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanSkipNext,
    bool CanSkipPrevious)
{
    /// <summary>Nehraje nic; proužek s hudbou se v tomhle stavu vůbec nezobrazuje.</summary>
    public static NowPlaying Nothing { get; } =
        new(string.Empty, string.Empty, string.Empty, false, false, false, false);

    /// <summary>Má se proužek zobrazit? Bez názvu není co ukázat.</summary>
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// Druhý řádek proužku: interpret a zdroj. Interpret u videí často chybí, takže se
    /// vypisuje jen to, co je k dispozici, a zdroj zůstává vždycky.
    /// </summary>
    public string Subtitle =>
        string.Join(" • ", new[] { Artist, Source }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
