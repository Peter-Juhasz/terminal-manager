using System.Buffers;
using System.Globalization;
using System.Text;

namespace App.Services;

/// <summary>The states of the progress a command reports, as the OSC 9;4 sequence defines them.</summary>
public enum ProgressState
{
    None = 0,
    Normal = 1,
    Error = 2,
    Indeterminate = 3,
    Paused = 4,
}

/// <summary>A progress a command reported, with <see cref="Value"/> in percent, if it has any.</summary>
public readonly record struct ProgressReport(ProgressState State, int? Value);

/// <summary>
/// Takes the progress sequences (<c>ESC ] 9 ; 4 ; state ; value</c>, ended by <c>ESC \</c> or <c>BEL</c>), that Windows Terminal and ConEmu show
/// on the taskbar, out of the output of a command, even when one is split between the pieces it is read in, and keeps the progress they report.
/// </summary>
public sealed class ProgressSequenceParser
{
    private const string Prefix = "\e]9;4;";

    // Longer than any valid sequence, so something that only starts like one isn't held back for long.
    private const int MaxLength = 32;

    // The start of a sequence at the end of the last piece, which may be continued in the next; it is never longer than MaxLength.
    private readonly char[] _pending = new char[MaxLength];
    private int _pendingLength;

    /// <summary>
    /// The progress reported so far, the way it would be shown on the taskbar: with the percent it shows,
    /// which an error or a pause reported without one leaves as it was.
    /// </summary>
    public ProgressReport Progress { get; private set; } = new(ProgressState.None, 0);

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="output"/> without the progress sequences, updating <see cref="Progress"/> by each of them.
    /// The start of a sequence that may continue in the next piece is held back until then.
    /// </summary>
    public void Parse(ReadOnlySpan<char> text, StringBuilder output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (_pendingLength == 0)
        {
            ParseCore(text, output);
            return;
        }

        // The start of a sequence was held back, so it is parsed joined with what follows it, which is rare enough not to avoid the copy.
        var length = _pendingLength + text.Length;
        var joined = ArrayPool<char>.Shared.Rent(length);
        try
        {
            _pending.AsSpan(0, _pendingLength).CopyTo(joined);
            text.CopyTo(joined.AsSpan(_pendingLength));
            _pendingLength = 0;
            ParseCore(joined.AsSpan(0, length), output);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(joined);
        }
    }

    /// <summary>Appends what was held back as the start of a sequence to <paramref name="output"/>, when there is nothing more to come.</summary>
    public void Flush(StringBuilder output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.Append(_pending, 0, _pendingLength);
        _pendingLength = 0;
    }

    private void ParseCore(ReadOnlySpan<char> text, StringBuilder output)
    {
        while (true)
        {
            var escape = text.IndexOf('\e');
            if (escape < 0)
            {
                output.Append(text);
                return;
            }

            output.Append(text[..escape]);
            text = text[escape..];
            switch (TryReadSequence(text, out var length, out var report))
            {
                case SequenceResult.Complete:
                    if (report is { } value)
                    {
                        Report(value);
                    }

                    text = text[length..];
                    break;

                case SequenceResult.Incomplete:
                    text.CopyTo(_pending);
                    _pendingLength = text.Length;
                    return;

                default:
                    output.Append('\e');
                    text = text[1..];
                    break;
            }
        }
    }

    private void Report(ProgressReport report)
    {
        // As on the taskbar, an error or a pause without a value keeps the value it had.
        var value = Progress.Value;
        if (report.Value is { } reported && (reported > 0 || report.State is not (ProgressState.Error or ProgressState.Paused)))
        {
            value = reported;
        }
        else if (report.State is ProgressState.None)
        {
            value = 0;
        }

        Progress = new ProgressReport(report.State, value);
    }

    // Reads the sequence that text starts with, which is incomplete only while it is shorter than MaxLength.
    private static SequenceResult TryReadSequence(ReadOnlySpan<char> text, out int length, out ProgressReport? report)
    {
        length = 0;
        report = null;

        if (text.Length < Prefix.Length)
        {
            return Prefix.AsSpan().StartsWith(text, StringComparison.Ordinal) ? SequenceResult.Incomplete : SequenceResult.Invalid;
        }

        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return SequenceResult.Invalid;
        }

        for (var index = Prefix.Length; index < text.Length && index < MaxLength; index++)
        {
            switch (text[index])
            {
                case '\a':
                    length = index + 1;
                    break;

                case '\e' when index + 1 == text.Length:
                    return SequenceResult.Incomplete;

                case '\e' when text[index + 1] == '\\':
                    length = index + 2;
                    break;

                case '\e':
                    return SequenceResult.Invalid;

                default:
                    continue;
            }

            report = ParseBody(text[Prefix.Length..index]);
            return SequenceResult.Complete;
        }

        return text.Length < MaxLength ? SequenceResult.Incomplete : SequenceResult.Invalid;
    }

    // Unknown states are taken out of the output all the same, as a terminal wouldn't show them either.
    private static ProgressReport? ParseBody(ReadOnlySpan<char> body)
    {
        var separator = body.IndexOf(';');
        var stateText = separator < 0 ? body : body[..separator];
        if (!int.TryParse(stateText, NumberStyles.None, CultureInfo.InvariantCulture, out var state) || state is < 0 or > 4)
        {
            return null;
        }

        int? value = null;
        if (separator >= 0 && int.TryParse(body[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            value = Math.Clamp(parsed, 0, 100);
        }

        return new ProgressReport((ProgressState)state, value);
    }

    private enum SequenceResult
    {
        Invalid,
        Incomplete,
        Complete,
    }
}
