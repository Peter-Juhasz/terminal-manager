using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Shows the output of a command in a <see cref="RichTextBlock"/> as it is written, with a run for each <see cref="OutputSegment"/>,
/// so it can be selected across the standard output and error alike.
/// </summary>
public static class TerminalOutput
{
    // The subscriptions of each text block, so they can be undone when it is recycled to show other output.
    private static readonly ConditionalWeakTable<RichTextBlock, Subscription> Subscriptions = [];

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments",
        typeof(IReadOnlyList<OutputSegment>),
        typeof(TerminalOutput),
        new PropertyMetadata(null, OnSegmentsChanged));

    public static IReadOnlyList<OutputSegment>? GetSegments(RichTextBlock element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (IReadOnlyList<OutputSegment>?)element.GetValue(SegmentsProperty);
    }

    public static void SetSegments(RichTextBlock element, IReadOnlyList<OutputSegment>? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(SegmentsProperty, value);
    }

    private static void OnSegmentsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var block = (RichTextBlock)sender;
        if (Subscriptions.TryGetValue(block, out var previous))
        {
            previous.Dispose();
            Subscriptions.Remove(block);
        }

        block.Blocks.Clear();
        if (e.NewValue is IReadOnlyList<OutputSegment> segments)
        {
            Subscriptions.Add(block, new Subscription(block, segments));
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Paragraph _paragraph = new();
        private readonly IReadOnlyList<OutputSegment> _segments;
        private readonly Dictionary<OutputSegment, Run> _runs = [];

        // The last one is shown without the line break it ends with, so there is no empty line below the output.
        private OutputSegment? _last;

        public Subscription(RichTextBlock block, IReadOnlyList<OutputSegment> segments)
        {
            _segments = segments;
            block.Blocks.Add(_paragraph);

            foreach (var segment in segments)
            {
                Add(segment);
            }

            if (segments is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged += OnCollectionChanged;
            }
        }

        public void Dispose()
        {
            if (_segments is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged -= OnCollectionChanged;
            }

            foreach (var segment in _runs.Keys)
            {
                segment.PropertyChanged -= OnSegmentChanged;
            }

            _runs.Clear();
            _paragraph.Inlines.Clear();
        }

        private void Add(OutputSegment segment)
        {
            if (_last is not null)
            {
                _runs[_last].Text = _last.Text;
            }

            _last = segment;
            var run = new Run { Text = TrimEnd(segment.Text) };
            if (segment.IsError)
            {
                run.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }

            _paragraph.Inlines.Add(run);
            _runs.Add(segment, run);
            segment.PropertyChanged += OnSegmentChanged;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add && e.NewItems is { } added)
            {
                foreach (OutputSegment segment in added)
                {
                    Add(segment);
                }

                return;
            }

            // Output only ever grows, but anything else is shown by starting over.
            foreach (var segment in _runs.Keys)
            {
                segment.PropertyChanged -= OnSegmentChanged;
            }

            _runs.Clear();
            _last = null;
            _paragraph.Inlines.Clear();
            foreach (var segment in _segments)
            {
                Add(segment);
            }
        }

        private void OnSegmentChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is OutputSegment segment && _runs.TryGetValue(segment, out var run))
            {
                run.Text = segment == _last ? TrimEnd(segment.Text) : segment.Text;
            }
        }

        private static string TrimEnd(string text) => text.TrimEnd('\r', '\n');
    }
}
