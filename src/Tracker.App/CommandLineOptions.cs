namespace Tracker.App;

internal sealed record CommandLineOptions(string? LogPath, bool Demo, bool Replay, bool Help, bool Version)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? logPath = null;
        var demo = false;
        var replay = false;
        var help = false;
        var version = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--log" when index + 1 < args.Length:
                    logPath = args[++index];
                    break;
                case "--demo":
                    demo = true;
                    replay = true;
                    break;
                case "--replay":
                    replay = true;
                    break;
                case "--version" or "-v":
                    version = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"Neznámý nebo neúplný parametr: {args[index]}");
            }
        }

        return new(logPath, demo, replay, help, version);
    }
}
