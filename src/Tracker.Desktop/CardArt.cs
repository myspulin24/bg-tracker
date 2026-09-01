using System.ComponentModel;
using System.Windows.Media;

namespace Tracker.Desktop;

/// <summary>
/// Držák jedné kresby. Pro každé ID karty existuje jediná instance, takže se pohled na ni může
/// navázat dřív, než je obrázek stažený, a doplní se sám. Kdyby se místo toho měnil model pohledu,
/// přestavěl by se celý seznam a právě otevřené podokno by se zavřelo.
/// </summary>
public sealed class CardArt : INotifyPropertyChanged
{
    private ImageSource? image;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImageSource? Image
    {
        get => image;
        internal set
        {
            image = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImage)));
        }
    }

    /// <summary>Řídí viditelnost, aby se před stažením nezobrazil prázdný rámeček.</summary>
    public bool HasImage => image is not null;
}
