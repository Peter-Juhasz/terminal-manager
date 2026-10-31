using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

// Parses by pwsh, so these only run where it is installed, on Windows, as the app does.
[TestClass]
[OSCondition(OperatingSystems.Windows)]
public sealed class PowerShellHighlighterTests(TestContext testContext)
{
    [TestMethod]
    public async Task HighlightAsyncColorsKeywordsCommentsStringsAndTypeNames()
    {
        using var highlighter = new PowerShellHighlighter();

        var code = await highlighter.HighlightAsync("foreach ($i in 1..3) { [int]$i; 'x' } # done", testContext.CancellationToken);

        Assert.IsNotNull(code);
        Assert.AreSequenceEqual(
            [
                ("foreach", SyntaxKind.Keyword),
                ("in", SyntaxKind.Keyword),
                ("int", SyntaxKind.TypeName),
                ("'x'", SyntaxKind.String),
                ("# done", SyntaxKind.Comment),
            ],
            Colored(code));
    }

    [TestMethod]
    public async Task HighlightAsyncLeavesCommandNamesAndArgumentsPlain()
    {
        using var highlighter = new PowerShellHighlighter();

        var code = await highlighter.HighlightAsync("Get-ChildItem -Path src -Recurse", testContext.CancellationToken);

        Assert.IsNotNull(code);
        Assert.IsEmpty(code.Spans);
    }

    [TestMethod]
    public async Task HighlightAsyncColorsExpandableStringsAroundWhatIsExpanded()
    {
        using var highlighter = new PowerShellHighlighter();

        var code = await highlighter.HighlightAsync("\"a $x $([string] 'b') é\"", testContext.CancellationToken);

        Assert.IsNotNull(code);
        Assert.AreSequenceEqual(
            [
                ("\"a ", SyntaxKind.String),
                (" ", SyntaxKind.String),
                ("string", SyntaxKind.TypeName),
                ("'b'", SyntaxKind.String),
                (" é\"", SyntaxKind.String),
            ],
            Colored(code));
    }

    [TestMethod]
    public async Task HighlightAsyncColorsIncompleteInput()
    {
        using var highlighter = new PowerShellHighlighter();

        var code = await highlighter.HighlightAsync("if ($x) { Write-Host \"unterminated", testContext.CancellationToken);

        Assert.IsNotNull(code);
        Assert.AreSequenceEqual([("if", SyntaxKind.Keyword), ("\"unterminated", SyntaxKind.String)], Colored(code));
    }

    [TestMethod]
    public async Task HighlightAsyncAnswersEachRequest()
    {
        using var highlighter = new PowerShellHighlighter();

        var first = await highlighter.HighlightAsync("# one", testContext.CancellationToken);
        var second = await highlighter.HighlightAsync("while ($true) {}", testContext.CancellationToken);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreSequenceEqual([("# one", SyntaxKind.Comment)], Colored(first));
        Assert.AreSequenceEqual([("while", SyntaxKind.Keyword)], Colored(second));
    }

    private static IEnumerable<(string Text, SyntaxKind Kind)> Colored(HighlightedCode code) => code.GetRuns()
        .Where(static run => run.Kind is not null)
        .Select(static run => (run.Text, run.Kind!.Value));
}
