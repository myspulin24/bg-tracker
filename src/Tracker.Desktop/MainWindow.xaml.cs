using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Tracker.Core;

namespace Tracker.Desktop;

public partial class MainWindow : Window
{
    private enum TrackerMode
    {
        Listening,
        Demo,
        Live
    }

    /// <summary>Výška, na kterou je rozložení navržené, aby se žádný panel nemusel scrollovat.</summary>
    private const double ExpandedDesignHeight = 1163;

    /// <summary>Táž výška se sbalenou sekcí desek.</summary>
    private const double CollapsedDesignHeight = 879;

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

        // Na monitoru, kam se plné rozložení nevejde, se desky sbalí rovnou. Jinak by uživatel
        // přišel o spodek okna včetně ovládacích tlačítek.
        SetBoardsVisible(SystemParameters.WorkArea.Height - 24 >= ExpandedDesignHeight);
        DataContext = viewModel;

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += Timer_Tick;
        Loaded += MainWindow_Loaded;
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
    /// Nastaví výšku okna podle toho, jestli je sekce desek rozbalená, a omezí ji pracovní
    /// plochou. Návrhové výšky odpovídají obsahu, takže nezbývá prázdné místo ani se nic neořízne.
    /// </summary>
    private void UpdateWindowHeight()
    {
        var wanted = BoardsContent.Visibility == Visibility.Visible
            ? ExpandedDesignHeight
            : CollapsedDesignHeight;
        var available = SystemParameters.WorkArea.Height - 24;
        expandedHeight = Math.Max(MinHeight, Math.Min(wanted, available));
        if (!isCollapsed)
        {
            Height = expandedHeight;
        }
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
        if (mode == TrackerMode.Listening)
        {
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

    private async void SelectLogButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Vyberte Hearthstone Power.log",
            Filter = "Power.log|Power.log|Log soubory (*.log)|*.log|Všechny soubory (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await StartLiveAsync(dialog.FileName, autoDiscovered: false);
        }
    }

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
        viewModel.PauseButtonText = "Pozastavit";
        viewModel.IsPauseEnabled = false;
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Start();
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
