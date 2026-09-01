using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracker.Core;

/// <summary>
/// Popisy karet z veřejné databáze HearthstoneJSON. Log nese jen Card ID a statistiky, takže
/// efekt karty se odjinud vzít nedá. Databáze se stáhne jednou, uloží se z ní jen
/// battlegroundská podmnožina a ta se pak čte z disku.
/// </summary>
public sealed class CardTextProvider
{
    public const string DefaultUrl = "https://api.hearthstonejson.com/v1/latest/enUS/cards.json";

    /// <summary>Po dvou týdnech se databáze stáhne znovu; do té doby stačí uložená kopie.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string cachePath;
    private readonly string url;
    private readonly HttpClient client;
    private readonly object gate = new();
    private Task<IReadOnlyDictionary<string, string>>? load;

    public CardTextProvider(string? cachePath = null, HttpMessageHandler? handler = null, string url = DefaultUrl)
    {
        this.cachePath = Path.GetFullPath(cachePath ?? DefaultCachePath);
        this.url = url;
        client = new HttpClient(handler ?? CompressingHandler());
        client.Timeout = TimeSpan.FromMinutes(3);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BattlegroundsTracker/{TrackerVersion.Current}");
    }

    public static string DefaultCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BattlegroundsTracker",
        "cards",
        "cardtext.json");

    public string CachePath => cachePath;

    /// <summary>
    /// Načte popisy, ať už z uložené kopie, nebo stažením. Volat se dá opakovaně, ale stahuje
    /// jen jednou; při neúspěchu vrátí prázdnou tabulku a kartička se vykreslí bez popisu.
    /// </summary>
    public Task<IReadOnlyDictionary<string, string>> LoadAsync()
    {
        lock (gate)
        {
            return load ??= LoadCoreAsync();
        }
    }

    /// <summary>
    /// Uklidí značky, kterými databáze řídí sazbu v klientu hry: <c>[x]</c>, ruční zalomení
    /// řádků, jednoduché HTML a mřížky před čísly poškození. Zůstane holá věta.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var body = text.StartsWith("[x]", StringComparison.Ordinal) ? text[3..] : text;
        var builder = new StringBuilder(body.Length);
        var insideTag = false;
        foreach (var character in body)
        {
            if (insideTag)
            {
                insideTag = character != '>';
                continue;
            }

            switch (character)
            {
                case '<':
                    insideTag = true;
                    break;
                case '$':
                case '#':
                    break;
                case ' ':
                case '\n':
                case '\r':
                case '\t':
                    AppendSpace(builder);
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendSpace(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ')
        {
            builder.Append(' ');
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadCoreAsync()
    {
        var cached = ReadCache();
        if (cached is not null && IsFresh())
        {
            return cached;
        }

        try
        {
            var texts = await DownloadAsync().ConfigureAwait(false);
            if (texts.Count > 0)
            {
                WriteCache(texts);
                return texts;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
                                              or UnauthorizedAccessException or JsonException
                                              or TaskCanceledException)
        {
            // Bez sítě se použije i prošlá kopie; starý popis je pořád lepší než žádný.
        }

        return cached ?? new Dictionary<string, string>();
    }

    private async Task<Dictionary<string, string>> DownloadAsync()
    {
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var stream = await client.GetStreamAsync(url).ConfigureAwait(false);

        // Databáze má přes devět megabajtů, proto se čte proudem, ne celá do paměti.
        var entries = JsonSerializer.DeserializeAsyncEnumerable<CardEntry>(stream, ReadOptions);
        await foreach (var entry in entries.ConfigureAwait(false))
        {
            if (entry?.Id is not { Length: > 0 } id || !IsBattlegroundsCard(id))
            {
                continue;
            }

            var text = Clean(entry.Text);
            if (text.Length > 0)
            {
                texts[id] = text;
            }
        }

        return texts;
    }

    /// <summary>
    /// Ukládá se jen to, co může tracker potkat. Texty všech karet zaberou přes dva megabajty,
    /// battlegroundská část zhruba desetinu.
    /// </summary>
    private static bool IsBattlegroundsCard(string id) =>
        id.StartsWith("BG", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("TB_Bacon", StringComparison.OrdinalIgnoreCase);

    private bool IsFresh()
    {
        var info = new FileInfo(cachePath);
        return info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge;
    }

    private Dictionary<string, string>? ReadCache()
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return null;
            }

            using var stream = File.OpenRead(cachePath);
            var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            return texts is null
                ? null
                : new Dictionary<string, string>(texts, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or JsonException
                                              or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteCache(Dictionary<string, string> texts)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var partial = $"{cachePath}.part";
            using (var stream = File.Create(partial))
            {
                JsonSerializer.Serialize(stream, texts);
            }

            File.Move(partial, cachePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Bez uložené kopie se databáze příště stáhne znovu; nic horšího se nestane.
        }
    }

    private static HttpMessageHandler CompressingHandler() => new HttpClientHandler
    {
        // Nekomprimovaně jde o devět megabajtů JSONu, komprimovaně o necelé dva.
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    private sealed record CardEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("text")] string? Text);
}
