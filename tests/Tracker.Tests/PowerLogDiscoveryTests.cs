using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Hledání <c>Power.log</c>. Testuje se přes <see cref="PowerLogDiscovery.FindInRoots"/>
/// nad dočasnými adresáři, protože skutečná instalace hry na build stroji být nemusí.
/// </summary>
public sealed class PowerLogDiscoveryTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "bgt-discovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Vyrobí instalaci hry s logem a vrátí cestu k logu.</summary>
    private string Install(string relativeRoot, string? sessionDirectory = null, DateTime? written = null)
    {
        var root = Path.Combine(workspace, relativeRoot);
        var logRoot = Path.Combine(root, "Logs");
        var directory = sessionDirectory is null ? logRoot : Path.Combine(logRoot, sessionDirectory);
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, "Power.log");
        File.WriteAllText(log, "D 00:00:00.0 GameState.DebugPrintPower() - CREATE_GAME");
        if (written is { } stamp)
        {
            File.SetLastWriteTimeUtc(log, stamp);
        }

        return log;
    }

    private string Root(string relativeRoot) => Path.Combine(workspace, relativeRoot);

    [Fact]
    public void FindsLogInInstallOnAnotherDrive()
    {
        // Přesně případ, na kterém stará verze selhala: hra mimo Program Files, ve vlastní
        // složce, kterou by šlo uhodnout jen náhodou.
        var expected = Install(Path.Combine("D-disk", "Blizzard", "Hearthstone"));

        var found = PowerLogDiscovery.FindInRoots([Root(Path.Combine("D-disk", "Blizzard", "Hearthstone"))]);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FindsLogInSessionDirectory()
    {
        var expected = Install("Hearthstone", "Hearthstone_2026_09_03_20_54_34");

        var found = PowerLogDiscovery.FindInRoots([Root("Hearthstone")]);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void PrefersNewestLogAcrossRootsAndSessions()
    {
        Install("Stara", written: new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        Install("Nova", "Hearthstone_2026_09_02_10_00_00", new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc));
        var newest = Install("Nova", "Hearthstone_2026_09_03_10_00_00", new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc));

        var found = PowerLogDiscovery.FindInRoots([Root("Stara"), Root("Nova")]);

        Assert.Equal(newest, found);
    }

    [Fact]
    public void ReturnsNullWhenNoRootHasLog()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "Prazdna", "Logs"));

        Assert.Null(PowerLogDiscovery.FindInRoots([Root("Prazdna"), Root("Neexistuje")]));
    }

    [Fact]
    public void ExplicitPathWinsAndMissingExplicitPathIsNull()
    {
        var log = Install("Rucni");

        Assert.Equal(log, PowerLogDiscovery.Find(log));
        Assert.Null(PowerLogDiscovery.Find(Path.Combine(workspace, "neni-tam", "Power.log")));
    }

    [Fact]
    public void InstallRootsCoverProgramFilesAndEveryFixedDrive()
    {
        var roots = PowerLogDiscovery.InstallRoots();

        Assert.NotEmpty(roots);
        // Každý pevný disk musí být pokrytý, jinak by hra mimo systémový disk propadla.
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            var expected = Path.Combine(drive.RootDirectory.FullName, "Hearthstone");
            Assert.Contains(expected, roots, StringComparer.OrdinalIgnoreCase);
        }

        Assert.Contains(roots, root => root.EndsWith(Path.Combine("Games", "Hearthstone"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(roots, root => root.EndsWith(Path.Combine("Battle.net", "Hearthstone"), StringComparison.OrdinalIgnoreCase));
    }
}
