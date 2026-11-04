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

    /// <summary>Starts asking <c>help</c> for the commands, so the first completion doesn't wait for it.</summary>
    public void Prepare(string workingFolder) => _ = _commands.Value;

    /// <remarks><paramref name="prefix"/> is ignored, as what it defines isn't a file or a folder.</remarks>
    public async Task<CommandCompletion?> CompleteAsync(string workingFolder, string prefix, string input, int cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(input);

        var commands = await _commands.Value.WaitAsync(cancellationToken);
        return await Task.Run(() => Complete(workingFolder, commands, input, cursor, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Completes the first word of <paramref name="input"/>, when <paramref name="cursor"/> is in it,
    /// to the folders and files of <paramref name="workingFolder"/>, and the <paramref name="commands"/>, it starts, sorted by name.
    /// </summary>
    public static CommandCompletion? Complete(string workingFolder, IReadOnlyList<CompletionMatch> commands, string input, int cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(commands);
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

        // all sorted by name together; the sort is stable, so of the same name, a folder comes before a file, and a file before a command
        var matches = folders
            .Concat(files)
            .Concat(matchingCommands)
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
