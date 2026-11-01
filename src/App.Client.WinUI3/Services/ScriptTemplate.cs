using System.Text.RegularExpressions;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Expands the variables of a script template, each written as <c>{{ name }}</c>.
/// </summary>
/// <remarks>
/// Spaces around a name are not part of it. Names are told apart by case, and an empty one is not a variable.
/// </remarks>
public static partial class ScriptTemplate
{
    [GeneratedRegex(@"\{\{\s*([^{}\s](?:[^{}]*[^{}\s])?)\s*\}\}")]
    private static partial Regex VariableRegex();

    /// <summary>The names of the variables in <paramref name="template"/>, each once, in the order they first appear.</summary>
    public static IReadOnlyList<string> GetVariables(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in VariableRegex().Matches(template))
        {
            var name = match.Groups[1].Value;
            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>Replaces each variable in <paramref name="template"/> by its value in <paramref name="values"/>; those without one are left as they are.</summary>
    public static string Expand(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        return VariableRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }
}
