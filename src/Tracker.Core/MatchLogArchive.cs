using System.Text.Json;

namespace Tracker.Core;

public sealed class MatchLogArchive : IDisposable
{
    private sealed record Checkpoint(string SourcePath, long SourcePosition, string? ActiveMatchFile);

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
    }

    public string DataDirectory { get; }
    public string MatchesDirectory { get; }
    public string SourcePath { get; }
    public long ResumePosition { get; private set; }
    public string? ActiveMatchPath { get; private set; }
    public bool HasActiveMatch => ActiveMatchPath is not null;

    public static MatchLogArchive Open(string dataDirectory, string sourcePath) => new(dataDirectory, sourcePath);

    public IEnumerable<string> ReadActiveLines()
    {
        if (ActiveMatchPath is null || !File.Exists(ActiveMatchPath))
        {
            yield break;
        }

        foreach (var line in File.ReadLines(ActiveMatchPath))
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
        ActiveMatchPath = null;
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
