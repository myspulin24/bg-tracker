using Tracker.Core;
using Xunit;

using Rect = Tracker.Core.WindowPlacement.Rect;

namespace Tracker.Tests;

/// <summary>
/// Hlavička okna je jediné místo, kterým se overlay dá přetáhnout, takže nesmí skončit
/// mimo monitory. Případy vycházejí z hlášení, že se okno přesunulo na druhý monitor
/// a lišta zůstala mimo dosah.
/// </summary>
public sealed class WindowPlacementTests
{
    /// <summary>Dva monitory vedle sebe: 1920×1080 vlevo, 2560×1440 vpravo.</summary>
    private static readonly Rect TwoMonitors = new(0, 0, 1920 + 2560, 1440);

    /// <summary>Jeden monitor 1920×1080.</summary>
    private static readonly Rect OneMonitor = new(0, 0, 1920, 1080);

    [Fact]
    public void KeepsWindowThatIsAlreadyVisible()
    {
        var (left, top) = WindowPlacement.Clamp(300, 120, 500, 1163, TwoMonitors);

        Assert.Equal(300, left);
        Assert.Equal(120, top);
        Assert.True(WindowPlacement.IsReachable(300, 120, 500, 1163, TwoMonitors));
    }

    [Fact]
    public void PullsHeaderBackWhenWindowIsAboveTheTopEdge()
    {
        // Okno vyšší než monitor, vystředěné na svisle: horní okraj vyjde do minusu
        // a hlavička je nedosažitelná.
        var (_, top) = WindowPlacement.Clamp(200, -140, 500, 1360, OneMonitor);

        Assert.Equal(0, top);
        Assert.False(WindowPlacement.IsReachable(200, -140, 500, 1360, OneMonitor));
    }

    [Fact]
    public void PullsWindowBackFromBeyondTheRightEdge()
    {
        // Odpojený druhý monitor nechá okno na souřadnicích, které už neexistují.
        var (left, top) = WindowPlacement.Clamp(3200, 200, 500, 1163, OneMonitor);

        Assert.Equal(1920 - WindowPlacement.VisibleWidth, left);
        Assert.Equal(200, top);
    }

    [Fact]
    public void PullsWindowBackFromBeyondTheLeftEdge()
    {
        var (left, _) = WindowPlacement.Clamp(-800, 100, 500, 1163, OneMonitor);

        Assert.Equal(-(500 - WindowPlacement.VisibleWidth), left);
    }

    [Fact]
    public void PullsHeaderBackWhenWindowIsBelowTheBottomEdge()
    {
        var (_, top) = WindowPlacement.Clamp(100, 2000, 500, 1163, OneMonitor);

        Assert.Equal(1080 - WindowPlacement.VisibleHeaderHeight, top);
    }

    [Fact]
    public void AllowsWindowOnSecondMonitor()
    {
        // Poloha na druhém monitoru je legitimní a nesmí se strhávat na první.
        var (left, top) = WindowPlacement.Clamp(2400, 300, 500, 1163, TwoMonitors);

        Assert.Equal(2400, left);
        Assert.Equal(300, top);
    }

    [Fact]
    public void HandlesMonitorAboveOrLeftOfPrimary()
    {
        // Druhý monitor vlevo nahoře dává virtuální obrazovce záporný počátek.
        var screen = new Rect(-1920, -1080, 1920 + 2560, 1080 + 1440);

        var (left, top) = WindowPlacement.Clamp(-1500, -900, 500, 1163, screen);

        Assert.Equal(-1500, left);
        Assert.Equal(-900, top);
    }

    [Fact]
    public void SurvivesDegenerateInput()
    {
        var empty = new Rect(0, 0, 0, 0);

        // Bez známé obrazovky se nemá kam posouvat; pozice zůstane.
        Assert.Equal((10d, 20d), WindowPlacement.Clamp(10, 20, 500, 1163, empty));
        Assert.Equal((double.NaN, 5d), WindowPlacement.Clamp(double.NaN, 5, 500, 1163, OneMonitor));
    }
}
