using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Tracker.Core;

namespace Tracker.Desktop;

public partial class MainWindow : Window
{
    private enum TrackerMode
    {
        Listening,
        Demo,
        Live,
        Replay
    }

    /// <summary>Šířka, na kterou je rozložení navržené. Obsah se sází v ní a Viewbox ho škáluje.</summary>
    private const double DesignWidth = 500;

    /// <summary>Výška, na kterou je rozložení navržené, aby se žádný panel nemusel scrollovat.</summary>
    private const double ExpandedDesignHeight = 1163;

    /// <summary>Táž výška se sbalenou sekcí desek.</summary>
    private const double CollapsedDesignHeight = 879;

    /// <summary>Jakou část výšky pracovní plochy má overlay zabrat.</summary>
    private const double WorkAreaShare = 0.94;

    /// <summary>
    /// Mezní zvětšení. Bez dolní hranice by na malém monitoru zdrobněla písmena k nečitelnosti,
    /// bez horní by na velkém overlay zabral půl obrazovky.
    /// </summary>
    private const double MinScale = 0.70;
    private const double MaxScale = 1.30;

    /// <summary>Pod tímhle zvětšením se sekce desek radši sbalí, než aby se drobnilo písmo.</summary>
    private const double CollapseBelowScale = 0.85;

    /// <summary>Jak často se v živém režimu ověří, jestli hra nezaložila nový session log.</summary>
    private const int LiveDiscoveryIntervalTicks = 15;

    private readonly MainViewModel viewModel = new();
    private PowerLogParser parser = new();
    private readonly DispatcherTimer timer;
    private GameStateTracker tracker = new();
    private PowerLogTailReader? liveReader;
    private MatchLogArchive? matchArchive;
    private int demoIndex;
    private double expandedHeight = ExpandedDesignHeight;
    private double expandedMinHeight = 640;
    private bool isCollapsed;
    private bool isReading;
    private bool isSourceAutoDiscovered;
    private int ticksSinceDiscovery;
    private TrackerMode mode;
    private readonly CancellationTokenSource updates = new();

    public MainWindow()
    {
        InitializeComponent();

        // Na monitoru, kde by se plné rozložení muselo hodně zdrobnit, se desky sbalí rovnou.
        // Zbytek se pak vejde v čitelnější velikosti.
        SetBoardsVisible(ScaleFor(ExpandedDesignHeight) >= CollapseBelowScale);
        DataContext = viewModel;

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += Timer_Tick;
        Loaded += MainWindow_Loaded;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void ToggleBoardsButton_Click(object sender, RoutedEventArgs eventArgs) =>
        SetBoardsVisible(BoardsContent.Visibility != Visibility.Visible);

    /// <summary>
    /// Sbalí nebo rozbalí sekci s deskami a rovnou přizpůsobí výšku okna, aby po sbalení
    /// nezůstalo prázdné místo a po rozbalení se obsah vešel.
    /// </summary>
    private void SetBoardsVisible(bool visible)
    {
        BoardsContent.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BoardsChevron.Text = visible ? "▾" : "▸";
        BoardsHeader.Text = visible ? "MOJE DESKA" : "DESKY (skryto)";
        UpdateWindowHeight();
    }

    /// <summary>
    /// Přepočítá velikost okna. Rozložení se sází v návrhových jednotkách a <c>Viewbox</c> ho
    /// zvětší nebo zmenší na okno, takže overlay zabere stejný podíl obrazovky na FullHD i na 4K
    /// a nic se nemusí ořezávat ani scrollovat.
    /// </summary>
    private void UpdateWindowHeight()
    {
        var design = BoardsContent.Visibility == Visibility.Visible
            ? ExpandedDesignHeight
            : CollapsedDesignHeight;
        RootCard.Height = design;

        var scale = ScaleFor(design);
        MinWidth = DesignWidth * MinScale;
        MinHeight = CollapsedDesignHeight * MinScale;
        expandedMinHeight = MinHeight;
        expandedHeight = design * scale;
        Width = DesignWidth * scale;
        if (!isCollapsed)
        {
            Height = expandedHeight;
        }

        EnsureOnScreen();
    }

    /// <summary>Plocha všech monitorů. Okno smí být na kterémkoli z nich, ale ne mimo ně.</summary>
    private static WindowPlacement.Rect VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Stáhne okno zpět, když jeho hlavička skončí mimo monitory. Overlay se dá přetáhnout jen
    /// za hlavičku, takže bez tohohle zůstane okno nedosažitelné — stane se to po odpojení
    /// druhého monitoru, po změně rozlišení nebo když je okno vyšší než obrazovka, na které
    /// se otevřelo, protože vystředění pak pošle horní okraj do minusu.
    /// </summary>
    private void EnsureOnScreen()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        var (left, top) = WindowPlacement.Clamp(Left, Top, Width, Height, VirtualScreen);
        if (!double.IsNaN(left) && Math.Abs(left - Left) > 0.5)
        {
            Left = left;
        }

        if (!double.IsNaN(top) && Math.Abs(top - Top) > 0.5)
        {
            Top = top;
        }
    }

