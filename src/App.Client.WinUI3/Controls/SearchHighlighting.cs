using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Highlights where the text searched for is found in a <see cref="RichTextBlock"/>, in any case, with a text highlighter,
/// so the runs it is shown in are left as they are.
/// </summary>
/// <remarks>
/// The text block doesn't tell when its text changes, so whatever shows text in it highlights it again with <see cref="Update"/>.
/// </remarks>
public static class SearchHighlighting
{
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query",
        typeof(string),
        typeof(SearchHighlighting),
        new PropertyMetadata(null, OnQueryChanged));

    public static string? GetQuery(RichTextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(QueryProperty);
    }

    public static void SetQuery(RichTextBlock element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(QueryProperty, value);
    }

    private static void OnQueryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (RichTextBlock)sender;

        // subscribed once, however many times the query changes
        block.ActualThemeChanged -= OnActualThemeChanged;
        block.ActualThemeChanged += OnActualThemeChanged;
        Update(block);
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args) => Update((RichTextBlock)sender);

    /// <summary>Highlights the text searched for in what <paramref name="block"/> shows now.</summary>
    public static void Update(RichTextBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var query = GetQuery(block);
        if (string.IsNullOrEmpty(query))
        {
            if (block.TextHighlighters.Count > 0)
            {
                block.TextHighlighters.Clear();
            }

            return;
        }

        var text = GetText(block);
        var resources = SyntaxHighlighting.GetThemeResources(block);
        var highlighter = new TextHighlighter
        {
            Background = (Brush)resources["SearchHighlightBackgroundBrush"],
            Foreground = (Brush)resources["SearchHighlightForegroundBrush"],
        };
        for (var index = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase); index >= 0; index = text.IndexOf(query, index + query.Length, StringComparison.CurrentCultureIgnoreCase))
        {
            highlighter.Ranges.Add(new TextRange { StartIndex = index, Length = query.Length });
        }

        block.TextHighlighters.Clear();
        if (highlighter.Ranges.Count > 0)
        {
            block.TextHighlighters.Add(highlighter);
        }
    }

    // The text as it is counted by the ranges of a highlighter: the runs of the paragraphs one after the other.
    private static string GetText(RichTextBlock block)
    {
        var builder = new StringBuilder();
        foreach (var paragraph in block.Blocks.OfType<Paragraph>())
        {
            foreach (var run in paragraph.Inlines.OfType<Run>())
            {
                builder.Append(run.Text);
            }
        }

        return builder.ToString();
    }
}
