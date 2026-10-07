using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Pupitre;

public static class Converters
{
    public static IValueConverter Not { get; } = new NotConverter();

    public static IValueConverter TrueToVisible { get; } = new VisibilityConverter(value => value is true);

    public static IValueConverter FalseToVisible { get; } = new VisibilityConverter(value => value is false);

    public static IValueConverter PositiveToVisible { get; } = new VisibilityConverter(value => value is > 0);

    public static IValueConverter ZeroToVisible { get; } = new VisibilityConverter(value => value is 0);

    /// <summary>Visible when all bound values are equal, e.g. an item's name and the selected name.</summary>
    public static IMultiValueConverter EqualToVisible { get; } = new EqualityVisibilityConverter();

    private sealed class EqualityVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Length > 0 && values.All(value => Equals(value, values[0])) ? Visibility.Visible : Visibility.Collapsed;

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class NotConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    }

    private sealed class VisibilityConverter(Func<object?, bool> isVisible) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            isVisible(value) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
