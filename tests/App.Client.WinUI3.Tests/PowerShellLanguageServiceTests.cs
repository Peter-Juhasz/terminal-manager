using App.Extensions.PowerShell;
using App.Services;

namespace App.Client.WinUI3.Tests;

// Completes by pwsh, so these only run where it is installed, on Windows, as the app does.
[TestClass]
[OSCondition(OperatingSystems.Windows)]
public sealed class PowerShellLanguageServiceTests(TestContext testContext)
{
    [TestMethod]
    public async Task CompleteAsyncCompletesCommands()
    {
        using var service = new PowerShellLanguageService();

        var completion = await service.CompleteAsync(Environment.CurrentDirectory, "Get-ChildIt", 11, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.AreEqual(0, completion.ReplacementIndex);
        Assert.AreEqual(11, completion.ReplacementLength);
        Assert.Contains("Get-ChildItem", completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    public async Task CompleteAsyncCompletesWhatPrefixDefines()
    {
        using var service = new PowerShellLanguageService();

        var completion = await service.CompleteAsync(Environment.CurrentDirectory, "$projectRoot = 'C:\\'\n", "$projectR", 9, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.AreEqual(0, completion.ReplacementIndex);
        Assert.AreEqual(9, completion.ReplacementLength);
        Assert.Contains("$projectRoot", completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    public async Task CompleteAsyncCompletesPathsInWorkingFolder()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "alpha one.txt"), string.Empty);
            File.WriteAllText(Path.Combine(folder.FullName, "alpine.txt"), string.Empty);
            using var service = new PowerShellLanguageService();

            var completion = await service.CompleteAsync(folder.FullName, "dir al", 6, testContext.CancellationToken);

            Assert.IsNotNull(completion);
            Assert.AreEqual(4, completion.ReplacementIndex);
            Assert.AreSequenceEqual(["'.\\alpha one.txt'", ".\\alpine.txt"], completion.Matches.Select(static match => match.CompletionText));
            Assert.AreSequenceEqual(["alpha one.txt", "alpine.txt"], completion.Matches.Select(static match => match.ListItemText));
            Assert.AreSequenceEqual(["ProviderItem", "ProviderItem"], completion.Matches.Select(static match => match.ResultType));
            Assert.AreEqual(Path.Combine(folder.FullName, "alpine.txt"), completion.Matches[1].ToolTip);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task CompleteAsyncCompletesBeforeCursorOfLaterLine()
    {
        using var service = new PowerShellLanguageService();

        var completion = await service.CompleteAsync(Environment.CurrentDirectory, "$x = 1\rWrite-Hos 'é'", 16, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.AreEqual(7, completion.ReplacementIndex);
        Assert.Contains("Write-Host", completion.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    public async Task CompleteAsyncAnswersEachRequest()
    {
        using var service = new PowerShellLanguageService();

        var first = await service.CompleteAsync(Environment.CurrentDirectory, "Get-Dat", 7, testContext.CancellationToken);
        var second = await service.CompleteAsync(Environment.CurrentDirectory, "Get-Locatio", 11, testContext.CancellationToken);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.Contains("Get-Date", first.Matches.Select(static match => match.CompletionText));
        Assert.Contains("Get-Location", second.Matches.Select(static match => match.CompletionText));
    }

    [TestMethod]
    public async Task CompleteAsyncReturnsNoMatchesForNothingToComplete()
    {
        using var service = new PowerShellLanguageService();

        var completion = await service.CompleteAsync(Environment.CurrentDirectory, "Zzzzqqq-Nothing", 15, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.IsEmpty(completion.Matches);
    }

    [TestMethod]
    public void ApplyReplacesCompletedText()
    {
        var completion = new CommandCompletion(ReplacementIndex: 4, ReplacementLength: 2, Matches: [Match("alpha"), Match("alpine")]);

        Assert.AreEqual("dir alpine -Force", completion.Apply("dir al -Force", 1));
        Assert.AreEqual(10, completion.GetCaret(1));
    }

    [TestMethod]
    [DataRow("Get-ChildItem", "get-chi", 7)]
    [DataRow("Recurse", "-Rec", 3)]
    [DataRow("HOME", "$ho", 2)]
    [DataRow("Path", "$env:Pa", 2)]
    [DataRow("src", ".\\sr", 2)]
    [DataRow("alpha one.txt", "'.\\alpha o", 7)]
    [DataRow("ChildItem", "Get-*Item", 0)]
    [DataRow("Get-ChildItem", "Get-*Item", 4)]
    [DataRow("Get-ChildItem", "ChildItem", 0)]
    [DataRow("Get-Date", "", 0)]
    public void GetMatchedLengthIsWhatWasTypedOfListItemText(string listItemText, string typed, int expected) =>
        Assert.AreEqual(expected, CompletionMatch.GetMatchedLength(listItemText, typed));

    [TestMethod]
    public async Task CompleteAsyncSetsMatchedLengths()
    {
        using var service = new PowerShellLanguageService();

        var completion = await service.CompleteAsync(Environment.CurrentDirectory, "Get-ChildItem -Rec", 18, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.AreEqual(3, completion.Matches.Single(static match => match.ListItemText == "Recurse").MatchedLength);
    }

    [TestMethod]
    [DataRow("Get-ChildItem -Path .", 3, 0, 13, "Get-ChildItem [[-Path]")]
    [DataRow("Get-ChildItem -Path .", 16, 14, 5, "[string[]] Path")]
    [DataRow("gci -Recurse", 1, 0, 3, "Alias of Get-ChildItem")]
    [DataRow("[System.IO.File]::Exists('a')", 20, 18, 6, "static bool Exists(string path)")]
    [DataRow("[System.IO.File]::Exists('a')", 5, 1, 14, "class System.IO.File")]
    [DataRow("$x = Get-Date; $x.DayOfWeek", 20, 18, 9, "System.DayOfWeek DayOfWeek { get; }")]
    public async Task GetQuickInfoAsyncTellsWhatIsAtPosition(string input, int position, int start, int length, string text)
    {
        using var service = new PowerShellLanguageService();

        var info = await service.GetQuickInfoAsync(Environment.CurrentDirectory, prefix: string.Empty, input, position, testContext.CancellationToken);

        Assert.IsNotNull(info);
        Assert.AreEqual(start, info.Start);
        Assert.AreEqual(length, info.Length);
        Assert.StartsWith(text, info.Text);
    }

    [TestMethod]
    public async Task GetQuickInfoAsyncTellsWhatPrefixDefines()
    {
        using var service = new PowerShellLanguageService();

        var info = await service.GetQuickInfoAsync(Environment.CurrentDirectory, "$projectRoot = [System.IO.DirectoryInfo]'C:\\'\n", "$projectRoot.FullName", 15, testContext.CancellationToken);

        Assert.IsNotNull(info);
        Assert.AreEqual(13, info.Start);
        Assert.AreEqual("string FullName { get; }", info.Text);
    }

    [TestMethod]
    [DataRow("Get-ChildItem -Path .", 20)]
    [DataRow("Get-ChildItem -Path .", 13)]
    [DataRow("'just a string'", 3)]
    public async Task GetQuickInfoAsyncReturnsNullForNothingToTell(string input, int position)
    {
        using var service = new PowerShellLanguageService();

        var info = await service.GetQuickInfoAsync(Environment.CurrentDirectory, prefix: string.Empty, input, position, testContext.CancellationToken);

        Assert.IsNull(info);
    }

    private static CompletionMatch Match(string text) => new(CompletionText: text, ListItemText: text, ResultType: "ProviderItem", ToolTip: null);
}
