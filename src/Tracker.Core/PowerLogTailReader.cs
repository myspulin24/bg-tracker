namespace Tracker.Core;

public sealed class PowerLogTailReader(string path, long initialPosition = 0)
{
    private long position = Math.Max(0, initialPosition);

    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public long Position => position;

    public void Rewind() => position = 0;

    public async Task<int> ReadNewLinesAsync(Func<string, bool> handleLine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handleLine);
        if (!File.Exists(Path))
        {
            return 0;
        }

        await using var stream = new FileStream(
            Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length < position)
        {
            position = 0;
        }

        stream.Seek(position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var changes = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (handleLine(line))
            {
                changes++;
            }
        }

        position = stream.Position;
        return changes;
    }
}
