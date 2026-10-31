namespace App.Client.WinUI3.Services;

/// <summary>
/// What the text between <paramref name="ReplacementIndex"/> and <paramref name="ReplacementLength"/> of the input can be completed to.
/// </summary>
public sealed record CommandCompletion(int ReplacementIndex, int ReplacementLength, IReadOnlyList<string> Matches)
{
    /// <summary>The input, with the text completed replaced by the match at <paramref name="match"/>.</summary>
    public string Apply(string input, int match)
    {
        ArgumentNullException.ThrowIfNull(input);

        return string.Concat(input.AsSpan(0, ReplacementIndex), Matches[match], input.AsSpan(ReplacementIndex + ReplacementLength));
    }

    /// <summary>Where the caret is put after the match at <paramref name="match"/>, at its end.</summary>
    public int GetCaret(int match) => ReplacementIndex + Matches[match].Length;
}