    /// <summary>
    /// Vrátí okno doprostřed hlavního monitoru v návrhové velikosti. Poslední záchrana, když
    /// okno skončí mimo obrazovku nebo se ztratí na monitoru, který už není.
    /// </summary>
    private void ResetWindowButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Normal;
        UpdateWindowHeight();

        var work = SystemParameters.WorkArea;
        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
        EnsureOnScreen();
        Activate();
    }

    /// <summary>
    /// Změna monitorů přijde na vlastním vlákně, takže se přepočet musí vrátit na vlákno
    /// rozhraní. Odpojený monitor jinak nechá okno na souřadnicích, které už neexistují.
    /// </summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs eventArgs) =>
        Dispatcher.BeginInvoke(() =>
        {
            UpdateWindowHeight();
            EnsureOnScreen();
        });

    /// <summary>
    /// Zvětšení pro danou návrhovou výšku. Bere se z výšky pracovní plochy, ne z počtu pixelů:
    /// pracovní plocha už je v jednotkách nezávislých na DPI, takže se do výpočtu nezanese
    /// zvětšení, které si Windows nastavují samy.
    /// </summary>
    private static double ScaleFor(double designHeight)
    {
        var available = SystemParameters.WorkArea.Height * WorkAreaShare;
        return Math.Clamp(available / designHeight, MinScale, MaxScale);
    }

    /// <summary>
    /// Nasadí verzi staženou při minulém běhu a uklidí po předchozí aktualizaci. Běžící proces
    /// dál používá původní obraz, takže se nová verze projeví až příštím spuštěním.
    /// </summary>
    private void ApplyPendingUpdate()
    {
        if (Environment.ProcessPath is not { } executablePath)
        {
            return;
        }

        if (UpdateInstaller.Apply(executablePath))
        {
            viewModel.HasUpdate = true;
            viewModel.IsUpdateReady = true;
            viewModel.UpdateStatus = "Nová verze je nainstalovaná a spustí se po restartu aplikace.";
        }
    }

    /// <summary>
    /// Na pozadí zjistí, jestli je novější vydání, a rovnou ho stáhne. Instalace se odkládá na
    /// další start, aby aktualizace nepřerušila rozehraný zápas.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        if (Environment.ProcessPath is not { } executablePath || viewModel.IsUpdateReady)
        {
            return;
        }

        var update = await new UpdateService().FindNewerReleaseAsync(updates.Token);
        if (update is null || updates.IsCancellationRequested)
        {
            return;
        }

        viewModel.HasUpdate = true;
        viewModel.UpdateStatus = $"Stahuji novou verzi v{update.Version}…";

        var downloaded = await new UpdateService().DownloadAsync(update, executablePath, updates.Token);
        if (updates.IsCancellationRequested)
        {
            return;
        }

        viewModel.IsUpdateReady = downloaded;
        viewModel.UpdateStatus = downloaded
            ? $"Verze v{update.Version} je připravená a nainstaluje se po restartu aplikace."
            : $"Verze v{update.Version} je k dispozici, ale stažení se nepovedlo: {update.ReleaseUrl}";
    }

    private void RestartForUpdateButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (Environment.ProcessPath is not { } executablePath)
        {
            return;
        }

        UpdateInstaller.Apply(executablePath);
        Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
        Close();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        // Teprve teď má okno skutečnou pozici. WindowStartupLocation="CenterScreen" ho vystředí
        // na monitor s kurzorem, ale velikost se počítá z hlavní pracovní plochy, takže na menším
        // monitoru může být okno vyšší než obrazovka a hlavička skončí nad její horní hranou.
        EnsureOnScreen();

        ApplyPendingUpdate();
        _ = CheckForUpdateAsync();

        var commandLineLog = ParseLogArgument(Environment.GetCommandLineArgs());
        if (commandLineLog is not null && PowerLogDiscovery.Find(commandLineLog) is { } explicitLog)
        {
            await StartLiveAsync(explicitLog, autoDiscovered: false);
        }
        else
        {
            var discoveredLog = PowerLogDiscovery.Find();
            if (discoveredLog is not null && IsCurrentSessionLog(discoveredLog))
            {
                await StartLiveAsync(discoveredLog, autoDiscovered: true);
            }
            else
            {
                StartListening();
            }
        }
    }

    private async void Timer_Tick(object? sender, EventArgs eventArgs)
    {
        if (isReading)
        {
            return;
        }

        if (mode == TrackerMode.Live && liveReader is not null)
        {
            isReading = true;
            try
            {
                var changes = await liveReader.ReadNewLinesAsync(HandleLiveLine);
                matchArchive?.SaveCheckpoint(liveReader.Position);
                if (changes > 0)
                {
                    viewModel.Update(tracker.State);
                }
            }
            catch (IOException exception)
            {
                viewModel.SourceDescription = $"chyba čtení: {exception.Message}";
            }
            finally
            {
                isReading = false;
            }

            if (++ticksSinceDiscovery >= LiveDiscoveryIntervalTicks)
            {
                ticksSinceDiscovery = 0;
                if (FindRotatedSessionLog() is { } rotatedLog)
                {
                    await StartLiveAsync(rotatedLog, autoDiscovered: true);
                }
            }

            return;
        }

        var discoveredLog = PowerLogDiscovery.Find();
        if (discoveredLog is not null && IsCurrentSessionLog(discoveredLog))
        {
            await StartLiveAsync(discoveredLog, autoDiscovered: true);
            return;
        }

        if (mode == TrackerMode.Listening)
        {
            return;
        }

        if (demoIndex >= DemoMatch.Lines.Count)
        {
            timer.Stop();
            viewModel.PauseButtonText = "Přehrát znovu";
            return;
        }

        HandleLine(DemoMatch.Lines[demoIndex++]);
        viewModel.Update(tracker.State);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (mode == TrackerMode.Demo && demoIndex >= DemoMatch.Lines.Count)
        {
            StartDemo();
            return;
        }

        if (timer.IsEnabled)
        {
            timer.Stop();
            viewModel.PauseButtonText = "Pokračovat";
        }
        else
        {
            timer.Start();
            viewModel.PauseButtonText = "Pozastavit";
        }
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (mode is TrackerMode.Listening or TrackerMode.Replay)
        {
            // Ze záznamu vede cesta zpátky k živé hře jen přes naslouchání; přehrát ho znovu
            // by ukázalo přesně totéž.
            StartListening();
        }
        else if (mode == TrackerMode.Demo)
        {
            StartDemo();
        }
        else if (liveReader is not null)
        {
            await StartLiveAsync(liveReader.Path, isSourceAutoDiscovered);
        }
    }

    private void DemoButton_Click(object sender, RoutedEventArgs eventArgs) => StartDemo();

    private void PatchNotesButton_Click(object sender, RoutedEventArgs eventArgs) => PatchNotesWindow.Show(this);

    /// <summary>
    /// Ovládání trackeru se schovává do menu, aby spodní lišta nezabírala celý řádek.
    /// Menu se otevírá levým kliknutím a nad tlačítkem, protože lišta sedí u dolní hrany.
    /// </summary>
    private void ActionsButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            // Tlačítko sedí u pravého dolního rohu okna. Výchozí umístění by menu poslalo
            // doprava mimo okno, protože na širší ploše ho WPF nemá kam odrazit, takže se
            // menu zarovná pravou hranou k tlačítku a vyskočí nad něj.
            menu.Placement = PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            [
                new CustomPopupPlacement(
                    new Point(targetSize.Width - popupSize.Width, -popupSize.Height - 6),
                    PopupPrimaryAxis.Horizontal)
            ];
            menu.IsOpen = true;
        }
    }

    private async void SelectLogButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Vyberte zápas nebo Hearthstone Power.log",
            Filter = "Zápasy a logy|*.power.log.br;*.power.log;Power.log|Uložené zápasy (*.power.log.br)|*.power.log.br|Log soubory (*.log)|*.log|Všechny soubory (*.*)|*.*",
            InitialDirectory = Directory.Exists(MatchesDirectory) ? MatchesDirectory : null,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        // Vlastní archiv se jen přehraje. Pouštět ho jako živý zdroj by z něj udělalo další
        // kopii v matches a přepsalo checkpoint, který ukazuje na běžící hru.
        if (MatchLogArchive.IsMatchArchive(dialog.FileName))
        {
            await StartReplayAsync(dialog.FileName);
            return;
        }

        await StartLiveAsync(dialog.FileName, autoDiscovered: false);
    }

    /// <summary>Adresář, kam si tracker ukládá dohrané zápasy.</summary>
    private static string MatchesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BattlegroundsTracker",
        "matches");

    /// <summary>
    /// Přehraje uložený zápas jen pro čtení: nic se nearchivuje a checkpoint zůstane, kde byl.
    /// Zabalený soubor se rozbalí za běhu.
    /// </summary>
    private async Task StartReplayAsync(string path)
    {
        timer.Stop();
        matchArchive?.Dispose();
        matchArchive = null;
        liveReader = null;
        var replayTracker = new GameStateTracker();
        var replayParser = new PowerLogParser();
        mode = TrackerMode.Replay;
        isSourceAutoDiscovered = false;
        demoIndex = 0;

        viewModel.IsLoading = true;
        viewModel.LoadProgress = 0;
        viewModel.LoadStatus = $"Načítám {MatchLabel(path)}…";
        viewModel.IsPauseEnabled = false;

        // Půl milionu řádků se parsuje pár sekund. Na vlákně rozhraní by okno po tu dobu
        // zamrzlo, takže se čte na pozadí a hlásí se postup.
        var progress = new Progress<double>(fraction =>
        {
            viewModel.LoadProgress = fraction * 100;
            viewModel.LoadStatus = $"Načítám {MatchLabel(path)}… {fraction * 100:N0} %";
        });

        var lines = 0;
        try
        {
            lines = await Task.Run(() =>
            {
                var count = 0;
                foreach (var line in MatchLogArchive.ReadMatch(path, progress))
                {
                    replayTracker.Apply(replayParser.Parse(line));
                    count++;
                }

                return count;
            });
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            viewModel.IsLoading = false;
            viewModel.SourceDescription = "zápas se nepodařilo přečíst";
            viewModel.SourceTooltip = exception.Message;
            return;
        }

        tracker = replayTracker;
        parser = replayParser;
        viewModel.IsLoading = false;
        viewModel.Update(tracker.State);
        viewModel.ModeLabel = "ZÁZNAM";
        viewModel.SourceDescription = MatchLabel(path);
        viewModel.SourceTooltip = $"{path}{Environment.NewLine}{lines:N0} řádků";
    }

    /// <summary>
    /// Krátký popis uloženého zápasu do hlavičky. Celé jméno souboru je dlouhé přes čtyřicet
    /// znaků a z hlavičky přetékalo, proto se z něj vytáhne jen datum a čas.
    /// </summary>
    private static string MatchLabel(string path)
    {
        var name = Path.GetFileName(path);
        var stamp = MatchStampRegex().Match(name);
        return stamp.Success &&
               DateTime.TryParseExact(stamp.Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var started)
            ? $"zápas {started:d. M. HH:mm}"
            : name;
    }

    [GeneratedRegex(@"d{8}-d{6}")]
    private static partial Regex MatchStampRegex();

    private async Task StartLiveAsync(string path, bool autoDiscovered)
    {
        timer.Stop();
        matchArchive?.Dispose();
        tracker = new GameStateTracker();
        parser = new PowerLogParser();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BattlegroundsTracker");
        matchArchive = MatchLogArchive.Open(dataDirectory, path);

        foreach (var line in matchArchive.ReadActiveLines())
        {
            HandleLine(line);
        }

        if (matchArchive.HasActiveMatch && !tracker.State.IsGameActive)
        {
            matchArchive.CompleteMatch();
        }

        liveReader = new PowerLogTailReader(path, matchArchive.ResumePosition);
        mode = TrackerMode.Live;
        isSourceAutoDiscovered = autoDiscovered;
        ticksSinceDiscovery = 0;
        demoIndex = 0;
        viewModel.ModeLabel = "ŽIVĚ";
        viewModel.SourceDescription = System.IO.Path.GetFileName(path);
        viewModel.PauseButtonText = "Pozastavit";
        viewModel.IsPauseEnabled = true;

        isReading = true;
        try
        {
            await liveReader.ReadNewLinesAsync(HandleLiveLine);
            matchArchive.SaveCheckpoint(liveReader.Position);
            viewModel.Update(tracker.State);
            timer.Interval = TimeSpan.FromMilliseconds(350);
            timer.Start();
        }
        catch (IOException exception)
        {
            viewModel.SourceDescription = $"chyba čtení: {exception.Message}";
        }
        finally
        {
            isReading = false;
        }
    }

    private void StartDemo()
    {
        timer.Stop();
        matchArchive?.Dispose();
        matchArchive = null;
        tracker = new GameStateTracker();
        parser = new PowerLogParser();
        liveReader = null;
        mode = TrackerMode.Demo;
        demoIndex = 0;
        viewModel.Update(tracker.State);
        viewModel.ModeLabel = "DEMO";
        viewModel.SourceDescription = "syntetická data";
        viewModel.PauseButtonText = "Pozastavit";
        viewModel.IsPauseEnabled = true;
        timer.Interval = TimeSpan.FromMilliseconds(180);
        timer.Start();
    }

    private void StartListening()
    {
        timer.Stop();
        matchArchive?.Dispose();
        matchArchive = null;
        tracker = new GameStateTracker();
        parser = new PowerLogParser();
        liveReader = null;
        mode = TrackerMode.Listening;
        demoIndex = 0;
        viewModel.Update(tracker.State);
        viewModel.ModeLabel = "NASLOUCHÁM";
        viewModel.SourceDescription = "čekám na nový Power.log";
        viewModel.SourceTooltip = ListeningDiagnosis();
        viewModel.PauseButtonText = "Pozastavit";
        viewModel.IsPauseEnabled = false;
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Start();
    }

    /// <summary>
    /// Když se log nenajde, stav „čekám na nový Power.log“ vypadá stejně, ať chybí logování ve
    /// hře, hra neběží, nebo je nainstalovaná mimo prohledávané cesty. Tooltip proto vypíše,
    /// co se ověřilo a kde se hledalo, aby to uživatel poznal bez ptaní.
    /// </summary>
    private static string ListeningDiagnosis()
    {
        var lines = new List<string> { "Power.log aktuální relace hry jsem nenašel." };

        try
        {
            lines.Add(Process.GetProcessesByName("Hearthstone").Length > 0
                ? "• Hearthstone běží."
                : "• Hearthstone neběží. Tracker se připojí, až ho spustíte.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            lines.Add("• Jestli Hearthstone běží, se nedá zjistit. Běží hra jako správce?");
        }

        var config = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Blizzard", "Hearthstone", "log.config");
        try
        {
            lines.Add(File.Exists(config) &&
                      File.ReadAllText(config).Contains("[Power]", StringComparison.OrdinalIgnoreCase)
                ? "• log.config má sekci [Power]."
                : $"• V {config} chybí sekce [Power]. Bez ní hra Power.log vůbec nepíše; po doplnění restartujte hru.");
        }
        catch (IOException)
        {
            lines.Add($"• {config} se nepodařilo přečíst.");
        }
        catch (UnauthorizedAccessException)
        {
            lines.Add($"• K {config} nejsou práva.");
        }

        var roots = PowerLogDiscovery.InstallRoots();
        var withLogs = roots.Where(root => Directory.Exists(Path.Combine(root, "Logs"))).ToArray();
        lines.Add(withLogs.Length > 0
            ? "• Adresář Logs jsem našel v: " + string.Join(", ", withLogs)
            : $"• Ani v jedné z {roots.Count} prohledaných instalací není adresář Logs.");

        lines.Add("• Vlastní cestu k logu lze vybrat v menu tlačítkem Vybrat log.");
        return string.Join(Environment.NewLine, lines);
    }

    private bool HandleLine(string line) => tracker.Apply(parser.Parse(line));

    private bool HandleLiveLine(string line) =>
        MatchRecorder.Handle(parser, tracker, matchArchive, line, DateTimeOffset.Now);

    /// <summary>
    /// Po restartu hry vznikne nový session adresář a starý `Power.log` se přejmenuje. Bez
    /// občasného přehledání by tracker dál držel soubor, do kterého už nikdo nepíše.
    /// Ručně vybraný zdroj se nikdy nepřebíjí.
    /// </summary>
    private string? FindRotatedSessionLog()
    {
        if (!isSourceAutoDiscovered || liveReader is null)
        {
            return null;
        }

        var discovered = PowerLogDiscovery.Find();
        return discovered is not null &&
               !discovered.Equals(liveReader.Path, StringComparison.OrdinalIgnoreCase) &&
               IsCurrentSessionLog(discovered)
            ? discovered
            : null;
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        timer.Stop();
        updates.Cancel();
        matchArchive?.Dispose();
        base.OnClosed(eventArgs);
    }

    private static bool IsCurrentSessionLog(string path)
    {
        try
        {
            var gameProcesses = Process.GetProcessesByName("Hearthstone");
            if (gameProcesses.Length == 0)
            {
                return false;
            }

            var earliestStart = gameProcesses.Min(process => process.StartTime.ToUniversalTime());
            return File.GetLastWriteTimeUtc(path) >= earliestStart.AddMinutes(-1);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ClickCount == 2)
        {
            ToggleCollapsed();
            return;
        }

        DragMove();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs eventArgs) => ToggleCollapsed();

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs) => Close();

    private void ToggleCollapsed()
    {
        if (isCollapsed)
        {
            ContentPanel.Visibility = Visibility.Visible;
            MinHeight = expandedMinHeight;
            Height = expandedHeight;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            CollapseButton.Content = "−";
            CollapseButton.ToolTip = "Sbalit overlay";
        }
        else
        {
            expandedHeight = ActualHeight;
            expandedMinHeight = MinHeight;
            ContentPanel.Visibility = Visibility.Collapsed;
            MinHeight = 64;
            Height = 64;
            ResizeMode = ResizeMode.NoResize;
            CollapseButton.Content = "□";
            CollapseButton.ToolTip = "Rozbalit overlay";
        }

        isCollapsed = !isCollapsed;
    }

    private static string? ParseLogArgument(string[] arguments)
    {
        for (var index = 1; index < arguments.Length - 1; index++)
        {
            if (arguments[index].Equals("--log", StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
