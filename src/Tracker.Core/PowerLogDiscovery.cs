using Microsoft.Win32;

namespace Tracker.Core;

/// <summary>
/// Hledá <c>Power.log</c> aktuální instalace Hearthstonu. Hra ho píše do podadresáře
/// <c>Logs</c> ve své instalaci, a to buď přímo, nebo do adresáře relace
/// <c>Logs\Hearthstone_&lt;datum&gt;</c>.
/// </summary>
public static class PowerLogDiscovery
{
    /// <summary>
    /// Kde hledat instalaci, když ji registry neprozradí. Skládá se z kořene disku a těchto
    /// cest, protože instalátor Blizzardu nechá uživatele vybrat libovolnou složku a lidé
    /// hru běžně stěhují na druhý disk.
    /// </summary>
    private static readonly string[][] DriveRelativeInstalls =
    [
        ["Hearthstone"],
        ["Games", "Hearthstone"],
        ["Program Files", "Hearthstone"],
        ["Program Files (x86)", "Hearthstone"],
        ["Battle.net", "Hearthstone"],
        ["Blizzard", "Hearthstone"],
        ["Blizzard Entertainment", "Hearthstone"],
        ["Games", "Blizzard", "Hearthstone"],
        ["Games", "Battle.net", "Hearthstone"]
    ];

    public static string? Find(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));
            return File.Exists(expanded) ? expanded : null;
        }

        return FindInRoots(InstallRoots());
    }

    /// <summary>
    /// Kořeny, ve kterých se hledá. První je instalace podle registry, protože ta platí
    /// i pro cestu, na kterou by se nedalo uhodnout.
    /// </summary>
    public static IReadOnlyList<string> InstallRoots()
    {
        var roots = new List<string>();

        foreach (var registered in RegisteredInstalls())
        {
            roots.Add(registered);
        }

        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Hearthstone");
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hearthstone");
        AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone");

        foreach (var drive in FixedDrives())
        {
            foreach (var parts in DriveRelativeInstalls)
            {
                AddRoot(roots, drive, parts);
            }
        }

        return [.. roots.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Hlubší hledání pro průvodce připojením: projde všechny pevné disky do zadané hloubky
    /// a vrátí složky, ve kterých leží <c>Hearthstone.exe</c>. Najde i instalaci ve vlastní
    /// složce typu <c>D:\Hry\Hearthstone</c>, na kterou obvyklé cesty nesáhnou. Do pravidelného
    /// hledání nepatří, protože prochází disk; volá se jen na přání uživatele.
    /// </summary>
    public static IReadOnlyList<string> ScanDrives(int maxDepth = 2)
    {
        var found = new List<string>();
        foreach (var drive in FixedDrives())
        {
            found.AddRange(FindInstallsUnder(drive, maxDepth));
        }

        return [.. found.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Složky pod daným kořenem, do zadané hloubky, které obsahují <c>Hearthstone.exe</c>.</summary>
    public static IReadOnlyList<string> FindInstallsUnder(string root, int maxDepth)
    {
        var found = new List<string>();
        Walk(root, 0);
        return found;

        void Walk(string directory, int depth)
        {
            if (File.Exists(Path.Combine(directory, "Hearthstone.exe")))
            {
                found.Add(directory);
                return;
            }

            if (depth >= maxDepth)
            {
                return;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (IsSkippable(exception))
            {
                // Chráněné nebo mizející složky se přeskočí; hra v nich není.
                return;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith('$') ||
                    name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    // Symbolické odkazy a spojení by mohly vést do smyčky nebo na jiný disk.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (IsSkippable(exception))
                {
                    continue;
                }

                Walk(child, depth + 1);
            }
        }

        static bool IsSkippable(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    }

    /// <summary>
    /// Nejnovější <c>Power.log</c> v zadaných instalacích. Oddělené od <see cref="InstallRoots"/>,
    /// aby se dalo testovat bez skutečné instalace hry.
    /// </summary>
    public static string? FindInRoots(IEnumerable<string> installRoots)
    {
        ArgumentNullException.ThrowIfNull(installRoots);

        var candidates = new List<string>();
        foreach (var root in installRoots)
        {
            AddLogsOf(candidates, root);
        }

        return candidates
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Instalace zapsané v registry. Odinstalační klíč Blizzardu nese <c>InstallLocation</c>,
    /// takže se najde i hra na jiném disku nebo ve vlastní složce.
    /// </summary>
    private static IEnumerable<string> RegisteredInstalls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        // 32bitový pohled je první: hra je 32bitová aplikace, takže její klíč obvykle leží
        // ve WOW6432Node. 64bitový pohled a HKCU jsou tu pro instalace jen pro jednoho uživatele.
        (RegistryHive Hive, RegistryView View)[] places =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64)
        ];

        string[] subKeys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone",
            @"SOFTWARE\Blizzard Entertainment\Hearthstone"
        ];

        var found = new List<string>();
        foreach (var (hive, view) in places)
        {
            foreach (var subKey in subKeys)
            {
                if (ReadInstallLocation(hive, view, subKey) is { } location)
                {
                    found.Add(location);
                }
            }
        }

        return found;
    }

    private static string? ReadInstallLocation(RegistryHive hive, RegistryView view, string subKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            var value = key?.GetValue("InstallLocation") as string;
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value.Trim());
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
                                              or UnauthorizedAccessException
                                              or IOException
                                              or ArgumentException)
        {
            // Nedostupný nebo poškozený klíč jen znamená, že se instalace najde jinak.
            return null;
        }
    }

    private static IEnumerable<string> FixedDrives()
    {
        try
        {
            return
            [
                .. DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed)
                    .Select(drive => drive.RootDirectory.FullName)
            ];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void AddRoot(List<string> roots, string root, params string[] parts)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            roots.Add(Path.Combine([root, .. parts]));
        }
    }

    /// <summary>
    /// Přidá kandidáty pro jednu instalaci: log přímo v <c>Logs</c> i logy jednotlivých relací.
    /// </summary>
    private static void AddLogsOf(List<string> candidates, string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return;
        }

        var logRoot = Path.Combine(installRoot, "Logs");
        candidates.Add(Path.Combine(logRoot, "Power.log"));
        if (!Directory.Exists(logRoot))
        {
            return;
        }

        try
        {
            foreach (var sessionDirectory in Directory.EnumerateDirectories(logRoot, "Hearthstone_*", SearchOption.TopDirectoryOnly))
            {
                candidates.Add(Path.Combine(sessionDirectory, "Power.log"));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ručně vybraná cesta zůstane dostupná, i když je instalační adresář chráněný.
        }
        catch (IOException)
        {
            // Hra může adresář relace přejmenovat, právě když hledání běží.
        }
    }
}
