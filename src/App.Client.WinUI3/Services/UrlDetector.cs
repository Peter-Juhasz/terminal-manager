namespace App.Client.WinUI3.Services;

/// <summary>Where a web address was found in a text, and what it is.</summary>
public readonly record struct UrlMatch(int Start, int Length, Uri Uri);

/// <summary>Finds the <c>http</c> and <c>https</c> addresses written in a text, such as the output of a command.</summary>
/// <remarks>
/// An address ends at whatever can't be in one unescaped, such as a space or a quote, and the punctuation after it is left out,
/// as are closing brackets that weren't opened in it, so one written in a sentence or in brackets ends where it is meant to.
/// </remarks>
public static class UrlDetector
{
    /// <summary>The longest address looked for, so a long word that starts like one isn't read to its end.</summary>
    public const int MaxLength = 4096;

    private const string Http = "http://";
    private const string Https = "https://";

    /// <summary>Finds the addresses in <paramref name="text"/>, in order, up to <paramref name="maxCount"/> of them.</summary>
    public static List<UrlMatch> FindAll(ReadOnlySpan<char> text, int maxCount = int.MaxValue)
    {
        List<UrlMatch> matches = [];
        var position = 0;
        while (matches.Count < maxCount)
        {
            var found = text[position..].IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                break;
            }

            var start = position + found;
            if (TryMatch(text[start..], out var length, out var uri))
            {
                matches.Add(new(Start: start, Length: length, Uri: uri));
                position = start + length;
            }
            else
            {
                position = start + 4;
            }
        }

        return matches;
    }

    // whether an address starts the text, and how long it is
    private static bool TryMatch(ReadOnlySpan<char> text, out int length, out Uri uri)
    {
        length = 0;
        uri = null!;
        var schemeLength = GetSchemeLength(text);
        if (schemeLength is 0)
        {
            return false;
        }

        var end = schemeLength;
        while (end < text.Length && end < MaxLength && !IsTerminator(text[end]))
        {
            end++;
        }

        length = GetLength(text[..end]);
        return length > schemeLength
            && Uri.TryCreate(text[..length].ToString(), UriKind.Absolute, out uri!)
            && uri.Scheme is "http" or "https"
            && uri.Host is not "";
    }

    private static int GetSchemeLength(ReadOnlySpan<char> text)
    {
        if (text.StartsWith(Https, StringComparison.OrdinalIgnoreCase))
        {
            return Https.Length;
        }

        if (text.StartsWith(Http, StringComparison.OrdinalIgnoreCase))
        {
            return Http.Length;
        }

        return 0;
    }

    // the length of the address the word is, without the punctuation, and the closing brackets not opened in it, at its end
    private static int GetLength(ReadOnlySpan<char> word)
    {
        var length = word.Length;
        while (length > 0)
        {
            var last = word[length - 1];
            var isLeftOut = last switch
            {
                '.' or ',' or ';' or ':' or '!' or '?' or '*' => true,
                ')' => IsUnopened(word[..length], '(', ')'),
                ']' => IsUnopened(word[..length], '[', ']'),
                '}' => IsUnopened(word[..length], '{', '}'),
                _ => false,
            };
            if (!isLeftOut)
            {
                break;
            }

            length--;
        }

        return length;
    }

    private static bool IsUnopened(ReadOnlySpan<char> text, char open, char close) => text.Count(open) < text.Count(close);

    private static bool IsTerminator(char c) => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>' or '"' or '\'' or '`';
}
