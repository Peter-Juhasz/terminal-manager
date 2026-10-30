using System.Globalization;
using System.Text;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Benchmarks.Baseline;

// The original version, as it was before the output was redesigned.
/// <summary>
/// Takes the progress sequences (<c>ESC ] 9 ; 4 ; state ; value</c>, ended by <c>ESC \</c> or <c>BEL</c>), that Windows Terminal and ConEmu show
/// on the taskbar, out of the output of a command, even when one is split between the pieces it is read in.
/// </summary>
public sealed class ProgressSequenceParser
{
    private const string Prefix = "\e]9;4;";

    // Longer than any valid sequence, so something that only starts like one isn't held back for long.
    private const int MaxLength = 32;

    // The start of a sequence at the end of the last piece, which may be continued in the next.
    private string _pending = string.Empty;

    /// <summary>
    /// Returns <paramref name="text"/> without the progress sequences, reporting each to <paramref name="report"/>.
    /// The end of a sequence that may continue in the next piece is held back until then.
    /// </summary>
    public string Parse(string text, Action<ProgressReport> report)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(report);

        text = string.Concat(_pending, text);
        _pending = string.Empty;

        if (!text.Contains('\e', StringComparison.Ordinal))
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var start = 0;
        while (start < text.Length)
        {
            var escape = text.IndexOf('\e', start);
            if (escape < 0)
            {
                result.Append(text, start, text.Length - start);
                break;
            }

            result.Append(text, start, escape - start);
            switch (TryReadSequence(text, escape, out var end, out var sequence))
            {
                case SequenceResult.Complete:
                    if (sequence is { } value)
                    {
                        report(value);
                    }

                    start = end;
                    break;

                case SequenceResult.Incomplete:
                    _pending = text[escape..];
                    return result.ToString();

                default:
                    result.Append('\e');
                    start = escape + 1;
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>Returns what was held back as the start of a sequence, when there is nothing more to come.</summary>
    public string Flush()
    {
        var pending = _pending;
        _pending = string.Empty;
        return pending;
    }

    private static SequenceResult TryReadSequence(string text, int start, out int end, out ProgressReport? report)
    {
        end = start;
        report = null;

        var available = text.Length - start;
        if (available < Prefix.Length)
        {
            return string.CompareOrdinal(text, start, Prefix, 0, available) == 0 ? SequenceResult.Incomplete : SequenceResult.Invalid;
        }

        if (string.CompareOrdinal(text, start, Prefix, 0, Prefix.Length) != 0)
        {
            return SequenceResult.Invalid;
        }

        var body = start + Prefix.Length;
        for (var index = body; index < text.Length; index++)
        {
            if (index - start >= MaxLength)
            {
                return SequenceResult.Invalid;
            }

            var terminatorLength = text[index] switch
            {
                '\a' => 1,
                '\e' when index + 1 >= text.Length => 0,
                '\e' when text[index + 1] == '\\' => 2,
                '\e' => -1,
                _ => (int?)null,
            };

            switch (terminatorLength)
            {
                case null:
                    continue;
                case 0:
                    return SequenceResult.Incomplete;
                case < 0:
                    return SequenceResult.Invalid;
            }

            end = index + terminatorLength.Value;
            report = ParseBody(text.AsSpan(body, index - body));
            return SequenceResult.Complete;
        }

        return text.Length - start < MaxLength ? SequenceResult.Incomplete : SequenceResult.Invalid;
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
