using System.Net;
using System.Text;
using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public sealed class CardTextTests
{
    /// <summary>Počítá dotazy, aby šlo ověřit, že se databáze stahuje jen jednou.</summary>
    private sealed class CountingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(respond());
        }
    }

    /// <summary>Tvar odpovídá skutečné databázi HearthstoneJSON, jen zkrácené na pár karet.</summary>
    private const string Database = """
        [
          { "id": "BG31_035", "name": "Groundbreaker", "techLevel": 6,
            "text": "[x]After you play a Naga, gain\n+1/+1. <i>(Improved by\nevery 3 spells)</i>" },
          { "id": "BG22_202", "name": "Tad", "text": "When you sell this,\nget a random Murloc." },
          { "id": "BG24_000", "name": "Vanilla" },
          { "id": "EX1_116", "name": "Leeroy Jenkins", "text": "<b>Battlecry:</b> Summon whelps." }
        ]
        """;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string TemporaryCache() =>
        Path.Combine(Path.GetTempPath(), $"tracker-cardtext-{Guid.NewGuid():N}", "cardtext.json");

    [Theory]
    [InlineData("[x]After you play a Naga, gain\n+1/+1.", "After you play a Naga, gain +1/+1.")]
    [InlineData("<b>Battlecry:</b> Deal $3 damage.", "Battlecry: Deal 3 damage.")]
    [InlineData("<i>(Improved by\nevery 3 spells)</i>", "(Improved by every 3 spells)")]
    [InlineData("Deal #2 damage.\n\n<b>Taunt</b>", "Deal 2 damage. Taunt")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void StripsTheMarkupThatOnlyTheGameClientUnderstands(string? raw, string expected) =>
        Assert.Equal(expected, CardTextProvider.Clean(raw));

    [Fact]
    public async Task KeepsOnlyBattlegroundsCardsThatHaveSomeText()
    {
        var cachePath = TemporaryCache();
        try
        {
            var handler = new CountingHandler(() => Json(Database));
            var texts = await new CardTextProvider(cachePath, handler).LoadAsync();

            Assert.Equal("After you play a Naga, gain +1/+1. (Improved by every 3 spells)", texts["BG31_035"]);
            Assert.Equal("When you sell this, get a random Murloc.", texts["BG22_202"]);

            // Vanilla minion popis nemá a karta mimo Battlegrounds se do mezipaměti nedostane.
            Assert.False(texts.ContainsKey("BG24_000"));
            Assert.False(texts.ContainsKey("EX1_116"));
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Delete(cachePath);
        }
    }

    [Fact]
    public async Task ReadsTheStoredCopyInsteadOfDownloadingAgain()
    {
        var cachePath = TemporaryCache();
        try
        {
            var handler = new CountingHandler(() => Json(Database));
            await new CardTextProvider(cachePath, handler).LoadAsync();
            Assert.True(File.Exists(cachePath));

            // Nový poskytovatel nad čerstvou kopií se na síť už neptá.
            var second = await new CardTextProvider(cachePath, handler).LoadAsync();
            Assert.Equal("When you sell this, get a random Murloc.", second["BG22_202"]);
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Delete(cachePath);
        }
    }

    [Fact]
    public async Task FallsBackToTheStoredCopyWhenTheDownloadFails()
    {
        var cachePath = TemporaryCache();
        try
        {
            await new CardTextProvider(cachePath, new CountingHandler(() => Json(Database))).LoadAsync();

            // Kopie se tváří jako prošlá, takže se poskytovatel pokusí stáhnout znovu.
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-30));

            var offline = new CardTextProvider(cachePath, new CountingHandler(
                () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            var texts = await offline.LoadAsync();

            // Prošlý popis je pořád lepší než žádný.
            Assert.Equal("When you sell this, get a random Murloc.", texts["BG22_202"]);
        }
        finally
        {
            Delete(cachePath);
        }
    }

    private static void Delete(string cachePath)
    {
        var directory = Path.GetDirectoryName(cachePath)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
