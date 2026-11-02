using System.Text.RegularExpressions;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Expands the variables of a script template, each written as <c>{{ name }}</c>, or <c>{{ name = value }}</c> to give it a default value.
/// </summary>
/// <remarks>
/// Spaces around a name or a default value are not part of it. Names are told apart by case, and an empty one is not a variable.
/// </remarks>
public static partial class ScriptTemplate
{
    [GeneratedRegex(@"\{\{\s*([^{}=\s](?:[^{}=]*[^{}=\s])?)\s*(?:=\s*([^{}]*?)\s*)?\}\}")]
    private static partial Regex VariableRegex();

    /// <summary>The variables in <paramref name="template"/>, each once, in the order they first appear.</summary>
    /// <remarks>A variable given more than one default value takes the first.</remarks>
    public static IReadOnlyList<TemplateVariable> GetVariables(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var variables = new List<TemplateVariable>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in VariableRegex().Matches(template))
        {
            var name = match.Groups[1].Value;
            string? defaultValue = match.Groups[2] is { Success: true } group ? group.Value : null;
            if (!indexes.TryGetValue(name, out var index))
            {
                indexes.Add(name, variables.Count);
                variables.Add(new(Name: name, DefaultValue: defaultValue));
                continue;
            }

            if (variables[index] is { DefaultValue: null } && defaultValue is not null)
            {
                variables[index] = variables[index] with { DefaultValue = defaultValue };
            }
        }

        return variables;
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

/// <summary>A variable of a script template.</summary>
/// <param name="DefaultValue">The value filled in to begin with, if the template gives one.</param>
public sealed record TemplateVariable(string Name, string? DefaultValue);
