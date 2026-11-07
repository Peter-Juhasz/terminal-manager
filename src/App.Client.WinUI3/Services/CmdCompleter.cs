using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Completes the first word of a command of the Command Prompt, the command, to the name of a folder or a file in the working folder,
/// or a command <c>help</c> lists; what comes after it isn't completed.
/// </summary>
public sealed partial class CmdCompleter : ICommandCompleter
{
    // the characters cmd takes a name apart at, or reads as something else, unless it is quoted
    private static readonly char[] CharactersToQuote = [' ', '&', '(', ')', '[', ']', '{', '}', '^', '=', ';', '!', '\'', '+', ',', '`', '~'];

    private static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true };

    // the commands help lists, which are the same everywhere, so they are only asked for once
    private readonly Lazy<Task<IReadOnlyList<CompletionMatch>>> _commands = new(static () => LoadCommandsAsync(CancellationToken.None));

    // the executables of the folders in PATH, by the PATH and PATHEXT they were looked for with, as projects can set their own;
    // each is only looked for once, as there are many of them
    private readonly ConcurrentDictionary<(string? Path, string? PathExtensions), Lazy<Task<IReadOnlyList<CompletionMatch>>>> _executables = [];

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

    private Task<IReadOnlyList<CompletionMatch>> GetExecutablesAsync(IReadOnlyDictionary<string, string> environmentVariables)
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
    /// Completes the first word of <paramref name="input"/>, when <paramref name="cursor"/> is in it,
    /// to the folders and files of <paramref name="workingFolder"/>, the <paramref name="commands"/>, and the <paramref name="executables"/>, it starts, sorted by name.
    /// </summary>
    public static CommandCompletion? Complete(string workingFolder, IReadOnlyList<CompletionMatch> commands, IReadOnlyList<CompletionMatch> executables, string input, int cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(executables);
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
        var name = isQuoted ? typed[1..] : typed;
        if (isQuoted ? name.Contains('"') : name.ContainsAny(' ', '\t'))
        {
            return null;
        }

        // only the names in the working folder itself are offered
        if (name.ContainsAny('\\', '/', ':') || name.ContainsAny('*', '?'))
        {
            return null;
        }

        var folders = new List<CompletionMatch>();
        var files = new List<CompletionMatch>();
        if (Directory.Exists(workingFolder))
        {
            foreach (var entry in new DirectoryInfo(workingFolder).EnumerateFileSystemInfos("*", Options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entry.Name.AsSpan().StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isFolder = entry.Attributes.HasFlag(FileAttributes.Directory);
                var match = new CompletionMatch(
                    CompletionText: Quote(entry.Name, isQuoted),
                    ListItemText: entry.Name,
                    ResultType: isFolder ? "ProviderContainer" : "ProviderItem",
                    ToolTip: entry.FullName,
                    MatchedLength: name.Length);
                (isFolder ? folders : files).Add(match);
            }
        }

        // a built-in command isn't found by cmd when it is quoted
        var nameText = name.ToString();
        var matchingCommands = isQuoted
            ? Enumerable.Empty<CompletionMatch>()
            : commands.Where(command => command.ListItemText.StartsWith(nameText, StringComparison.OrdinalIgnoreCase)).Select(command => command with { MatchedLength = nameText.Length });

        // an executable is found by cmd quoted as well
        var matchingExecutables = executables
            .Where(executable => executable.ListItemText.StartsWith(nameText, StringComparison.OrdinalIgnoreCase))
            .Select(executable => executable with { CompletionText = Quote(executable.ListItemText, isQuoted), MatchedLength = nameText.Length });

        // all sorted by name together; the sort is stable, so of the same name, a folder comes before a file, a file before a command, and a command before an executable
        var matches = folders
            .Concat(files)
            .Concat(matchingCommands)
            .Concat(matchingExecutables)
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
    /// The files of the folders in <paramref name="path"/> whose extension is one of <paramref name="pathExtensions"/>, sorted by name;
    /// of the same name, only the first one is kept, as that is the one cmd runs.
    /// </summary>
    public static IReadOnlyList<CompletionMatch> FindExecutables(string? path, string? pathExtensions, CancellationToken cancellationToken)
    {
        var extensions = (pathExtensions ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static extension => extension.StartsWith('.'))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        if (path is null || extensions.Count is 0)
        {
            return [];
        }

        var executables = new Dictionary<string, CompletionMatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // a folder in PATH may be quoted, and may refer to environment variables
            var folder = Environment.ExpandEnvironmentVariables(entry.Trim('"'));
            if (folder is "" || !Path.IsPathFullyQualified(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", Options))
                {
                    if (!extensions.Contains(file.Extension))
                    {
                        continue;
                    }

                    executables.TryAdd(file.Name, new(CompletionText: file.Name, ListItemText: file.Name, ResultType: "Command", ToolTip: file.FullName));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // a folder that can't be read is left out
            }
        }

        return executables.Values
            .OrderBy(static executable => executable.ListItemText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
    private static string Quote(string name, bool isQuoted) => isQuoted || name.AsSpan().IndexOfAny(CharactersToQuote) >= 0 ? $"\"{name}\"" : name;
}
