using System.Text;

namespace Tracker.Core;

/// <summary>
/// Soubor <c>log.config</c>, kterým Hearthstone řídí, co zapisuje do logů. Bez sekce
/// <c>[Power]</c> s <c>FilePrinting=True</c> hra <c>Power.log</c> vůbec nevytváří a tracker
/// zůstane navždy v režimu naslouchání; to je nejčastější příčina, proč „to nefunguje“ na
/// cizím počítači. Třída soubor přečte, doplní nebo opraví jen tuhle sekci a ostatní nechá
/// tak, jak jsou, protože je tam mohl zapsat jiný nástroj nebo hra sama.
/// </summary>
public static class HearthstoneLogConfig
{
    public const string SectionName = "Power";

    /// <summary>Hodnoty, které hra potřebuje, aby Power.log psala celý včetně tagů entit.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> RequiredValues =
    [
        new("LogLevel", "1"),
        new("FilePrinting", "True"),
        new("ConsolePrinting", "False"),
        new("ScreenPrinting", "False"),
        new("Verbose", "True")
    ];

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Blizzard", "Hearthstone", "log.config");

    /// <summary>Stav sekce <c>[Power]</c> v obsahu souboru.</summary>
    public sealed record Status(bool HasSection, bool FilePrinting, bool LogLevelOk, bool Verbose)
    {
        /// <summary>Hra bude psát Power.log tak, jak ho parser potřebuje.</summary>
        public bool IsReady => HasSection && FilePrinting && LogLevelOk && Verbose;

        public static Status Missing => new(false, false, false, false);
    }

    /// <summary>Prohlédne obsah souboru; <c>null</c> znamená, že soubor neexistuje.</summary>
    public static Status Inspect(string? content)
    {
        if (content is null)
        {
            return Status.Missing;
        }

        var inSection = false;
        var hasSection = false;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (TryReadHeader(line, out var name))
            {
                inSection = name.Equals(SectionName, StringComparison.OrdinalIgnoreCase);
                hasSection |= inSection;
                continue;
            }

            if (inSection && TryReadPair(line, out var key, out var value))
            {
                values[key] = value;
            }
        }

        return new Status(
            hasSection,
            IsTrue(values.GetValueOrDefault("FilePrinting")),
            values.GetValueOrDefault("LogLevel")?.Trim() == "1",
            IsTrue(values.GetValueOrDefault("Verbose")));
    }

    /// <summary>
    /// Vrátí obsah se správnou sekcí <c>[Power]</c>. Existující sekce dostane opravené hodnoty
    /// a ostatní klíče si nechá; chybějící sekce se přidá na konec. Volání nad hotovým obsahem
    /// ho vrátí beze změny, takže se dá bezpečně opakovat.
    /// </summary>
    public static string Ensure(string? content)
    {
        // Zachová konce řádků souboru; hra i Windows píší CRLF, ale ruční úprava mohla nechat LF.
        var newLine = content is not null && content.Contains('\n') && !content.Contains("\r\n") ? "\n" : "\r\n";
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var output = new List<string>();
        var inSection = false;
        var sectionSeen = false;
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Chybějící klíče se doplní na konec sekce, ale před prázdné řádky, které ji oddělují
        // od další; jinak by sekce po opravě vypadala rozsekaná.
        void CloseSection()
        {
            if (!inSection)
            {
                return;
            }

            var insertAt = output.Count;
            while (insertAt > 0 && output[insertAt - 1].Length == 0)
            {
                insertAt--;
            }

            foreach (var (key, value) in RequiredValues)
            {
                if (written.Add(key))
                {
                    output.Insert(insertAt++, $"{key}={value}");
                }
            }

            inSection = false;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (TryReadHeader(line.Trim(), out var name))
            {
                CloseSection();
                inSection = name.Equals(SectionName, StringComparison.OrdinalIgnoreCase);
                sectionSeen |= inSection;
                output.Add(line);
                continue;
            }

            if (inSection && TryReadPair(line.Trim(), out var key, out _))
            {
                var required = RequiredValues.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (required.Key is not null)
                {
                    // Klíč, který hra potřebuje, dostane správnou hodnotu na svém původním místě;
                    // opakovaný výskyt se zahodí, aby nepřebil ten první.
                    if (written.Add(required.Key))
                    {
                        output.Add($"{required.Key}={required.Value}");
                    }

                    continue;
                }
            }

            output.Add(line);
        }

        CloseSection();

        if (!sectionSeen)
        {
            if (output.Count > 0 && output[^1].Length > 0)
            {
                output.Add(string.Empty);
            }

            output.Add($"[{SectionName}]");
            foreach (var (key, value) in RequiredValues)
            {
                output.Add($"{key}={value}");
            }
        }

        return string.Join(newLine, output) + newLine;
    }

    /// <summary>
    /// Opraví soubor na disku. Existující obsah nejdřív zazálohuje do <c>log.config.bak</c>,
    /// aby se dal vrátit. Vrací <c>true</c>, když se něco zapsalo; hotový soubor nechá být.
    /// </summary>
    public static bool Apply(string path)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        var ensured = Ensure(existing);
        if (existing is not null && Inspect(existing).IsReady && NormalizeNewLines(existing) == NormalizeNewLines(ensured))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (existing is not null)
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }

        File.WriteAllText(path, ensured, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static string NormalizeNewLines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    private static bool TryReadHeader(string line, out string name)
    {
        if (line.Length >= 3 && line[0] == '[' && line[^1] == ']')
        {
            name = line[1..^1].Trim();
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool TryReadPair(string line, out string key, out string value)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0 || line.StartsWith('#') || line.StartsWith(';'))
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separator].Trim();
        value = line[(separator + 1)..].Trim();
        return key.Length > 0;
    }

    private static bool IsTrue(string? value) =>
        value is not null && (value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1");
}
