using System.ComponentModel;
using System.Windows.Media;

namespace Tracker.Desktop;

/// <summary>
/// Kresba a popis jedné karty. Pro každé Card ID existuje jediná instance, takže se na ni pohled
/// může navázat dřív, než jsou data stažená, a doplní se sama. Kdyby se místo toho měnil model
/// pohledu, přestavěl by se celý seznam a právě otevřené podokno by se zavřelo.
/// </summary>
public sealed class CardInfo : INotifyPropertyChanged
{
    private ImageSource? image;
    private string text = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImageSource? Image
    {
        get => image;
        internal set
        {
            image = value;
            Raise(nameof(Image));
            Raise(nameof(HasImage));
        }
    }

    /// <summary>Popis efektu karty tak, jak ho ukazuje hra, jen bez značek pro sazbu.</summary>
    public string Text
    {
        get => text;
        internal set
        {
            text = value;
            Raise(nameof(Text));
            Raise(nameof(HasText));
        }
    }

    /// <summary>Řídí viditelnost, aby se před stažením nekreslil prázdný portrét.</summary>
    public bool HasImage => image is not null;

    /// <summary>Vanilla minioni popis nemají, u nich se rámeček popisu vynechá.</summary>
    public bool HasText => text.Length > 0;

    private void Raise(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
