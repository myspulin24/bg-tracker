using Tracker.App;
using Tracker.Core;

try
{
    var options = CommandLineOptions.Parse(args);
    if (options.Version)
    {
        Console.WriteLine($"Hearthstone Battlegrounds Tracker {TrackerVersion.Display}");
        Console.WriteLine(TrackerVersion.Copyright);
        return 0;
    }

    if (options.Help)
    {
        PrintHelp();
        return 0;
    }

    var path = options.Demo
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "samples", "pilot.Power.log"))
        : PowerLogLocator.Find(options.LogPath);

    if (path is null || !File.Exists(path))
    {
        PrintMissingLog(path);
        return 2;
    }

    var parser = new PowerLogParser();
    var tracker = new GameStateTracker();
    var reader = new PowerLogReader(path);

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    bool HandleLine(string line) => tracker.Apply(parser.Parse(line));

    await reader.ReadNewLinesAsync(HandleLine, cancellation.Token);
    ConsoleDashboard.Render(tracker.State, path, clear: false);

    if (options.Replay)
    {
        return 0;
    }

    while (!cancellation.IsCancellationRequested)
    {
        await Task.Delay(350, cancellation.Token);
        var changes = await reader.ReadNewLinesAsync(HandleLine, cancellation.Token);
        if (changes > 0)
        {
            ConsoleDashboard.Render(tracker.State, path);
        }
    }

    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    PrintHelp();
    return 1;
}
catch (IOException exception)
{
    Console.Error.WriteLine($"Power.log se nepodařilo přečíst: {exception.Message}");
    return 3;
}

static void PrintHelp()
{
    Console.WriteLine("""
        Hearthstone Battlegrounds Tracker — pilot

        Použití:
          dotnet run --project src/Tracker.App
          dotnet run --project src/Tracker.App -- --log "D:\Hearthstone\Logs\Power.log"
          dotnet run --project src/Tracker.App -- --demo
          dotnet run --project src/Tracker.App -- --replay --log "cesta\Power.log"

        Parametry:
          --log <cesta>  Explicitní cesta k Power.log
          --demo         Načte přiložený syntetický ukázkový log a skončí
          --replay       Načte existující log, vykreslí stav a skončí
          --version, -v  Vypíše verzi aplikace
          --help, -h     Zobrazí tuto nápovědu
        """);
}

static void PrintMissingLog(string? requestedPath)
{
    Console.Error.WriteLine(requestedPath is null
        ? "Power.log nebyl v obvyklých umístěních nalezen."
        : $"Power.log neexistuje: {requestedPath}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Spusťte nejprve ukázku:");
    Console.Error.WriteLine("  dotnet run --project src/Tracker.App -- --demo");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Nebo zadejte skutečnou cestu:");
    Console.Error.WriteLine("  dotnet run --project src/Tracker.App -- --log \"D:\\Hearthstone\\Logs\\Power.log\"");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Nastavení log.config je popsáno v README.md.");
}
