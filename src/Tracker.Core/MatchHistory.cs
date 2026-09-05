using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracker.Core;

/// <summary>
/// Jeden dohraný zápas v historii. Hrdina, umístění, režim a čas se berou z logu; MMR log
/// neobsahuje (kapitola 9.5), takže zůstatek po zápase doplňuje uživatel a změna se počítá
/// proti předchozímu známému zůstatku v témže režimu.
/// </summary>
public sealed class MatchRecord
{
    /// <summary>Jméno archivu zápasu bez přípony, například <c>match-20260905-171234-123</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset EndedAt { get; set; }
    public bool IsDuos { get; set; }
    public string? HeroName { get; set; }
    public string? HeroCardId { get; set; }

    /// <summary>Hrdina spoluhráče v Duos; mimo Duos prázdné.</summary>
    public string? TeammateHeroName { get; set; }

    /// <summary>Konečné umístění; v Duos umístění týmu.</summary>
    public int? Place { get; set; }

    public int PlaceCount { get; set; } = 8;
    public int? Rounds { get; set; }

    /// <summary>Zůstatek MMR po zápase, jak ho hra ukázala; zadává uživatel.</summary>
    public int? Mmr { get; set; }
}

/// <summary>
/// Historie dohraných zápasů: přidávání ze stavu trackeru, výběr posledních v daném režimu
/// a počítání změn MMR. Drží se chronologicky a nejvýš <see cref="Capacity"/> záznamů.
/// </summary>
public sealed class MatchHistory
{
    public const int Capacity = 500;

    private readonly List<MatchRecord> records;

    public MatchHistory(IEnumerable<MatchRecord>? records = null)
    {
        this.records = records is null
            ? []
            : [.. records.Where(record => !string.IsNullOrWhiteSpace(record.Id))
                .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(record => record.EndedAt)];
        Trim();
    }

    /// <summary>Historie se změnila: přibyl zápas, nebo se doplnilo MMR.</summary>
    public event EventHandler? Changed;

    /// <summary>Všechny záznamy od nejstaršího.</summary>
    public IReadOnlyList<MatchRecord> Records => records;

    /// <summary>
    /// Záznam z dohraného zápasu. Vrací <c>null</c>, když se hra ani nerozjela: bez prvního
    /// kola není co zapsat, typicky když hráč lobby opustil při výběru hrdiny.
    /// </summary>
    public static MatchRecord? FromState(TrackerState state, string id, DateTimeOffset endedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.GamesSeen == 0 || state.IsGameActive || state.Turn is null)
        {
            return null;
        }

        var local = state.LocalParticipant;
        return new MatchRecord
        {
            Id = id,
            EndedAt = endedAt,
            IsDuos = state.IsDuos,
            HeroName = local?.HeroName,
            HeroCardId = local?.HeroCardId,
            TeammateHeroName = state.IsDuos ? state.Teammate?.HeroName : null,
            Place = state.FinalPlace ?? local?.LeaderboardPlace,
            PlaceCount = state.PlaceCount,
            Rounds = state.Round
        };
    }

    /// <summary>
    /// Identifikátor zápasu z cesty k jeho archivu, aby historie a záznam patřily k sobě.
    /// Bez archivu se vezme čas konce.
    /// </summary>
    public static string IdFor(string? archivePath, DateTimeOffset endedAt)
    {
        if (!string.IsNullOrWhiteSpace(archivePath))
        {
            var name = Path.GetFileName(archivePath);
            var cut = name.IndexOf(".power.log", StringComparison.OrdinalIgnoreCase);
            return cut > 0 ? name[..cut] : Path.GetFileNameWithoutExtension(name);
        }

        return $"match-{endedAt:yyyyMMdd-HHmmss-fff}";
    }

    /// <summary>Přidá zápas; tentýž zápas (podle <see cref="MatchRecord.Id"/>) podruhé nepřidá.</summary>
    public bool Add(MatchRecord? record)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.Id) ||
            records.Any(existing => existing.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        records.Add(record);
        records.Sort((left, right) => left.EndedAt.CompareTo(right.EndedAt));
        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Posledních <paramref name="count"/> zápasů daného režimu, nejnovější první.</summary>
    public IReadOnlyList<MatchRecord> Latest(bool duos, int count) =>
    [
        .. records.Where(record => record.IsDuos == duos)
            .OrderByDescending(record => record.EndedAt)
            .Take(Math.Max(0, count))
    ];

    /// <summary>Aktuální MMR v režimu: zůstatek z posledního zápasu, u kterého je doplněný.</summary>
    public int? CurrentMmr(bool duos) => records
        .Where(record => record.IsDuos == duos && record.Mmr is not null)
        .OrderByDescending(record => record.EndedAt)
        .FirstOrDefault()?.Mmr;

    /// <summary>
    /// Změna MMR v zápase: rozdíl proti nejbližšímu předchozímu zápasu téhož režimu se známým
    /// zůstatkem. Bez obou hodnot se změna nedá určit.
    /// </summary>
    public int? ChangeFor(MatchRecord record)
    {
        if (record.Mmr is not { } after)
        {
            return null;
        }

        var previous = records
            .Where(candidate => candidate.IsDuos == record.IsDuos && candidate.Mmr is not null &&
                                candidate.EndedAt < record.EndedAt)
            .OrderByDescending(candidate => candidate.EndedAt)
            .FirstOrDefault();
        return previous?.Mmr is { } before ? after - before : null;
    }

    /// <summary>Zapíše zůstatek MMR k zápasu. Vrací <c>true</c>, když se něco změnilo.</summary>
    public bool SetMmr(string id, int? mmr)
    {
        var record = records.FirstOrDefault(candidate => candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (record is null || record.Mmr == mmr)
        {
            return false;
        }

        record.Mmr = mmr;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Trim()
    {
        if (records.Count > Capacity)
        {
            records.RemoveRange(0, records.Count - Capacity);
        }
    }
}

/// <summary>
/// Ukládá historii do <c>history.json</c> ve složce dat, nezávisle na archivu zápasů: archiv
/// se ořezává podle nastavené retence, historie zůstává.
/// </summary>
public static class MatchHistoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "history.json");

    public static MatchHistory Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new MatchHistory();
            }

            var records = JsonSerializer.Deserialize<List<MatchRecord>>(File.ReadAllText(path), Options);
            return new MatchHistory(records);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MatchHistory();
        }
    }

    public static void Save(MatchHistory history, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(history.Records, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
