namespace Tracker.Core;

/// <summary>
/// Bonusy, které platí pro celou hru a hra si je drží na entitě hráče, ne na jednotlivých
/// minionech: o kolik víc dávají tavern kouzla, blood gemy a plošné buffy na elementály
/// a piráty. Hodnoty jsou kumulativní součty za celou hru, ne přírůstky za jeden efekt.
/// </summary>
/// <remarks>
/// Tagy jsou z enumu hry: <c>TAVERN_SPELL_ATTACK_INCREASE</c> / <c>TAVERN_SPELL_HEALTH_INCREASE</c>,
/// <c>BACON_BLOODGEMBUFFATKVALUE</c> / <c>BACON_BLOODGEMBUFFHEALTHVALUE</c>,
/// <c>BACON_ELEMENTAL_BUFFATKVALUE</c> / <c>BACON_ELEMENTAL_BUFFHEALTHVALUE</c>,
/// <c>BACON_PIRATE_BUFFATKVALUE</c> / <c>BACON_PIRATE_BUFFHEALTHVALUE</c>.
/// </remarks>
public sealed class GlobalBuffs
{
    /// <summary>Kolik útoku přidávají tavern kouzla nad svůj základ.</summary>
    public int SpellAttack { get; internal set; }

    /// <summary>Kolik života přidávají tavern kouzla nad svůj základ.</summary>
    public int SpellHealth { get; internal set; }

    /// <summary>Útok, který dává jeden blood gem.</summary>
    public int BloodGemAttack { get; internal set; }

    /// <summary>Život, který dává jeden blood gem.</summary>
    public int BloodGemHealth { get; internal set; }

    /// <summary>Plošný bonus útoku pro elementály (Nomi a spol.).</summary>
    public int ElementalAttack { get; internal set; }

    /// <summary>Plošný bonus života pro elementály.</summary>
    public int ElementalHealth { get; internal set; }

    /// <summary>Plošný bonus útoku pro piráty.</summary>
    public int PirateAttack { get; internal set; }

    /// <summary>Plošný bonus života pro piráty.</summary>
    public int PirateHealth { get; internal set; }

    public bool HasSpell => SpellAttack != 0 || SpellHealth != 0;
    public bool HasBloodGem => BloodGemAttack != 0 || BloodGemHealth != 0;
    public bool HasElemental => ElementalAttack != 0 || ElementalHealth != 0;
    public bool HasPirate => PirateAttack != 0 || PirateHealth != 0;

    /// <summary>Nic z toho v této hře nenastalo, takže není co ukazovat.</summary>
    public bool IsEmpty => !HasSpell && !HasBloodGem && !HasElemental && !HasPirate;

    internal void Reset()
    {
        SpellAttack = 0;
        SpellHealth = 0;
        BloodGemAttack = 0;
        BloodGemHealth = 0;
        ElementalAttack = 0;
        ElementalHealth = 0;
        PirateAttack = 0;
        PirateHealth = 0;
    }
}
