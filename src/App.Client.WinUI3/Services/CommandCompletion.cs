namespace App.Client.WinUI3.Services;

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
}

/// <summary>
/// A match of a completion: the text put in, the text it is listed by, the name of the kind of thing it is, such as <c>Command</c> or <c>ProviderContainer</c>,
/// and a description of it, such as the syntax of a command, or the full path of a file.
/// </summary>
public sealed record CompletionMatch(string CompletionText, string ListItemText, string ResultType, string? ToolTip);
