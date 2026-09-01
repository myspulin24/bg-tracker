namespace Tracker.App;

internal static class PowerLogLocator
{
    public static string? Find(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));
        }

        var candidates = new List<string>();
        Add(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Hearthstone", "Logs", "Power.log");
        Add(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hearthstone", "Logs", "Power.log");
        Add(candidates, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone", "Logs", "Power.log");
        Add(candidates, AppContext.BaseDirectory, "Logs", "Power.log");

        return candidates
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void Add(List<string> paths, string root, params string[] parts)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            paths.Add(Path.Combine([root, .. parts]));
        }
    }
}
