using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class OutputLineFilterTests
{
    private const OutputStream Out = OutputStream.StandardOutput;
    private const OutputStream Error = OutputStream.StandardError;

    private static OutputLineFilter Filter(string query, bool isCaseSensitive = false, bool isRegex = false) =>
        new(TextSearch.Create(query, isCaseSensitive, isRegex)!);

    [TestMethod]
    public void KeepsOnlyTheLinesTheQueryIsFoundIn()
    {
        var filter = Filter("b");
        filter.Update([new(Out, "a\r\nb1\r\nc\r\nb2\r\n")]);

        Assert.AreEqual("b1\r\nb2\r\n", filter.Output.ToString());
    }

    [TestMethod]
    public void FindsTheQueryInTheFirstAndLastLines()
    {
        var filter = Filter("x");
        filter.Update([new(Out, "x1\nmid\nx2\n")]);

        Assert.AreEqual("x1\nx2\n", filter.Output.ToString());
    }

    [TestMethod]
    public void JoinsALineWrittenInMoreThanOneChunk()
    {
        var filter = Filter("needle");
        filter.Update([new(Out, "a\nhay nee"), new(Out, "dle hay\nb\n")]);

        Assert.AreEqual("hay needle hay\n", filter.Output.ToString());
    }

    [TestMethod]
    public void KeepsTheStreamOfEachPartOfALine()
    {
        var filter = Filter("ab");
        filter.Update([new(Out, "a"), new(Error, "b\n")]);

        Assert.AreSequenceEqual(new OutputChunk[] { new(Out, "a"), new(Error, "b\n") }, filter.Output.Chunks);
    }

    [TestMethod]
    public void JoinsTheLinesFoundInTheSameStream()
    {
        var filter = Filter("x");
        filter.Update([new(Out, "x1\n"), new(Out, "x2\n"), new(Error, "x3\n")]);

        Assert.AreSequenceEqual(new OutputChunk[] { new(Out, "x1\nx2\n"), new(Error, "x3\n") }, filter.Output.Chunks);
    }

    [TestMethod]
    public void SearchesTheLastLineOnlyOnceTheOutputIsComplete()
    {
        var filter = Filter("x");
        filter.Update([new(Out, "a\nx")]);
        Assert.IsTrue(filter.Output.IsEmpty);

        filter.Complete();
        Assert.AreEqual("x", filter.Output.ToString());
    }

    [TestMethod]
    public void SearchesOnlyTheChunksAppendedSinceTheLastUpdate()
    {
        var filter = Filter("x");
        List<OutputChunk> chunks = [new(Out, "x1\n")];
        filter.Update(chunks);
        chunks.Add(new(Out, "y\nx2\n"));
        filter.Update(chunks);

        Assert.AreEqual("x1\nx2\n", filter.Output.ToString());
    }

    [TestMethod]
    public void DoesNotMatchTheLineBreak()
    {
        var filter = Filter(@"a$", isRegex: true);
        filter.Update([new(Out, "ba\r\nab\r\n")]);

        Assert.AreEqual("ba\r\n", filter.Output.ToString());
    }

    [TestMethod]
    public void MatchesARegexInEachLineByItself()
    {
        var filter = Filter(@"^\d+$", isRegex: true);
        filter.Update([new(Out, "12\nab\n3"), new(Out, "4\n")]);

        Assert.AreEqual("12\n34\n", filter.Output.ToString());
    }

    [TestMethod]
    public void MatchesCaseOnlyWhenCaseSensitive()
    {
        var filter = Filter("A", isCaseSensitive: true);
        filter.Update([new(Out, "a\nA\n")]);

        Assert.AreEqual("A\n", filter.Output.ToString());
    }

    [TestMethod]
    public void RaisesAppendedOncePerUpdate()
    {
        var filter = Filter("x");
        var appended = 0;
        filter.Output.Appended += (_, _) => appended++;

        filter.Update([new(Out, "x1\n"), new(Error, "x2\n"), new(Out, "x3\n")]);

        Assert.AreEqual(1, appended);
    }

    [TestMethod]
    public void SplitsTheLinesFoundIntoChunksOfLimitedLength()
    {
        var line = new string('x', 1000) + "\n";
        var filter = Filter("x");
        filter.Update([new(Out, string.Concat(Enumerable.Repeat(line, 40)))]);

        Assert.IsGreaterThan(1, filter.Output.Chunks.Count);
        Assert.IsTrue(filter.Output.Chunks.All(static chunk => chunk.Text.Length < OutputLineFilter.MaxChunkLength + 1001));
        Assert.AreEqual(40 * line.Length, filter.Output.ToString().Length);
    }
}
