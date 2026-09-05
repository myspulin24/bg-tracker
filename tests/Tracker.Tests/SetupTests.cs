using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Průvodce připojením: oprava <c>log.config</c> musí doplnit jen sekci <c>[Power]</c> a ostatní
/// nechat, hledání na disku musí najít hru i ve vlastní složce a log běžící hry se musí poznat
/// i tehdy, když hra běží jako správce a start procesu není znát.
/// </summary>
public sealed class SetupTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "bgt-setup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void InspectsThePowerSection()
    {
        Assert.False(HearthstoneLogConfig.Inspect(null).HasSection);
        Assert.False(HearthstoneLogConfig.Inspect("[Zone]\nLogLevel=1\nFilePrinting=True\n").HasSection);

        var incomplete = HearthstoneLogConfig.Inspect("[Power]\nLogLevel=1\nFilePrinting=False\n");
        Assert.True(incomplete.HasSection);
        Assert.False(incomplete.FilePrinting);
        Assert.False(incomplete.IsReady);

        // Hra i jiné nástroje píší hodnoty s různou velikostí písmen.
        var ready = HearthstoneLogConfig.Inspect("[power]\r\nloglevel = 1\r\nfileprinting=true\r\nVerbose=TRUE\r\n");
        Assert.True(ready.IsReady);
    }

    [Fact]
    public void AddsTheSectionAndKeepsEverythingElse()
    {
        var original = "[Achievements]\r\nLogLevel=1\r\nFilePrinting=True\r\n\r\n[Zone]\r\nLogLevel=1\r\nFilePrinting=True\r\n";

        var ensured = HearthstoneLogConfig.Ensure(original);

        Assert.StartsWith(original, ensured);
        Assert.Contains("[Power]", ensured);
        Assert.Contains("FilePrinting=True", ensured[ensured.IndexOf("[Power]", StringComparison.Ordinal)..]);
        Assert.True(HearthstoneLogConfig.Inspect(ensured).IsReady);
        Assert.Equal(ensured, HearthstoneLogConfig.Ensure(ensured));
        Assert.True(HearthstoneLogConfig.Inspect(HearthstoneLogConfig.Ensure(null)).IsReady);
    }

    [Fact]
    public void RepairsAnIncompleteSectionInPlace()
    {
        var original = "[Power]\r\nLogLevel=0\r\nFilePrinting=False\r\nCustomKey=keep\r\n\r\n[Zone]\r\nLogLevel=1\r\n";

        var ensured = HearthstoneLogConfig.Ensure(original);
        var status = HearthstoneLogConfig.Inspect(ensured);

        Assert.True(status.IsReady);
        Assert.Contains("CustomKey=keep", ensured);
        Assert.Single(ensured.Split("[Power]"), part => part.Contains("Verbose=True", StringComparison.Ordinal));
        Assert.EndsWith("[Zone]\r\nLogLevel=1\r\n", ensured);
        Assert.Equal(1, ensured.Split("[Power]").Length - 1);
    }

    [Fact]
    public void AppliesToDiskWithABackupAndLeavesAReadyFileAlone()
    {
        var path = Path.Combine(workspace, "Blizzard", "Hearthstone", "log.config");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[Zone]\r\nLogLevel=1\r\n");

        Assert.True(HearthstoneLogConfig.Apply(path));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal("[Zone]\r\nLogLevel=1\r\n", File.ReadAllText(path + ".bak"));
        Assert.True(HearthstoneLogConfig.Inspect(File.ReadAllText(path)).IsReady);

        Assert.False(HearthstoneLogConfig.Apply(path));

        // Chybějící soubor i složka vzniknou.
        var fresh = Path.Combine(workspace, "new", "log.config");
        Assert.True(HearthstoneLogConfig.Apply(fresh));
        Assert.True(HearthstoneLogConfig.Inspect(File.ReadAllText(fresh)).IsReady);
        Assert.False(File.Exists(fresh + ".bak"));
    }

    [Fact]
    public void FindsTheGameInACustomFolderUpToTheGivenDepth()
    {
        var shallow = Path.Combine(workspace, "Hry", "Hearthstone");
        var deep = Path.Combine(workspace, "a", "b", "Hearthstone");
        Directory.CreateDirectory(shallow);
        Directory.CreateDirectory(deep);
        Directory.CreateDirectory(Path.Combine(workspace, "$Recycle.Bin", "Hearthstone"));
        File.WriteAllText(Path.Combine(shallow, "Hearthstone.exe"), "x");
        File.WriteAllText(Path.Combine(deep, "Hearthstone.exe"), "x");
        File.WriteAllText(Path.Combine(workspace, "$Recycle.Bin", "Hearthstone", "Hearthstone.exe"), "x");

        var found = PowerLogDiscovery.FindInstallsUnder(workspace, maxDepth: 2);

        Assert.Equal([shallow], found);
        Assert.Contains(deep, PowerLogDiscovery.FindInstallsUnder(workspace, maxDepth: 3));
    }

    [Fact]
    public void TakesAFreshLogAsCurrentWhenTheGameRunsElevated()
    {
        var log = Path.Combine(workspace, "Power.log");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(log, "x");
        var now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(log, now.AddMinutes(-3).UtcDateTime);

        var elevated = new GameProcessState(true, null, true);

        // Hra se spustila před minutou, log je o dvě minuty starší než její start i s tolerancí.
        var known = new GameProcessState(true, now.AddMinutes(-1), false);

        Assert.True(SetupDiagnostics.IsCurrentSessionLog(log, elevated, now));
        Assert.False(SetupDiagnostics.IsCurrentSessionLog(log, known, now));
        Assert.False(SetupDiagnostics.IsCurrentSessionLog(log, GameProcessState.NotRunning, now));

        File.SetLastWriteTimeUtc(log, now.AddMinutes(-30).UtcDateTime);
        Assert.False(SetupDiagnostics.IsCurrentSessionLog(log, elevated, now));
    }
}
