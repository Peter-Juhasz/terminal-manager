using Microsoft.UI.Xaml;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Functions for <c>x:Bind</c>, in place of converters.
/// </summary>
public static class Bind
{
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static string Format(string format, object? arg) => string.Format(System.Globalization.CultureInfo.CurrentCulture, format, arg);

    public static Visibility VisibleIfNotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfNull(object? value) => value is null ? Visibility.Visible : Visibility.Collapsed;
}
