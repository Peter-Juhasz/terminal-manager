namespace App.Services;

/// <summary>
/// What the text between <paramref name="ReplacementIndex"/> and <paramref name="ReplacementLength"/> of the input can be completed to.
/// </summary>
public sealed record CommandCompletion(int ReplacementIndex, int ReplacementLength, IReadOnlyList<CompletionMatch> Matches)
{
    /// <summary>The input, with the text completed replaced by the match at <paramref name="match"/>.</summary>
    public string Apply(string input, int match)
    {
        ArgumentNullException.ThrowIfNull(input);

        return string.Concat(input.AsSpan(0, ReplacementIndex), Matches[match].CompletionText, input.AsSpan(ReplacementIndex + ReplacementLength));
    }

    /// <summary>Where the caret is put after the match at <paramref name="match"/>, at its end.</summary>
    public int GetCaret(int match) => ReplacementIndex + Matches[match].CompletionText.Length;

    /// <summary>The completion, with how much of the text each match is listed by is what was typed of it before <paramref name="cursor"/> in <paramref name="input"/>.</summary>
    public CommandCompletion WithMatchedLengths(string input, int cursor)
    {
        ArgumentNullException.ThrowIfNull(input);

        var typed = cursor > ReplacementIndex && cursor <= input.Length ? input[ReplacementIndex..cursor] : "";
        return this with { Matches = Matches.Select(match => match with { MatchedLength = CompletionMatch.GetMatchedLength(match.ListItemText, typed) }).ToArray() };
    }
}

/// <summary>
/// A match of a completion: the text put in, the text it is listed by, the name of the kind of thing it is, such as <c>Command</c> or <c>ProviderContainer</c>,
/// a description of it, such as the syntax of a command, or the full path of a file, and how much of the start of the text it is listed by was typed.
/// </summary>
public sealed record CompletionMatch(string CompletionText, string ListItemText, string ResultType, string? ToolTip, int MatchedLength = 0)
{
    /// <summary>
    /// How much of the start of <paramref name="listItemText"/> is what was <paramref name="typed"/>, which a match may be listed without the start of,
    /// such as the folder of a path, the <c>-</c> of a parameter, the <c>$</c> and scope of a variable, or the quote around either; none when it doesn't start with it.
    /// </summary>
    public static int GetMatchedLength(string listItemText, ReadOnlySpan<char> typed)
    {
        ArgumentNullException.ThrowIfNull(listItemText);

        var name = typed[(typed.LastIndexOfAny('\\', '/', ':') + 1)..].TrimStart("-$@[\"'");

        // what a wildcard matches isn't known, only what is before it
        if (name.IndexOfAny('*', '?') is >= 0 and var wildcard)
        {
            name = name[..wildcard];
        }

        return listItemText.AsSpan().StartsWith(name, StringComparison.OrdinalIgnoreCase) ? name.Length : 0;
    }
}
