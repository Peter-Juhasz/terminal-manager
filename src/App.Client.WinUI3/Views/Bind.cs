using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

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

    /// <summary>The logo at <paramref name="source"/>, or the generic terminal glyph without one.</summary>
    public static IconElement TerminalIcon(Uri? source) => source is null
        ? new FontIcon { Glyph = "\uE756" }
        : new ImageIcon { Source = new SvgImageSource(source) };
}
