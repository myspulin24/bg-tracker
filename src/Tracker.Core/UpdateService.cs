using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tracker.Core;

/// <summary>
/// Zjišťuje a stahuje novější vydání z GitHub Releases. Veřejné repozitáře nevyžadují žádné
/// přihlášení, takže aplikace nemusí nikde držet token.
/// </summary>
public sealed class UpdateService(string repository = UpdateService.DefaultRepository, HttpMessageHandler? handler = null)
{
    public const string DefaultRepository = "myspulin24/bg-tracker";

    /// <summary>Jméno přílohy vydání, kterou aplikace umí nainstalovat sama.</summary>
    public const string AssetName = "BattlegroundsTracker.exe";

    private readonly HttpClient client = CreateClient(handler);

    /// <summary>
    /// Vrátí novější vydání, nebo <c>null</c>, pokud běží nejnovější verze nebo se zjištění
    /// nepovedlo. Selhání je záměrně tiché: nedostupný GitHub nesmí ovlivnit sledování hry.
    /// </summary>
    public async Task<AvailableUpdate?> FindNewerReleaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{repository}/releases/latest";
            await using var stream = await client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return await ReadAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stáhne vydání vedle běžícího programu jako <c>*.exe.new</c>. Instalace proběhne až
    /// při dalším startu, aby aktualizace nikdy nepřerušila rozehraný zápas.
    /// </summary>
    public async Task<bool> DownloadAsync(
        AvailableUpdate update,
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        var staged = UpdateInstaller.StagedPath(executablePath);
        var partial = staged + ".part";
        try
        {
            File.Delete(partial);
            await using (var source = await client.GetStreamAsync(update.DownloadUrl, cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (!await MatchesChecksumAsync(partial, update.Sha256, cancellationToken).ConfigureAwait(false))
            {
                File.Delete(partial);
                return false;
            }

            File.Move(partial, staged, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
                                              or UnauthorizedAccessException or TaskCanceledException)
        {
            TryDelete(partial);
            return false;
        }
    }

    private async Task<AvailableUpdate?> ReadAsync(JsonElement release, CancellationToken cancellationToken)
    {
        if (!release.TryGetProperty("tag_name", out var tag) ||
            !TryParseVersion(tag.GetString(), out var version) ||
            !TryParseVersion(TrackerVersion.Numeric, out var current) ||
            version <= current)
        {
            return null;
        }

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Uri? download = null;
        string? checksum = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var assetName) ? assetName.GetString() : null;
            var uri = asset.TryGetProperty("browser_download_url", out var assetUrl) ? assetUrl.GetString() : null;
            if (name is null || uri is null)
            {
                continue;
            }

            if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
            {
                download = new Uri(uri);
            }
            else if (name.Equals($"{AssetName}.sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksum = uri;
            }
        }

        if (download is null)
        {
            return null;
        }

        var releaseUrl = release.TryGetProperty("html_url", out var html) && html.GetString() is { } page
            ? new Uri(page)
            : new Uri($"https://github.com/{repository}/releases/latest");

        return new AvailableUpdate(
            version.ToString(3),
            download,
            releaseUrl,
            checksum is null ? null : await ReadChecksumAsync(checksum, cancellationToken).ConfigureAwait(false));
    }

    private async Task<string?> ReadChecksumAsync(string checksumUrl, CancellationToken cancellationToken)
    {
        try
        {
            var content = await client.GetStringAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
            // Soubor má tvar "<hex>  <jméno souboru>", stačí první slovo.
            var hex = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return hex is { Length: 64 } ? hex : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task<bool> MatchesChecksumAsync(
        string path,
        string? expected,
        CancellationToken cancellationToken)
    {
        if (expected is null)
        {
            // Bez kontrolního součtu se aspoň ověří, že nejde o chybovou stránku místo programu.
            return new FileInfo(path).Length > 1_000_000;
        }

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return Version.TryParse(trimmed, out version!);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Zbytek po nedokončeném stahování se uklidí při příštím pokusu.
        }
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler)
    {
        var created = handler is null ? new HttpClient() : new HttpClient(handler);
        created.Timeout = TimeSpan.FromMinutes(10);
        // GitHub API odmítá požadavky bez User-Agent.
        created.DefaultRequestHeaders.UserAgent.ParseAdd($"BattlegroundsTracker/{TrackerVersion.Numeric}");
        created.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return created;
    }
}
