using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Nastavení se ukládá jako čitelný JSON a musí přežít ruční úpravy: chybějící klíče, cizí
/// klíče, hodnoty mimo rozsah i poškozený soubor. Nic z toho nesmí aplikaci zastavit.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "bgtracker-settings-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RoundTripsEverySetting()
    {
        var settings = new TrackerSettings
        {
            Theme = ThemeMode.Light,
            Accent = AccentColor.Rose,
            Opacity = 0.7,
            Scale = 1.25,
            FitToScreen = false,
            ScreenShare = 0.6,
            ShowStats = false,
            ShowLobby = false,
            ShowNextOpponent = false,
            ShowRaces = false,
            ShowBattleTags = false,
            ShowBoards = false,
            ShowHand = true,
            ShowDetails = false,
            DetailPlacement = DetailPlacement.Below,
            ShowEvents = false,
            EventCount = 3,
            ShowMedia = false,
            LobbyDensity = LobbyDensity.Comfortable,
            ShowCardArt = false,
            AlwaysOnTop = false,
            RememberWindowPosition = false,
            WindowLeft = 120.5,
            WindowTop = -40,
            StartCollapsed = true,
            CheckForUpdates = false,
            HearthstoneDirectory = @"D:\Games\Hearthstone"
        };

        SettingsStore.Save(settings, SettingsPath);
        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal(ThemeMode.Light, loaded.Theme);
        Assert.Equal(AccentColor.Rose, loaded.Accent);
        Assert.Equal(0.7, loaded.Opacity);
        Assert.Equal(1.25, loaded.Scale);
        Assert.False(loaded.FitToScreen);
        Assert.Equal(0.6, loaded.ScreenShare);
        Assert.False(loaded.ShowStats);
        Assert.False(loaded.ShowLobby);
        Assert.False(loaded.ShowNextOpponent);
        Assert.False(loaded.ShowRaces);
        Assert.False(loaded.ShowBattleTags);
        Assert.False(loaded.ShowBoards);
        Assert.True(loaded.ShowHand);
        Assert.False(loaded.ShowDetails);
        Assert.Equal(DetailPlacement.Below, loaded.DetailPlacement);
        Assert.False(loaded.ShowEvents);
        Assert.Equal(3, loaded.EventCount);
        Assert.False(loaded.ShowMedia);
        Assert.Equal(LobbyDensity.Comfortable, loaded.LobbyDensity);
        Assert.False(loaded.ShowCardArt);
        Assert.False(loaded.AlwaysOnTop);
        Assert.False(loaded.RememberWindowPosition);
        Assert.Equal(120.5, loaded.WindowLeft);
        Assert.Equal(-40, loaded.WindowTop);
        Assert.True(loaded.StartCollapsed);
        Assert.False(loaded.CheckForUpdates);
        Assert.Equal(@"D:\Games\Hearthstone", loaded.HearthstoneDirectory);

        // Soubor je čitelný JSON s výčty jako slova, ne čísly, aby se dal upravit ručně.
        Assert.Contains("\"Theme\": \"Light\"", File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void GivesDefaultsWhenTheFileIsMissingOrBroken()
    {
        var defaults = new TrackerSettings();

        var missing = SettingsStore.Load(SettingsPath);
        Assert.Equal(defaults.Accent, missing.Accent);
        Assert.Equal(defaults.Scale, missing.Scale);
        Assert.True(missing.ShowLobby);

        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{ \"Theme\": \"Light\", \"Scale\": ");
        var broken = SettingsStore.Load(SettingsPath);
        Assert.Equal(ThemeMode.Dark, broken.Theme);
        Assert.Equal(1.0, broken.Scale);
    }

    [Fact]
    public void IgnoresUnknownKeysAndFillsMissingOnes()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, """
            {
              // komentář z ruční úpravy
              "accent": "Amber",
              "SomethingFromTheFuture": { "nested": true },
              "EventCount": 4,
            }
            """);

        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal(AccentColor.Amber, loaded.Accent);
        Assert.Equal(4, loaded.EventCount);
        Assert.True(loaded.ShowBoards);
        Assert.Equal(DetailPlacement.Right, loaded.DetailPlacement);
    }

    [Fact]
    public void ClampsValuesOutsideTheAllowedRanges()
    {
        var settings = new TrackerSettings
        {
            Opacity = 2,
            Scale = 0.1,
            ScreenShare = 0,
            EventCount = 99,
            WindowLeft = double.NaN,
            HearthstoneDirectory = "   "
        }.Normalized();

        Assert.Equal(TrackerSettings.MaxOpacity, settings.Opacity);
        Assert.Equal(TrackerSettings.MinScale, settings.Scale);
        Assert.Equal(TrackerSettings.MinScreenShare, settings.ScreenShare);
        Assert.Equal(TrackerSettings.MaxEventCount, settings.EventCount);
        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.HearthstoneDirectory);
    }
}
