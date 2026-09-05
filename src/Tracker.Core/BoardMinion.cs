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
    int? Cost,
    int? ScriptDataNum1 = null,
    int? ScriptDataNum2 = null,
    bool IsTeammatePairCandidate = false,
    bool IsTeammateTripleCandidate = false)
{
    /// <summary>Statistiky ve tvaru <c>3/4</c>; pokud nejsou známé, vrací pomlčku.</summary>
    public string Stats => Attack is null && Health is null
        ? "—"
        : $"{Attack?.ToString() ?? "?"}/{Health?.ToString() ?? "?"}";

    /// <summary>
    /// Nápověda pro Duos: karta by spoluhráči složila pár nebo triple, tak jak to hra značí
    /// ikonou na kartě v nabídce. Mimo Duos a u karet, kterých se to netýká, je prázdná.
    /// </summary>
    public string TeammateHint => IsTeammateTripleCandidate
        ? "triple pro spoluhráče"
        : IsTeammatePairCandidate ? "pár pro spoluhráče" : string.Empty;

    /// <summary>Klíčová slova celým jménem, například <c>Taunt · Divine Shield</c>.</summary>
    public string Keywords
    {
        get
        {
            var builder = new StringBuilder();
            Append(builder, HasTaunt, "Taunt");
            Append(builder, HasDivineShield, "Divine Shield");
            Append(builder, HasReborn, "Reborn");
            Append(builder, HasVenomous, "Venomous");
            Append(builder, HasWindfury, "Windfury");
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
        entity.Cost,
        entity.ScriptDataNum1,
        entity.ScriptDataNum2,
        entity.IsTeammatePairCandidate,
        entity.IsTeammateTripleCandidate);

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
