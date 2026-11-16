using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShellDeck;

public static class Converters
{
    public static IValueConverter Not { get; } = new NotConverter();

    public static IValueConverter TrueToVisible { get; } = new VisibilityConverter(value => value is true);

    public static IValueConverter FalseToVisible { get; } = new VisibilityConverter(value => value is false);

    public static IValueConverter PositiveToVisible { get; } = new VisibilityConverter(value => value is > 0);

    public static IValueConverter NotNullToVisible { get; } = new VisibilityConverter(value => value is not null);

    public static IValueConverter NullToVisible { get; } = new VisibilityConverter(value => value is null);

    /// <summary>Hidden (not collapsed) when the value is not null: the element keeps its space, so it still sizes its cell.</summary>
    public static IValueConverter NullToVisibleElseHidden { get; } = new VisibilityConverter(value => value is null, Visibility.Hidden);

    public static IValueConverter ZeroToVisible { get; } = new VisibilityConverter(value => value is 0);

    /// <summary>Visible when all bound values are equal, e.g. an item's name and the selected name.</summary>
    public static IMultiValueConverter EqualToVisible { get; } = new EqualityVisibilityConverter();

    /// <summary>The first key of a <see cref="ShortcutAction"/>, as menus show it next to the command.</summary>
    public static IValueConverter ShortcutKeys { get; } = new ShortcutKeysConverter();

    private sealed class ShortcutKeysConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is ShortcutAction action && Shortcuts.All.FirstOrDefault(shortcut => shortcut.Action == action) is { } shortcut
                ? ShortcutHelp.Format(shortcut)
                : string.Empty;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

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

    private sealed class VisibilityConverter(Func<object?, bool> isVisible, Visibility otherwise = Visibility.Collapsed) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            isVisible(value) ? Visibility.Visible : otherwise;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
