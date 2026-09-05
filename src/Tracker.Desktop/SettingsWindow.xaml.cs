using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Tracker.Core;

namespace Tracker.Desktop;

/// <summary>
/// Okno nastavení. Je jen jedno a formulář je svázaný přímo s <see cref="UserSettings"/>, takže
/// se každá změna projeví v overlayi hned a uloží se sama; žádné tlačítko Uložit není.
/// </summary>
public partial class SettingsWindow : Window
{
    private static SettingsWindow? open;

    private readonly UserSettings settings;

    private SettingsWindow(UserSettings settings)
    {
        this.settings = settings;
        InitializeComponent();
        DataContext = settings;
        DataFolderText.Text = DataDirectory;
        SettingsPathText.Text = SettingsStore.DefaultPath;
        Nav.SelectedIndex = 0;
    }

    private static string DataDirectory => AppPaths.DataDirectory;

    /// <summary>Otevře okno, nebo přenese do popředí to, které už je otevřené.</summary>
    public static void Open(Window owner, UserSettings settings)
    {
        if (open is { } existing)
        {
            existing.Activate();
            return;
        }

        open = new SettingsWindow(settings) { Owner = owner };
        open.Closed += (_, _) => open = null;
        open.Show();
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        var index = Nav.SelectedIndex;
        PageAppearance.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageLayout.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageBehavior.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageData.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs eventArgs) => settings.ResetToDefaults();

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", DataDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Bez Průzkumníka se složka jen neotevře; cesta je vypsaná vedle tlačítka.
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Vyberte složku s instalací Hearthstonu",
            InitialDirectory = Directory.Exists(settings.HearthstoneDirectory) ? settings.HearthstoneDirectory : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            settings.HearthstoneDirectory = dialog.FolderName;
        }
    }

    private void ClearDirectoryButton_Click(object sender, RoutedEventArgs eventArgs) =>
        settings.HearthstoneDirectory = string.Empty;

    /// <summary>Průvodce patří k hlavnímu oknu, aby přežil zavření nastavení.</summary>
    private void SetupButton_Click(object sender, RoutedEventArgs eventArgs) => SetupWindow.Open(Owner ?? this, settings);
}
