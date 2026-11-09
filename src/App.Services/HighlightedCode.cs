namespace App.Services;

/// <summary>The kinds of text of a script that are colored; the rest is shown as it is.</summary>
public enum SyntaxKind
{
    Keyword,
    Comment,
    String,
    TypeName,
}

/// <summary>The <paramref name="Length"/> characters of a script from <paramref name="Start"/>, which are of the kind <paramref name="Kind"/>.</summary>
public sealed record SyntaxSpan(int Start, int Length, SyntaxKind Kind);

/// <summary>A piece of a script, colored as <paramref name="Kind"/>, or shown as it is when <see langword="null"/>.</summary>
public readonly record struct HighlightedRun(string Text, SyntaxKind? Kind);

/// <summary><paramref name="Text"/>, with the <paramref name="Spans"/> of it that are colored, in order, none of which overlap.</summary>
public sealed record HighlightedCode(string Text, IReadOnlyList<SyntaxSpan> Spans)
{
    /// <summary><paramref name="text"/>, with nothing colored, such as before it was parsed.</summary>
    public static HighlightedCode Plain(string text) => new(Text: text, Spans: []);

    /// <summary>The text, in pieces each colored as a single kind, or not at all.</summary>
    public IEnumerable<HighlightedRun> GetRuns()
    {
        var position = 0;
        foreach (var span in Spans)
        {
            if (span.Start > position)
            {
                yield return new(Text[position..span.Start], Kind: null);
            }

            yield return new(Text.Substring(span.Start, span.Length), span.Kind);
            position = span.Start + span.Length;
        }

        if (position < Text.Length)
        {
            yield return new(Text[position..], Kind: null);
        }
    }

    /// <summary>
    /// <paramref name="text"/>, with those of <paramref name="spans"/> that are within it, in order, and don't overlap the ones before them,
    /// so the spans of a parser that is out of step with it can't break showing it.
    /// </summary>
    public static HighlightedCode Create(string text, IEnumerable<SyntaxSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(spans);

        List<SyntaxSpan> valid = [];
        var end = 0;
        foreach (var span in spans.OrderBy(static span => span.Start))
        {
            if (span is not { Start: >= 0, Length: > 0 } || span.Start < end || span.Start + span.Length > text.Length)
            {
                continue;
            }

            valid.Add(span);
            end = span.Start + span.Length;
        }

        return new(Text: text, Spans: valid);
    }
}
