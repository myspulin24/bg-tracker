using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tracker.Core;

namespace Tracker.Desktop;

/// <summary>
/// Mapuje ID karty na <see cref="CardArt"/> a stará se o načtení obrázku na pozadí. Volá se
/// z vlákna rozhraní při skládání modelu pohledu, proto tabulka nepotřebuje zámek.
/// </summary>
public sealed class CardArtCache(CardArtProvider provider)
{
    private readonly Dictionary<string, CardArt> entries = new(StringComparer.OrdinalIgnoreCase);
    // Dispečer aplikace, ne aktuálního vlákna: sdílená instance vzniká při prvním dotazu a ten
    // nemusí přijít z vlákna rozhraní.
    private readonly Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <summary>Sdílená instance pro celé okno; kresby se tak stahují jednou za běh aplikace.</summary>
    public static CardArtCache Shared { get; } = new(new CardArtProvider());

    /// <summary>
    /// Vrátí držák kresby pro danou kartu a při prvním dotazu spustí stahování. Vrací vždy stejnou
    /// instanci, aby porovnání modelů pohledu zůstalo stabilní.
    /// </summary>
    public CardArt? Get(string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        if (entries.TryGetValue(cardId, out var existing))
        {
            return existing;
        }

        var art = new CardArt();
        entries[cardId] = art;
        _ = FillAsync(cardId, art);
        return art;
    }

    private async Task FillAsync(string cardId, CardArt art)
    {
        var path = await provider.GetAsync(cardId).ConfigureAwait(false);
        if (path is null || Decode(path) is not { } image)
        {
            return;
        }

        await dispatcher.InvokeAsync(() => art.Image = image);
    }

    /// <summary>
    /// Načte obrázek celý do paměti a zmrazí ho. Bez <c>OnLoad</c> by soubor zůstal otevřený
    /// a zmrazení dovolí předat obrázek z vlákna na pozadí do rozhraní.
    /// </summary>
    private static BitmapImage? Decode(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
