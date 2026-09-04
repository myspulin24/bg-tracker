using Tracker.Core;
using Xunit;

namespace Tracker.Tests;

public sealed class StatFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1049, "1k")]
    [InlineData(1050, "1k")]
    [InlineData(1100, "1,1k")]
    [InlineData(2400, "2,4k")]
    [InlineData(2499, "2,4k")]
    [InlineData(9999, "9,9k")]
    [InlineData(10_000, "10k")]
    [InlineData(12_345, "12k")]
    [InlineData(-2400, "−2,4k")]
    public void ShortensThousandsWithoutEverRoundingUp(int value, string expected) =>
        Assert.Equal(expected, StatFormat.Compact(value));

    [Fact]
    public void KeepsThePlaceholderForUnknownValues()
    {
        Assert.Equal("—", StatFormat.Compact(null));
        Assert.Equal("?", StatFormat.Compact(null, "?"));
        Assert.Equal("2,4k", StatFormat.Compact(2400));
    }

    /// <summary>Odpočet do trinketu se vypisuje česky, takže musí skloňovat.</summary>
    [Theory]
    [InlineData(1, "za 1 kolo")]
    [InlineData(2, "za 2 kola")]
    [InlineData(4, "za 4 kola")]
    [InlineData(5, "za 5 kol")]
    [InlineData(8, "za 8 kol")]
    [InlineData(0, "už tohle kolo")]
    [InlineData(-1, "už tohle kolo")]
    public void DeclinesTheTurnCountdown(int turns, string expected) =>
        Assert.Equal(expected, StatFormat.TurnsLeft(turns));
}
