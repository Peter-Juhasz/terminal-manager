using System.Text.RegularExpressions;
using App.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed partial class TextDiffTests(TestContext testContext)
{
    [TestMethod]
    public void SplitLinesIgnoresLineBreakAtEnd()
    {
        Assert.AreSequenceEqual(new[] { "a", "b" }, TextDiff.SplitLines("a\r\nb\n"));
        Assert.AreSequenceEqual(new[] { "a", "", "b" }, TextDiff.SplitLines("a\n\nb"));
        Assert.IsEmpty(TextDiff.SplitLines(string.Empty));
    }

    [TestMethod]
    public void SameLinesAreUnchanged()
    {
        var result = Compare(["a", "b"], ["a", "b"]);

        Assert.AreSequenceEqual(new[] { "= a|a", "= b|b" }, Describe(result));
        Assert.IsFalse(result.HasChanges);
        Assert.IsFalse(result.IsApproximate);
    }

    [TestMethod]
    public void EmptySidesAreAllInsertedOrDeleted()
    {
        Assert.AreSequenceEqual(new[] { "+ |a", "+ |b" }, Describe(Compare([], ["a", "b"])));
        Assert.AreSequenceEqual(new[] { "- a|" }, Describe(Compare(["a"], [])));
        Assert.IsEmpty(Compare([], []).Rows);
    }

    [TestMethod]
    public void InsertedAndDeletedLinesKeepTheRestAligned()
    {
        var result = Compare(["a", "b", "c", "d"], ["a", "x", "b", "d"]);

        Assert.AreSequenceEqual(new[] { "= a|a", "+ |x", "= b|b", "- c|", "= d|d" }, Describe(result));
    }

    [TestMethod]
    public void LinesAreNumberedInTheirSide()
    {
        var rows = Compare(["a", "b"], ["x", "a", "b"]).Rows;

        Assert.IsNull(rows[0].Left);
        Assert.AreEqual(1, rows[0].Right!.Number);
        Assert.AreEqual(1, rows[1].Left!.Number);
        Assert.AreEqual(2, rows[1].Right!.Number);
        Assert.AreEqual(2, rows[2].Left!.Number);
        Assert.AreEqual(3, rows[2].Right!.Number);
    }

    [TestMethod]
    public void ReplacedLinesArePairedAsModifiedInOrder()
    {
        var result = Compare(["a", "b", "z"], ["a", "c", "d", "z"]);

        Assert.AreSequenceEqual(new[] { "= a|a", "~ b|c", "+ |d", "= z|z" }, Describe(result));
    }

    [TestMethod]
    public void ModifiedLineHasChangedWords()
    {
        var row = Compare(["Build succeeded in 12 ms"], ["Build failed in 12 ms"]).Rows.Single();

        Assert.AreEqual(DiffKind.Modified, row.Kind);
        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 6, Length: 9) }, row.Left!.Changes);
        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 6, Length: 6) }, row.Right!.Changes);
    }

    [TestMethod]
    public void AdjacentChangedTokensAreOneChange()
    {
        var row = Compare(["a b c"], ["a x.y c"]).Rows.Single();

        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 2, Length: 1) }, row.Left!.Changes);
        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 2, Length: 3) }, row.Right!.Changes);
    }

    [TestMethod]
    public void ModifiedLineWithNothingInCommonIsChangedWhole()
    {
        var row = Compare(["abc"], ["xyz"]).Rows.Single();

        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 0, Length: 3) }, row.Left!.Changes);
        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 0, Length: 3) }, row.Right!.Changes);
    }

    [TestMethod]
    public void CaseIsIgnoredOnlyIfAsked()
    {
        Assert.IsTrue(Compare(["Error"], ["ERROR"]).HasChanges);
        Assert.IsFalse(Compare(["Error"], ["ERROR"], new TextDiffOptions { IgnoreCase = true }).HasChanges);
    }

    [TestMethod]
    public void WhitespaceIsIgnoredOnlyIfAsked()
    {
        Assert.IsTrue(Compare(["a  b "], [" a b"]).HasChanges);
        Assert.IsFalse(Compare(["a  b "], [" a b"], new TextDiffOptions { IgnoreWhitespace = true }).HasChanges);
    }

    [TestMethod]
    public void WhitespaceIsNotAChangeOfModifiedLineIfIgnored()
    {
        var row = Compare(["a  b c"], ["a b d"], new TextDiffOptions { IgnoreWhitespace = true }).Rows.Single();

        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 5, Length: 1) }, row.Left!.Changes);
        Assert.AreSequenceEqual(new[] { new DiffSpan(Start: 4, Length: 1) }, row.Right!.Changes);
    }

    [TestMethod]
    public void LinesDifferingOnlyInIgnoredPatternsAreUnchanged()
    {
        var options = new TextDiffOptions { IgnoredPatterns = [Duration()] };

        var result = Compare(["Done in 12 ms", "ok"], ["Done in 345 ms", "ok"], options);

        Assert.AreSequenceEqual(new[] { "= Done in 12 ms|Done in 345 ms", "= ok|ok" }, Describe(result));
    }

    [TestMethod]
    public void TooManyChangesAreApproximatedAsReplacedWhole()
    {
        var result = Compare(["s", "a", "b", "c", "e"], ["s", "x", "a", "y", "e"], new TextDiffOptions { MaxEditDistance = 2 });

        Assert.IsTrue(result.IsApproximate);
        Assert.AreSequenceEqual(new[] { "= s|s", "~ a|x", "~ b|a", "~ c|y", "= e|e" }, Describe(result));
    }

    [TestMethod]
    public void CancelledDiffThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => TextDiff.Compare(["a"], ["b"], cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public void RandomDiffsHaveFewestChangesAndRebuildBothSides()
    {
        var random = new Random(Seed: 42);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var left = RandomLines(random);
            var right = RandomLines(random);

            var rows = Compare(left, right).Rows;

            Assert.AreSequenceEqual(left, rows.Where(static row => row.Left is not null).Select(static row => row.Left!.Text).ToArray());
            Assert.AreSequenceEqual(right, rows.Where(static row => row.Right is not null).Select(static row => row.Right!.Text).ToArray());

            // a modified row is a line deleted and another inserted
            var changes = rows.Sum(static row => row.Kind switch
            {
                DiffKind.Unchanged => 0,
                DiffKind.Modified => 2,
                _ => 1,
            });
            Assert.AreEqual(left.Length + right.Length - 2 * LongestCommonSubsequence(left, right), changes);
        }
    }

    private TextDiffResult Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, TextDiffOptions? options = null) =>
        TextDiff.Compare(left, right, options, testContext.CancellationToken);

    private static string[] RandomLines(Random random) =>
        Enumerable.Range(0, random.Next(12)).Select(_ => ((char)('a' + random.Next(4))).ToString()).ToArray();

    private static int LongestCommonSubsequence(string[] a, string[] b)
    {
        var lengths = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                lengths[i, j] = a[i - 1] == b[j - 1]
                    ? lengths[i - 1, j - 1] + 1
                    : Math.Max(lengths[i - 1, j], lengths[i, j - 1]);
            }
        }

        return lengths[a.Length, b.Length];
    }

    private static string[] Describe(TextDiffResult result) => result.Rows
        .Select(static row => $"{row.Kind switch
        {
            DiffKind.Unchanged => '=',
            DiffKind.Deleted => '-',
            DiffKind.Inserted => '+',
            _ => '~',
        }} {row.Left?.Text}|{row.Right?.Text}")
        .ToArray();

    [GeneratedRegex(@"\d+ ms")]
    private static partial Regex Duration();
}
