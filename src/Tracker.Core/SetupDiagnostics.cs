using System.Diagnostics;

namespace Tracker.Core;

/// <summary>Co tracker ví o běžícím procesu hry.</summary>
/// <param name="IsRunning">Proces <c>Hearthstone</c> existuje.</param>
/// <param name="StartedAt">Kdy se spustil; <c>null</c>, když to Windows neprozradí.</param>
/// <param name="IsInaccessible">
/// Proces běží, ale jeho start se přečíst nedá. Typicky běží jako správce a tracker ne;
/// Windows pak neelevovanému procesu detaily o elevovaném nedají.
/// </param>
public sealed record GameProcessState(bool IsRunning, DateTimeOffset? StartedAt, bool IsInaccessible)
{
    public static GameProcessState NotRunning => new(false, null, false);
}

/// <summary>Jeden krok průvodce připojením: stav a co s ním.</summary>
public sealed record SetupReport(
    GameProcessState Game,
    IReadOnlyList<string> InstallRoots,
    string? CustomDirectory,
    bool CustomDirectoryHasLogs,
    string LogConfigPath,
    bool LogConfigExists,
    HearthstoneLogConfig.Status LogConfig,
    string? LatestPowerLog,
    DateTimeOffset? LatestPowerLogWritten,
    bool LatestPowerLogIsCurrent)
{
    /// <summary>Instalace se našla: aspoň jeden kořen s adresářem <c>Logs</c>, nebo zadaná složka.</summary>
    public bool InstallFound => InstallRoots.Count > 0 || CustomDirectoryHasLogs;

    /// <summary>Všechno sedí a tracker se připojí, jakmile hra začne psát log.</summary>
    public bool IsReady => InstallFound && LogConfig.IsReady;
}

/// <summary>
/// Diagnostika pro průvodce připojením: běží hra, kde je nainstalovaná, píše log a je ten log
/// z běžící relace. Všechno čisté čtení, nic se tu nemění; opravy dělá volající tlačítky.
/// </summary>
public static class SetupDiagnostics
{
    /// <summary>Jak starý smí být log, aby se bral za log běžící hry, když start procesu není znát.</summary>
    public static readonly TimeSpan FreshLogAge = TimeSpan.FromMinutes(10);

    /// <summary>Hra zapisuje start relace o chvíli později, než ji Windows spustí.</summary>
    private static readonly TimeSpan StartTolerance = TimeSpan.FromMinutes(1);

    public static GameProcessState ProbeGame()
    {
        try
        {
            var processes = Process.GetProcessesByName("Hearthstone");
            if (processes.Length == 0)
            {
                return GameProcessState.NotRunning;
            }

            try
            {
                var started = processes.Min(process => process.StartTime.ToUniversalTime());
                return new GameProcessState(true, new DateTimeOffset(started, TimeSpan.Zero), false);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return new GameProcessState(true, null, true);
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return GameProcessState.NotRunning;
        }
    }

    /// <summary>
    /// Patří log běžící hře? Když je znát start procesu, musí být log zapsaný po něm. Když hra
    /// běží jako správce a start se přečíst nedá, bere se log, do kterého hra nedávno psala;
    /// jinak by tracker na takovém počítači zůstal navždy v naslouchání.
    /// </summary>
    public static bool IsCurrentSessionLog(string path, DateTimeOffset now) =>
        IsCurrentSessionLog(path, ProbeGame(), now);

    public static bool IsCurrentSessionLog(string path, GameProcessState game, DateTimeOffset now)
    {
        if (!game.IsRunning)
        {
            return false;
        }

        DateTimeOffset written;
        try
        {
            written = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return game.StartedAt is { } started
            ? written >= started - StartTolerance
            : IsFresh(written, now);
    }

    /// <summary>Zapsaný v posledních deseti minutách, nebo dokonce „v budoucnosti“ kvůli posunu hodin.</summary>
    public static bool IsFresh(DateTimeOffset written, DateTimeOffset now) => now - written <= FreshLogAge;

    /// <summary>Sesbírá stav pro průvodce. Zadaná složka hry z nastavení má přednost.</summary>
    public static SetupReport Collect(string? customDirectory, DateTimeOffset now)
    {
        var game = ProbeGame();
        var roots = PowerLogDiscovery.InstallRoots()
            .Where(root => Directory.Exists(Path.Combine(root, "Logs")))
            .ToArray();
        var customHasLogs = !string.IsNullOrWhiteSpace(customDirectory) &&
                            Directory.Exists(Path.Combine(customDirectory, "Logs"));

        var configPath = HearthstoneLogConfig.DefaultPath;
        string? configContent = null;
        try
        {
            configContent = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nečitelný soubor se hlásí jako chybějící; oprava ho zkusí přepsat.
        }

        string[] searchRoots = customHasLogs ? [customDirectory!, .. roots] : roots;
        var latest = PowerLogDiscovery.FindInRoots(searchRoots);
        DateTimeOffset? written = null;
        if (latest is not null)
        {
            try
            {
                written = new DateTimeOffset(File.GetLastWriteTimeUtc(latest), TimeSpan.Zero);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                latest = null;
            }
        }

        return new SetupReport(
            game,
            roots,
            string.IsNullOrWhiteSpace(customDirectory) ? null : customDirectory,
            customHasLogs,
            configPath,
            configContent is not null,
            HearthstoneLogConfig.Inspect(configContent),
            latest,
            written,
            latest is not null && IsCurrentSessionLog(latest, game, now));
    }
}
