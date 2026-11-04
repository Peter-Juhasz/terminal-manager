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
    public void ParsesCommandsOfHelp()
    {
        Assert.AreSequenceEqual(["break", "cd", "convert"], Commands.Select(static command => command.CompletionText));
        Assert.AreEqual("Converts FAT volumes to NTFS.  You cannot convert the current drive.", Commands[2].ToolTip);
        Assert.IsTrue(Commands.All(static command => command.ResultType is "Command"));
    }

    [TestMethod]
    public void CompletesFoldersFilesAndCommandsSortedByName()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, "b", cursor: 1, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreEqual(0, completion.ReplacementIndex);
        Assert.AreEqual(1, completion.ReplacementLength);
        Assert.AreSequenceEqual(["break", "build", "build.cmd"], completion.Matches.Select(static match => match.CompletionText));
        Assert.AreSequenceEqual(["Command", "ProviderContainer", "ProviderItem"], completion.Matches.Select(static match => match.ResultType));
        Assert.IsTrue(completion.Matches.All(static match => match.MatchedLength is 1));
    }

    [TestMethod]
    public void ReplacesTheWholeFirstWord()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, "  rexx arg", cursor: 4, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreEqual(2, completion.ReplacementIndex);
        Assert.AreEqual(4, completion.ReplacementLength);
        Assert.AreEqual("  Readme.md arg", completion.Apply("  rexx arg", match: 0));
    }

    [TestMethod]
    public void QuotesNamesWithSpaces()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, "my", cursor: 2, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreEqual("\"My Folder\"", completion.Matches.Single().CompletionText);
    }

    [TestMethod]
    public void CompletesAfterAnOpeningQuoteWithoutCommands()
    {
        var completion = CmdCompleter.Complete(_folder, Commands, "\"b", cursor: 2, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreSequenceEqual(["\"build\"", "\"build.cmd\""], completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    public void CompletesCommandsWhenTheWorkingFolderIsMissing()
    {
        var completion = CmdCompleter.Complete(Path.Combine(_folder, "missing"), Commands, "c", cursor: 1, CancellationToken.None);

        Assert.IsNotNull(completion);
        Assert.AreSequenceEqual(["cd", "convert"], completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    [DataRow("build ", 6)]
    [DataRow("build b", 7)]
    [DataRow("src\\b", 5)]
    [DataRow("zzz", 3)]
    public void DoesNotCompleteOtherThanTheFirstWordInTheWorkingFolder(string input, int cursor)
    {
        Assert.IsNull(CmdCompleter.Complete(_folder, Commands, input, cursor, CancellationToken.None));
    }
}
