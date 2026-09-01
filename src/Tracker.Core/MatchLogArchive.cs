using System.IO.Compression;
using System.Text.Json;

namespace Tracker.Core;

public sealed class MatchLogArchive : IDisposable
{
    private sealed record Checkpoint(string SourcePath, long SourcePosition, string? ActiveMatchFile);

    /// <summary>Přípona dokončeného zápasu. Rozehraný zůstává v prostém textu.</summary>
    public const string PackedExtension = ".power.log.br";

    /// <summary>Přípona rozehraného zápasu, do kterého se ještě zapisuje.</summary>
    public const string PlainExtension = ".power.log";

    /// <summary>
    /// Kolik dokončených zápasů se drží. Bez stropu složka roste donekonečna a i zabalený
    /// zápas má pár megabajtů. Ořez proběhne při každém otevření archivu, takže se složka
    /// srovná hned po startu aplikace.
    /// </summary>
    public const int RetainedMatches = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string checkpointPath;
    private StreamWriter? activeWriter;
    private long savedPosition = -1;
    private string? savedActiveMatchPath;

    private MatchLogArchive(string dataDirectory, string sourcePath)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        MatchesDirectory = Path.Combine(DataDirectory, "matches");
        SourcePath = Path.GetFullPath(sourcePath);
        checkpointPath = Path.Combine(DataDirectory, "checkpoint.json");
        Directory.CreateDirectory(MatchesDirectory);
        RestoreCheckpoint();
        PackLeftovers();
        Prune();
    }

    public string DataDirectory { get; }
    public string MatchesDirectory { get; }
    public string SourcePath { get; }
    public long ResumePosition { get; private set; }
    public string? ActiveMatchPath { get; private set; }
    public bool HasActiveMatch => ActiveMatchPath is not null;

    public static MatchLogArchive Open(string dataDirectory, string sourcePath) => new(dataDirectory, sourcePath);

    /// <summary>
    /// Čte zápasový log bez ohledu na to, jestli je zabalený. Volající tak nemusí řešit, kterou
    /// příponu soubor má.
    /// </summary>
    public static IEnumerable<string> ReadMatch(string path) => ReadMatch(path, null);

    /// <summary>
    /// Totéž s hlášením postupu 0 až 1. Počítá se z pozice v souboru na disku, protože počet
    /// řádků se u zabaleného zápasu předem zjistit nedá, aniž by se celý rozbalil.
    /// </summary>
    public static IEnumerable<string> ReadMatch(string path, IProgress<double>? progress)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        using var file = File.OpenRead(path);
        var total = Math.Max(1, file.Length);
        using var reader = path.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
            ? new StreamReader(new BrotliStream(file, CompressionMode.Decompress))
            : new StreamReader(file);

        var reported = -1;
        while (reader.ReadLine() is { } line)
        {
            if (progress is not null)
            {
                // Hlásí se po celých procentech; častější hlášení jen zdržuje vlákno rozhraní.
                var percent = (int)(100 * file.Position / total);
                if (percent != reported)
                {
                    reported = percent;
                    progress.Report(percent / 100.0);
                }
            }

            yield return line;
        }

        progress?.Report(1);
    }

    /// <summary>Rozpozná soubor, který vyrobil tracker, ať už zabalený, nebo ne.</summary>
    public static bool IsMatchArchive(string path) =>
        path.EndsWith(PackedExtension, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(PlainExtension, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<string> ReadActiveLines()
    {
        if (ActiveMatchPath is null || !File.Exists(ActiveMatchPath))
        {
            yield break;
        }

        foreach (var line in ReadMatch(ActiveMatchPath))
        {
            yield return line;
        }
    }

    public void StartMatch(DateTimeOffset startedAt)
    {
        if (HasActiveMatch)
        {
            return;
        }

        var baseName = $"match-{startedAt:yyyyMMdd-HHmmss-fff}";
        var candidate = Path.Combine(MatchesDirectory, $"{baseName}.power.log");
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(MatchesDirectory, $"{baseName}-{suffix++}.power.log");
        }

        ActiveMatchPath = candidate;
        activeWriter = CreateWriter(candidate, append: false);
    }

    public void Append(string line)
    {
        if (ActiveMatchPath is null)
        {
            return;
        }

        activeWriter ??= CreateWriter(ActiveMatchPath, append: true);
        activeWriter.WriteLine(line);
    }

    public void CompleteMatch()
    {
        activeWriter?.Dispose();
        activeWriter = null;
        var finished = ActiveMatchPath;
        ActiveMatchPath = null;
        if (finished is not null)
        {
            Pack(finished);
            Prune();
        }
    }

    /// <summary>
    /// Zabalí dokončený zápas Brotli. Na reálném logu to je 29× méně místa a zhruba 150 ms
    /// práce; gzip dá jen 21× a trvá dvakrát tak dlouho. Nejsilnější stupeň Brotli sice dá 44×,
    /// ale dvě minuty čekání uprostřed hraní nestojí za pár megabajtů.
    /// </summary>
    private static void Pack(string path)
    {
        if (!File.Exists(path) || path.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var packed = Path.ChangeExtension(path, null) + ".log.br";
        var partial = $"{packed}.part";
        try
        {
            using (var source = File.OpenRead(path))
            using (var target = File.Create(partial))
            using (var packer = new BrotliStream(target, CompressionLevel.Optimal))
            {
                source.CopyTo(packer);
            }

            File.Move(partial, packed, overwrite: true);
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nepovedené zabalení nechává původní soubor na pokoji; zkusí se při dalším startu.
            TryDelete(partial);
        }
    }

    /// <summary>
    /// Zabalí zápasy, které zůstaly v prostém textu: z verzí před zavedením komprese nebo po
    /// pádu aplikace. Rozehraný zápas se vynechá, ten se ještě čte při obnovení.
    /// </summary>
    private void PackLeftovers()
    {
        foreach (var path in Files(PlainExtension))
        {
            if (!string.Equals(path, ActiveMatchPath, StringComparison.OrdinalIgnoreCase))
            {
                Pack(path);
            }
        }
    }

    /// <summary>Nechá jen posledních <see cref="RetainedMatches"/> dokončených zápasů.</summary>
    private void Prune()
    {
        var finished = Files(PackedExtension)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Skip(RetainedMatches);

        foreach (var path in finished)
        {
            TryDelete(path);
        }
    }

    private IEnumerable<string> Files(string extension)
    {
        try
        {
            return [.. Directory.EnumerateFiles(MatchesDirectory, $"*{extension}")];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Zamčený soubor se uklidí při příštím startu.
        }
    }

    public void SaveCheckpoint(long sourcePosition)
    {
        ResumePosition = Math.Max(0, sourcePosition);
        if (ResumePosition == savedPosition &&
            string.Equals(ActiveMatchPath, savedActiveMatchPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(checkpointPath))
        {
            return;
        }

        Directory.CreateDirectory(DataDirectory);
        var checkpoint = new Checkpoint(SourcePath, ResumePosition, ActiveMatchPath);
        var temporaryPath = $"{checkpointPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(checkpoint, JsonOptions));
        File.Move(temporaryPath, checkpointPath, overwrite: true);
        savedPosition = ResumePosition;
        savedActiveMatchPath = ActiveMatchPath;
    }

    public void Dispose()
    {
        activeWriter?.Dispose();
        activeWriter = null;
    }

    private void RestoreCheckpoint()
    {
        if (!File.Exists(checkpointPath))
        {
            return;
        }

        try
        {
            var checkpoint = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(checkpointPath));
            if (checkpoint is null ||
                !checkpoint.SourcePath.Equals(SourcePath, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(SourcePath) || checkpoint.SourcePosition > new FileInfo(SourcePath).Length)
            {
                return;
            }

            ResumePosition = checkpoint.SourcePosition;
            if (checkpoint.ActiveMatchFile is not null && File.Exists(checkpoint.ActiveMatchFile))
            {
                ActiveMatchPath = checkpoint.ActiveMatchFile;
            }

            savedPosition = ResumePosition;
            savedActiveMatchPath = ActiveMatchPath;
        }
        catch (JsonException)
        {
            // A damaged checkpoint is safely ignored; the source log will be replayed once.
        }
        catch (IOException)
        {
            // A temporarily locked checkpoint is safely ignored.
        }
    }

    private static StreamWriter CreateWriter(string path, bool append) => new(path, append)
    {
        AutoFlush = true
    };
}
