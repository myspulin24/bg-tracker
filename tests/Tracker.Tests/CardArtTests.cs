using System.Net;
using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public sealed class CardArtTests
{
    /// <summary>Počítá dotazy, aby šlo ověřit, že se karta stahuje jen jednou.</summary>
    private sealed class CountingHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(respond(request.RequestUri!));
        }
    }

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private static HttpResponseMessage Image() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) };

    private static HttpResponseMessage Missing() => new(HttpStatusCode.NotFound);

    private static string TemporaryDirectory() =>
        Path.Combine(Path.GetTempPath(), $"tracker-cardart-{Guid.NewGuid():N}");

    [Fact]
    public async Task DownloadsCardArtOnceAndThenServesItFromDisk()
    {
        var directory = TemporaryDirectory();
        try
        {
            var handler = new CountingHandler(_ => Image());
            var provider = new CardArtProvider(directory, handler);

            var first = await provider.GetAsync("BG31_035");
            Assert.NotNull(first);
            Assert.Equal(Path.Combine(directory, "BG31_035.jpg"), first);
            Assert.Equal(Jpeg, await File.ReadAllBytesAsync(first));

            // Druhý dotaz i úplně nový poskytovatel už čtou hotový soubor.
            Assert.Equal(first, await provider.GetAsync("BG31_035"));
            Assert.Equal(first, await new CardArtProvider(directory, handler).GetAsync("BG31_035"));
            Assert.Equal(1, handler.Requests);

            // Po nedokončeném stahování nesmí v mezipaměti zůstat nic, co by se tvářilo jako obrázek.
            Assert.Empty(Directory.GetFiles(directory, "*.part"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task AsksOnlyOnceForACardThatHasNoArtwork()
    {
        var directory = TemporaryDirectory();
        try
        {
            var handler = new CountingHandler(_ => Missing());
            var provider = new CardArtProvider(directory, handler);

            // Enchanty a pomocné karty kresbu nemají; opakovat dotaz nemá smysl.
            Assert.Null(await provider.GetAsync("TB_BaconShop_HP_068e3"));
            Assert.Null(await provider.GetAsync("TB_BaconShop_HP_068e3"));
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task RetriesAfterAFailedDownloadButNeverTouchesTheDiskForOddCardIds()
    {
        var directory = TemporaryDirectory();
        try
        {
            var offline = true;
            var handler = new CountingHandler(_ => offline
                ? throw new HttpRequestException("bez sítě")
                : Image());
            var provider = new CardArtProvider(directory, handler);

            Assert.Null(await provider.GetAsync("BGS_039"));

            // Až se síť vrátí, další dotaz to zkusí znovu — výpadek se nesmí zapamatovat natrvalo.
            offline = false;
            Assert.NotNull(await provider.GetAsync("BGS_039"));
            Assert.Equal(2, handler.Requests);

            // Jméno souboru vzniká z ID karty, takže cokoli mimo písmena, číslice a podtržítko
            // se odmítne dřív, než se sáhne na disk.
            Assert.Null(await provider.GetAsync(@"..\..\checkpoint"));
            Assert.Null(await provider.GetAsync(null));
            Assert.Null(await provider.GetAsync(" "));
            Assert.Equal(2, handler.Requests);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static void Delete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
