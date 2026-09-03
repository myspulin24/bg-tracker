namespace Tracker.Core;

/// <summary>
/// Kam smí okno overlaye. Čistá geometrie bez WPF, aby se dala testovat: hlavička okna
/// je jediné místo, kterým se okno dá přetáhnout, takže musí vždycky zůstat na některé
/// obrazovce. Bez toho zůstane okno mimo dosah a uživatel s ním nemůže nic dělat.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Kolik z okna musí být vidět svisle, aby se dalo chytit za hlavičku.</summary>
    public const double VisibleHeaderHeight = 64;

    /// <summary>Kolik z okna musí být vidět vodorovně, aby na hlavičku zbylo místo.</summary>
    public const double VisibleWidth = 120;

    /// <summary>
    /// Posune okno tak, aby jeho hlavička ležela uvnitř plochy všech monitorů. Velikost
    /// nemění; ta se řídí návrhovým rozložením.
    /// </summary>
    /// <param name="left">Levý okraj okna.</param>
    /// <param name="top">Horní okraj okna.</param>
    /// <param name="width">Šířka okna.</param>
    /// <param name="height">Výška okna.</param>
    /// <param name="screen">Plocha všech monitorů, tedy virtuální obrazovka.</param>
    public static (double Left, double Top) Clamp(
        double left,
        double top,
        double width,
        double height,
        Rect screen)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || screen.Width <= 0 || screen.Height <= 0)
        {
            return (left, top);
        }

        // Vodorovně smí okno vyčnívat, ale ne tak, aby z něj zbyl jen pruh bez hlavičky.
        var visibleWidth = Math.Min(VisibleWidth, Math.Max(1, width));
        var minLeft = screen.Left - Math.Max(0, width - visibleWidth);
        var maxLeft = screen.Right - visibleWidth;

        // Svisle nesmí být hlavička nad horní hranou ani pod dolní: nad hranou ji nelze chytit
        // a pod hranou by z okna nebylo vidět nic.
        var minTop = screen.Top;
        var maxTop = screen.Bottom - Math.Min(VisibleHeaderHeight, Math.Max(1, height));

        return (
            Clamp(left, minLeft, maxLeft),
            Clamp(top, minTop, maxTop));
    }

    /// <summary>Je hlavička okna dosažitelná, tedy uvnitř plochy monitorů?</summary>
    public static bool IsReachable(double left, double top, double width, double height, Rect screen)
    {
        var (clampedLeft, clampedTop) = Clamp(left, top, width, height, screen);
        return Math.Abs(clampedLeft - left) < 0.5 && Math.Abs(clampedTop - top) < 0.5;
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);

    /// <summary>Obdélník obrazovky. Vlastní typ, aby <c>Tracker.Core</c> nezávisel na WPF.</summary>
    public readonly record struct Rect(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }
}
