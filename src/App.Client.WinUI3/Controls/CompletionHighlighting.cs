using App.Client.WinUI3.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Shows the text a match of a completion is listed by in a <see cref="TextBlock"/>, with what was typed of it in bold, as Visual Studio does.
/// </summary>
/// <remarks>A text highlighter can't make text bold, only color it, so the text is split into a bold run and the rest.</remarks>
public static class CompletionHighlighting
{
    public static readonly DependencyProperty MatchProperty = DependencyProperty.RegisterAttached(
        "Match",
        typeof(CompletionMatch),
        typeof(CompletionHighlighting),
        new PropertyMetadata(null, OnMatchChanged));

    public static CompletionMatch? GetMatch(TextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (CompletionMatch?)element.GetValue(MatchProperty);
    }

    public static void SetMatch(TextBlock element, CompletionMatch? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(MatchProperty, value);
    }

    private static void OnMatchChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (TextBlock)sender;
        block.Inlines.Clear();
        if (e.NewValue is not CompletionMatch { ListItemText: var text, MatchedLength: var matched })
        {
            return;
        }

        matched = Math.Clamp(matched, 0, text.Length);
        if (matched > 0)
        {
            block.Inlines.Add(new Run { Text = text[..matched], FontWeight = FontWeights.Bold });
        }

        if (matched < text.Length)
        {
            block.Inlines.Add(new Run { Text = text[matched..] });
        }
    }
}
