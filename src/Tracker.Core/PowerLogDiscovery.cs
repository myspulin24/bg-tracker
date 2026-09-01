namespace Tracker.Core;

public static class PowerLogDiscovery
{
    public static string? Find(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));
            return File.Exists(expanded) ? expanded : null;
        }

        var candidates = new List<string>();
        AddLogRoot(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Hearthstone", "Logs");
        AddLogRoot(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hearthstone", "Logs");
        AddLogRoot(candidates, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone", "Logs");

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            AddLogRoot(candidates, drive.RootDirectory.FullName, "Hearthstone", "Logs");
            AddLogRoot(candidates, drive.RootDirectory.FullName, "Games", "Hearthstone", "Logs");
        }

        return candidates
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void AddLogRoot(List<string> paths, string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var logRoot = Path.Combine([root, .. parts]);
        paths.Add(Path.Combine(logRoot, "Power.log"));
        if (!Directory.Exists(logRoot))
        {
            return;
        }

        try
        {
            foreach (var sessionDirectory in Directory.EnumerateDirectories(logRoot, "Hearthstone_*", SearchOption.TopDirectoryOnly))
            {
                paths.Add(Path.Combine(sessionDirectory, "Power.log"));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // A manually selected path remains available when an install directory is protected.
        }
        catch (IOException)
        {
            // The game may rotate its session directory while discovery is running.
        }
    }

    private static void Add(List<string> paths, string root, params string[] parts)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            paths.Add(Path.Combine([root, .. parts]));
        }
    }
}
