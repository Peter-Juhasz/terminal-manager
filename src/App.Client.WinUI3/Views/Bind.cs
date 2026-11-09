using App.Services;
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

    /// <summary>The number of a line of a side of a comparison, or nothing where the side has no line.</summary>
    public static string LineNumber(DiffLine? line) => line?.Number.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>Whether the left side of a row of a comparison is shown removed: deleted, or changed into the right side.</summary>
    public static Visibility RemovedIf(DiffKind kind) => VisibleIf(kind is DiffKind.Deleted or DiffKind.Modified);

    /// <summary>Whether the right side of a row of a comparison is shown added: inserted, or changed from the left side.</summary>
    public static Visibility AddedIf(DiffKind kind) => VisibleIf(kind is DiffKind.Inserted or DiffKind.Modified);

    /// <summary>Whether the left side of a row of a comparison is empty, filled in to line up with a line added on the right.</summary>
    public static Visibility LeftFillerIf(DiffKind kind) => VisibleIf(kind is DiffKind.Inserted);

    /// <summary>Whether the right side of a row of a comparison is empty, filled in to line up with a line removed on the left.</summary>
    public static Visibility RightFillerIf(DiffKind kind) => VisibleIf(kind is DiffKind.Deleted);

    /// <summary>
    /// The style of the badge of a project with <paramref name="runningCount"/> commands running: a dot for one, a number for more,
    /// and a red dot when none are, as it is then only shown for a command that failed.
    /// </summary>
    public static Style ProjectBadgeStyle(int runningCount) => (Style)Application.Current.Resources[runningCount switch
    {
        0 => "CriticalDotInfoBadgeStyle",
        1 => "AttentionDotInfoBadgeStyle",
        _ => "AttentionValueInfoBadgeStyle",
    }];

    /// <summary>The color of a progress ring in <paramref name="state"/>, as a progress bar shows an error or a pause.</summary>
    public static Brush ProgressForeground(ProgressState state) => (Brush)Application.Current.Resources[state switch
    {
        ProgressState.Error => "SystemFillColorCriticalBrush",
        ProgressState.Paused => "SystemFillColorCautionBrush",
        _ => "AccentFillColorDefaultBrush",
    }];

    // The icons of the kinds of matches, each loaded once.
    private static readonly Dictionary<string, SvgImageSource> CompletionIcons = [];

    /// <summary>
    /// The icon of a match of a completion of the kind <paramref name="resultType"/>, a name of the kinds PowerShell completes,
    /// the one Visual Studio shows for the same kind of thing in C#.
    /// </summary>
    public static ImageSource CompletionIcon(string resultType)
    {
        var name = resultType switch
        {
            // a command is called as a method is
            "Command" or "Method" => "MethodPublic",
            "Property" => "PropertyPublic",
            "Variable" => "LocalVariable",
            "ParameterName" => "Parameter",

            // the values a parameter takes are mostly those of an enum, or a set like one
            "ParameterValue" => "EnumerationItemPublic",
            "Type" => "ClassPublic",
            "Namespace" => "Namespace",
            "Keyword" or "DynamicKeyword" => "IntelliSenseKeyword",
            "ProviderContainer" => "FolderClosed",
            "ProviderItem" => "Document",
            "History" => "History",
            _ => "TextFile",
        };

        if (!CompletionIcons.TryGetValue(name, out var icon))
        {
            icon = new SvgImageSource(new Uri($"ms-appx:///Assets/Completion/{name}.svg"));
            CompletionIcons[name] = icon;
        }

        return icon;
    }

    /// <summary>The logo at <paramref name="source"/>, or the generic terminal glyph without one.</summary>
    public static IconElement TerminalIcon(Uri? source) => source is null
        ? new FontIcon { Glyph = "\uE756" }
        : new ImageIcon { Source = new SvgImageSource(source) };

    /// <summary>The logo at <paramref name="source"/>, or the generic terminal glyph without one, <paramref name="size"/> pixels wide and high.</summary>
    public static IconElement TerminalIcon(Uri? source, double size) => source is null
        ? new FontIcon { Glyph = "\uE756", FontSize = size, Width = size, Height = size }
        : new ImageIcon { Source = new SvgImageSource(source), Width = size, Height = size };
}
