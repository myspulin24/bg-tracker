namespace Tracker.Core;

/// <summary>
/// Jeden ze dvou slotů na trinket. Hra si pro každý slot drží entitu od začátku hry: dokud je
/// slot prázdný, jmenuje se <c>Lesser Trinket</c> nebo <c>Greater Trinket</c> s kartou
/// <c>BG30_Trinket_1st</c> / <c>BG30_Trinket_2nd</c> a nese odpočet
/// <c>BACON_TURNS_LEFT_TO_DISCOVER_TRINKET</c>. Po výběru se <em>tatáž entita</em> přepíše na
/// vybraný trinket, takže slot se pozná jen podle karty, se kterou vznikl.
/// </summary>
/// <param name="Slot">1 pro malý trinket, 2 pro velký.</param>
/// <param name="Name">Jméno vybraného trinketu, nebo <c>null</c>, když je slot ještě prázdný.</param>
/// <param name="TurnsLeft">Kolik kol zbývá do výběru; po výběru už hra odpočet neposílá.</param>
/// <param name="CardId">
/// Karta vybraného trinketu, kterou se dohledá popis efektu. Zůstává prázdná, dokud v entitě
/// leží karta prázdného slotu — jméno se totiž obnoví dřív než karta.
/// </param>
public readonly record struct Trinket(int Slot, string? Name, int? TurnsLeft, string? CardId = null)
{
    /// <summary>Je už trinket vybraný?</summary>
    public bool IsFilled => !string.IsNullOrWhiteSpace(Name);

    /// <summary>
    /// Jména prázdných slotů. Po výběru trinketu se v téže entitě mění jméno i karta, ale ne
    /// zároveň: naměřeno, že jméno se z descriptorů obnoví hned, kdežto karta až tehdy, když
    /// entitu zmíní řádek s novým <c>cardId</c>. Obsazenost se proto pozná po jménu, které je
    /// zároveň to, co se v panelu vypisuje.
    /// </summary>
    private static readonly string[] PlaceholderNames = ["Lesser Trinket", "Greater Trinket"];

    /// <summary>Je to ještě jméno prázdného slotu, tedy není z čeho vypsat trinket?</summary>
    public static bool IsPlaceholderName(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        PlaceholderNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Slot podle karty prázdného slotu, nebo <c>null</c>, jde-li o jinou kartu. Používá se
    /// k označení slotu ve chvíli, kdy entita vznikne.
    /// </summary>
    public static int? SlotFromCardId(string? cardId)
    {
        if (string.Equals(cardId, "BG30_Trinket_1st", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return string.Equals(cardId, "BG30_Trinket_2nd", StringComparison.OrdinalIgnoreCase) ? 2 : null;
    }
}
