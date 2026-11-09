using System.Text;
using App.Client.WinUI3.Controls;
using App.Domain;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class OutputRunBuilderTests
{
    private const OutputStream Out = OutputStream.StandardOutput;
    private const OutputStream Error = OutputStream.StandardError;

    [TestMethod]
    public void AppendJoinsChunksOfSameStream()
    {
        var builder = new OutputRunBuilder();
        builder.Append(new(Out, "a"));
        builder.Append(new(Out, "b"));

        AssertUpdates([new(0, Out, "ab")], Flush(builder));
        Assert.AreEqual(1, builder.Count);
    }

    [TestMethod]
    public void AppendStartsRunWhenStreamChanges()
    {
        var builder = new OutputRunBuilder();
        builder.Append(new(Out, "a\n"));
        builder.Append(new(Error, "b\n"));
        builder.Append(new(Out, "c"));

        AssertUpdates([new(0, Out, "a"), new(1, Error, "\nb"), new(2, Out, "\nc")], Flush(builder));
        Assert.AreEqual(3, builder.Count);
    }

    [TestMethod]
    public void AppendStartsRunWhenLastIsFull()
    {
        var builder = new OutputRunBuilder();
        var half = new string('a', OutputRunBuilder.MaxRunLength / 2);
        builder.Append(new(Out, half));
        builder.Append(new(Out, half));
        builder.Append(new(Out, half));

        AssertUpdates([new(0, Out, half + half), new(1, Out, half)], Flush(builder));
    }

    [TestMethod]
    public void FlushReturnsRunsThatChangedOnly()
    {
        var builder = new OutputRunBuilder();
        builder.Append(new(Out, "a"));
        builder.Append(new(Error, "b"));
        Flush(builder);

        AssertUpdates([], Flush(builder));

        builder.Append(new(Error, "c"));
        AssertUpdates([new(1, Error, "bc")], Flush(builder));

        builder.Append(new(Out, "d"));
        builder.Append(new(Error, "e"));
        AssertUpdates([new(2, Out, "d"), new(3, Error, "e")], Flush(builder));
    }

    [TestMethod]
    public void AppendHoldsBackLineBreaksAtEnd()
    {
        var builder = new OutputRunBuilder();

        builder.Append(new(Out, "a\r\n"));
        AssertUpdates([new(0, Out, "a")], Flush(builder));

        builder.Append(new(Out, "\r\n"));
        AssertUpdates([], Flush(builder));

        builder.Append(new(Out, "b\r\n"));
        AssertUpdates([new(0, Out, "a\r\n\r\nb")], Flush(builder));
    }

    [TestMethod]
    public void AppendDoesNotSplitLineBreakBetweenRuns()
    {
        var builder = new OutputRunBuilder();
        var full = new string('a', OutputRunBuilder.MaxRunLength);
        builder.Append(new(Out, full + "\r"));
        builder.Append(new(Out, "\nb"));

        AssertUpdates([new(0, Out, full), new(1, Out, "\r\nb")], Flush(builder));
    }

    [TestMethod]
    public void OutputOfLineBreaksOnlyShowsNothing()
    {
        var builder = new OutputRunBuilder();
        builder.Append(new(Out, "\r\n"));
        builder.Append(new(Error, "\n"));

        AssertUpdates([], Flush(builder));
        Assert.AreEqual(0, builder.Count);
    }

    // Applies the updates to runs the way the control does, checking that they show all of the output after each.
    [TestMethod]
    public void RunsShowAllOfOutputWithoutLineBreaksAtEnd()
    {
        string[] pieces = ["a", "bc", "\r\n", "\n", "\r", new string('x', 1000)];
        var random = new Random(42);
        var builder = new OutputRunBuilder();
        var runs = new List<RunUpdate>();
        var output = new StringBuilder();

        for (var round = 0; round < 500; round++)
        {
            for (var count = random.Next(4); count > 0; count--)
            {
                var text = new StringBuilder();
                for (var length = random.Next(5); length > 0; length--)
                {
                    text.Append(pieces[random.Next(pieces.Length)]);
                }

                var chunk = new OutputChunk(random.Next(8) == 0 ? Error : Out, text.ToString());
                builder.Append(chunk);
                output.Append(chunk.Text);
            }

            foreach (var update in Flush(builder))
            {
                if (update.Index < runs.Count)
                {
                    Assert.AreEqual(runs.Count - 1, update.Index, "Only the last run is updated.");
                    Assert.AreEqual(runs[^1].Stream, update.Stream);
                    runs[^1] = update;
                }
                else
                {
                    Assert.AreEqual(runs.Count, update.Index, "Runs are added in order.");
                    runs.Add(update);
                }
            }

            Assert.AreEqual(runs.Count, builder.Count);
            Assert.AreEqual(output.ToString().TrimEnd('\r', '\n'), string.Concat(runs.Select(static run => run.Text)), $"Round {round}");
        }

        Assert.IsGreaterThan(10, runs.Count);
    }

    private static List<RunUpdate> Flush(OutputRunBuilder builder)
    {
        var updates = new List<RunUpdate>();
        builder.Flush(updates);
        return updates;
    }

    private static void AssertUpdates(RunUpdate[] expected, List<RunUpdate> actual) =>
        Assert.AreSequenceEqual(expected, actual);
}
