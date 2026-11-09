using App.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class HighlightedCodeTests
{
    [TestMethod]
    public void GetRunsSplitsTextAroundSpans()
    {
        var code = HighlightedCode.Create("if ($x) { 'a' } # b", [
            new(Start: 0, Length: 2, Kind: SyntaxKind.Keyword),
            new(Start: 10, Length: 3, Kind: SyntaxKind.String),
            new(Start: 16, Length: 3, Kind: SyntaxKind.Comment),
        ]);

        Assert.AreSequenceEqual(
            [
                new HighlightedRun(Text: "if", Kind: SyntaxKind.Keyword),
                new HighlightedRun(Text: " ($x) { ", Kind: null),
                new HighlightedRun(Text: "'a'", Kind: SyntaxKind.String),
                new HighlightedRun(Text: " } ", Kind: null),
                new HighlightedRun(Text: "# b", Kind: SyntaxKind.Comment),
            ],
            code.GetRuns());
    }

    [TestMethod]
    public void GetRunsOfPlainTextIsWholeText()
    {
        Assert.AreSequenceEqual([new HighlightedRun(Text: "dir", Kind: null)], HighlightedCode.Plain("dir").GetRuns());
        Assert.IsEmpty(HighlightedCode.Plain(string.Empty).GetRuns());
    }

    [TestMethod]
    public void CreateOrdersSpansAndDropsThoseOutOfTextOrOverlapping()
    {
        var code = HighlightedCode.Create("abcdef", [
            new(Start: 4, Length: 2, Kind: SyntaxKind.String),
            new(Start: 0, Length: 3, Kind: SyntaxKind.Keyword),
            new(Start: 2, Length: 1, Kind: SyntaxKind.Comment),
            new(Start: 5, Length: 9, Kind: SyntaxKind.Comment),
            new(Start: -1, Length: 1, Kind: SyntaxKind.Comment),
            new(Start: 3, Length: 0, Kind: SyntaxKind.Comment),
        ]);

        Assert.AreSequenceEqual(
            [new SyntaxSpan(Start: 0, Length: 3, Kind: SyntaxKind.Keyword), new SyntaxSpan(Start: 4, Length: 2, Kind: SyntaxKind.String)],
            code.Spans);
    }
}
