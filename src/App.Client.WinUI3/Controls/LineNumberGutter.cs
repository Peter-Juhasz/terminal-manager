using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace App.Client.WinUI3.Controls;

/// <summary>
/// Numbers the lines of the output shown by <see cref="TerminalOutput"/> in a text block beside it, which is scrolled vertically along with it,
/// but not horizontally, and is left out of what is selected and copied of the output.
/// </summary>
/// <remarks>
/// Both are laid out in rows of the same <see cref="TextBlock.LineHeight"/>, so each line of the output takes a whole number of rows of the numbers.
/// While long lines aren't wrapped, a line takes a row, and one more for each carriage return in it that isn't followed by a line feed,
/// as that breaks the line too; while they are wrapped, the rows of a line are told by where the next one starts.
/// The lines of output filtered by <see cref="ViewModels.OutputLineFilter"/> are numbered as they are in the output they were found in.
/// </remarks>
public sealed partial class LineNumberGutter
{
    private readonly RichTextBlock _output;
    private readonly ScrollViewer _outputScroller;
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _numbers;
    private readonly StringBuilder _text = new();
    private readonly List<int> _rows = [];
    private bool _isEnabled;
    private bool _isUpdatePending;

    public LineNumberGutter(RichTextBlock output, ScrollViewer outputScroller, ScrollViewer scroller, TextBlock numbers)
    {
        _output = output;
        _outputScroller = outputScroller;
        _scroller = scroller;
        _numbers = numbers;

        // the output is laid out again for more of it, and for a width that wraps it differently
        output.SizeChanged += (_, _) => Invalidate();
        output.RegisterPropertyChangedCallback(TerminalOutput.OutputProperty, (_, _) => Invalidate());
        outputScroller.ViewChanged += (_, _) => SyncScroll();
    }

    /// <summary>Whether the lines are numbered.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            Invalidate();
        }
    }

    /// <summary>Numbers the lines again once the output is laid out, such as after its wrapping changed.</summary>
    public void Invalidate()
    {
        if (_isUpdatePending)
        {
            return;
        }

        // deferred, so where the lines are is told by the output as it is laid out now, and only once for all that changed together
        _isUpdatePending = _output.DispatcherQueue.TryEnqueue(Update);
    }

    private void Update()
    {
        _isUpdatePending = false;
        if (!_isEnabled || TerminalOutput.GetOutput(_output) is not { IsEmpty: false } output)
        {
            _scroller.Visibility = Visibility.Collapsed;
            _numbers.Text = string.Empty;
            return;
        }

        _rows.Clear();
        if (_output.TextWrapping is TextWrapping.NoWrap)
        {
            CountRows();
        }
        else
        {
            MeasureRows();
        }

        var lineNumbers = output.LineNumbers;
        _text.Clear();
        for (var line = 0; line < _rows.Count; line++)
        {
            if (line > 0)
            {
                _text.Append('\n', _rows[line - 1]);
            }

            _text.Append(lineNumbers is not null && line < lineNumbers.Count ? lineNumbers[line] : line + 1);
        }

        _numbers.Text = _text.ToString();
        _scroller.Visibility = Visibility.Visible;
        SyncScroll();
    }

    // The rows of each line, by the line breaks in it, when the output isn't wrapped.
    private void CountRows()
    {
        var rows = 1;
        var previous = '\0';
        foreach (var run in _output.GetRuns())
        {
            foreach (var character in run.Text)
            {
                if (character is '\n')
                {
                    _rows.Add(rows);
                    rows = 1;
                }
                else if (previous is '\r')
                {
                    // a carriage return by itself breaks the line too
                    rows++;
                }

                previous = character;
            }
        }

        _rows.Add(rows);
    }

    // The rows of each line, by how far below it the next one starts, when the output is wrapped.
    private void MeasureRows()
    {
        var lineHeight = _output.LineHeight is > 0 and var height ? height : _numbers.LineHeight;
        var top = 0.0;
        var isLineStart = false;
        foreach (var run in _output.GetRuns())
        {
            var text = run.Text;
            for (var index = 0; index < text.Length; index++)
            {
                if (isLineStart)
                {
                    var nextTop = run.ContentStart.GetPositionAtOffset(index, LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Forward).Y;

                    // a line not laid out yet has no place, so it is given a row until it is
                    var rows = double.IsFinite(nextTop) ? (nextTop - top) / lineHeight : 1;
                    _rows.Add(rows is >= 1 and < 10_000 ? (int)Math.Round(rows) : 1);
                    if (double.IsFinite(nextTop))
                    {
                        top = nextTop;
                    }

                    isLineStart = false;
                }

                isLineStart = text[index] is '\n';
            }
        }

        // the rows of the last line don't matter, as nothing is numbered below it; a line break at the very end starts no line, as there is none shown
        _rows.Add(1);
    }

    // The numbers are scrolled as far as the output is, which they can't be scrolled by themselves.
    private void SyncScroll()
    {
        if (_scroller.Visibility is Visibility.Visible)
        {
            _scroller.ChangeView(null, _outputScroller.VerticalOffset, null, disableAnimation: true);
        }
    }
}
