using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tracker.Core;

namespace Tracker.Desktop;

/// <summary>
/// Mapuje Card ID na <see cref="CardInfo"/> a na pozadí k němu dohraje kresbu i popis. Volá se
/// z vlákna rozhraní při skládání modelu pohledu, proto tabulka nepotřebuje zámek.
/// </summary>
public sealed class CardCache(CardArtProvider art, CardTextProvider texts)
{
    private readonly Dictionary<string, CardInfo> entries = new(StringComparer.OrdinalIgnoreCase);

    // Dispečer aplikace, ne aktuálního vlákna: sdílená instance vzniká při prvním dotazu a ten
    // nemusí přijít z vlákna rozhraní.
    private readonly Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <summary>Sdílená instance pro celé okno; data se tak stahují jednou za běh aplikace.</summary>
    public static CardCache Shared { get; } = new(new CardArtProvider(), new CardTextProvider());

    /// <summary>Databáze popisů, aby se do ní dalo nahlédnout i mimo <see cref="CardInfo" />.</summary>
    public CardTextProvider Texts => texts;

    /// <summary>
    /// Vrátí držák dat pro danou kartu a při prvním dotazu spustí stahování. Vrací vždy stejnou
    /// instanci, aby porovnání modelů pohledu zůstalo stabilní.
    /// </summary>
    public CardInfo? Get(string? cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        if (entries.TryGetValue(cardId, out var existing))
        {
            return existing;
        }

        var info = new CardInfo();
        entries[cardId] = info;
        _ = FillArtAsync(cardId, info);
        _ = FillTextAsync(cardId, info);
        return info;
    }

    private async Task FillArtAsync(string cardId, CardInfo info)
    {
        var path = await art.GetAsync(cardId).ConfigureAwait(false);
        if (path is null || Decode(path) is not { } image)
        {
            return;
        }

        await dispatcher.InvokeAsync(() => info.Image = image);
    }

    private async Task FillTextAsync(string cardId, CardInfo info)
    {
        // Databáze se stahuje celá a jen jednou; každá další karta už jen sáhne do hotové tabulky.
        var database = await texts.LoadAsync().ConfigureAwait(false);
        if (!database.TryGetValue(cardId, out var text))
        {
            return;
        }

        await dispatcher.InvokeAsync(() => info.Text = text);
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
