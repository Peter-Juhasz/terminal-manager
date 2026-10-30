using System.Runtime.CompilerServices;
using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Shows the output of a command in a <see cref="RichTextBlock"/> as it is written, in the runs laid out by <see cref="OutputRunBuilder"/>,
/// so it can be selected across the standard output and error alike.
/// </summary>
/// <remarks>
/// The text block lays out all of its text again whenever any of it changes, so showing more takes the longer the more there is,
/// which is why more is shown only at an interval.
/// </remarks>
public static class TerminalOutput
{
    // The subscriptions of each text block, so they can be undone when it is recycled to show other output.
    private static readonly ConditionalWeakTable<RichTextBlock, Subscription> Subscriptions = [];

    public static readonly DependencyProperty OutputProperty = DependencyProperty.RegisterAttached(
        "Output",
        typeof(CommandOutput),
        typeof(TerminalOutput),
        new PropertyMetadata(null, OnOutputChanged));

    public static CommandOutput? GetOutput(RichTextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (CommandOutput?)element.GetValue(OutputProperty);
    }

    public static void SetOutput(RichTextBlock element, CommandOutput? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(OutputProperty, value);
    }

    private static void OnOutputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (RichTextBlock)sender;
        if (Subscriptions.TryGetValue(block, out var previous))
        {
            previous.Dispose();
            Subscriptions.Remove(block);
        }

        block.Blocks.Clear();
        if (e.NewValue is CommandOutput output)
        {
            Subscriptions.Add(block, new Subscription(block, output));
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Paragraph _paragraph = new();
        private readonly CommandOutput _output;
        private readonly OutputRunBuilder _builder = new();
        private readonly List<RunUpdate> _updates = [];

        // The last run, which is the only one whose text still changes.
        private Run? _last;
        private int _runCount;

        // The number of chunks of the output that were laid out.
        private int _chunkCount;

        public Subscription(RichTextBlock block, CommandOutput output)
        {
            _output = output;
            block.Blocks.Add(_paragraph);
            Show();
            output.Appended += OnAppended;
        }

        public void Dispose()
        {
            _output.Appended -= OnAppended;
            _paragraph.Inlines.Clear();
        }

        private void OnAppended(object? sender, EventArgs e) => Show();

        // Shows the chunks appended since the last time.
        private void Show()
        {
            var chunks = _output.Chunks;
            for (; _chunkCount < chunks.Count; _chunkCount++)
            {
                _builder.Append(chunks[_chunkCount]);
            }

            _builder.Flush(_updates);
            foreach (var update in _updates)
            {
                if (_last is not null && update.Index < _runCount)
                {
                    _last.Text = update.Text;
                    continue;
                }

                _last = new Run { Text = update.Text };
                if (update.Stream is OutputStream.StandardError)
                {
                    _last.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                }

                _paragraph.Inlines.Add(_last);
                _runCount++;
            }

            _updates.Clear();
        }
    }
}
