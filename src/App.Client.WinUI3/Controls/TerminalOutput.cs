using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using App.Client.WinUI3.ViewModels;
using App.Domain;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Shows the output of a command in a <see cref="RichTextBlock"/> as it is written, in the runs laid out by <see cref="OutputRunBuilder"/>,
/// so it can be selected across the standard output and error alike. Once it is complete, the web addresses in it are shown as links,
/// which are opened when clicked with Ctrl held down, so clicking to select text doesn't open them.
/// </summary>
/// <remarks>
/// The text block lays out all of its text again whenever any of it changes, so showing more takes the longer the more there is,
/// which is why more is shown only at an interval. The links are laid out once, all of the output with them, as it no longer changes.
/// </remarks>
public static partial class TerminalOutput
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

        // what the previous output was highlighted in is gone
        SearchHighlighting.Update(block);
    }

    private sealed partial class Subscription : IDisposable
    {
        private readonly RichTextBlock _block;
        private readonly Paragraph _paragraph = new();
        private readonly CommandOutput _output;
        private readonly OutputRunBuilder _builder = new();
        private readonly List<RunUpdate> _updates = [];

        // The last run, which is the only one whose text still changes.
        private Run? _last;
        private int _runCount;

        // The number of chunks of the output that were laid out.
        private int _chunkCount;

        // Whether the output was laid out with its links, after which it is laid out again as a whole if anything is appended.
        private bool _isShownWithLinks;

        public Subscription(RichTextBlock block, CommandOutput output)
        {
            _block = block;
            _output = output;
            block.Blocks.Add(_paragraph);
            if (output is { IsCompleted: true, Links.Count: > 0 })
            {
                ShowWithLinks();
            }
            else
            {
                Show();
            }

            output.Appended += OnAppended;
            output.Completed += OnCompleted;
        }

        public void Dispose()
        {
            _output.Appended -= OnAppended;
            _output.Completed -= OnCompleted;
            _paragraph.Inlines.Clear();
        }

        private void OnAppended(object? sender, EventArgs e)
        {
            if (_isShownWithLinks || _output is { IsCompleted: true, Links.Count: > 0 })
            {
                ShowWithLinks();
                return;
            }

            Show();
        }

        // without links, the output is already laid out as it would be with them
        private void OnCompleted(object? sender, EventArgs e)
        {
            if (_output.Links.Count > 0)
            {
                ShowWithLinks();
            }
        }

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

                _last = CreateRun(update.Stream, update.Text);
                _paragraph.Inlines.Add(_last);
                _runCount++;
            }

            if (_updates.Count > 0)
            {
                SearchHighlighting.Update(_block);
            }

            _updates.Clear();
        }

        // Lays out all of the output again, in the same runs, with the parts of them the links are in taken out into hyperlinks;
        // a link split between two runs, such as where the stream it was written to changes, is one hyperlink of both parts.
        private void ShowWithLinks()
        {
            _isShownWithLinks = true;
            _paragraph.Inlines.Clear();

            var builder = new OutputRunBuilder();
            foreach (var chunk in _output.Chunks)
            {
                builder.Append(chunk);
            }

            builder.Flush(_updates);
            var links = _output.Links;
            var next = 0;

            // the hyperlink of the next link, once the start of it was laid out
            Hyperlink? hyperlink = null;
            var runStart = 0;
            foreach (var update in _updates)
            {
                var text = update.Text;
                var position = 0;
                while (position < text.Length)
                {
                    if (next < links.Count && links[next].Start + links[next].Length <= runStart + position)
                    {
                        next++;
                        hyperlink = null;
                        continue;
                    }

                    // the text before the next link, or after the last one
                    var linkStart = next < links.Count ? links[next].Start - runStart : text.Length;
                    if (linkStart > position)
                    {
                        var end = Math.Min(linkStart, text.Length);
                        _paragraph.Inlines.Add(CreateRun(update.Stream, text[position..end]));
                        position = end;
                        continue;
                    }

                    var link = links[next];
                    var linkEnd = Math.Min(link.Start + link.Length - runStart, text.Length);
                    hyperlink ??= AddHyperlink(link.Uri);
                    hyperlink.Inlines.Add(new Run { Text = text[position..linkEnd] });
                    position = linkEnd;
                }

                runStart += text.Length;
            }

            _updates.Clear();
            SearchHighlighting.Update(_block);
        }

        private Hyperlink AddHyperlink(Uri uri)
        {
            // left out of the tab order, as there can be many of them, and the output is moved through as a whole
            var hyperlink = new Hyperlink { IsTabStop = false };
            ToolTipService.SetToolTip(hyperlink, $"{uri.AbsoluteUri}\nCtrl+click to follow link");
            hyperlink.Click += (_, _) => OnLinkClick(uri);
            _paragraph.Inlines.Add(hyperlink);
            return hyperlink;
        }

        private static Run CreateRun(OutputStream stream, string text)
        {
            var run = new Run { Text = text };
            if (stream is OutputStream.StandardError)
            {
                run.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            }

            return run;
        }

        // opened only with Ctrl held down, so a click to select text in it doesn't open it
        private static void OnLinkClick(Uri uri)
        {
            if (!InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
            catch (Win32Exception ex)
            {
                Debug.WriteLine($"The link couldn't be opened: {ex}");
            }
        }
    }
}
