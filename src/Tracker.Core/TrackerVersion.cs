using System.Reflection;

namespace Tracker.Core;

/// <summary>
/// Verze běžící aplikace. Čte se z atributů sestavení, které plní <c>Version</c>
/// v <c>Directory.Build.props</c>, takže existuje jen jedno místo, kde se verze zvyšuje.
/// </summary>
public static class TrackerVersion
{
    /// <summary>Verze ve tvaru <c>0.1.0</c>, bez případného build metadata za znakem <c>+</c>.</summary>
    public static string Current { get; } = Read();

    /// <summary>Verze pro zobrazení v UI, tedy <c>v0.1.0</c>.</summary>
    public static string Display => $"v{Current}";

    /// <summary>Copyright ze stejného zdroje jako vlastnosti souboru <c>.exe</c>.</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>
    /// Verze se čte z této knihovny, ne ze spouštěného programu. Všechny projekty dědí stejné
    /// <c>Version</c> z <c>Directory.Build.props</c>, takže výsledek nezávisí na tom, jestli kód
    /// běží v overlayi, v konzoli nebo pod testovacím hostitelem.
    /// </summary>
    private static Assembly Self => typeof(TrackerVersion).Assembly;

    private static string Read()
    {
        var assembly = Self;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // InformationalVersion může nést i commit ve tvaru "0.1.0+abc1234".
            var metadata = informational.IndexOf('+');
            return metadata < 0 ? informational : informational[..metadata];
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
