using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace Tracker.Desktop;

/// <summary>
/// Okno s patch notes Hearthstonu ve vestavěném prohlížeči. Drží se nad hrou stejně jako
/// overlay, dá se zvětšit, přesunout i zavřít. Když na stroji chybí runtime WebView2,
/// okno se neotevře a odkaz se pošle do systémového prohlížeče, takže aplikace na
/// runtimu není závislá.
/// </summary>
public partial class PatchNotesWindow : Window
{
    /// <summary>Patch notes Hearthstonu; odkaz na konkrétní vydání zůstává platný i později.</summary>
    public const string PatchNotesUrl = "https://hearthstone.blizzard.com/en-us/news/24296231";

    private static PatchNotesWindow? open;

    private PatchNotesWindow()
    {
        InitializeComponent();
        AddressText.Text = PatchNotesUrl;
        // Maximalizovat se dá i dvojklikem na titulní lištu nebo přichycením k okraji,
        // což jde mimo tlačítko, takže glyf se hlídá přes stav okna.
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    /// <summary>
    /// Ukáže okno s patch notes. Druhé kliknutí už otevřené okno jen vytáhne do popředí,
    /// aby se okna nevršila na sebe.
    /// </summary>
    public static void Show(Window owner)
    {
        if (open is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        if (!IsRuntimeAvailable())
        {
            // Bez runtime nemá cenu otevírat prázdné okno; odkaz vyřídí systémový prohlížeč.
            OpenInBrowser();
            return;
        }

        var window = new PatchNotesWindow();
        open = window;
        window.Closed += (_, _) => open = null;

        // Vedle overlaye, ne přes něj. Owner drží okno nad overlayem a zavře ho s ním.
        window.Owner = owner;
        window.Left = Math.Max(0, owner.Left - window.Width - 12);
        window.Top = owner.Top;
        window.Show();
        _ = window.StartBrowserAsync();
    }

    /// <summary>Runtime WebView2 se pozná podle verze; když chybí, volání vyhodí výjimku.</summary>
    private static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is DllNotFoundException or FileNotFoundException)
        {
            return false;
        }
    }

    private async Task StartBrowserAsync()
    {
        try
        {
            // Vlastní složka s daty: výchozí je vedle .exe, kam nemusí být právo zápisu.
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BattlegroundsTracker",
                "webview2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await Browser.EnsureCoreWebView2Async(environment);

            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            // Odkazy s target=_blank by jinak otevřely druhé okno WebView2 bez chrome.
            Browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                Browser.CoreWebView2.Navigate(args.Uri);
            };
            Browser.CoreWebView2.SourceChanged += (_, _) => AddressText.Text = Browser.Source?.ToString() ?? PatchNotesUrl;
            Browser.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    ShowStatus($"Stránku se nepodařilo načíst ({args.WebErrorStatus}).", withBrowserButton: true);
                }
            };

            Browser.Source = new Uri(PatchNotesUrl);
            StatusOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            ShowStatus($"Vestavěný prohlížeč se nepodařilo spustit: {exception.Message}", withBrowserButton: true);
        }
    }

    private void ShowStatus(string message, bool withBrowserButton)
    {
        StatusText.Text = message;
        StatusAction.Visibility = withBrowserButton ? Visibility.Visible : Visibility.Collapsed;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private static void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PatchNotesUrl) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Bez výchozího prohlížeče se nedá dělat nic rozumného.
        }
    }

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Glyfy Segoe MDL2 Assets: E923 je zmenšit zpět, E922 maximalizovat.
    private void UpdateMaximizeGlyph() =>
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "" : "";

    private void MinimizeButton_Click(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs eventArgs) => ToggleMaximized();

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();

    private void BackButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (Browser.CoreWebView2 is not null && Browser.CanGoBack)
        {
            Browser.GoBack();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (Browser.CoreWebView2 is not null)
        {
            Browser.Reload();
        }
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs eventArgs) => OpenInBrowser();
}
