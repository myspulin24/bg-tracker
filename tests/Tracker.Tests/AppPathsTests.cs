using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Složka dat se dá přesměrovat proměnnou prostředí; bez ní zůstává pod LOCALAPPDATA. Test
/// proměnnou po sobě uklidí, protože platí pro celý proces.
/// </summary>
public sealed class AppPathsTests
{
    [Fact]
    public void UsesLocalAppDataUnlessTheVariableSaysOtherwise()
    {
        var previous = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
            Assert.EndsWith(Path.Combine("BattlegroundsTracker"), AppPaths.DataDirectory);
            Assert.EndsWith(Path.Combine("BattlegroundsTracker", "matches"), AppPaths.MatchesDirectory);
            Assert.EndsWith(Path.Combine("BattlegroundsTracker", "settings.json"), SettingsStore.DefaultPath);

            var custom = Path.Combine(Path.GetTempPath(), "bgtracker-data");
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, custom);
            Assert.Equal(Path.GetFullPath(custom), AppPaths.DataDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(custom), "cardart"), CardArtProvider.DefaultCacheDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, previous);
        }
    }
}
