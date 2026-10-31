using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

// Completes by pwsh, so these only run where it is installed, on Windows, as the app does.
[TestClass]
[OSCondition(OperatingSystems.Windows)]
public sealed class PowerShellCompleterTests(TestContext testContext)
{
    [TestMethod]
    public async Task CompleteAsyncCompletesCommands()
    {
        using var completer = new PowerShellCompleter();

        var completion = await completer.CompleteAsync(Environment.CurrentDirectory, "Get-ChildIt", 11, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.AreEqual(0, completion.ReplacementIndex);
        Assert.AreEqual(11, completion.ReplacementLength);
        Assert.Contains("Get-ChildItem", completion.Matches);
    }

    [TestMethod]
    public async Task CompleteAsyncCompletesPathsInWorkingFolder()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "alpha one.txt"), string.Empty);
            File.WriteAllText(Path.Combine(folder.FullName, "alpine.txt"), string.Empty);
            using var completer = new PowerShellCompleter();

            var completion = await completer.CompleteAsync(folder.FullName, "dir al", 6, testContext.CancellationToken);

            Assert.IsNotNull(completion);
            Assert.AreEqual(4, completion.ReplacementIndex);
            Assert.AreSequenceEqual(["'.\\alpha one.txt'", ".\\alpine.txt"], completion.Matches);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task CompleteAsyncCompletesBeforeCursorOfLaterLine()
    {
        using var completer = new PowerShellCompleter();

        var completion = await completer.CompleteAsync(Environment.CurrentDirectory, "$x = 1\rWrite-Hos 'é'", 16, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.AreEqual(7, completion.ReplacementIndex);
        Assert.Contains("Write-Host", completion.Matches);
    }

    [TestMethod]
    public async Task CompleteAsyncAnswersEachRequest()
    {
        using var completer = new PowerShellCompleter();

        var first = await completer.CompleteAsync(Environment.CurrentDirectory, "Get-Dat", 7, testContext.CancellationToken);
        var second = await completer.CompleteAsync(Environment.CurrentDirectory, "Get-Locatio", 11, testContext.CancellationToken);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.Contains("Get-Date", first.Matches);
        Assert.Contains("Get-Location", second.Matches);
    }

    [TestMethod]
    public async Task CompleteAsyncReturnsNoMatchesForNothingToComplete()
    {
        using var completer = new PowerShellCompleter();

        var completion = await completer.CompleteAsync(Environment.CurrentDirectory, "Zzzzqqq-Nothing", 15, testContext.CancellationToken);

        Assert.IsNotNull(completion);
        Assert.IsEmpty(completion.Matches);
    }

    [TestMethod]
    public void ApplyReplacesCompletedText()
    {
        var completion = new CommandCompletion(ReplacementIndex: 4, ReplacementLength: 2, Matches: ["alpha", "alpine"]);

        Assert.AreEqual("dir alpine -Force", completion.Apply("dir al -Force", 1));
        Assert.AreEqual(10, completion.GetCaret(1));
    }
}
