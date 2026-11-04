using System.Text;
using App.Client.WinUI3.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Highlights where a <see cref="TextSearch"/> is found in a <see cref="RichTextBlock"/> or a <see cref="TextBlock"/>, with a text highlighter,
/// so the runs it is shown in are left as they are.
/// </summary>
/// <remarks>
/// A rich text block doesn't tell when its text changes, so whatever shows text in it highlights it again with <see cref="Update"/>.
/// A text block is highlighted again when its <see cref="TextBlock.Text"/> changes.
/// </remarks>
public static class SearchHighlighting
{
    public static readonly DependencyProperty SearchProperty = DependencyProperty.RegisterAttached(
        "Search",
        typeof(TextSearch),
        typeof(SearchHighlighting),
        new PropertyMetadata(null, OnSearchChanged));

    // whether a text block is highlighted again when its text changes, so it is watched once
    private static readonly DependencyProperty IsTextWatchedProperty = DependencyProperty.RegisterAttached(
        "IsTextWatched",
        typeof(bool),
        typeof(SearchHighlighting),
        new PropertyMetadata(false));

    // reused, as the highlighting is only ever updated on the UI thread
    private static readonly List<(int Start, int Length)> Matches = [];
    private static readonly StringBuilder Text = new();

    public static TextSearch? GetSearch(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (TextSearch?)element.GetValue(SearchProperty);
    }

    public static void SetSearch(FrameworkElement element, TextSearch? value)
    {
        ArgumentNullException.ThrowIfNull(element);

        // the same search highlights the same, even if it was made again
        if (!Equals(GetSearch(element), value))
        {
            element.SetValue(SearchProperty, value);
        }
    }

    private static void OnSearchChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;

        // subscribed once, however many times the search changes
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

        var search = GetSearch(block);
        Highlight(block, block.TextHighlighters, search, search is null ? string.Empty : GetText(block));
    }

    private static void Update(TextBlock block)
    {
        var search = GetSearch(block);
        Highlight(block, block.TextHighlighters, search, block.Text ?? string.Empty);
    }

    // replaces the highlighter of where the search was found with one of where it is found in the text now
    private static void Highlight(FrameworkElement element, IList<TextHighlighter> highlighters, TextSearch? search, string text)
    {
        Matches.Clear();
        search?.FindMatches(text, Matches);
        if (Matches.Count is 0)
        {
            if (highlighters.Count > 0)
            {
                highlighters.Clear();
            }

            return;
        }

        var resources = SyntaxHighlighting.GetThemeResources(element);
        var highlighter = new TextHighlighter
        {
            Background = (Brush)resources["SearchHighlightBackgroundBrush"],
            Foreground = (Brush)resources["SearchHighlightForegroundBrush"],
        };
        foreach (var (start, length) in Matches)
        {
            highlighter.Ranges.Add(new TextRange { StartIndex = start, Length = length });
        }

        highlighters.Clear();
        highlighters.Add(highlighter);
    }

    // The text as it is counted by the ranges of a highlighter: the runs of the paragraphs one after the other.
    private static string GetText(RichTextBlock block)
    {
        Text.Clear();
        foreach (var paragraph in block.Blocks.OfType<Paragraph>())
        {
            foreach (var run in paragraph.Inlines.OfType<Run>())
            {
                Text.Append(run.Text);
            }
        }

        return Text.ToString();
    }
}
