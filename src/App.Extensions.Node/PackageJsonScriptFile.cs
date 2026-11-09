using System.Collections.Frozen;
using System.Text.Json;
using App.Domain;

namespace App.Extensions.Node;

/// <summary>
/// Reads the scripts of a Node.js package from the package.json file in the working folder, each run by the package manager of the package.
/// </summary>
/// <remarks>
/// The package manager is the one named by <c>packageManager</c>, or else the one whose lock file is in the folder, or else npm.
/// The scripts run before or after another one of the same name, such as <c>prebuild</c> for <c>build</c>, are left out, as the package manager runs them by itself.
/// </remarks>
public sealed class PackageJsonScriptFile : IScriptFileFormat
{
    public const string Name = "package.json";

    // the lock file of each package manager, in the order they are looked for
    private static readonly (string FileName, string PackageManager)[] LockFiles =
    [
        ("pnpm-lock.yaml", "pnpm"),
        ("yarn.lock", "yarn"),
        ("bun.lock", "bun"),
        ("bun.lockb", "bun"),
    ];

    private static readonly string[] LockFileNames = [.. LockFiles.Select(static lockFile => lockFile.FileName)];

    private static readonly FrozenSet<string> PackageManagers = FrozenSet.Create(StringComparer.Ordinal, "npm", "pnpm", "yarn", "bun");

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public string FileName => Name;

    public string DisplayName => "npm";

    public IReadOnlyList<string> RelatedFileNames => LockFileNames;

    // the package manager is run by a shell; an app given arguments, or an interpreter, can't run it
    public bool IsSupportedBy(Project project) => project.Language is CommandLanguage.Cmd or CommandLanguage.PowerShell or CommandLanguage.Shell;

    public IReadOnlyList<Script> Parse(string text, Project project, IReadOnlySet<string> relatedFiles)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(relatedFiles);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, DocumentOptions);
        }
        catch (JsonException exception)
        {
            throw new ScriptFileFormatException("The file isn't valid JSON.", exception.LineNumber is { } line ? (int)line + 1 : null);
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                throw new ScriptFileFormatException("The file must be an object.");
            }

            if (!document.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind is JsonValueKind.Null)
            {
                return [];
            }

            if (scripts.ValueKind is not JsonValueKind.Object)
            {
                throw new ScriptFileFormatException("'scripts' must be an object of names to commands.");
            }

            var packageManager = GetPackageManager(document.RootElement, relatedFiles);
            var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var script in scripts.EnumerateObject())
            {
                if (script.Value.ValueKind is not JsonValueKind.String)
                {
                    throw new ScriptFileFormatException($"Script '{script.Name}' must be a command.");
                }

                // a later one replaces an earlier one by the same name, as it does for the package manager
                bodies[script.Name] = script.Value.GetString()!;
            }

            return bodies
                .Where(script => script.Key.Trim() is { Length: > 0 } && !IsLifecycleScript(script.Key, bodies))
                .Select(script => new Script
                {
                    Id = ScriptFileSnapshot.CreateScriptId(Name, script.Key),
                    Name = script.Key,
                    Command = $"{packageManager} run {QuoteName(script.Key, project.Language)}",
                    Description = script.Value.Trim() is { Length: > 0 } body ? body : null,
                })
                .ToList();
        }
    }

    // the one named by packageManager, such as "pnpm@9.1.0", or the one whose lock file is there
    private static string GetPackageManager(JsonElement package, IReadOnlySet<string> relatedFiles)
    {
        if (package.TryGetProperty("packageManager", out var named) && named.ValueKind is JsonValueKind.String
            && named.GetString()!.Split('@', 2)[0].Trim() is var name && PackageManagers.Contains(name))
        {
            return name;
        }

        return LockFiles.FirstOrDefault(lockFile => relatedFiles.Contains(lockFile.FileName)).PackageManager ?? "npm";
    }

    // run by the package manager before or after the script of the rest of the name
    private static bool IsLifecycleScript(string name, Dictionary<string, string> scripts) =>
        (name.StartsWith("pre", StringComparison.Ordinal) && name.Length > 3 && scripts.ContainsKey(name[3..]))
        || (name.StartsWith("post", StringComparison.Ordinal) && name.Length > 4 && scripts.ContainsKey(name[4..]));

    // a name of only these characters is taken as it is by any shell
    private static string QuoteName(string name, CommandLanguage language) => name.All(static c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '.' or '/' or '+' or '=')
        ? name
        : language switch
        {
            CommandLanguage.PowerShell => PowerShellScript.Quote(name),
            CommandLanguage.Shell => ShellScript.Quote(name),
            _ => $"\"{name}\"",
        };
}
