using System.Buffers;
using System.Text;
using App.Client.WinUI3.Services;
using App.Domain;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// The lines of the output of a command in which <see cref="Search"/> is found, kept as an <see cref="Output"/> of their own,
/// so they are shown like any output. It is updated with the chunks appended since the last time, so the output is searched only once.
/// </summary>
/// <remarks>
/// A line is shown with its line break, in the stream each part of it was written to. The last line isn't searched until it ends, as more of it
/// may yet be written, or until the command ended, as told by <see cref="Complete"/>.
/// </remarks>
public sealed class OutputLineFilter(TextSearch search)
{
    /// <summary>How long the text of a chunk of the lines found grows before another is started, which keeps it off the large object heap.</summary>
    public const int MaxChunkLength = 16 * 1024;

    private const string LineBreaks = "\r\n";

    // the parts of the last line, which hasn't ended yet, as they were written
    private readonly List<(OutputStream Stream, ReadOnlyMemory<char> Text)> _line = [];

    // the text of the last line when it is made of more than one part, so it can be searched as one
    private readonly ArrayBufferWriter<char> _lineText = new();

    // the lines found since the last update, joined while they were written to the same stream
    private readonly StringBuilder _found = new();
    private OutputStream _foundStream;
    private readonly List<OutputChunk> _chunks = [];

    // the number of chunks of the output that were searched
    private int _chunkCount;
    private bool _isCompleted;

    public TextSearch Search { get; } = search;

    /// <summary>The lines in which <see cref="Search"/> was found.</summary>
    public CommandOutput Output { get; } = new();

    /// <summary>Searches the chunks of <paramref name="chunks"/> appended since the last time, which is all of them the first time.</summary>
    public void Update(IReadOnlyList<OutputChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        for (; _chunkCount < chunks.Count; _chunkCount++)
        {
            SearchChunk(chunks[_chunkCount]);
        }

        Flush();
    }

    /// <summary>Searches the last line, which ended with the output, even without a line break.</summary>
    public void Complete()
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        if (_line.Count > 0)
        {
            EndLine();
        }

        Flush();
        Output.Complete();
    }

    private void SearchChunk(OutputChunk chunk)
    {
        var text = chunk.Text.AsSpan();
        var first = text.IndexOf('\n');
        if (first < 0)
        {
            _line.Add((chunk.Stream, chunk.Text.AsMemory()));
            return;
        }

        // the start of the chunk ends the line it is part of
        var start = 0;
        if (_line.Count > 0)
        {
            start = first + 1;
            _line.Add((chunk.Stream, chunk.Text.AsMemory(0, start)));
            EndLine();
        }

        var end = text.LastIndexOf('\n') + 1;
        if (start < end)
        {
            SearchLines(chunk.Stream, text[start..end]);
        }

        if (end < text.Length)
        {
            _line.Add((chunk.Stream, chunk.Text.AsMemory(end)));
        }
    }

    // Searches whole lines, each ending with its line break, written to the same stream.
    private void SearchLines(OutputStream stream, ReadOnlySpan<char> lines)
    {
        // A regular expression is matched in each line by itself, so it can't match across lines, and its anchors match where each starts and ends.
        if (Search.IsRegex)
        {
            while (!lines.IsEmpty)
            {
                var length = lines.IndexOf('\n') + 1;
                var line = lines[..length];
                if (Search.IsMatch(line.TrimEnd(LineBreaks)))
                {
                    Add(stream, line);
                }

                lines = lines[length..];
            }

            return;
        }

        // The query can't span lines, so the lines it isn't found in are skipped as they are searched for it, without looking for where each ends.
        while (!lines.IsEmpty)
        {
            var index = Search.IndexOf(lines);
            if (index < 0)
            {
                return;
            }

            var start = lines[..index].LastIndexOf('\n') + 1;
            var end = index + lines[index..].IndexOf('\n') + 1;
            Add(stream, lines[start..end]);
            lines = lines[end..];
        }
    }

    // Searches the last line, now that it ended, and starts the next.
    private void EndLine()
    {
        if (_line.Count is 1)
        {
            var (stream, text) = _line[0];
            if (Search.IsMatch(text.Span.TrimEnd(LineBreaks)))
            {
                Add(stream, text.Span);
            }

            _line.Clear();
            return;
        }

        _lineText.ResetWrittenCount();
        foreach (var (_, text) in _line)
        {
            _lineText.Write(text.Span);
        }

        if (Search.IsMatch(_lineText.WrittenSpan.TrimEnd(LineBreaks)))
        {
            foreach (var (stream, text) in _line)
            {
                Add(stream, text.Span);
            }
        }

        _line.Clear();
    }

    private void Add(OutputStream stream, ReadOnlySpan<char> text)
    {
        if (_found.Length > 0 && (stream != _foundStream || _found.Length >= MaxChunkLength))
        {
            EndChunk();
        }

        _foundStream = stream;
        _found.Append(text);
    }

    private void EndChunk()
    {
        _chunks.Add(new(_foundStream, _found.ToString()));
        _found.Clear();
    }

    // Shows the lines found since the last time, all at once.
    private void Flush()
    {
        if (_found.Length > 0)
        {
            EndChunk();
        }

        if (_chunks.Count is 0)
        {
            return;
        }

        Output.Append(_chunks);
        _chunks.Clear();
    }
}
