using System.Text;
using System.Threading.Channels;
using App.Domain;

namespace App.Services;

/// <summary>What was written since the output was last read, with the progress reported so far.</summary>
public readonly record struct OutputBatch(IReadOnlyList<OutputChunk> Chunks, ProgressReport Progress);

/// <summary>
/// Collects the output of a command from the threads that read its streams, with the progress sequences taken out of its standard output,
/// so everything written since it was last read can be shown at once, however small the pieces it was read in.
/// </summary>
public sealed class OutputBuffer : IOutputSink
{
    /// <summary>The most text in a chunk, which keeps chunks off the large object heap, however much was written at once.</summary>
    public const int MaxChunkLength = 8 * 1024;

    // What the text is reset to after it was read, as clearing a larger one would allocate as much as it held again.
    private const int InitialCapacity = 8 * 1024;

    private readonly Lock _lock = new();
    private readonly ProgressSequenceParser _parser = new();

    // What was written since the last read, with where the text written to each stream ends in it, in order.
    private StringBuilder _text = new(InitialCapacity);
    private List<(OutputStream Stream, int End)> _pieces = [];
    private bool _isCompleted;

    // What was read last, which is written to after the next read, so reading doesn't hold up writing; only the reader touches these.
    private StringBuilder _readText = new(InitialCapacity);
    private List<(OutputStream Stream, int End)> _readPieces = [];

    // Holds an item while there is something to read, so the reader is woken once for any number of writes, none of which wait.
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed when nothing more will be written, so the end of the output can be shown right away.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Adds <paramref name="text"/> written to <paramref name="stream"/>, unless the output is complete. Can be called from any thread.</summary>
    public void Write(OutputStream stream, ReadOnlySpan<char> text)
    {
        lock (_lock)
        {
            if (_isCompleted)
            {
                return;
            }

            var start = _text.Length;
            var progress = _parser.Progress;
            if (stream is OutputStream.StandardOutput)
            {
                _parser.Parse(text, _text);
            }
            else
            {
                _text.Append(text);
            }

            if (!AddPiece(stream, start) && _parser.Progress == progress)
            {
                return;
            }
        }

        _signal.Writer.TryWrite(true);
    }

    /// <summary>
    /// Ends the output, with what was held back as the start of a progress sequence, as nothing more will be written.
    /// Whatever is written after is ignored.
    /// </summary>
    public void Complete()
    {
        lock (_lock)
        {
            if (_isCompleted)
            {
                return;
            }

            _isCompleted = true;
            var start = _text.Length;
            _parser.Flush(_text);
            AddPiece(OutputStream.StandardOutput, start);
        }

        // The reader is woken even if nothing was added, as a write that ended just before may not have woken it yet.
        _signal.Writer.TryWrite(true);
        _signal.Writer.TryComplete();
        _completion.TrySetResult();
    }

    /// <summary>Waits until there is something to read, returning <see langword="false"/> once the output is complete, and was read to the end.</summary>
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) => _signal.Reader.WaitToReadAsync(cancellationToken);

    /// <summary>
    /// Takes everything written since the last read, in chunks of no more than <see cref="MaxChunkLength"/>, with the progress reported so far.
    /// Can't be called from more than one thread at a time.
    /// </summary>
    public OutputBatch Read()
    {
        // Taken first, so anything written from now on wakes the reader again.
        _signal.Reader.TryRead(out _);

        StringBuilder text;
        List<(OutputStream Stream, int End)> pieces;
        ProgressReport progress;
        lock (_lock)
        {
            text = _text;
            pieces = _pieces;
            progress = _parser.Progress;
            _text = _readText;
            _pieces = _readPieces;
        }

        // When only the progress changed, or the reader was woken for what it already read, there is nothing to split, so no list is made.
        var batch = pieces.Count > 0 ? new OutputBatch(Split(text, pieces), progress) : new OutputBatch([], progress);

        pieces.Clear();
        _readPieces = pieces;
        _readText = text.Capacity > InitialCapacity ? new StringBuilder(InitialCapacity) : text.Clear();
        return batch;
    }

    private static List<OutputChunk> Split(StringBuilder text, List<(OutputStream Stream, int End)> pieces)
    {
        List<OutputChunk> chunks = [with(capacity: pieces.Count + (text.Length / MaxChunkLength))];
        var start = 0;
        foreach (var (stream, end) in pieces)
        {
            while (start < end)
            {
                var length = Math.Min(end - start, MaxChunkLength);

                // A surrogate pair isn't split, so the text of each chunk is valid on its own.
                if (start + length < end && char.IsHighSurrogate(text[start + length - 1]))
                {
                    length--;
                }

                chunks.Add(new OutputChunk(stream, text.ToString(start, length)));
                start += length;
            }
        }

        return chunks;
    }

    // Returns whether anything was added since start.
    private bool AddPiece(OutputStream stream, int start)
    {
        var end = _text.Length;
        if (end == start)
        {
            return false;
        }

        if (_pieces.Count > 0 && _pieces[^1].Stream == stream)
        {
            _pieces[^1] = (stream, end);
        }
        else
        {
            _pieces.Add((stream, end));
        }

        return true;
    }
}
