using System.ComponentModel;
using System.Globalization;
using Tracker.Core;

namespace Tracker.Desktop;

/// <summary>
/// Řádek historie zápasů. Hrdina, umístění a datum jsou z logu; zůstatek MMR je editovatelný,
/// protože ho log nenese a doplňuje ho uživatel. Rovnost se porovnává podle obsahu, aby
/// <c>Sync</c> ve view modelu nepřepisoval řádky, které se nezměnily.
/// </summary>
public sealed class MatchHistoryRowViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private readonly Action<string, int?> commitMmr;
    private readonly int? storedMmr;
    private string mmrText;

    public MatchHistoryRowViewModel(MatchRecord record, int? change, Action<string, int?> commitMmr)
    {
        this.commitMmr = commitMmr;
        Id = record.Id;
        IsDuos = record.IsDuos;
        Date = record.EndedAt.ToLocalTime().ToString("d. M.", Czech);
        Hero = record.HeroName ?? "Neznámý hrdina";
        Teammate = record.TeammateHeroName;
        Place = record.Place is { } place ? $"{place}/{record.PlaceCount}" : "—";
        IsFirst = record.Place == 1;
        IsTop = record.Place is { } top && top <= record.PlaceCount / 2;
        Change = change switch
        {
            > 0 => $"+{change}",
            < 0 => $"−{-change}",
            0 => "±0",
            null => "—"
        };
        IsGain = change > 0;
        IsLoss = change < 0;
        storedMmr = record.Mmr;
        mmrText = record.Mmr?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        var when = record.EndedAt.ToLocalTime().ToString("d. M. yyyy H:mm", Czech);
        var rounds = record.Rounds is { } count ? $", {count} kol" : string.Empty;
        var mate = record.TeammateHeroName is { } mateName ? $" · spoluhráč {mateName}" : string.Empty;
        Tooltip = $"{when}{rounds}{mate}{Environment.NewLine}Zůstatek MMR po zápase doplňte do pole vpravo a potvrďte Enterem.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public bool IsDuos { get; }
    public string Date { get; }
    public string Hero { get; }
    public string? Teammate { get; }
    public string Place { get; }
    public bool IsFirst { get; }
    public bool IsTop { get; }
    public string Change { get; }
    public bool IsGain { get; }
    public bool IsLoss { get; }
    public string Tooltip { get; }

    /// <summary>Hrdina i spoluhráč v Duos, oddělení tečkou.</summary>
    public string Heroes => Teammate is null ? Hero : $"{Hero} · {Teammate}";

    /// <summary>
    /// Text v poli MMR. Zapisuje se při opuštění pole nebo Enterem; prázdné pole MMR smaže,
    /// text, který není číslo, se nezapíše a zůstane jen v poli.
    /// </summary>
    public string MmrText
    {
        get => mmrText;
        set
        {
            if (string.Equals(mmrText, value, StringComparison.Ordinal))
            {
                return;
            }

            mmrText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MmrText)));

            var cleaned = value.Replace(" ", string.Empty).Replace(" ", string.Empty).Trim();
            if (cleaned.Length == 0)
            {
                commitMmr(Id, null);
            }
            else if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mmr) && mmr >= 0)
            {
                commitMmr(Id, mmr);
            }
        }
    }

    public override bool Equals(object? other) =>
        other is MatchHistoryRowViewModel row &&
        row.Id == Id && row.Date == Date && row.Heroes == Heroes && row.Place == Place &&
        row.Change == Change && row.storedMmr == storedMmr;

    public override int GetHashCode() => HashCode.Combine(Id, Date, Heroes, Place, Change, storedMmr);
}
