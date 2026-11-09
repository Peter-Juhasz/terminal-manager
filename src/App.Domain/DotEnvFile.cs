using System.Text;

namespace App.Domain;

/// <summary>
/// Reads environment variables from a .env file, one <c>NAME=value</c> on each line.
/// </summary>
/// <remarks>
/// Blank lines and those starting with <c>#</c> are skipped, as are those without a name. A line may start with <c>export</c>, as in a shell script.
/// A value may be quoted: in double quotes, <c>\n</c>, <c>\t</c>, <c>\"</c> and <c>\\</c> are unescaped; in single quotes, it is taken as it is.
/// An unquoted value ends at a <c>#</c> after a space, which starts a comment. Variables in values are not expanded.
/// </remarks>
public static class DotEnvFile
{
    public const string FileName = ".env";

    /// <summary>Reads the variables in the file at <paramref name="path"/>, which are none when it doesn't exist; a later one replaces an earlier one by the same name.</summary>
    public static IReadOnlyDictionary<string, string> Read(string path) => File.Exists(path)
        ? Parse(File.ReadAllText(path))
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (line.IsEmpty || line[0] is '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            // the line is trimmed, so a separator past its start has a name before it
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            variables[line[..separator].TrimEnd().ToString()] = ParseValue(line[(separator + 1)..].Trim());
        }

        return variables;
    }

    private static string ParseValue(ReadOnlySpan<char> value)
    {
        if (value is ['"', .. var doubleQuoted, '"'])
        {
            return Unescape(doubleQuoted);
        }

        if (value is ['\'', .. var singleQuoted, '\''])
        {
            return singleQuoted.ToString();
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment switch
        {
            -1 => value.ToString(),
            _ => value[..comment].TrimEnd().ToString(),
        };
    }

    private static string Unescape(ReadOnlySpan<char> value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is not '\\' || i == value.Length - 1)
            {
                builder.Append(value[i]);
                continue;
            }

            i++;
            builder.Append(value[i] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                var other => other,
            });
        }

        return builder.ToString();
    }
}
