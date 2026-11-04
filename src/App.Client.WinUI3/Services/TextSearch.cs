using System.Buffers;
using System.Text.RegularExpressions;

namespace App.Client.WinUI3.Services;

/// <summary>
/// What is searched for in a text: a <see cref="Query"/> found as it is, in any case unless <see cref="IsCaseSensitive"/>, or a regular expression
/// when <see cref="IsRegex"/>. The text is compared ordinally, so a match is always as long as the query it was found by.
/// </summary>
/// <remarks>
/// It is prepared once by <see cref="Create"/>, so it is kept, and handed to whatever searches with it, rather than made again for every text.
/// Searches are equal when they search for the same.
/// </remarks>
public sealed class TextSearch : IEquatable<TextSearch>
{
    // a regular expression that takes this long to match is found nowhere, so a slow pattern can't hang the app
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private readonly SearchValues<string>? _values;
    private readonly Regex? _regex;

    private TextSearch(string query, bool isCaseSensitive, SearchValues<string>? values, Regex? regex)
    {
        Query = query;
        IsCaseSensitive = isCaseSensitive;
        _values = values;
        _regex = regex;
    }

    public string Query { get; }

    public bool IsCaseSensitive { get; }

    public bool IsRegex => _regex is not null;

    public bool Equals(TextSearch? other) => other is not null && Query == other.Query && IsCaseSensitive == other.IsCaseSensitive && IsRegex == other.IsRegex;

    public override bool Equals(object? obj) => Equals(obj as TextSearch);

    public override int GetHashCode() => HashCode.Combine(Query, IsCaseSensitive, IsRegex);

    /// <summary>The search for <paramref name="query"/>, or null when it is empty, or when it is an invalid regular expression.</summary>
    public static TextSearch? Create(string? query, bool isCaseSensitive, bool isRegex)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        if (!isRegex)
        {
            var comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return new(query, isCaseSensitive, SearchValues.Create([query], comparison), regex: null);
        }

        var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            return new(query, isCaseSensitive, values: null, new Regex(query, options, MatchTimeout));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether the search is found anywhere in <paramref name="text"/>.</summary>
    public bool IsMatch(ReadOnlySpan<char> text)
    {
        if (_values is not null)
        {
            return text.IndexOfAny(_values) >= 0;
        }

        try
        {
            return _regex!.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>Where the query is first found in <paramref name="text"/>, or -1; only for a search that isn't a regular expression.</summary>
    public int IndexOf(ReadOnlySpan<char> text) => _values is not null
        ? text.IndexOfAny(_values)
        : throw new InvalidOperationException("A regular expression is matched line by line.");

    /// <summary>Adds where the search is found in <paramref name="text"/> to <paramref name="matches"/>, in order; empty matches are left out.</summary>
    public void FindMatches(ReadOnlySpan<char> text, List<(int Start, int Length)> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (_values is not null)
        {
            var length = Query.Length;
            for (var offset = 0; offset < text.Length;)
            {
                var index = text[offset..].IndexOfAny(_values);
                if (index < 0)
                {
                    break;
                }

                matches.Add((offset + index, length));
                offset += index + length;
            }

            return;
        }

        var count = matches.Count;
        try
        {
            foreach (var match in _regex!.EnumerateMatches(text))
            {
                if (match.Length > 0)
                {
                    matches.Add((match.Index, match.Length));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // what was found before it took too long is not to be relied on
            matches.RemoveRange(count, matches.Count - count);
        }
    }
}
