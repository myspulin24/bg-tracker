using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tracker.Core;

namespace Tracker.Desktop;

/// <summary>
/// Průvodce připojením. Ukazuje čtyři kroky, které musí sedět, aby tracker viděl hru: běžící
/// proces, zapnuté logování v <c>log.config</c>, nalezená instalace a <c>Power.log</c> běžící
/// relace. Každý krok má stav a tlačítko, které ho opraví; kontrola se sama opakuje, takže
/// uživatel vidí výsledek hned po zásahu i po startu hry.
/// </summary>
public partial class SetupWindow : Window
{
    private static SetupWindow? open;

    private readonly UserSettings settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>Kdy průvodce naposledy zapsal log.config; hra ho načítá jen při startu.</summary>
    private DateTimeOffset? configFixedAt;
    private string? configError;
    private bool scanning;

    private SetupWindow(UserSettings settings)
    {
        this.settings = settings;
        InitializeComponent();
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            timer.Start();
        };
        Closed += (_, _) => timer.Stop();
    }

    private enum StepState
    {
        Ok,
        Waiting,
        Warning,
        Error
    }

    /// <summary>Otevře průvodce, nebo přenese do popředí ten, který už je otevřený.</summary>
    public static void Open(Window owner, UserSettings settings)
    {
        if (open is { } existing)
        {
            existing.Activate();
            return;
        }

        open = new SetupWindow(settings) { Owner = owner };
        open.Closed += (_, _) => open = null;
        open.Show();
    }

    private void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        var report = SetupDiagnostics.Collect(settings.Model.HearthstoneDirectory, now);
        RenderGame(report);
        RenderConfig(report);
        RenderInstall(report);
        RenderLog(report);
        RenderSummary(report);
        CheckedText.Text = $"Zkontrolováno {now.ToLocalTime():T}; opakuje se každé 2 s.";
    }

    private void RenderGame(SetupReport report)
    {
        ElevateButton.Visibility = report.Game.IsInaccessible ? Visibility.Visible : Visibility.Collapsed;
        switch (report.Game)
        {
            case { IsRunning: false }:
                SetStep(GameGlyph, StepState.Waiting);
                GameDetail.Text = "Hearthstone neběží. Spusťte hru; tracker se připojí sám, jakmile začne psát log.";
                break;
            case { IsInaccessible: true }:
                SetStep(GameGlyph, StepState.Warning);
                GameDetail.Text = "Hearthstone běží jako správce, tracker ne, takže Windows neprozradí, kdy se hra spustila. " +
                                  "Tracker pozná log podle času zápisu; když se přesto nepřipojí, spusťte ho také jako správce.";
                break;
            default:
                SetStep(GameGlyph, StepState.Ok);
                GameDetail.Text = $"Hearthstone běží od {report.Game.StartedAt?.ToLocalTime():t}.";
                break;
        }
    }

    private void RenderConfig(SetupReport report)
    {
        FixConfigButton.IsEnabled = !report.LogConfig.IsReady;
        OpenConfigButton.IsEnabled = report.LogConfigExists;

        if (!report.LogConfigExists)
        {
            SetStep(ConfigGlyph, StepState.Error);
            ConfigDetail.Text = $"Soubor {report.LogConfigPath} neexistuje, hra proto nepíše žádné logy. Zapnutí logování ho vytvoří.";
        }
        else if (!report.LogConfig.HasSection)
        {
            SetStep(ConfigGlyph, StepState.Error);
            ConfigDetail.Text = $"V {report.LogConfigPath} chybí sekce [Power]. Bez ní hra Power.log vůbec nevytváří a tracker nemá co číst.";
        }
        else if (!report.LogConfig.IsReady)
        {
            SetStep(ConfigGlyph, StepState.Warning);
            ConfigDetail.Text = $"Sekce [Power] v {report.LogConfigPath} je neúplná: chybí FilePrinting=True, LogLevel=1 nebo Verbose=True. " +
                                "Power.log by byl prázdný nebo bez tagů entit.";
        }
        else
        {
            SetStep(ConfigGlyph, StepState.Ok);
            ConfigDetail.Text = $"Sekce [Power] je v pořádku: {report.LogConfigPath}";
        }

        if (configError is not null)
        {
            ConfigDetail.Text += $" Zápis se nepovedl: {configError}";
        }

        // Hra čte log.config jen při startu. Když se soubor změnil až po jejím spuštění, nic se
        // neprojeví, dokud ji uživatel nevypne a nezapne; to je ta věc, na kterou se zapomíná.
        var restartNeeded = report.Game.IsRunning && report.LogConfig.IsReady &&
                            ConfigWrittenAfterGameStart(report);
        ConfigNote.Text = "Hra načítá log.config jen při startu. Vypněte Hearthstone a spusťte ho znovu; teprve pak začne Power.log vznikat.";
        ConfigNote.Visibility = restartNeeded ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool ConfigWrittenAfterGameStart(SetupReport report)
    {
        DateTimeOffset written;
        try
        {
            written = new DateTimeOffset(File.GetLastWriteTimeUtc(report.LogConfigPath), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (configFixedAt is { } fixedAt && fixedAt > written)
        {
            written = fixedAt;
        }

        // U hry spuštěné jako správce start není znát; oprava provedená v tomto okně během
        // jejího běhu ale restart vyžaduje určitě.
        return report.Game.StartedAt is { } started ? written > started : configFixedAt is not null;
    }

    private void RenderInstall(SetupReport report)
    {
        ScanButton.IsEnabled = !scanning;
        ScanButton.Content = scanning ? "Prohledávám…" : "Prohledat disky";

        var found = new List<string>();
        if (report.CustomDirectory is { } custom && report.CustomDirectoryHasLogs)
        {
            found.Add($"{custom} (z nastavení)");
        }

        found.AddRange(report.InstallRoots.Where(root =>
            !root.Equals(report.CustomDirectory, StringComparison.OrdinalIgnoreCase)));

        if (found.Count > 0)
        {
            SetStep(InstallGlyph, StepState.Ok);
            InstallDetail.Text = "Instalace s adresářem Logs: " + string.Join("; ", found);
        }
        else if (report.CustomDirectory is { } missing)
        {
            SetStep(InstallGlyph, StepState.Warning);
            InstallDetail.Text = $"Složka z nastavení {missing} nemá adresář Logs. Vyberte složku, ve které leží Hearthstone.exe, nebo nechte prohledat disky.";
        }
        else
        {
            SetStep(InstallGlyph, StepState.Error);
            InstallDetail.Text = "Na obvyklých místech ani v registrech není instalace hry s adresářem Logs. " +
                                 "Nechte prohledat disky, nebo složku vyberte ručně (ta, ve které je Hearthstone.exe).";
        }
    }

    private void RenderLog(SetupReport report)
    {
        if (report.LatestPowerLog is { } latest)
        {
            var written = report.LatestPowerLogWritten?.ToLocalTime().ToString("g") ?? "?";
            if (report.LatestPowerLogIsCurrent)
            {
                SetStep(LogGlyph, StepState.Ok);
                LogDetail.Text = $"Hra do logu píše: {latest} (naposledy {written}). Tracker se k němu připojil, nebo se právě připojuje.";
            }
            else if (!report.Game.IsRunning)
            {
                SetStep(LogGlyph, StepState.Waiting);
                LogDetail.Text = $"Poslední log je z minulé relace: {latest} ({written}). Nový vznikne po startu hry s prvním zápasem.";
            }
            else
            {
                SetStep(LogGlyph, StepState.Warning);
                LogDetail.Text = $"Poslední log {latest} ({written}) je starší než běžící hra. Buď zápas ještě nezačal, " +
                                 "nebo hra načetla log.config bez sekce [Power]; v tom případě ji restartujte.";
            }

            return;
        }

        if (!report.LogConfig.IsReady)
        {
            SetStep(LogGlyph, StepState.Error);
            LogDetail.Text = "Žádný Power.log nevznikl, protože logování ve hře není zapnuté (krok 2).";
        }
        else if (!report.InstallFound)
        {
            SetStep(LogGlyph, StepState.Error);
            LogDetail.Text = "Bez nalezené složky s hrou není kde log hledat (krok 3).";
        }
        else if (report.Game.IsRunning)
        {
            SetStep(LogGlyph, StepState.Warning);
            LogDetail.Text = "Hra běží, ale Power.log zatím nevznikl. Vzniká s prvním zápasem; pokud jste logování zapnuli až po startu hry, restartujte ji.";
        }
        else
        {
            SetStep(LogGlyph, StepState.Waiting);
            LogDetail.Text = "Žádný Power.log zatím není. Vznikne, až hra se zapnutým logováním spustí zápas.";
        }
    }

    private void RenderSummary(SetupReport report)
    {
        string background;
        string border;
        if (report.IsReady && report.LatestPowerLogIsCurrent)
        {
            SummaryText.Text = "Vše je připravené a tracker log běžící hry vidí.";
            SummaryHint.Text = "Toto okno můžete zavřít. Overlay přepne na ŽIVĚ, jakmile dočte, co už hra zapsala.";
            (background, border) = ("Brush.PositiveSoft", "Brush.Positive");
        }
        else if (report.IsReady)
        {
            SummaryText.Text = "Nastavení je v pořádku. Tracker se připojí sám, jakmile hra začne psát Power.log.";
            SummaryHint.Text = report.Game.IsRunning
                ? "Power.log vzniká s prvním zápasem. Pokud jste logování zapnuli až za běhu hry, restartujte ji."
                : "Spusťte Hearthstone a začněte zápas; nic dalšího není potřeba.";
            (background, border) = ("Brush.AccentSoft", "Brush.AccentBorder");
        }
        else
        {
            SummaryText.Text = "Tracker se zatím připojit nemůže. Opravte označené kroky.";
            SummaryHint.Text = !report.LogConfig.IsReady
                ? "Nejčastější příčina: hra nemá zapnuté logování. Stačí kliknout na Zapnout logování a restartovat hru."
                : "Ukažte trackeru složku, ve které je hra nainstalovaná.";
            (background, border) = ("Brush.NegativeSoft", "Brush.Warning");
        }

        SummaryBorder.SetResourceReference(Border.BackgroundProperty, background);
        SummaryBorder.SetResourceReference(Border.BorderBrushProperty, border);
    }

    /// <summary>Glyfy Segoe MDL2: fajfka, hodiny, vykřičník, křížek; barva z motivu.</summary>
    private static void SetStep(TextBlock glyph, StepState state)
    {
        var (text, brush) = state switch
        {
            StepState.Ok => ("", "Brush.Positive"),
            StepState.Waiting => ("", "Brush.Text3"),
            StepState.Warning => ("", "Brush.Warning"),
            _ => ("", "Brush.Negative")
        };

        glyph.Text = text;
        glyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }

    private void FixConfigButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        configError = null;
        try
        {
            if (HearthstoneLogConfig.Apply(HearthstoneLogConfig.DefaultPath))
            {
                configFixedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            configError = exception.Message;
        }

        Refresh();
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var path = HearthstoneLogConfig.DefaultPath;
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            // Bez Poznámkového bloku se soubor jen neotevře; cesta je vypsaná v kroku.
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (scanning)
        {
            return;
        }

        scanning = true;
        ScanDetail.Visibility = Visibility.Collapsed;
        Refresh();

        // Procházení disků trvá i několik sekund; okno mezitím dál dýchá a obnovuje stav.
        var installs = await Task.Run(() => PowerLogDiscovery.ScanDrives(maxDepth: 3));
        scanning = false;

        if (installs.Count == 0)
        {
            ScanDetail.Text = "Prohledání disků do třetí úrovně složek Hearthstone.exe nenašlo. Vyberte složku ručně.";
        }
        else
        {
            // Když je instalací víc, vyhraje ta s adresářem Logs; do ní hra opravdu píše.
            var chosen = installs.FirstOrDefault(root => Directory.Exists(Path.Combine(root, "Logs"))) ?? installs[0];
            settings.HearthstoneDirectory = chosen;
            ScanDetail.Text = installs.Count == 1
                ? $"Nalezeno a uloženo do nastavení: {chosen}"
                : $"Nalezeno {installs.Count} instalací: {string.Join("; ", installs)}. Do nastavení se uložila {chosen}.";
        }

        ScanDetail.Visibility = Visibility.Visible;
        Refresh();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Vyberte složku, ve které je Hearthstone.exe",
            InitialDirectory = Directory.Exists(settings.HearthstoneDirectory) ? settings.HearthstoneDirectory : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            settings.HearthstoneDirectory = dialog.FolderName;
            ScanDetail.Visibility = Visibility.Collapsed;
            Refresh();
        }
    }

    /// <summary>
    /// Nový proces s právy správce; ten původní se zavře, aby dva trackery nečetly tentýž log.
    /// Odmítnutí výzvy UAC skončí výjimkou a tracker běží dál beze změny.
    /// </summary>
    private void ElevateButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (Environment.ProcessPath is not { } executablePath)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }

        Application.Current.MainWindow?.Close();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs eventArgs) => Refresh();

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();
}
