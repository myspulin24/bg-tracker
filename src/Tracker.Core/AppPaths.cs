namespace Tracker.Core;

/// <summary>
/// Kde má tracker svá data: nastavení, archiv zápasů, mezipaměť karet a data prohlížeče.
/// Výchozí je <c>%LOCALAPPDATA%\BattlegroundsTracker</c>. Proměnná prostředí
/// <c>BGTRACKER_DATA_DIR</c> to přebije; hodí se pro přenosnou instalaci a pro snímky
/// rozhraní nad čistými daty, protože .NET na Windows proměnnou <c>LOCALAPPDATA</c> nečte
/// a bere složku přímo ze systému.
/// </summary>
public static class AppPaths
{
    public const string DataDirectoryVariable = "BGTRACKER_DATA_DIR";

    public static string DataDirectory
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BattlegroundsTracker")
                : Path.GetFullPath(custom);
        }
    }

    public static string MatchesDirectory => Path.Combine(DataDirectory, "matches");
}
