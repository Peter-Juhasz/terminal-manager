using App.Client.WinUI3.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Shows a script in a <see cref="RichTextBlock"/>, with what it is made of colored by the brushes of the app for each <see cref="SyntaxKind"/>.
/// </summary>
/// <remarks>
/// A run can't refer to a theme resource, so its brush is looked up in the theme the text block is shown in, which can be another one than that of the app,
/// and the script is shown again whenever that theme changes.
/// </remarks>
public static class SyntaxHighlighting
{
    private static readonly AccessibilitySettings Accessibility = new();

    public static readonly DependencyProperty CodeProperty = DependencyProperty.RegisterAttached(
        "Code",
        typeof(HighlightedCode),
        typeof(SyntaxHighlighting),
        new PropertyMetadata(null, OnCodeChanged));

    public static HighlightedCode? GetCode(RichTextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (HighlightedCode?)element.GetValue(CodeProperty);
    }

    public static void SetCode(RichTextBlock element, HighlightedCode? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CodeProperty, value);
    }

    private static void OnCodeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (RichTextBlock)sender;

        // subscribed once, however many times the code changes
        block.ActualThemeChanged -= OnActualThemeChanged;
        block.ActualThemeChanged += OnActualThemeChanged;
        Show(block, (HighlightedCode?)e.NewValue);
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        var block = (RichTextBlock)sender;
        Show(block, GetCode(block));
    }

    private static void Show(RichTextBlock block, HighlightedCode? code)
    {
        block.Blocks.Clear();
        if (code is null)
        {
            return;
        }

        var theme = Accessibility.HighContrast ? "HighContrast" : block.ActualTheme is ElementTheme.Light ? "Light" : "Default";
        var resources = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[theme];
        var paragraph = new Paragraph();
        foreach (var run in code.GetRuns())
        {
            var inline = new Run { Text = run.Text };
            if (run.Kind is { } kind)
            {
                inline.Foreground = (Brush)resources[kind switch
                {
                    SyntaxKind.Keyword => "SyntaxKeywordBrush",
                    SyntaxKind.Comment => "SyntaxCommentBrush",
                    SyntaxKind.String => "SyntaxStringBrush",
                    SyntaxKind.TypeName => "SyntaxTypeNameBrush",
                    _ => throw new ArgumentOutOfRangeException(nameof(code), kind, null),
                }];
            }

            paragraph.Inlines.Add(inline);
        }

        block.Blocks.Add(paragraph);
    }
}
