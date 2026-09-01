using System.Text;

namespace Tracker.Core;

/// <summary>Snímek jedné karty na desce, v nabídce Boba nebo v ruce.</summary>
public sealed record BoardMinion(
    int EntityId,
    string Name,
    string? CardId,
    int ZonePosition,
    int? Attack,
    int? Health,
    int? TechLevel,
    string? Race,
    bool IsGolden,
    bool HasTaunt,
    bool HasDivineShield,
    bool HasReborn,
    bool HasVenomous,
    bool HasWindfury,
    int? Cost)
{
    /// <summary>Statistiky ve tvaru <c>3/4</c>; pokud nejsou známé, vrací pomlčku.</summary>
    public string Stats => Attack is null && Health is null
        ? "—"
        : $"{Attack?.ToString() ?? "?"}/{Health?.ToString() ?? "?"}";

    /// <summary>Zkratky klíčových slov, například <c>T · DS · W</c>.</summary>
    public string Keywords
    {
        get
        {
            var builder = new StringBuilder();
            Append(builder, HasTaunt, "T");
            Append(builder, HasDivineShield, "DS");
            Append(builder, HasReborn, "R");
            Append(builder, HasVenomous, "V");
            Append(builder, HasWindfury, "W");
            return builder.ToString();
        }
    }

    internal static BoardMinion From(TrackedEntity entity) => new(
        entity.EntityId,
        entity.Name ?? $"entity #{entity.EntityId}",
        entity.CardId,
        entity.ZonePosition,
        entity.Attack,
        entity.EffectiveHealth,
        entity.IsMinion ? entity.TechLevel : null,
        entity.Race,
        entity.IsGolden,
        entity.HasTaunt,
        entity.HasDivineShield,
        entity.HasReborn,
        entity.HasVenomous || entity.HasPoisonous,
        entity.HasWindfury,
        entity.Cost);

    private static void Append(StringBuilder builder, bool present, string label)
    {
        if (!present)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(" · ");
        }

        builder.Append(label);
    }
}
