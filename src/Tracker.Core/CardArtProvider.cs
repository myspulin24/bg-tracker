using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;

namespace Tracker.Core;

/// <summary>
/// Překládá ID karty na soubor s kresbou v místní mezipaměti. Hra sama drží obrázky v assetech
/// Unity, které se bez zvláštního nástroje otevřít nedají, takže se kresba stahuje z veřejné CDN
/// HearthstoneJSON. Každá karta se stáhne jen jednou; potom už podokno funguje i bez sítě.
/// </summary>
public sealed class CardArtProvider
{
    /// <summary>Kresba karty bez rámečku, 256×256 px, zhruba 14 kB.</summary>
    public const string DefaultBaseUrl = "https://art.hearthstonejson.com/v1/256x";

    private readonly string cacheDirectory;
    private readonly string baseUrl;
    private readonly HttpClient client;

    // Rozpracovaná i dokončená stahování. Díky sdílené úloze se stejná karta nikdy nestahuje
    // dvakrát, i když se objeví na desce i v nabídce zároveň.
    private readonly ConcurrentDictionary<string, Task<string?>> loads = new(StringComparer.OrdinalIgnoreCase);

    // Karty, na které CDN odpověděla 404. Jsou to enchanty a pomocné karty bez vlastní kresby,
    // takže nemá smysl se na ně ptát znovu.
    private readonly ConcurrentDictionary<string, byte> missing = new(StringComparer.OrdinalIgnoreCase);

    public CardArtProvider(
        string? cacheDirectory = null,
        HttpMessageHandler? handler = null,
        string baseUrl = DefaultBaseUrl)
    {
        this.cacheDirectory = Path.GetFullPath(cacheDirectory ?? DefaultCacheDirectory);
        this.baseUrl = baseUrl.TrimEnd('/');
        client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BattlegroundsTracker/{TrackerVersion.Current}");
    }

    /// <summary>Kam se kresby ukládají, když volající nechce vlastní složku.</summary>
    public static string DefaultCacheDirectory => Path.Combine(AppPaths.DataDirectory, "cardart");

    public string CacheDirectory => cacheDirectory;

    /// <summary>
    /// Vrátí cestu k obrázku karty, nebo <c>null</c>, pokud kresba neexistuje nebo se ji nepodařilo
    /// stáhnout. Selhání je tiché: bez obrázku se podokno jen vykreslí jako dosud.
    /// </summary>
    public Task<string?> GetAsync(string? cardId)
    {
        if (!IsSafeCardId(cardId) || missing.ContainsKey(cardId))
        {
            return Task.FromResult<string?>(null);
        }

        return ForgetOnFailure(cardId, loads.GetOrAdd(cardId, LoadAsync));
    }

    /// <summary>
    /// Jméno souboru je odvozené od ID karty, proto se přijímají jen znaky, které se v ID
    /// vyskytují. Nic jiného se nesmí dostat do cesty na disku ani do adresy.
    /// </summary>
    private static bool IsSafeCardId([NotNullWhen(true)] string? cardId)
    {
        if (string.IsNullOrEmpty(cardId) || cardId.Length > 64)
        {
            return false;
        }

        foreach (var character in cardId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Neúspěch se z tabulky odstraní, aby si aplikace o kresbu řekla znovu, až bude síť zpátky.
    /// Běží až po dokončení úlohy, takže se nemůže potkat se zápisem v <c>GetOrAdd</c>.
    /// </summary>
    private async Task<string?> ForgetOnFailure(string cardId, Task<string?> load)
    {
        var path = await load.ConfigureAwait(false);
        if (path is null)
        {
            loads.TryRemove(cardId, out _);
        }

        return path;
    }

    private async Task<string?> LoadAsync(string cardId)
    {
        var path = Path.Combine(cacheDirectory, $"{cardId}.jpg");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return path;
        }

        try
        {
            using var response = await client.GetAsync($"{baseUrl}/{cardId}.jpg").ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                missing[cardId] = 0;
                return null;
            }

            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return null;
            }

            Directory.CreateDirectory(cacheDirectory);

            // Zápis přes dočasný soubor: nedokončené stahování nesmí zůstat v mezipaměti jako
            // useknutý obrázek, který by se pak už nikdy nestáhl znovu.
            var partial = $"{path}.part";
            await File.WriteAllBytesAsync(partial, bytes).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
                                              or UnauthorizedAccessException or TaskCanceledException)
        {
            return null;
        }
    }
}
