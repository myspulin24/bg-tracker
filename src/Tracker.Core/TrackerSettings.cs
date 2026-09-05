using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracker.Core;

/// <summary>Světlý, nebo tmavý základ vzhledu.</summary>
public enum ThemeMode
{
    Dark,
    Light
}

/// <summary>Zvýrazňovací barva overlaye: lokální hráč, tiery, vybrané prvky, ovládání.</summary>
public enum AccentColor
{
    Cyan,
    Blue,
    Violet,
    Emerald,
    Amber,
    Rose
}

/// <summary>Kde stojí panel s bonusy, trinkety a ekonomikou.</summary>
public enum DetailPlacement
{
    Right,
    Below
}

/// <summary>Hustota řádků v tabulce lobby.</summary>
public enum LobbyDensity
{
    Compact,
    Comfortable
}

/// <summary>
/// Uživatelské nastavení overlaye. Čistá data bez WPF, aby se dala uložit, načíst a otestovat;
/// vzhled z nich skládá desktop. Každá hodnota má výchozí stav, takže chybějící klíč v souboru
/// nic nerozbije, a rozsahy hlídá <see cref="Normalized"/>, aby ručně upravený soubor
/// nemohl vyrobit neviditelné nebo obří okno.
/// </summary>
public sealed class TrackerSettings
{
    public const double MinOpacity = 0.5;
    public const double MaxOpacity = 1.0;
    public const double MinScale = 0.6;
    public const double MaxScale = 1.6;
    public const double MinScreenShare = 0.4;
    public const double MaxScreenShare = 0.98;
    public const int MinEventCount = 2;
    public const int MaxEventCount = 6;

    /// <summary>Tmavý základ je výchozí: overlay leží nad hrou, která je sama tmavá.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    public AccentColor Accent { get; set; } = AccentColor.Cyan;

    /// <summary>Krytí podkladu okna. Pod jednou už by text nad hrou přestal být čitelný.</summary>
    public double Opacity { get; set; } = 0.96;

    /// <summary>Zvětšení rozložení; 1 znamená návrhové jednotky jedna ku jedné.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Omezit zvětšení tak, aby se okno vešlo do části výšky pracovní plochy.</summary>
    public bool FitToScreen { get; set; } = true;

    /// <summary>Jakou část výšky pracovní plochy smí okno nejvýš zabrat.</summary>
    public double ScreenShare { get; set; } = 0.85;

    public bool ShowStats { get; set; } = true;
    public bool ShowLobby { get; set; } = true;
    public bool ShowNextOpponent { get; set; } = true;
    public bool ShowRaces { get; set; } = true;
    public bool ShowBattleTags { get; set; } = true;
    public bool ShowBoards { get; set; } = true;

    /// <summary>Ruka je navíc k tomu, co ukazuje hra sama, proto začíná schovaná.</summary>
    public bool ShowHand { get; set; }

    public bool ShowDetails { get; set; } = true;
    public DetailPlacement DetailPlacement { get; set; } = DetailPlacement.Right;
    public bool ShowEvents { get; set; } = true;
    public int EventCount { get; set; } = 5;
    public bool ShowMedia { get; set; } = true;
    public LobbyDensity LobbyDensity { get; set; } = LobbyDensity.Compact;

    /// <summary>Kresby karet se stahují z internetu; kdo to nechce, vidí kartičky bez obrázku.</summary>
    public bool ShowCardArt { get; set; } = true;

    public bool AlwaysOnTop { get; set; } = true;
    public bool RememberWindowPosition { get; set; } = true;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool StartCollapsed { get; set; }
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Instalace Hearthstonu mimo prohledávané cesty. Prázdné znamená, že se hledá jen
    /// v registrech a na obvyklých místech.
    /// </summary>
    public string? HearthstoneDirectory { get; set; }

    /// <summary>Srovná hodnoty do povolených rozsahů. Vrací tentýž objekt, ať se dá volat v řetězu.</summary>
    public TrackerSettings Normalized()
    {
        Opacity = Clamp(Opacity, MinOpacity, MaxOpacity, 0.96);
        Scale = Clamp(Scale, MinScale, MaxScale, 1.0);
        ScreenShare = Clamp(ScreenShare, MinScreenShare, MaxScreenShare, 0.85);
        EventCount = Math.Clamp(EventCount, MinEventCount, MaxEventCount);
        if (!Enum.IsDefined(Theme))
        {
            Theme = ThemeMode.Dark;
        }

        if (!Enum.IsDefined(Accent))
        {
            Accent = AccentColor.Cyan;
        }

        if (!Enum.IsDefined(DetailPlacement))
        {
            DetailPlacement = DetailPlacement.Right;
        }

        if (!Enum.IsDefined(LobbyDensity))
        {
            LobbyDensity = LobbyDensity.Compact;
        }

        if (WindowLeft is { } left && (double.IsNaN(left) || double.IsInfinity(left)))
        {
            WindowLeft = null;
        }

        if (WindowTop is { } top && (double.IsNaN(top) || double.IsInfinity(top)))
        {
            WindowTop = null;
        }

        HearthstoneDirectory = string.IsNullOrWhiteSpace(HearthstoneDirectory) ? null : HearthstoneDirectory.Trim();
        return this;
    }

    public TrackerSettings Clone() => (TrackerSettings)MemberwiseClone();

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Clamp(value, min, max);
}

/// <summary>
/// Ukládá nastavení do <c>%LOCALAPPDATA%\BattlegroundsTracker\settings.json</c>. Čitelný JSON,
/// aby se dal upravit i ručně; poškozený nebo chybějící soubor dá výchozí hodnoty a nikdy
/// aplikaci nezastaví.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public static TrackerSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new TrackerSettings();
            }

            var settings = JsonSerializer.Deserialize<TrackerSettings>(File.ReadAllText(path), Options);
            return (settings ?? new TrackerSettings()).Normalized();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new TrackerSettings();
        }
    }

    /// <summary>Zapíše přes dočasný soubor, aby po pádu uprostřed zápisu nezůstal useknutý JSON.</summary>
    public static void Save(TrackerSettings settings, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalized(), Options));
        File.Move(temporary, path, overwrite: true);
    }
}
