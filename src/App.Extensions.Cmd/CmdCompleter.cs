using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using App.Services;

namespace App.Extensions.Cmd;

/// <summary>
/// Completes the first word of a command of the Command Prompt, the command, to the name of a folder or a file in the working folder,
/// a command <c>help</c> lists, or an executable in PATH, or, after a folder, to a name in that folder; what comes after it isn't completed.
/// </summary>
public sealed partial class CmdCompleter : ICommandCompleter
{
    // the characters cmd takes a name apart at, or reads as something else, unless it is quoted
    private static readonly System.Buffers.SearchValues<char> CharactersToQuote = System.Buffers.SearchValues.Create([' ', '&', '(', ')', '[', ']', '{', '}', '^', '=', ';', '!', '\'', '+', ',', '`', '~']);

    private static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true };

    // the commands help lists, which are the same everywhere, so they are only asked for once
    private readonly Lazy<Task<IReadOnlyList<CompletionMatch>>> _commands = new(static () => LoadCommandsAsync(CancellationToken.None));

    // the executables of the folders in PATH, by the PATH and PATHEXT they were looked for with, as projects can set their own;
    // each is only looked for once, as there are many of them
    private readonly ConcurrentDictionary<(string? Path, string? PathExtensions), Lazy<Task<PathExecutables>>> _executables = [];

    /// <summary>Starts asking <c>help</c> for the commands, and looking for the executables in PATH of this process, so the first completion doesn't wait for them.</summary>
    public void Prepare(string workingFolder) => Prepare(workingFolder, ReadOnlyDictionary<string, string>.Empty);

    /// <summary>
    /// Starts asking <c>help</c> for the commands, and looking for the executables in PATH, so the first completion doesn't wait for them;
    /// PATH and PATHEXT are taken from <paramref name="environmentVariables"/> when they are set there, as commands are run with them.
    /// </summary>
    public void Prepare(string workingFolder, IReadOnlyDictionary<string, string> environmentVariables)
    {
        ArgumentNullException.ThrowIfNull(environmentVariables);

        _ = _commands.Value;
        _ = GetExecutablesAsync(environmentVariables);
    }

    /// <remarks><paramref name="prefix"/> is ignored, as what it defines isn't a file or a folder.</remarks>
    public Task<CommandCompletion?> CompleteAsync(string workingFolder, string prefix, string input, int cursor, CancellationToken cancellationToken) =>
        CompleteAsync(workingFolder, ReadOnlyDictionary<string, string>.Empty, input, cursor, cancellationToken);

    /// <summary>
    /// Completes the first word of <paramref name="input"/>; the executables are those in PATH, with the extensions in PATHEXT,
    /// taken from <paramref name="environmentVariables"/> when they are set there, as commands are run with them, otherwise from this process.
    /// </summary>
    public async Task<CommandCompletion?> CompleteAsync(string workingFolder, IReadOnlyDictionary<string, string> environmentVariables, string input, int cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(environmentVariables);
        ArgumentNullException.ThrowIfNull(input);

        var commands = await _commands.Value.WaitAsync(cancellationToken);
        var executables = await GetExecutablesAsync(environmentVariables).WaitAsync(cancellationToken);
        return await Task.Run(() => Complete(workingFolder, commands, executables, input, cursor, cancellationToken), cancellationToken);
    }

    private Task<PathExecutables> GetExecutablesAsync(IReadOnlyDictionary<string, string> environmentVariables)
    {
        var key = (Path: GetVariable(environmentVariables, "PATH"), PathExtensions: GetVariable(environmentVariables, "PATHEXT"));
        return _executables.GetOrAdd(key, static key => new(() => Task.Run(() => FindExecutables(key.Path, key.PathExtensions, CancellationToken.None)))).Value;
    }

    // the variable as commands are run with it: as the project sets it, or else as this process has it
    private static string? GetVariable(IReadOnlyDictionary<string, string> environmentVariables, string name) =>
        environmentVariables.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Key: not null, Value: var value }
            ? value
            : Environment.GetEnvironmentVariable(name);

    /// <summary>
    /// Completes the first word of <paramref name="input"/>, when <paramref name="cursor"/> is in it, sorted by name: when it has no folder,
    /// to the folders and files of <paramref name="workingFolder"/>, the <paramref name="commands"/>, and the executables of <paramref name="path"/>, it starts;
    /// when it has a relative folder, to the folders and files of that folder of <paramref name="workingFolder"/>, and the folders and executables of that folder
    /// of each folder of <paramref name="path"/>, as cmd looks for it in them as well; and when it has a full path, to the folders and files of its folder.
    /// The whole word is replaced, but a match is listed by its name alone.
    /// </summary>
    public static CommandCompletion? Complete(string workingFolder, IReadOnlyList<CompletionMatch> commands, PathExecutables path, string input, int cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(input);

        if (cursor < 0 || cursor > input.Length)
        {
            return null;
        }

        var start = input.Length - input.AsSpan().TrimStart().Length;
        if (cursor < start)
        {
            return null;
        }

        // only the first word is completed, so there is nothing to complete once the caret is past it
        var typed = input.AsSpan(start, cursor - start);
        var isQuoted = typed is ['"', ..];
        var word = isQuoted ? typed[1..] : typed;
        if (isQuoted ? word.Contains('"') : word.ContainsAny(' ', '\t'))
        {
            return null;
        }

        // cmd takes a slash for the start of a switch, not for a folder, and what a wildcard matches isn't known
        if (word.ContainsAny('/', '*', '?'))
        {
            return null;
        }

        var separator = word.LastIndexOf('\\');
        var relativeFolder = word[..(separator + 1)].ToString();
        var name = word[(separator + 1)..].ToString();

        // a drive without a folder, or a folder relative to a drive or to the root of one, isn't completed
        if (name.Contains(':') || relativeFolder is not "" && Path.IsPathRooted(relativeFolder) && !Path.IsPathFullyQualified(relativeFolder))
        {
            return null;
        }

        var isFullPath = relativeFolder is not "" && Path.IsPathFullyQualified(relativeFolder);
        var folder = isFullPath ? relativeFolder : Path.Combine(workingFolder, relativeFolder);
        var entries = Enumerate(folder, relativeFolder, name, isQuoted, static _ => true, cancellationToken).ToArray();

        IEnumerable<CompletionMatch> others = (relativeFolder, isQuoted) switch
        {
            // a built-in command isn't found by cmd when it is quoted, but an executable is
            ("", false) => commands.Where(command => command.ListItemText.StartsWith(name, StringComparison.OrdinalIgnoreCase)).Select(command => command with { MatchedLength = name.Length })
                .Concat(path.Match(name, isQuoted)),
            ("", true) => path.Match(name, isQuoted),

            // what is in the working folder is found first, so the same name of a folder in PATH isn't listed again
            _ when !isFullPath => path.FindIn(relativeFolder, name, isQuoted, cancellationToken).ExceptBy(entries.Select(static entry => entry.Match.ListItemText), static match => match.ListItemText, StringComparer.OrdinalIgnoreCase),
            _ => [],
        };

        // all sorted by name together; the sort is stable, so of the same name, a folder comes before a file, a file before a command, and a command before an executable
        var matches = entries
            .OrderBy(static entry => entry.IsFolder ? 0 : 1)
            .Select(static entry => entry.Match)
            .Concat(others)
            .OrderBy(static match => match.ListItemText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matches is [])
        {
            return null;
        }

        return new(ReplacementIndex: start, ReplacementLength: GetWordLength(input, start), Matches: matches);
    }

    /// <summary>
    /// The commands listed in the <paramref name="output"/> of <c>help</c>, in lower case, each with its description,
    /// which goes on in the lines after it that are indented.
    /// </summary>
    public static IReadOnlyList<CompletionMatch> ParseHelp(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var commands = new List<(string Name, StringBuilder Description)>();
        foreach (var line in output.AsSpan().EnumerateLines())
        {
            if (line.IsWhiteSpace())
            {
                continue;
            }

            if (char.IsWhiteSpace(line[0]))
            {
                if (commands is [.., (_, var description)])
                {
                    description.Append(' ').Append(line.Trim());
                }

                continue;
            }

            if (HelpLine().Match(line.ToString()) is { Success: true } match)
            {
                commands.Add((match.Groups["name"].Value.ToLowerInvariant(), new(match.Groups["description"].Value.TrimEnd())));
            }
        }

        return commands
            .DistinctBy(static command => command.Name)
            .Select(static command => new CompletionMatch(CompletionText: command.Name, ListItemText: command.Name, ResultType: "Command", ToolTip: command.Description.ToString()))
            .OrderBy(static command => command.ListItemText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The folders in <paramref name="path"/>, those that are fully qualified once the environment variables in them are expanded,
    /// the extensions in <paramref name="pathExtensions"/>, or those cmd has by default without it, and the executables in the folders.
    /// </summary>
    public static PathExecutables FindExecutables(string? path, string? pathExtensions, CancellationToken cancellationToken)
    {
        var extensions = (pathExtensions ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static extension => extension.StartsWith('.'))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        // a folder in PATH may be quoted, and may refer to environment variables
        var folders = (path ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static entry => Environment.ExpandEnvironmentVariables(entry.Trim('"')))
            .Where(static folder => folder is not "" && Path.IsPathFullyQualified(folder))
            .ToArray();

        var executables = new PathExecutables(Folders: folders, Extensions: extensions, Executables: []);
        return executables with { Executables = executables.FindIn(relativeFolder: "", name: "", isQuoted: false, cancellationToken) };
    }

    // the folders and files in folder whose name starts with name, each put in as relativeFolder and its name, and taken by include
    internal static IEnumerable<(CompletionMatch Match, bool IsFolder)> Enumerate(string folder, string relativeFolder, string name, bool isQuoted, Func<FileSystemInfo, bool> include, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        IEnumerator<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Options).GetEnumerator();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // a folder that can't be read is left out
            yield break;
        }

        using (entries)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!entries.MoveNext())
                    {
                        yield break;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    yield break;
                }

                var entry = entries.Current;
                if (!entry.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) || !include(entry))
                {
                    continue;
                }

                var isFolder = entry.Attributes.HasFlag(FileAttributes.Directory);
                yield return (new(
                    CompletionText: Quote(relativeFolder + entry.Name, isQuoted),
                    ListItemText: entry.Name,
                    ResultType: isFolder ? "ProviderContainer" : "ProviderItem",
                    ToolTip: entry.FullName,
                    MatchedLength: name.Length), isFolder);
            }
        }
    }

    // the name of a command, then its description after at least two spaces, which the text around the list doesn't have
    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9]*)\s{2,}(?<description>\S.*)$")]
    private static partial Regex HelpLine();

    // none when help can't be run, which isn't tried again
    private static async Task<IReadOnlyList<CompletionMatch>> LoadCommandsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd", "/d /c \"chcp 65001 >nul & help\"")
            {
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return [];
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return ParseHelp(output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    // how long the word at start is, up to the space after it, or its closing quote when it is quoted
    private static int GetWordLength(string input, int start)
    {
        var word = input.AsSpan(start);
        if (word is ['"', .. var rest])
        {
            return rest.IndexOf('"') is >= 0 and var close ? close + 2 : word.Length;
        }

        return word.IndexOfAny(' ', '\t') is >= 0 and var end ? end : word.Length;
    }

    // quoted when it was started with a quote, or cmd would take it apart without one
    internal static string Quote(string name, bool isQuoted) => isQuoted || name.AsSpan().IndexOfAny(CharactersToQuote) >= 0 ? $"\"{name}\"" : name;
}

/// <summary>
/// The fully qualified <paramref name="Folders"/> of PATH, in order, the <paramref name="Extensions"/> of PATHEXT, with their dots,
/// and the <paramref name="Executables"/> in the folders, sorted by name, of which only the first of the same name is kept, as that is the one cmd runs.
/// </summary>
public sealed record PathExecutables(IReadOnlyList<string> Folders, FrozenSet<string> Extensions, IReadOnlyList<CompletionMatch> Executables)
{
    /// <summary>The <see cref="Executables"/> whose name starts with <paramref name="name"/>.</summary>
    public IEnumerable<CompletionMatch> Match(string name, bool isQuoted) => Executables
        .Where(executable => executable.ListItemText.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        .Select(executable => executable with { CompletionText = CmdCompleter.Quote(executable.ListItemText, isQuoted), MatchedLength = name.Length });

    /// <summary>
    /// The executables, and the folders when <paramref name="relativeFolder"/> isn't empty, whose name starts with <paramref name="name"/>,
    /// in <paramref name="relativeFolder"/> of each of the <see cref="Folders"/>, sorted by name, of which only the first of the same name is kept.
    /// </summary>
    public IReadOnlyList<CompletionMatch> FindIn(string relativeFolder, string name, bool isQuoted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relativeFolder);
        ArgumentNullException.ThrowIfNull(name);

        // the folders in the folders of PATH are only offered to go on into, as the folders in PATH themselves would be too many
        var includeFolders = relativeFolder is not "";
        var matches = new Dictionary<string, CompletionMatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders)
        {
            var entries = CmdCompleter.Enumerate(
                Path.Combine(folder, relativeFolder),
                relativeFolder,
                name,
                isQuoted,
                entry => entry.Attributes.HasFlag(FileAttributes.Directory) ? includeFolders : Extensions.Contains(entry.Extension),
                cancellationToken);
            foreach (var (match, isFolder) in entries)
            {
                matches.TryAdd(match.ListItemText, isFolder ? match : match with { ResultType = "Command" });
            }
        }

        return matches.Values
            .OrderBy(static match => match.ListItemText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
