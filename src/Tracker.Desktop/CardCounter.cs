namespace Tracker.Desktop;

/// <summary>
/// Řádek s vlastním počítadlem karty. Karty typu „a zlepši tohle“ si hodnotu, kterou právě
/// dávají, drží samy v <c>TAG_SCRIPT_DATA_NUM_1</c> a <c>_2</c>, ne na entitě hráče jako
/// bonusy pro celou hru. Text karty přitom pořád ukazuje výchozí čísla, takže je to jediné
/// místo, kde se aktuální hodnota dá zjistit.
/// </summary>
/// <param name="Name">Jméno karty, která si počítadlo drží.</param>
/// <param name="Value">Hodnota, kterou karta právě dává.</param>
/// <param name="Card">Karta pro popis efektu v tooltipu.</param>
public sealed record CardCounter(string Name, string Value, CardInfo? Card);
