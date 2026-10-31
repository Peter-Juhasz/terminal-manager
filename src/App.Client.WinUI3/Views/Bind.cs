using App.Client.WinUI3.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

    /// <summary>The style of the badge of a project with <paramref name="runningCount"/> commands running: a dot for one, a number for more.</summary>
    public static Style RunningBadgeStyle(int runningCount) =>
        (Style)Application.Current.Resources[runningCount > 1 ? "AttentionValueInfoBadgeStyle" : "AttentionDotInfoBadgeStyle"];

    /// <summary>The color of a progress ring in <paramref name="state"/>, as a progress bar shows an error or a pause.</summary>
    public static Brush ProgressForeground(ProgressState state) => (Brush)Application.Current.Resources[state switch
    {
        ProgressState.Error => "SystemFillColorCriticalBrush",
        ProgressState.Paused => "SystemFillColorCautionBrush",
        _ => "AccentFillColorDefaultBrush",
    }];

    /// <summary>The glyph of a match of a completion of the kind <paramref name="resultType"/>, a name of the kinds PowerShell completes.</summary>
    public static string CompletionGlyph(string resultType) => resultType switch
    {
        "Command" => "",
        "ProviderContainer" => "",
        "ProviderItem" => "",
        "ParameterName" or "ParameterValue" => "",
        "Variable" or "Property" or "Method" => "",
        "Type" or "Namespace" => "",
        "Keyword" or "DynamicKeyword" => "",
        "History" => "",
        _ => "",
    };

    /// <summary>The logo at <paramref name="source"/>, or the generic terminal glyph without one.</summary>
    public static IconElement TerminalIcon(Uri? source) => source is null
        ? new FontIcon { Glyph = "\uE756" }
        : new ImageIcon { Source = new SvgImageSource(source) };
}
