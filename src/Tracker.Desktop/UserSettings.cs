using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tracker.Core;

namespace Tracker.Desktop;

/// <summary>
/// Nastavení pro rozhraní: obaluje <see cref="TrackerSettings"/> a hlásí každou změnu, aby na
/// ni mohlo okno nastavení, hlavní okno i uložení na disk reagovat hned. Hodnoty se drží
/// v modelu z Core, takže to, co se ukládá, je přesně to, co je vidět.
/// </summary>
public sealed class UserSettings : INotifyPropertyChanged
{
    public UserSettings(TrackerSettings model)
    {
        Model = model.Normalized();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrackerSettings Model { get; private set; }

    public ThemeMode Theme { get => Model.Theme; set => Set(Model.Theme, value, next => Model.Theme = next); }
    public AccentColor Accent { get => Model.Accent; set => Set(Model.Accent, value, next => Model.Accent = next); }
    public double Opacity { get => Model.Opacity; set => Set(Model.Opacity, value, next => Model.Opacity = next); }
    public double Scale { get => Model.Scale; set => Set(Model.Scale, value, next => Model.Scale = next); }
    public bool FitToScreen { get => Model.FitToScreen; set => Set(Model.FitToScreen, value, next => Model.FitToScreen = next); }
    public double ScreenShare { get => Model.ScreenShare; set => Set(Model.ScreenShare, value, next => Model.ScreenShare = next); }
    public bool ShowStats { get => Model.ShowStats; set => Set(Model.ShowStats, value, next => Model.ShowStats = next); }
    public bool ShowLobby { get => Model.ShowLobby; set => Set(Model.ShowLobby, value, next => Model.ShowLobby = next); }
    public bool ShowNextOpponent { get => Model.ShowNextOpponent; set => Set(Model.ShowNextOpponent, value, next => Model.ShowNextOpponent = next); }
    public bool ShowRaces { get => Model.ShowRaces; set => Set(Model.ShowRaces, value, next => Model.ShowRaces = next); }
    public bool ShowBattleTags { get => Model.ShowBattleTags; set => Set(Model.ShowBattleTags, value, next => Model.ShowBattleTags = next); }
    public bool ShowBoards { get => Model.ShowBoards; set => Set(Model.ShowBoards, value, next => Model.ShowBoards = next); }
    public bool ShowHand { get => Model.ShowHand; set => Set(Model.ShowHand, value, next => Model.ShowHand = next); }
    public bool ShowDetails { get => Model.ShowDetails; set => Set(Model.ShowDetails, value, next => Model.ShowDetails = next); }
    public DetailPlacement DetailPlacement { get => Model.DetailPlacement; set => Set(Model.DetailPlacement, value, next => Model.DetailPlacement = next); }
    public bool ShowEvents { get => Model.ShowEvents; set => Set(Model.ShowEvents, value, next => Model.ShowEvents = next); }
    public int EventCount { get => Model.EventCount; set => Set(Model.EventCount, value, next => Model.EventCount = next); }
    public bool ShowMedia { get => Model.ShowMedia; set => Set(Model.ShowMedia, value, next => Model.ShowMedia = next); }
    public LobbyDensity LobbyDensity { get => Model.LobbyDensity; set => Set(Model.LobbyDensity, value, next => Model.LobbyDensity = next); }
    public bool ShowCardArt { get => Model.ShowCardArt; set => Set(Model.ShowCardArt, value, next => Model.ShowCardArt = next); }
    public bool AlwaysOnTop { get => Model.AlwaysOnTop; set => Set(Model.AlwaysOnTop, value, next => Model.AlwaysOnTop = next); }
    public bool RememberWindowPosition { get => Model.RememberWindowPosition; set => Set(Model.RememberWindowPosition, value, next => Model.RememberWindowPosition = next); }
    public bool StartCollapsed { get => Model.StartCollapsed; set => Set(Model.StartCollapsed, value, next => Model.StartCollapsed = next); }
    public bool CheckForUpdates { get => Model.CheckForUpdates; set => Set(Model.CheckForUpdates, value, next => Model.CheckForUpdates = next); }

    public string HearthstoneDirectory
    {
        get => Model.HearthstoneDirectory ?? string.Empty;
        set => Set(Model.HearthstoneDirectory ?? string.Empty, value, next => Model.HearthstoneDirectory = string.IsNullOrWhiteSpace(next) ? null : next.Trim());
    }

    /// <summary>Posuvník ukazuje procenta; hodnota v modelu je násobek.</summary>
    public double ScalePercent
    {
        get => Math.Round(Model.Scale * 100);
        set => Scale = Math.Round(value) / 100;
    }

    public double OpacityPercent
    {
        get => Math.Round(Model.Opacity * 100);
        set => Opacity = Math.Round(value) / 100;
    }

    public double ScreenSharePercent
    {
        get => Math.Round(Model.ScreenShare * 100);
        set => ScreenShare = Math.Round(value) / 100;
    }

    /// <summary>Poloha okna se mění tažením, ne z formuláře, proto bez ohlášení změny.</summary>
    public void RememberPosition(double left, double top)
    {
        Model.WindowLeft = left;
        Model.WindowTop = top;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Model)));
    }

    /// <summary>Vrátí všechno na výchozí hodnoty a ohlásí každou vlastnost, ať se formulář překreslí.</summary>
    public void ResetToDefaults()
    {
        var position = (Model.WindowLeft, Model.WindowTop);
        Model = new TrackerSettings { WindowLeft = position.WindowLeft, WindowTop = position.WindowTop };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        assign(value);
        Model.Normalized();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(Scale) or nameof(Opacity) or nameof(ScreenShare))
        {
            // Posuvníky v procentech sdílí hodnotu s násobkem; kdo změnil jedno, ať vidí i druhé.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName + "Percent"));
        }
    }
}
