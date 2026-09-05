using Microsoft.Win32;
using System.ComponentModel;
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
using Tracker.Desktop.Themes;

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

    /// <summary>Šířka hlavního sloupce. Obsah se sází v návrhových jednotkách a Viewbox ho škáluje.</summary>
    private const double MainDesignWidth = 380;

    /// <summary>Šířka sloupce s detaily včetně odsazení od hlavního sloupce.</summary>
    private const double DetailsDesignWidth = 214;

    /// <summary>Návrhová výška hlavičky, tedy řádku 0 v rozložení. Sbalený overlay je jen tenhle pruh.</summary>
    private const double HeaderDesignHeight = 42;

    /// <summary>Jak často se v živém režimu ověří, jestli hra nezaložila nový session log.</summary>
    private const int LiveDiscoveryIntervalTicks = 15;

    /// <summary>Jak často se při naslouchání přepočítá diagnostika pro pruh s průvodcem.</summary>
    private const int ListeningRefreshTicks = 5;

    private readonly UserSettings settings;
    private readonly MatchHistory history;
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer resizeTimer;
    private readonly MediaSessionWatcher media;
    private readonly CancellationTokenSource updates = new();
    private PowerLogParser parser = new();
    private GameStateTracker tracker = new();
    private PowerLogTailReader? liveReader;
    private MatchLogArchive? matchArchive;
    private int demoIndex;
    private bool isCollapsed;
    private bool isReading;
    private bool isSourceAutoDiscovered;
    private int ticksSinceDiscovery;
    private TrackerMode mode;

    /// <summary>Přirozená výška rozbalené karty; z ní se počítá zvětšení i ve sbaleném stavu.</summary>
    private double expandedDesignHeight;

    /// <summary>Velikost, kterou okno dostalo od kódu. Co se od ní liší, změnil uživatel úchopem.</summary>
    private Size expectedSize;

    public MainWindow()
    {
        // Paleta musí být ve zdrojích dřív, než se okno postaví; jinak by první snímek přišel
        // ve výchozích barvách a pak přeskočil.
        settings = new UserSettings(SettingsStore.Load(SettingsStore.DefaultPath));
        ThemeManager.Apply(settings.Model, Application.Current.Resources);
        InitializeComponent();

        // Historie zápasů přežívá restart i ořez archivu; ukládá se hned při každé změně.
        history = MatchHistoryStore.Load(MatchHistoryStore.DefaultPath);
        history.Changed += (_, _) => SaveHistory();
        viewModel = new MainViewModel(settings, history);
        DataContext = viewModel;
        Topmost = settings.AlwaysOnTop;
        ApplyDetailsPlacement();
        RestoreStartupPosition();

        settings.PropertyChanged += Settings_PropertyChanged;
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            SaveSettings();
        };

        // Tažení za úchop mění velikost mnohokrát za sekundu; zvětšení se do nastavení zapíše,
        // až se ruka zastaví, jinak by okno s uživatelem bojovalo o každý pixel.
        resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        resizeTimer.Tick += (_, _) =>
        {
            resizeTimer.Stop();
            CommitUserResize();
        };

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += Timer_Tick;
        media = new MediaSessionWatcher(Dispatcher);
        media.Updated += Media_Updated;
        Loaded += MainWindow_Loaded;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void Media_Updated(object? sender, EventArgs eventArgs) =>
        viewModel.ApplyMedia(media.Current, media.Art);

    // Příkazy nic nevracejí: nový stav přijde z ohlášené změny a chyby si hlídá sledovač sám.
    private void MediaPlayPauseButton_Click(object sender, RoutedEventArgs eventArgs) =>
        _ = media.TogglePlayPauseAsync();

    private void MediaNextButton_Click(object sender, RoutedEventArgs eventArgs) =>
        _ = media.SkipNextAsync();

    private void MediaPreviousButton_Click(object sender, RoutedEventArgs eventArgs) =>
        _ = media.SkipPreviousAsync();

    private void SettingsButton_Click(object sender, RoutedEventArgs eventArgs) => SettingsWindow.Open(this, settings);

    private void DetailsToggleButton_Click(object sender, RoutedEventArgs eventArgs) =>
        settings.ShowDetails = !settings.ShowDetails;

    /// <summary>Návrhová šířka podle toho, jestli vpravo stojí panel s detaily.</summary>
    private double DesignWidth => MainDesignWidth + (IsDetailsRight ? DetailsDesignWidth : 0);

    private bool IsDetailsRight => settings.ShowDetails && settings.DetailPlacement == DetailPlacement.Right;

    /// <summary>
    /// Umístí panel s detaily vpravo, pod hlavní sloupec, nebo ho schová. S panelem vpravo
    /// roste návrhová šířka karty; bez něj se karta zúží zpátky na hlavní sloupec.
    /// </summary>
    private void ApplyDetailsPlacement()
    {
        var right = IsDetailsRight;
        DetailsRightHost.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
        DetailsColumn.Width = new GridLength(right ? DetailsDesignWidth : 0);
        DetailsBelowHost.Visibility = settings.ShowDetails && settings.DetailPlacement == DetailPlacement.Below
            ? Visibility.Visible
            : Visibility.Collapsed;
        RootCard.Width = DesignWidth;
        DetailsToggleGlyph.Text = settings.ShowDetails ? "" : "";
        DetailsToggleButton.ToolTip = settings.ShowDetails ? "Skrýt panel s detaily" : "Zobrazit panel s detaily";
        ApplyWindowSize();
    }

    /// <summary>
    /// Karta změnila přirozenou velikost: přibyla nebo zmizela sekce, změnil se počet událostí,
    /// sbalila se. Okno se jí musí přizpůsobit, jinak by Viewbox obsah zdrobnil nebo nechal
    /// prázdné okraje.
    /// </summary>
    private void RootCard_SizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (!isCollapsed && RootCard.ActualHeight > 0)
        {
            expandedDesignHeight = RootCard.ActualHeight;
        }

        ApplyWindowSize();
    }

    /// <summary>
    /// Přepočítá velikost okna z návrhové velikosti karty a zvětšení. Zvětšení se bere vždy
    /// z rozbalené karty, aby sbalená hlavička zůstala stejně velká, jako byla před sbalením.
    /// </summary>
    private void ApplyWindowSize()
    {
        if (expandedDesignHeight <= 0)
        {
            return;
        }

        var scale = ScaleFor(expandedDesignHeight);
        var designWidth = DesignWidth;
        var designHeight = isCollapsed ? HeaderDesignHeight : expandedDesignHeight;
        MinWidth = designWidth * TrackerSettings.MinScale;
        MinHeight = HeaderDesignHeight * TrackerSettings.MinScale;
        expectedSize = new Size(designWidth * scale, designHeight * scale);
        Width = expectedSize.Width;
        Height = expectedSize.Height;
        EnsureOnScreen();
    }

    /// <summary>
    /// Zvětšení z nastavení, případně stažené tak, aby se rozbalené okno vešlo do zvolené části
    /// výšky pracovní plochy. Pracovní plocha je v jednotkách nezávislých na DPI, takže se do
    /// výpočtu nezanese zvětšení, které si Windows nastavují samy.
    /// </summary>
    private double ScaleFor(double designHeight)
    {
        var scale = settings.Scale;
        if (settings.FitToScreen && designHeight > 0)
        {
            scale = Math.Min(scale, SystemParameters.WorkArea.Height * settings.ScreenShare / designHeight);
        }

        return Math.Clamp(scale, TrackerSettings.MinScale, TrackerSettings.MaxScale);
    }

    /// <summary>Velikost, kterou nenastavil kód, změnil uživatel úchopem; po chvíli klidu se přepočte.</summary>
    private void Window_SizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (!IsLoaded || isCollapsed || expandedDesignHeight <= 0 ||
            (Math.Abs(ActualWidth - expectedSize.Width) < 0.5 && Math.Abs(ActualHeight - expectedSize.Height) < 0.5))
        {
            return;
        }

        resizeTimer.Stop();
        resizeTimer.Start();
    }

    /// <summary>
    /// Z rozměru po tažení úchopem se odvodí nové zvětšení a zapíše do nastavení; okno pak
    /// dostane přesně odpovídající výšku i šířku, aby Viewbox nenechal prázdné pruhy.
    /// </summary>
    private void CommitUserResize()
    {
        if (isCollapsed || expandedDesignHeight <= 0)
        {
            return;
        }

        var byWidth = ActualWidth / DesignWidth;
        var byHeight = ActualHeight / expandedDesignHeight;
        var scale = Math.Round(Math.Clamp(Math.Max(byWidth, byHeight), TrackerSettings.MinScale, TrackerSettings.MaxScale), 2);
        if (Math.Abs(scale - settings.Scale) >= 0.01)
        {
            settings.Scale = scale;
        }
        else
        {
            ApplyWindowSize();
        }
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        var all = string.IsNullOrEmpty(eventArgs.PropertyName);
        switch (eventArgs.PropertyName)
        {
            case nameof(UserSettings.Theme) or nameof(UserSettings.Accent) or nameof(UserSettings.Opacity)
                or nameof(UserSettings.ShowCardArt):
                ThemeManager.Apply(settings.Model, Application.Current.Resources);
                break;
            case nameof(UserSettings.Scale) or nameof(UserSettings.FitToScreen) or nameof(UserSettings.ScreenShare):
                ApplyWindowSize();
                break;
            case nameof(UserSettings.ShowDetails) or nameof(UserSettings.DetailPlacement):
                ApplyDetailsPlacement();
                break;
            case nameof(UserSettings.AlwaysOnTop):
                Topmost = settings.AlwaysOnTop;
                break;
            case nameof(UserSettings.RetainedMatches):
                if (matchArchive is not null)
                {
                    matchArchive.RetainedMatches = settings.RetainedMatches;
                }

                break;
        }

        if (all)
        {
            ThemeManager.Apply(settings.Model, Application.Current.Resources);
            Topmost = settings.AlwaysOnTop;
            ApplyDetailsPlacement();
        }

        saveTimer.Stop();
        saveTimer.Start();
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(settings.Model, SettingsStore.DefaultPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nastavení, které se nedá zapsat, nesmí shodit overlay; platí do konce běhu.
        }
    }

    private void SaveHistory()
    {
        try
        {
            MatchHistoryStore.Save(history, MatchHistoryStore.DefaultPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Historie zůstane aspoň v paměti do konce běhu.
        }
    }

    /// <summary>
    /// Pole MMR v historii: Enter zapíše hodnotu hned, Escape vrátí původní. Bez toho by se
    /// hodnota uložila až s opuštěním pole, které v overlayi nemá kam odejít.
    /// </summary>
    private void HistoryMmr_KeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        if (eventArgs.Key == Key.Enter)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        }
    }

    /// <summary>
    /// Zapamatovaná poloha se nasadí ještě před zobrazením, aby okno nepřeskočilo. Bez ní se
    /// okno vystředí na hlavní pracovní plochu podle návrhové velikosti; přesnou velikost
    /// dorovná první rozvržení.
    /// </summary>
    private void RestoreStartupPosition()
    {
        if (settings.RememberWindowPosition && settings.Model.WindowLeft is { } left && settings.Model.WindowTop is { } top)
        {
            Left = left;
            Top = top;
            return;
        }

        var work = SystemParameters.WorkArea;
        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
    }

    private void Window_LocationChanged(object? sender, EventArgs eventArgs)
    {
        if (IsLoaded && WindowState == WindowState.Normal && settings.RememberWindowPosition &&
            !double.IsNaN(Left) && !double.IsNaN(Top))
        {
            settings.RememberPosition(Left, Top);
        }
    }

    /// <summary>Plocha všech monitorů. Okno smí být na kterémkoli z nich, ale ne mimo ně.</summary>
    private static WindowPlacement.Rect VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// Stáhne okno zpět, když jeho hlavička skončí mimo monitory. Overlay se dá přetáhnout jen
    /// za hlavičku, takže bez tohohle zůstane okno nedosažitelné — po odpojení druhého monitoru,
    /// po změně rozlišení nebo když se zapamatovaná poloha vztahuje k monitoru, který už není.
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
    /// Vrátí okno doprostřed hlavního monitoru. Poslední záchrana, když okno skončí mimo
    /// obrazovku nebo se ztratí na monitoru, který už není.
    /// </summary>
    private void ResetWindowButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Normal;
        ApplyWindowSize();

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
            ApplyWindowSize();
            EnsureOnScreen();
        });

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
        // Teprve teď má karta skutečnou výšku a okno skutečnou pozici.
        if (RootCard.ActualHeight > 0)
        {
            expandedDesignHeight = RootCard.ActualHeight;
        }

        ApplyWindowSize();
        if (!settings.RememberWindowPosition || settings.Model.WindowLeft is null)
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
            Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
        }

        EnsureOnScreen();
        if (settings.StartCollapsed && !isCollapsed)
        {
            ToggleCollapsed();
        }

        ApplyPendingUpdate();
        if (settings.CheckForUpdates)
        {
            _ = CheckForUpdateAsync();
        }

        await media.StartAsync();

        var commandLineLog = ParseLogArgument(Environment.GetCommandLineArgs());
        if (commandLineLog is not null && PowerLogDiscovery.Find(commandLineLog) is { } explicitLog)
        {
            // Log z příkazové řádky se jen přehraje, pokud nepatří běžící hře. Jako živý zdroj
            // by z něj vznikla další kopie v archivu zápasů a přepsal by checkpoint.
            if (IsCurrentSessionLog(explicitLog))
            {
                await StartLiveAsync(explicitLog, autoDiscovered: false);
            }
            else
            {
                await StartReplayAsync(explicitLog);
            }
        }
        else
        {
            var discoveredLog = DiscoverLog();
            if (discoveredLog is not null && IsCurrentSessionLog(discoveredLog))
            {
                await StartLiveAsync(discoveredLog, autoDiscovered: true);
            }
            else
            {
                StartListening();
                OfferSetupOnce();
            }
        }

        // Pro podporu na dálku: „spusť tracker s --setup“ otevře průvodce bez hledání v menu.
        if (Environment.GetCommandLineArgs().Skip(1).Any(argument => argument.Equals("--setup", StringComparison.OrdinalIgnoreCase)))
        {
            SetupWindow.Open(this, settings);
        }
    }

    /// <summary>
    /// Při prvním spuštění, kdy hra nemá zapnuté logování, se průvodce otevře sám; jinak by
    /// nový uživatel viděl jen NASLOUCHÁM a nevěděl, že je potřeba něco udělat. Podruhé už ne,
    /// pruh s tlačítkem v overlayi zůstává.
    /// </summary>
    private void OfferSetupOnce()
    {
        if (settings.Model.SetupOffered || !viewModel.IsSetupNeeded)
        {
            return;
        }

        settings.Model.SetupOffered = true;
        SaveSettings();
        SetupWindow.Open(this, settings);
    }

    /// <summary>Hledá log i v instalaci zadané v nastavení, pokud je; jinak jen na obvyklých místech.</summary>
    private string? DiscoverLog() => settings.Model.HearthstoneDirectory is { } custom
        ? PowerLogDiscovery.FindInRoots([custom, .. PowerLogDiscovery.InstallRoots()])
        : PowerLogDiscovery.Find();

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

        var discoveredLog = DiscoverLog();
        if (discoveredLog is not null && IsCurrentSessionLog(discoveredLog))
        {
            await StartLiveAsync(discoveredLog, autoDiscovered: true);
            return;
        }

        if (mode == TrackerMode.Listening)
        {
            // Hra mohla mezitím naběhnout nebo uživatel opravil log.config; pruh to má ukázat
            // bez čekání na další start trackeru.
            if (++ticksSinceDiscovery >= ListeningRefreshTicks)
            {
                ticksSinceDiscovery = 0;
                RefreshListeningState();
            }

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
    /// Ovládání trackeru se schovává do menu, aby patička nezabírala celý řádek. Menu se
    /// otevírá levým kliknutím a nad tlačítkem, protože patička sedí u dolní hrany.
    /// </summary>
    private void ActionsButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            // Tlačítko sedí u pravého dolního rohu okna. Výchozí umístění by menu poslalo
            // doprava mimo okno, takže se menu zarovná pravou hranou k tlačítku a vyskočí nad něj.
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
    private static string MatchesDirectory => AppPaths.MatchesDirectory;

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
        viewModel.IsListening = false;
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

    [GeneratedRegex(@"\d{8}-\d{6}")]
    private static partial Regex MatchStampRegex();

    private async Task StartLiveAsync(string path, bool autoDiscovered)
    {
        timer.Stop();
        matchArchive?.Dispose();
        tracker = new GameStateTracker();
        parser = new PowerLogParser();
        matchArchive = MatchLogArchive.Open(AppPaths.DataDirectory, path, settings.RetainedMatches);

        foreach (var line in matchArchive.ReadActiveLines())
        {
            HandleLine(line);
        }

        if (matchArchive.HasActiveMatch && !tracker.State.IsGameActive)
        {
            // Zápas dohraný, zatímco tracker neběžel: do historie patří stejně jako živý.
            MatchRecorder.RecordFinished(tracker.State, matchArchive, history, DateTimeOffset.Now);
            matchArchive.CompleteMatch();
        }

        liveReader = new PowerLogTailReader(path, matchArchive.ResumePosition);
        mode = TrackerMode.Live;
        isSourceAutoDiscovered = autoDiscovered;
        ticksSinceDiscovery = 0;
        demoIndex = 0;
        viewModel.ModeLabel = "ŽIVĚ";
        viewModel.IsListening = false;
        viewModel.SourceDescription = Path.GetFileName(path);
        viewModel.SourceTooltip = path;
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
        viewModel.IsListening = false;
        viewModel.SourceDescription = "syntetická data";
        viewModel.SourceTooltip = "Vestavěná ukázka bez běžící hry.";
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
        ticksSinceDiscovery = 0;
        viewModel.Update(tracker.State);
        viewModel.ModeLabel = "NASLOUCHÁM";
        viewModel.SourceDescription = "čekám na nový Power.log";
        viewModel.IsListening = true;
        viewModel.PauseButtonText = "Pozastavit";
        viewModel.IsPauseEnabled = false;
        RefreshListeningState();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Start();
    }

    /// <summary>
    /// Stav „čekám na nový Power.log“ vypadá stejně, ať chybí logování ve hře, hra neběží, nebo
    /// je nainstalovaná mimo prohledávané cesty. Pruh v overlayi proto řekne jednou větou, na
    /// co se čeká, a tooltip zdroje vypíše, co všechno se ověřilo.
    /// </summary>
    private void RefreshListeningState()
    {
        var report = SetupDiagnostics.Collect(settings.Model.HearthstoneDirectory, DateTimeOffset.UtcNow);
        viewModel.IsSetupNeeded = !report.IsReady;
        viewModel.ListeningHint = ListeningHint(report);
        viewModel.SourceTooltip = ListeningDiagnosis(report);
    }

    private static string ListeningHint(SetupReport report)
    {
        if (!report.LogConfig.IsReady)
        {
            return "Hra nemá zapnuté logování, Power.log nevzniká. Průvodce to opraví jedním kliknutím.";
        }

        if (!report.InstallFound)
        {
            return "Instalaci Hearthstonu jsem nenašel. Ukažte mi ji v průvodci.";
        }

        if (!report.Game.IsRunning)
        {
            return "Hearthstone neběží. Připojím se sám, až ho spustíte.";
        }

        return report.Game.IsInaccessible
            ? "Hearthstone běží jako správce. Připojím se, jakmile začne psát log."
            : "Hearthstone běží. Připojím se s prvním zápasem.";
    }

    private static string ListeningDiagnosis(SetupReport report)
    {
        var lines = new List<string> { "Power.log aktuální relace hry jsem nenašel." };

        lines.Add(report.Game switch
        {
            { IsRunning: false } => "• Hearthstone neběží. Tracker se připojí, až ho spustíte.",
            { IsInaccessible: true } => "• Hearthstone běží jako správce; log se pozná podle času zápisu.",
            _ => "• Hearthstone běží."
        });

        lines.Add(report.LogConfig switch
        {
            { IsReady: true } => "• log.config má sekci [Power].",
            { HasSection: true } => $"• Sekce [Power] v {report.LogConfigPath} je neúplná; průvodce ji opraví.",
            _ => $"• V {report.LogConfigPath} chybí sekce [Power]. Bez ní hra Power.log vůbec nepíše; po doplnění restartujte hru."
        });

        lines.Add(report.InstallRoots.Count > 0
            ? "• Adresář Logs jsem našel v: " + string.Join(", ", report.InstallRoots)
            : "• V žádné z prohledaných instalací není adresář Logs.");

        if (report.CustomDirectory is { } custom)
        {
            lines.Add(report.CustomDirectoryHasLogs
                ? $"• Instalace z nastavení: {custom}."
                : $"• Instalace z nastavení {custom} nemá adresář Logs.");
        }

        if (report.LatestPowerLog is { } latest)
        {
            lines.Add($"• Poslední Power.log: {latest} (zapsán {report.LatestPowerLogWritten?.ToLocalTime():g}), ale nepatří běžící hře.");
        }

        lines.Add("• Průvodce připojením je v menu i v nastavení; vlastní cestu k logu lze vybrat tlačítkem Vybrat log.");
        return string.Join(Environment.NewLine, lines);
    }

    private void SetupButton_Click(object sender, RoutedEventArgs eventArgs) => SetupWindow.Open(this, settings);

    private bool HandleLine(string line) => tracker.Apply(parser.Parse(line));

    private bool HandleLiveLine(string line) =>
        MatchRecorder.Handle(parser, tracker, matchArchive, line, DateTimeOffset.Now, history);

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

        var discovered = DiscoverLog();
        return discovered is not null &&
               !discovered.Equals(liveReader.Path, StringComparison.OrdinalIgnoreCase) &&
               IsCurrentSessionLog(discovered)
            ? discovered
            : null;
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        settings.PropertyChanged -= Settings_PropertyChanged;
        timer.Stop();
        saveTimer.Stop();
        resizeTimer.Stop();
        SaveSettings();
        updates.Cancel();
        media.Updated -= Media_Updated;
        media.Dispose();
        matchArchive?.Dispose();
        base.OnClosed(eventArgs);
    }

    /// <summary>
    /// Log patří běžící hře. Když hra běží jako správce a tracker ne, Windows start procesu
    /// neprozradí; pak rozhoduje, jestli do logu hra nedávno psala (viz <see cref="SetupDiagnostics"/>).
    /// </summary>
    private static bool IsCurrentSessionLog(string path) =>
        SetupDiagnostics.IsCurrentSessionLog(path, DateTimeOffset.UtcNow);

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

    /// <summary>
    /// Sbalí overlay na pruh hlavičky, nebo ho vrátí do plné velikosti. Hlavička musí zůstat
    /// vidět v původní velikosti: je to jediné místo, kterým se okno chytá a přetahuje, a taky
    /// jediná cesta zpátky. Sbalená hlavička je celá karta, takže se zakulatí i dole.
    /// </summary>
    private void ToggleCollapsed()
    {
        isCollapsed = !isCollapsed;
        ContentPanel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = isCollapsed ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        CollapseGlyph.Text = isCollapsed ? "" : "";
        CollapseButton.ToolTip = isCollapsed ? "Rozbalit overlay" : "Sbalit overlay";
        HeaderCard.CornerRadius = isCollapsed ? new CornerRadius(9) : new CornerRadius(9, 9, 0, 0);
        ApplyWindowSize();
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
