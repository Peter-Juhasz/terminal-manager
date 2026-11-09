using App.Client.WinUI3.Models;
using PeterJuhasz.Text.Yaml.Model;
using PeterJuhasz.Text.Yaml.Parsing;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Reads the scripts shared with a team, from the .terminal-manager.yml file in the working folder, which can be kept with the code.
/// </summary>
/// <remarks>
/// The scripts are a list under <c>scripts</c>, in the order they are shown, each with a <c>name</c> and a <c>command</c>,
/// and optionally a <c>description</c> shown in place of the command, and the <c>language</c> it is written in, or a list of them,
/// so it is only shown for projects whose commands are written in one of them. A script with variables, written as <c>{{ name }}</c>, is a template.
/// <code>
/// scripts:
/// - name: Build
///   command: dotnet build
/// - name: Deploy
///   description: Deploys to the given environment
///   language: powershell
///   command: ./deploy.ps1 -Environment {{ environment }}
/// </code>
/// Other keys are left out, so a file written for a later version can still be read.
/// </remarks>
public sealed class TerminalManagerScriptFile : IScriptFileFormat
{
    public const string Name = ".terminal-manager.yml";

    public string FileName => Name;

    public string DisplayName => "Team scripts";

    public IReadOnlyList<string> RelatedFileNames => [];

    // each script tells which languages it is written in
    public bool IsSupportedBy(Project project) => true;

    public IReadOnlyList<Script> Parse(string text, Project project, IReadOnlySet<string> relatedFiles)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(project);

        DocumentNode document;
        try
        {
            document = Parser.Parse(text);
        }
        catch (YamlException exception)
        {
            throw new ScriptFileFormatException(exception.Reason, exception.Line);
        }

        // an empty file has no scripts
        if (document.Root is ScalarNode { IsNull: true })
        {
            return [];
        }

        if (document.Root is not MappingNode root)
        {
            throw new ScriptFileFormatException("The file must be a mapping, with the scripts under 'scripts'.");
        }

        if (!root.TryGetValue("scripts", out var scriptsNode) || scriptsNode is ScalarNode { IsNull: true })
        {
            return [];
        }

        if (scriptsNode is not SequenceNode scripts)
        {
            throw new ScriptFileFormatException("'scripts' must be a list of scripts, each with a 'name' and a 'command'.");
        }

        var result = new List<Script>(scripts.Items.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (item, index) in scripts.Items.Select(static (item, index) => (item, index)))
        {
            if (item is not MappingNode mapping)
            {
                throw new ScriptFileFormatException($"Script {index + 1} must be a mapping, with a 'name' and a 'command'.");
            }

            if (mapping.GetText($"{index + 1}", "name")?.Trim() is not { Length: > 0 } name)
            {
                throw new ScriptFileFormatException($"Script {index + 1} has no name.");
            }

            if (!names.Add(name))
            {
                throw new ScriptFileFormatException($"Script '{name}' is written more than once.");
            }

            if (ParseScript(name, mapping, project) is { } script)
            {
                result.Add(script);
            }
        }

        return result;
    }

    // null when the script is written in languages other than that of the project
    private static Script? ParseScript(string name, MappingNode mapping, Project project)
    {
        var command = mapping.GetText(name, "command");
        var description = mapping.GetText(name, "description");
        var languages = mapping.GetLanguages(name);

        if (command?.Trim() is not { Length: > 0 } trimmed)
        {
            throw new ScriptFileFormatException($"Script '{name}' has no command.");
        }

        if (languages is not null && !languages.Contains(project.Language))
        {
            return null;
        }

        return new()
        {
            Id = ScriptFileSnapshot.CreateScriptId(Name, name),
            Name = name,
            Command = trimmed,
            Description = description?.Trim() is { Length: > 0 } trimmedDescription ? trimmedDescription : null,
            IsTemplate = ScriptTemplate.GetVariables(trimmed) is { Count: > 0 },
        };
    }
}

file static class NodeExtensions
{
    extension(MappingNode mapping)
    {
        /// <summary>The text of <paramref name="key"/> of the script named <paramref name="script"/>, or <see langword="null"/> when it isn't given.</summary>
        public string? GetText(string script, string key) => mapping.TryGetValue(key, out var node)
            ? node switch
            {
                ScalarNode { IsNull: true } => null,
                ScalarNode scalar => scalar.Value,
                _ => throw new ScriptFileFormatException($"'{key}' of script '{script}' must be text."),
            }
            : null;

        /// <summary>The languages the script named <paramref name="script"/> is written in, or <see langword="null"/> when it is shown for any.</summary>
        public IReadOnlySet<CommandLanguage>? GetLanguages(string script) => mapping.TryGetValue("language", out var node)
            ? node switch
            {
                ScalarNode { IsNull: true } => null,
                ScalarNode scalar => new HashSet<CommandLanguage> { ParseLanguage(script, scalar) },
                SequenceNode { Items: var items } => items.Select(item => ParseLanguage(script, item)).ToHashSet(),
                _ => throw new ScriptFileFormatException($"'language' of script '{script}' must be a language, or a list of them."),
            }
            : null;
    }

    private static CommandLanguage ParseLanguage(string script, Node node) => node switch
    {
        ScalarNode { Value: "cmd" } => CommandLanguage.Cmd,
        ScalarNode { Value: "powershell" or "pwsh" } => CommandLanguage.PowerShell,
        ScalarNode { Value: "shell" or "sh" or "bash" } => CommandLanguage.Shell,
        ScalarNode { Value: "arguments" } => CommandLanguage.Arguments,
        ScalarNode { Value: "csharp" } => CommandLanguage.CSharp,
        ScalarNode { Value: "python" } => CommandLanguage.Python,
        ScalarNode { Value: "javascript" or "node" } => CommandLanguage.JavaScript,
        ScalarNode { Value: var other } => throw new ScriptFileFormatException($"Script '{script}' has an unknown language '{other}'."),
        _ => throw new ScriptFileFormatException($"'language' of script '{script}' must be a language, or a list of them."),
    };
}
