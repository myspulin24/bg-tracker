using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tracker.Core;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Tracker.Desktop;

/// <summary>
/// Čte systémové rozhraní pro média (<c>Windows.Media.Control</c>), tedy totéž, co ovládá
/// okénko u tlačítek hlasitosti. Díky tomu vidí Spotify, YouTube v prohlížeči i YouTube
/// Music bez jakéhokoli přihlašování, bez klíčů k API a bez Premium — a stejnou cestou umí
/// poslat přehrát, pozastavit a přeskočit.
///
/// Naměřené chování, na kterém je zbytek postavený: události o změně stavu se hlásí, a to
/// dvakrát po sobě, takže se shodné hodnoty musí zahazovat. Pozice ve skladbě naopak stojí
/// na místě, protože ji přehrávače do systému průběžně neposílají — proto tu žádná není.
/// </summary>
public sealed class MediaSessionWatcher : IDisposable
{
    /// <summary>Záchranný přepočet, kdyby některý přehrávač změnu neohlásil.</summary>
    private static readonly TimeSpan SafetyInterval = TimeSpan.FromSeconds(2);

    /// <summary>Po kolikátém záchranném přepočtu se vyzvednou i názvy a obal.</summary>
    private const int FullRefreshEvery = 10;

    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer safety;
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private string trackKey = string.Empty;
    private int safetyTicks;
    private bool refreshQueued;
    private bool disposed;

