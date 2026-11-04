namespace App.Client.WinUI3.Services;

/// <summary>Finds what to color in a script, by what the language it is written in reads it as.</summary>
public interface ISyntaxHighlighter
{
    /// <summary>
    /// The spans to color in <paramref name="input"/>, parsed as if <paramref name="prefix"/> was before it, as what that leaves open, such as a here-string, changes what it means;
    /// <see langword="null"/> when it couldn't be parsed.
    /// </summary>
    Task<HighlightedCode?> HighlightAsync(string prefix, string input, CancellationToken cancellationToken);
}
