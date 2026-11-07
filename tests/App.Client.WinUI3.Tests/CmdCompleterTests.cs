using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class CmdCompleterTests
{
    private static readonly IReadOnlyList<CompletionMatch> Commands = CmdCompleter.ParseHelp("""
        For more information on a specific command, type HELP command-name
        BREAK          Sets or clears extended CTRL+C checking.
        CD             Displays the name of or changes the current directory.
        CONVERT        Converts FAT volumes to NTFS.  You cannot convert the
                       current drive.

        For more information on tools see the command-line reference in the online help.
        """);

    private static readonly IReadOnlyList<CompletionMatch> Executables =
    [
        new(CompletionText: "bash.exe", ListItemText: "bash.exe", ResultType: "Command", ToolTip: @"C:\bin\bash.exe"),
        new(CompletionText: "code.cmd", ListItemText: "code.cmd", ResultType: "Command", ToolTip: @"C:\bin\code.cmd"),
    ];

    private string _folder = null!;

    [TestInitialize]
    public void CreateFolder()
    {
        _folder = Directory.CreateTempSubdirectory("cmd-completer-").FullName;
        Directory.CreateDirectory(Path.Combine(_folder, "build"));
        Directory.CreateDirectory(Path.Combine(_folder, "My Folder"));
        File.WriteAllText(Path.Combine(_folder, "build.cmd"), "");
        File.WriteAllText(Path.Combine(_folder, "Readme.md"), "");
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void FindsExecutablesOfPathWithTheFirstOfTheSameName()
    {
        var first = Directory.CreateDirectory(Path.Combine(_folder, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_folder, "second")).FullName;
        File.WriteAllText(Path.Combine(first, "tool.exe"), "");
        File.WriteAllText(Path.Combine(first, "notes.txt"), "");
        File.WriteAllText(Path.Combine(second, "TOOL.EXE"), "");
        File.WriteAllText(Path.Combine(second, "run.Cmd"), "");

        var executables = CmdCompleter.FindExecutables($"{first};\"{second}\";;{Path.Combine(_folder, "missing")};relative", ".EXE;.CMD", CancellationToken.None);

        Assert.AreSequenceEqual(["run.Cmd", "tool.exe"], executables.Select(static executable => executable.CompletionText));
        Assert.AreEqual(Path.Combine(first, "tool.exe"), executables[1].ToolTip);
    }

    [TestMethod]
    public async Task CompletesExecutablesOfThePathAndExtensionsTheEnvironmentVariablesSet()
    {
        var tools = Directory.CreateDirectory(Path.Combine(_folder, "tools")).FullName;
        File.WriteAllText(Path.Combine(tools, "zzztool.exe"), "");
        File.WriteAllText(Path.Combine(tools, "zzztool.ps1"), "");
        var completer = new CmdCompleter();

        var completion = await completer.CompleteAsync(_folder, new Dictionary<string, string> { ["path"] = tools, ["PathExt"] = ".PS1" }, "zzz", cursor: 3, CancellationToken.None);
        var withoutVariables = await completer.CompleteAsync(_folder, prefix: "", "zzz", cursor: 3, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreSequenceEqual(["zzztool.ps1"], completion.Matches.Select(static match => match.CompletionText));
        Assert.IsNull(withoutVariables);
    }

    [TestMethod]
    public void CompletesExecutablesAlsoAfterAnOpeningQuote()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, Executables, "\"co", cursor: 3, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreSequenceEqual(["\"code.cmd\""], completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    public void ParsesCommandsOfHelp()
    {
        Assert.AreSequenceEqual(["break", "cd", "convert"], Commands.Select(static command => command.CompletionText));
        Assert.AreEqual("Converts FAT volumes to NTFS.  You cannot convert the current drive.", Commands[2].ToolTip);
        Assert.IsTrue(Commands.All(static command => command.ResultType is "Command"));
    }

    [TestMethod]
    public void CompletesFoldersFilesAndCommandsSortedByName()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, Executables, "b", cursor: 1, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreEqual(0, completion.ReplacementIndex);
        Assert.AreEqual(1, completion.ReplacementLength);
        Assert.AreSequenceEqual(["bash.exe", "break", "build", "build.cmd"], completion.Matches.Select(static match => match.CompletionText));
        Assert.AreSequenceEqual(["Command", "Command", "ProviderContainer", "ProviderItem"], completion.Matches.Select(static match => match.ResultType));
        Assert.IsTrue(completion.Matches.All(static match => match.MatchedLength is 1));
    }

    [TestMethod]
    public void ReplacesTheWholeFirstWord()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, Executables, "  rexx arg", cursor: 4, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreEqual(2, completion.ReplacementIndex);
        Assert.AreEqual(4, completion.ReplacementLength);
        Assert.AreEqual("  Readme.md arg", completion.Apply("  rexx arg", match: 0));
    }

    [TestMethod]
    public void QuotesNamesWithSpaces()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, Executables, "my", cursor: 2, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreEqual("\"My Folder\"", completion.Matches.Single().CompletionText);
    }

    [TestMethod]
    public void CompletesAfterAnOpeningQuoteWithoutCommands()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, Executables, "\"b", cursor: 2, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreSequenceEqual(["\"bash.exe\"", "\"build\"", "\"build.cmd\""], completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    public void CompletesCommandsWhenTheWorkingFolderIsMissing()
    {
        var completion = CmdCompleter.Complete(Path.Combine(_folder, "missing"), Commands, Executables, "c", cursor: 1, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreSequenceEqual(["cd", "code.cmd", "convert"], completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    [DataRow("build ", 6)]
    [DataRow("build b", 7)]
    [DataRow("src\\b", 5)]
    [DataRow("zzz", 3)]
    public void DoesNotCompleteOtherThanTheFirstWordInTheWorkingFolder(string input, int cursor)
    {
        Assert.IsNull(CmdCompleter.Complete(_folder, Commands, Executables, input, cursor, CancellationToken.None));
    }
}
