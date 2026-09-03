using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

/// <summary>
/// Proužek s hudbou dostává data ze systémového rozhraní pro média, které hlásí jen
/// identifikátor aplikace. Případy vycházejí z naměřených hodnot: Edge se hlásí jako
/// <c>MSEdge</c>, Spotify jako <c>Spotify.exe</c> a interpret u videí chybí.
/// </summary>
public sealed class NowPlayingTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("firefox.exe", "Firefox")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("com.squirrel.YouTube_Music_Desktop_App.YouTube Music Desktop App", "YouTube Music")]
    [InlineData("YoutubeMusic.exe", "YouTube Music")]
    [InlineData("TIDAL.exe", "Tidal")]
    public void TranslatesKnownPlayers(string appUserModelId, string expected) =>
        Assert.Equal(expected, MediaSourceName.Friendly(appUserModelId));

    /// <summary>
    /// Neznámý přehrávač se nesmí ztratit; z identifikátoru se vezme to čitelné, protože
    /// i nedokonalé jméno je lepší než prázdné místo v proužku.
    /// </summary>
    [Theory]
    [InlineData("PowerDVD.exe", "PowerDVD")]
    [InlineData("Vendor.Media.Player", "Player")]
    [InlineData("Balicek_kod!MojeAplikace", "MojeAplikace")]
    public void KeepsUnknownPlayersReadable(string appUserModelId, string expected) =>
        Assert.Equal(expected, MediaSourceName.Friendly(appUserModelId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HasNoNameWithoutIdentifier(string? appUserModelId) =>
        Assert.Equal(string.Empty, MediaSourceName.Friendly(appUserModelId));

    [Fact]
    public void JoinsArtistAndSource()
    {
        var playing = new NowPlaying("Sandstorm", "Darude", "Spotify", true, true, true, true);

        Assert.Equal("Darude • Spotify", playing.Subtitle);
        Assert.True(playing.HasTrack);
    }

    /// <summary>U videa v prohlížeči interpret často chybí, takže zbyde jen přehrávač.</summary>
    [Fact]
    public void SkipsMissingArtist()
    {
        var playing = new NowPlaying("ticho.wav", string.Empty, "Edge", true, true, false, false);

        Assert.Equal("Edge", playing.Subtitle);
    }

    [Fact]
    public void HasNoTrackWhenNothingPlays()
    {
        Assert.False(NowPlaying.Nothing.HasTrack);
        Assert.Equal(string.Empty, NowPlaying.Nothing.Subtitle);
    }

    /// <summary>
    /// Přehrávače hlásí tutéž změnu vícekrát za sebou, takže se shodné hodnoty musí dát
    /// poznat a zahodit. Proto je <see cref="NowPlaying" /> záznam se srovnáním podle hodnot.
    /// </summary>
    [Fact]
    public void ComparesByValue()
    {
        var first = new NowPlaying("Sandstorm", "Darude", "Spotify", true, true, true, true);
        var second = new NowPlaying("Sandstorm", "Darude", "Spotify", true, true, true, true);
        var paused = first with { IsPlaying = false };

        Assert.Equal(first, second);
        Assert.NotEqual(first, paused);
    }
}
