using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Highlights where the text searched for is found in a <see cref="RichTextBlock"/> or a <see cref="TextBlock"/>, in any case unless <c>IsCaseSensitive</c> is set, with a text highlighter,
/// so the runs it is shown in are left as they are.
/// </summary>
/// <remarks>
/// A rich text block doesn't tell when its text changes, so whatever shows text in it highlights it again with <see cref="Update"/>.
/// A text block is highlighted again when its <see cref="TextBlock.Text"/> changes.
/// </remarks>
public static class SearchHighlighting
{
    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query",
        typeof(string),
        typeof(SearchHighlighting),
        new PropertyMetadata(null, OnQueryChanged));

    public static readonly DependencyProperty IsCaseSensitiveProperty = DependencyProperty.RegisterAttached(
        "IsCaseSensitive",
        typeof(bool),
        typeof(SearchHighlighting),
        new PropertyMetadata(false, OnQueryChanged));

    // whether a text block is highlighted again when its text changes, so it is watched once
    private static readonly DependencyProperty IsTextWatchedProperty = DependencyProperty.RegisterAttached(
        "IsTextWatched",
        typeof(bool),
        typeof(SearchHighlighting),
        new PropertyMetadata(false));

    public static string? GetQuery(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(QueryProperty);
    }

    public static void SetQuery(FrameworkElement element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(QueryProperty, value);
    }

    public static bool GetIsCaseSensitive(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsCaseSensitiveProperty);
    }

    public static void SetIsCaseSensitive(FrameworkElement element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsCaseSensitiveProperty, value);
    }

    private static void OnQueryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;

        // subscribed once, however many times the query changes
        element.ActualThemeChanged -= OnActualThemeChanged;
        element.ActualThemeChanged += OnActualThemeChanged;
        if (element is TextBlock text && text.GetValue(IsTextWatchedProperty) is not true)
        {
            text.SetValue(IsTextWatchedProperty, true);
            text.RegisterPropertyChangedCallback(TextBlock.TextProperty, static (sender, _) => Update((TextBlock)sender));
        }

        UpdateAny(element);
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateAny(sender);

    private static void UpdateAny(FrameworkElement element)
    {
        switch (element)
        {
            case RichTextBlock block:
                Update(block);
                break;

            case TextBlock text:
                Update(text);
                break;
        }
    }

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

        var highlighter = CreateHighlighter(block, GetText(block), query);
        block.TextHighlighters.Clear();
        if (highlighter is not null)
        {
            block.TextHighlighters.Add(highlighter);
        }
    }

    private static void Update(TextBlock block)
    {
        var query = GetQuery(block);
        if (string.IsNullOrEmpty(query))
        {
            if (block.TextHighlighters.Count > 0)
            {
                block.TextHighlighters.Clear();
            }

            return;
        }

        var highlighter = CreateHighlighter(block, block.Text ?? string.Empty, query);
        block.TextHighlighters.Clear();
        if (highlighter is not null)
        {
            block.TextHighlighters.Add(highlighter);
        }
    }

    // a highlighter of where the query is found in the text, or null when it isn't found
    private static TextHighlighter? CreateHighlighter(FrameworkElement element, string text, string query)
    {
        var resources = SyntaxHighlighting.GetThemeResources(element);
        var highlighter = new TextHighlighter
        {
            Background = (Brush)resources["SearchHighlightBackgroundBrush"],
            Foreground = (Brush)resources["SearchHighlightForegroundBrush"],
        };
        var comparison = GetIsCaseSensitive(element) ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
        for (var index = text.IndexOf(query, comparison); index >= 0; index = text.IndexOf(query, index + query.Length, comparison))
        {
            highlighter.Ranges.Add(new TextRange { StartIndex = index, Length = query.Length });
        }

        if (highlighter.Ranges.Count is 0)
        {
            return null;
        }

        return highlighter;
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