    public MediaSessionWatcher(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        safety = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = SafetyInterval };
        safety.Tick += OnSafetyTick;
    }

    /// <summary>Co právě hraje. Mění se jen na vlákně rozhraní.</summary>
    public NowPlaying Current { get; private set; } = NowPlaying.Nothing;

    /// <summary>Obal skladby, pokud ho přehrávač dodal. Obrázek je zmrazený.</summary>
    public BitmapImage? Art { get; private set; }

    public event EventHandler? Updated;

    /// <summary>
    /// Připojí se k systémovému rozhraní. Když to selže — starší Windows, zakázaná služba —
    /// proužek s hudbou se prostě neobjeví a zbytek trackeru jde dál.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception exception)
        {
            Report($"systémové rozhraní pro média se neotevřelo: {exception.Message}");
            return;
        }

        if (disposed)
        {
            return;
        }

        manager.CurrentSessionChanged += OnSessionChanged;
        manager.SessionsChanged += OnSessionChanged;
        safety.Start();
        await RefreshAsync(withTrack: true);
    }

    public Task TogglePlayPauseAsync() => Command(current => current.TryTogglePlayPauseAsync());

    public Task SkipNextAsync() => Command(current => current.TrySkipNextAsync());

    public Task SkipPreviousAsync() => Command(current => current.TrySkipPreviousAsync());

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        safety.Stop();
        safety.Tick -= OnSafetyTick;
        DetachSession();

        if (manager is not null)
        {
            manager.CurrentSessionChanged -= OnSessionChanged;
            manager.SessionsChanged -= OnSessionChanged;
            manager = null;
        }
    }

    /// <summary>
    /// Pošle příkaz aktivnímu přehrávači. Výsledek se nezpracovává: nový stav se stejně
    /// dozvíme z ohlášené změny.
    /// </summary>
    private async Task Command(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> send)
    {
        if (session is not { } current)
        {
            return;
        }

        try
        {
            await send(current);
        }
        catch (Exception exception)
        {
            // Přehrávač může mezi kliknutím a příkazem skončit; tracker kvůli tomu padat nesmí.
            Report($"příkaz přehrávači selhal: {exception.Message}");
        }
    }

    /// <summary>
    /// Události ze systémového rozhraní přicházejí na vlastním vlákně, takže se přepočet
    /// vrací na vlákno rozhraní. Souběžné žádosti se slučují, protože přehrávače hlásí
    /// tutéž změnu vícekrát za sebou.
    /// </summary>
    private void Queue(bool withTrack) => dispatcher.InvokeAsync(
        async () =>
        {
            if (refreshQueued)
            {
                return;
            }

            refreshQueued = true;
            try
            {
                await RefreshAsync(withTrack);
            }
            finally
            {
                refreshQueued = false;
            }
        },
        DispatcherPriority.Background);

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args) =>
        Queue(withTrack: true);

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => Queue(withTrack: true);

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => Queue(withTrack: false);

    private void OnSafetyTick(object? sender, EventArgs args)
    {
        safetyTicks++;
        Queue(withTrack: safetyTicks % FullRefreshEvery == 0);
    }

    /// <summary>
    /// Vyzvedne stav aktivní relace. Názvy a obal se berou jen při <paramref name="withTrack" />,
    /// protože to jsou volání do cizího procesu; stav přehrávání je proti tomu zdarma.
    /// </summary>
    private async Task RefreshAsync(bool withTrack)
    {
        if (disposed || manager is null)
        {
            return;
        }

        GlobalSystemMediaTransportControlsSession? active;
        try
        {
            active = manager.GetCurrentSession();
        }
        catch (Exception exception)
        {
            Report($"aktivní relace se nezjistila: {exception.Message}");
            return;
        }

        if (!ReferenceEquals(active, session))
        {
            DetachSession();
            session = active;
            AttachSession();
            withTrack = true;
        }

        if (session is not { } current)
        {
            Publish(NowPlaying.Nothing, null);
            return;
        }

        try
        {
            var info = current.GetPlaybackInfo();
            var title = Current.Title;
            var artist = Current.Artist;
            var art = Art;

            if (withTrack)
            {
                var media = await current.TryGetMediaPropertiesAsync();
                title = media.Title?.Trim() ?? string.Empty;
                artist = media.Artist?.Trim() ?? string.Empty;

                // Obal se dekóduje jen při změně skladby, ne každé dvě sekundy.
                var key = $"{current.SourceAppUserModelId}|{title}|{artist}";
                if (key != trackKey)
                {
                    trackKey = key;
                    art = await LoadArtAsync(media.Thumbnail);
                }
            }

            Publish(
                new NowPlaying(
                    title,
                    artist,
                    MediaSourceName.Friendly(current.SourceAppUserModelId),
                    info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    info.Controls.IsPlayPauseToggleEnabled,
                    info.Controls.IsNextEnabled,
                    info.Controls.IsPreviousEnabled),
                art);
        }
        catch (Exception exception)
        {
            // Relace patří cizímu procesu, který může zmizet uprostřed volání. Proužek
            // v tom případě zhasne, ale tracker běží dál.
            Report($"stav přehrávače se nepřečetl: {exception.Message}");
            Publish(NowPlaying.Nothing, null);
        }
    }

    /// <summary>
    /// Načte obal do zmrazeného obrázku, aby se dal použít z vlákna rozhraní. Čte se přes
    /// <c>DataReader</c>, protože rozšíření pro převod WinRT streamů v .NET 8 už není.
    /// </summary>
    private static async Task<BitmapImage?> LoadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // Bez obalu se ukáže jen ikona noty; kvůli obrázku se nemá cenu ozývat.
            return null;
        }
    }

    private void AttachSession()
    {
        if (session is not { } current)
        {
            return;
        }

        current.MediaPropertiesChanged += OnMediaPropertiesChanged;
        current.PlaybackInfoChanged += OnPlaybackInfoChanged;
    }

    private void DetachSession()
    {
        if (session is not { } current)
        {
            return;
        }

        current.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        current.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        session = null;
    }

    private void Publish(NowPlaying playing, BitmapImage? art)
    {
        if (playing == Current && ReferenceEquals(art, Art))
        {
            return;
        }

        Current = playing;
        Art = art;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static void Report(string message) =>
        System.Diagnostics.Debug.WriteLine($"[hudba] {message}");
}
