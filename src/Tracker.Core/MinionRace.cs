namespace Tracker.Core;

/// <summary>
/// Překlad hodnot tagu <c>CARDRACE</c> na názvy typů minionů, jak je používá hra.
/// </summary>
public static class MinionRace
{
    public static string Display(string race) => race.ToUpperInvariant() switch
    {
        "MECHANICAL" => "Mech",
        "MURLOC" => "Murloc",
        "ELEMENTAL" => "Elemental",
        "PIRATE" => "Pirate",
        "DEMON" => "Demon",
        "BEAST" => "Beast",
        "DRAGON" => "Dragon",
        "NAGA" => "Naga",
        "UNDEAD" => "Undead",
        "QUILBOAR" => "Quilboar",
        "ALL" => "Amalgam",
        _ => Capitalize(race)
    };

    private static string Capitalize(string race) => race.Length == 0
        ? race
        : char.ToUpperInvariant(race[0]) + race[1..].ToLowerInvariant();
}
