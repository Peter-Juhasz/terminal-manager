using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Controls;

/// <summary>The text of a run, which is a new one when <see cref="Index"/> is past those of the runs before.</summary>
public readonly record struct RunUpdate(int Index, OutputStream Stream, string Text);

/// <summary>
/// Lays the output of a command out in runs of text written to the same stream as it is appended, so showing more of it takes setting the text
/// of the last run, and of those after it, instead of all of it. The line breaks at its end are held back until something follows them,
/// so there is no empty line below the output.
/// </summary>
public sealed class OutputRunBuilder
{
    /// <summary>
    /// How long a run grows before another is started for what follows, which keeps the text of each quick to set, and off the large object heap.
    /// </summary>
    public const int MaxRunLength = 16 * 1024;

    private const string LineBreaks = "\r\n";

    // The text of the last run, as the parts of the chunks it is made of, which are joined only when it is shown.
    private readonly List<ReadOnlyMemory<char>> _parts = [];
    private int _length;
    private OutputStream _stream;
    private bool _isChanged;

    // The line breaks at the end of the output, which are shown in the run of whatever follows them, if anything does.
    private readonly List<ReadOnlyMemory<char>> _trailingLineBreaks = [];

    // The runs that were followed by another since the last flush, with the text they ended up with.
    private readonly List<RunUpdate> _completed = [];

    /// <summary>The number of runs the output is laid out in.</summary>
    public int Count { get; private set; }

    /// <summary>Lays out <paramref name="chunk"/> after what was appended before.</summary>
    public void Append(OutputChunk chunk)
    {
        var text = chunk.Text;
        var length = text.AsSpan().TrimEnd(LineBreaks).Length;
        if (length == 0)
        {
            if (text.Length > 0)
            {
                _trailingLineBreaks.Add(text.AsMemory());
            }

            return;
        }

        if (Count == 0 || chunk.Stream != _stream || _length >= MaxRunLength)
        {
            StartRun(chunk.Stream);
        }

        // Line breaks look the same in any run, and this way a CR LF is never split between two.
        foreach (var lineBreaks in _trailingLineBreaks)
        {
            Add(lineBreaks);
        }

        _trailingLineBreaks.Clear();
        Add(text.AsMemory(0, length));
        if (length < text.Length)
        {
            _trailingLineBreaks.Add(text.AsMemory(length));
        }
    }

    /// <summary>Adds the runs whose text changed since the last time to <paramref name="updates"/>, in order.</summary>
    public void Flush(ICollection<RunUpdate> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        foreach (var update in _completed)
        {
            updates.Add(update);
        }

        _completed.Clear();
        if (_isChanged)
        {
            updates.Add(new RunUpdate(Count - 1, _stream, Join()));
            _isChanged = false;
        }
    }

    private void StartRun(OutputStream stream)
    {
        if (_isChanged)
        {
            _completed.Add(new RunUpdate(Count - 1, _stream, Join()));
            _isChanged = false;
        }

        _parts.Clear();
        _length = 0;
        _stream = stream;
        Count++;
    }

    private void Add(ReadOnlyMemory<char> part)
    {
        _parts.Add(part);
        _length += part.Length;
        _isChanged = true;
    }

    private string Join() => string.Create(_length, _parts, static (destination, parts) =>
    {
        foreach (var part in parts)
        {
            part.Span.CopyTo(destination);
            destination = destination[part.Length..];
        }
    });
}
