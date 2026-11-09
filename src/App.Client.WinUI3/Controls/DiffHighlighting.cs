using App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Shows a line of a side of a comparison in a <see cref="TextBlock"/>, with the parts of it that were changed highlighted with a text highlighter:
/// in red for a line of the left side, set as <see cref="RemovedLineProperty"/>, and in green for one of the right, set as <see cref="AddedLineProperty"/>.
/// </summary>
/// <remarks>The side is told by which property is set, as a property of an enum of the app's own isn't one XAML can hold.</remarks>
public static class DiffHighlighting
{
    public static readonly DependencyProperty RemovedLineProperty = DependencyProperty.RegisterAttached(
        "RemovedLine",
        typeof(DiffLine),
        typeof(DiffHighlighting),
        new PropertyMetadata(null, OnLineChanged));

    public static readonly DependencyProperty AddedLineProperty = DependencyProperty.RegisterAttached(
        "AddedLine",
        typeof(DiffLine),
        typeof(DiffHighlighting),
        new PropertyMetadata(null, OnLineChanged));

    public static DiffLine? GetRemovedLine(TextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (DiffLine?)element.GetValue(RemovedLineProperty);
    }

    public static void SetRemovedLine(TextBlock element, DiffLine? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RemovedLineProperty, value);
    }

    public static DiffLine? GetAddedLine(TextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (DiffLine?)element.GetValue(AddedLineProperty);
    }

    public static void SetAddedLine(TextBlock element, DiffLine? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AddedLineProperty, value);
    }

    private static void OnLineChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (TextBlock)sender;

        // subscribed once, however many times the line changes, as the block is recycled for other rows
        block.ActualThemeChanged -= OnActualThemeChanged;
        block.ActualThemeChanged += OnActualThemeChanged;
        Update(block, isRemoved: e.Property == RemovedLineProperty);
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        var block = (TextBlock)sender;
        Update(block, isRemoved: GetRemovedLine(block) is not null);
    }

    private static void Update(TextBlock block, bool isRemoved)
    {
        var line = isRemoved ? GetRemovedLine(block) : GetAddedLine(block);
        block.Text = line?.Text ?? string.Empty;
        block.TextHighlighters.Clear();
        if (line is not { Changes.Count: > 0 })
        {
            return;
        }

        var resources = SyntaxHighlighting.GetThemeResources(block);
        var highlighter = new TextHighlighter
        {
            Background = (Brush)resources[isRemoved ? "DiffRemovedHighlightBrush" : "DiffAddedHighlightBrush"],
            Foreground = (Brush)resources["DiffHighlightForegroundBrush"],
        };
        foreach (var change in line.Changes)
        {
            highlighter.Ranges.Add(new TextRange { StartIndex = change.Start, Length = change.Length });
        }

        block.TextHighlighters.Add(highlighter);
    }
}
