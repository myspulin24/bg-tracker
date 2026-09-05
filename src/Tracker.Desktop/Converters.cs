using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Tracker.Desktop;

/// <summary>
/// Vybraná hodnota výčtu proti přepínači: <c>Convert</c> říká, jestli je právě tahle možnost
/// zaškrtnutá, <c>ConvertBack</c> ji při zaškrtnutí zapíše. Umožňuje svázat skupinu
/// <c>RadioButton</c> přímo s vlastností typu výčtu bez kódu za oknem.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null && Nullable.GetUnderlyingType(targetType) is null
            ? Enum.Parse(targetType, parameter.ToString()!)
            : Binding.DoNothing;
}

/// <summary>Opak <c>BooleanToVisibilityConverter</c>: pravda skrývá.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Číslo s příponou do popisku posuvníku, například <c>85 %</c>.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double number ? $"{Math.Round(number)} %" : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
