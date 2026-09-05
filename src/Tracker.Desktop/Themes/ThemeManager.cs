using System.Windows;
using System.Windows.Media;
using Tracker.Core;

namespace Tracker.Desktop.Themes;

/// <summary>
/// Skládá barvy vzhledu z nastavení a zapisuje je do zdrojů aplikace pod pevnými klíči
/// <c>Brush.*</c>. XAML na ně odkazuje přes <c>DynamicResource</c>, takže přepnutí motivu
/// nebo akcentu překreslí celé okno bez restartu. Paleta je jedna pro celou aplikaci:
/// tlumené povrchy, hranice z průsvitné bílé nebo černé a jediná zvýrazňovací barva.
/// </summary>
internal static class ThemeManager
{
    /// <summary>Klíče, které se drží kvůli oknu s patch notes a starším stylům.</summary>
    private static readonly (string Legacy, string Current)[] Aliases =
    [
        ("Accent", "Brush.Accent"),
        ("PrimaryText", "Brush.Text"),
        ("SecondaryText", "Brush.Text2"),
        ("Positive", "Brush.Positive"),
        ("PanelBackground", "Brush.Surface"),
        ("PanelBorder", "Brush.Border"),
        ("WindowBackground", "Brush.Window")
    ];

    public static void Apply(TrackerSettings settings, ResourceDictionary resources)
    {
        var dark = settings.Theme == ThemeMode.Dark;
        var accent = AccentOf(settings.Accent, dark);
        var text = dark ? Rgb(0xE6, 0xED, 0xF3) : Rgb(0x1F, 0x23, 0x28);
        var teammate = dark ? Rgb(0x56, 0xD4, 0xA0) : Rgb(0x1F, 0x88, 0x3D);
        var opponent = dark ? Rgb(0xF0, 0x88, 0x3E) : Rgb(0xBC, 0x4C, 0x00);
        var line = dark ? Colors.White : Rgb(0x1F, 0x23, 0x28);

        // Okno a hlavička nesou krytí z nastavení; povrchy uvnitř jsou průsvitné jen lehce,
        // aby se pod nimi hra dala tušit, ale text zůstal čitelný.
        Set(resources, "Brush.Window", WithAlpha(dark ? Rgb(0x0D, 0x11, 0x17) : Rgb(0xF4, 0xF6, 0xF9), settings.Opacity));
        Set(resources, "Brush.Header", WithAlpha(dark ? Rgb(0x11, 0x16, 0x1E) : Rgb(0xFF, 0xFF, 0xFF), Math.Min(1, settings.Opacity + 0.02)));
        Set(resources, "Brush.Surface", dark ? Rgba(0x16, 0x1C, 0x26, 0.92) : Rgba(0xFF, 0xFF, 0xFF, 0.92));
        Set(resources, "Brush.Surface2", dark ? Rgb(0x1B, 0x22, 0x30) : Rgb(0xEE, 0xF1, 0xF5));
        Set(resources, "Brush.Hover", dark ? Rgb(0x24, 0x2D, 0x3C) : Rgb(0xE2, 0xE7, 0xEE));
        Set(resources, "Brush.Border", WithAlpha(line, dark ? 0.09 : 0.11));
        Set(resources, "Brush.BorderStrong", WithAlpha(line, dark ? 0.18 : 0.22));
        Set(resources, "Brush.Divider", WithAlpha(line, dark ? 0.07 : 0.09));
        Set(resources, "Brush.Text", text);
        Set(resources, "Brush.Text2", dark ? Rgb(0x9A, 0xA4, 0xB2) : Rgb(0x59, 0x63, 0x6E));
        Set(resources, "Brush.Text3", dark ? Rgb(0x66, 0x70, 0x85) : Rgb(0x8B, 0x94, 0x9E));
        Set(resources, "Brush.Accent", accent);
        Set(resources, "Brush.AccentSoft", WithAlpha(accent, dark ? 0.16 : 0.14));
        Set(resources, "Brush.AccentBorder", WithAlpha(accent, 0.45));
        Set(resources, "Brush.AccentText", dark ? Rgb(0x0B, 0x10, 0x16) : Colors.White);
        Set(resources, "Brush.Positive", dark ? Rgb(0x3F, 0xB9, 0x50) : Rgb(0x1A, 0x7F, 0x37));
        Set(resources, "Brush.PositiveSoft", WithAlpha(dark ? Rgb(0x3F, 0xB9, 0x50) : Rgb(0x1A, 0x7F, 0x37), 0.16));
        Set(resources, "Brush.Negative", dark ? Rgb(0xF8, 0x51, 0x49) : Rgb(0xCF, 0x22, 0x2E));
        Set(resources, "Brush.NegativeSoft", WithAlpha(dark ? Rgb(0xF8, 0x51, 0x49) : Rgb(0xCF, 0x22, 0x2E), 0.18));
        Set(resources, "Brush.Warning", dark ? Rgb(0xD2, 0x99, 0x22) : Rgb(0x9A, 0x67, 0x00));
        Set(resources, "Brush.Gold", dark ? Rgb(0xE3, 0xB3, 0x41) : Rgb(0xB0, 0x88, 0x00));
        Set(resources, "Brush.GoldSoft", WithAlpha(dark ? Rgb(0xE3, 0xB3, 0x41) : Rgb(0xB0, 0x88, 0x00), 0.16));
        Set(resources, "Brush.Health", dark ? Rgb(0xFF, 0x7B, 0x72) : Rgb(0xC9, 0x3C, 0x37));
        Set(resources, "Brush.Armor", dark ? Rgb(0x79, 0xC0, 0xFF) : Rgb(0x09, 0x69, 0xDA));
        Set(resources, "Brush.Teammate", teammate);
        Set(resources, "Brush.TeammateSoft", WithAlpha(teammate, 0.16));
        Set(resources, "Brush.Opponent", opponent);
        Set(resources, "Brush.OpponentSoft", WithAlpha(opponent, 0.16));
        Set(resources, "Brush.LocalSoft", WithAlpha(accent, dark ? 0.14 : 0.12));
        Set(resources, "Brush.ScrollThumb", WithAlpha(line, dark ? 0.22 : 0.28));
        Set(resources, "Brush.Shadow", dark ? Rgba(0, 0, 0, 0.55) : Rgba(0x1F, 0x23, 0x28, 0.18));

        foreach (var (legacy, current) in Aliases)
        {
            resources[legacy] = resources[current];
        }

        resources["CardArtVisibility"] = settings.ShowCardArt ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Barva akcentu na ukázku v nastavení; stejná, jakou dostane rozhraní.</summary>
    public static Color AccentOf(AccentColor accent, bool dark) => accent switch
    {
        AccentColor.Blue => dark ? Rgb(0x58, 0xA6, 0xFF) : Rgb(0x09, 0x69, 0xDA),
        AccentColor.Violet => dark ? Rgb(0xA7, 0x8B, 0xFA) : Rgb(0x7C, 0x3A, 0xED),
        AccentColor.Emerald => dark ? Rgb(0x34, 0xD3, 0x99) : Rgb(0x05, 0x96, 0x69),
        AccentColor.Amber => dark ? Rgb(0xF5, 0xB7, 0x40) : Rgb(0xB4, 0x53, 0x09),
        AccentColor.Rose => dark ? Rgb(0xFB, 0x71, 0x85) : Rgb(0xBE, 0x12, 0x3C),
        _ => dark ? Rgb(0x22, 0xD3, 0xEE) : Rgb(0x08, 0x91, 0xB2)
    };

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);

    private static Color Rgba(byte red, byte green, byte blue, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), red, green, blue);

    private static Color WithAlpha(Color color, double alpha) => Rgba(color.R, color.G, color.B, alpha);
}
