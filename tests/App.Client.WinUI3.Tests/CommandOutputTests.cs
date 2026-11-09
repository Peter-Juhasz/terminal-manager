using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;
using App.Domain;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class CommandOutputTests
{
    private const OutputStream Out = OutputStream.StandardOutput;
    private const OutputStream Error = OutputStream.StandardError;

    [TestMethod]
    public void AppendSkipsEmptyChunks()
    {
        var output = new CommandOutput();
        var appended = 0;
        output.Appended += (_, _) => appended++;

        output.Append([new(Out, "a"), new(Error, string.Empty), default, new(Error, "b")]);

        Assert.AreSequenceEqual(new OutputChunk[] { new(Out, "a"), new(Error, "b") }, output.Chunks);
        Assert.AreEqual(1, appended);
        Assert.IsFalse(output.IsEmpty);
    }

    [TestMethod]
    public void AppendOfNothingDoesNotRaiseAppended()
    {
        var output = new CommandOutput();
        var appended = 0;
        output.Appended += (_, _) => appended++;

        output.Append([]);
        output.Append([new(Out, string.Empty)]);

        Assert.AreEqual(0, appended);
        Assert.IsTrue(output.IsEmpty);
    }

    [TestMethod]
    public void ToStringJoinsAllChunks()
    {
        var output = new CommandOutput();
        output.Append([new(Out, "a"), new(Error, "b")]);
        output.Append([new(Out, "c")]);

        Assert.AreEqual("abc", output.ToString());
    }

    [TestMethod]
    public void ToStringOfNothingIsEmpty() => Assert.AreEqual(string.Empty, new CommandOutput().ToString());

    [TestMethod]
    public void HasNoLinksUntilComplete()
    {
        var output = new CommandOutput();
        output.Append([new(Out, "see https://example.com\n")]);

        Assert.IsEmpty(output.Links);
    }

    [TestMethod]
    public void FindsTheLinksAcrossChunksOnceComplete()
    {
        var output = new CommandOutput();
        var completed = 0;
        output.Completed += (_, _) => completed++;
        output.Append([new(Out, "see https://exa"), new(Error, "mple.com/a now")]);

        output.Complete();
        output.Complete();

        Assert.AreEqual(1, completed);
        Assert.AreSequenceEqual(new[] { new UrlMatch(Start: 4, Length: 21, Uri: new("https://example.com/a")) }, output.Links);
    }

    [TestMethod]
    public void FindsTheLinksAgainWhenAppendedToOnceComplete()
    {
        var output = new CommandOutput();
        output.Append([new(Out, "http://a.com")]);
        output.Complete();
        Assert.HasCount(1, output.Links);

        output.Append([new(Error, " http://b.com")]);

        Assert.HasCount(2, output.Links);
    }

    [TestMethod]
    public void FindsNoMoreThanTheMostLinks()
    {
        var output = new CommandOutput();
        output.Append([new(Out, string.Concat(Enumerable.Repeat("http://a.com ", CommandOutput.MaxLinks + 1)))]);
        output.Complete();

        Assert.HasCount(CommandOutput.MaxLinks, output.Links);
    }
}
