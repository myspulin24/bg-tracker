namespace Tracker.App;

internal sealed class PowerLogReader(string path)
{
    private long position;

    public async Task<int> ReadNewLinesAsync(Func<string, bool> handleLine, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        await using var stream = new FileStream(
            path,
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
        var changed = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (handleLine(line))
            {
                changed++;
            }
        }

        position = stream.Position;
        return changed;
    }
}
