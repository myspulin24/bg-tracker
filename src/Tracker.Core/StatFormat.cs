using System.Globalization;

namespace Tracker.Core;

/// <summary>
/// Zkrácený zápis statistik minionů. V pozdních kolech mají útok i život čtyři místa a v úzkých
/// sloupcích i na kartičce se přestanou vejít.
/// </summary>
public static class StatFormat
{
    /// <summary>
    /// Vrátí <c>948</c>, <c>1k</c>, <c>2,4k</c> nebo <c>12k</c>. Desetina se ukazuje jen do deseti
    /// tisíc; nad tím už je pod čarou a jen by přidávala znak.
    /// </summary>
    public static string Compact(int value)
    {
        var sign = value < 0 ? "−" : string.Empty;
        var magnitude = Math.Abs((long)value);
        if (magnitude < 1000)
        {
            return sign + magnitude.ToString(CultureInfo.InvariantCulture);
        }

        if (magnitude >= 10_000)
        {
            return $"{sign}{magnitude / 1000}k";
        }

        // Zaokrouhluje se dolů, aby zkratka nikdy netvrdila víc, než minion doopravdy má.
        var tenths = magnitude / 100 % 10;
        return tenths == 0
            ? $"{sign}{magnitude / 1000}k"
            : $"{sign}{magnitude / 1000},{tenths}k";
    }

    /// <summary>Totéž pro hodnotu, která nemusí být známá.</summary>
    public static string Compact(int? value, string unknown = "—") =>
        value is { } known ? Compact(known) : unknown;
}
