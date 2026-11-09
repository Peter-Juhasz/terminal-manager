using System.Text;
using App.Domain;
using App.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class OutputBufferTests(TestContext testContext)
{
    private const OutputStream Out = OutputStream.StandardOutput;
    private const OutputStream Error = OutputStream.StandardError;

    [TestMethod]
    public void ReadJoinsWritesToSameStream()
    {
        var output = new OutputBuffer();
        output.Write(Out, "a");
        output.Write(Out, "b");
        output.Write(Error, "c");
        output.Write(Out, "d");

        var batch = output.Read();

        AssertChunks([new(Out, "ab"), new(Error, "c"), new(Out, "d")], batch.Chunks);
    }

    [TestMethod]
    public void ReadReturnsWhatWasWrittenSinceLastRead()
    {
        var output = new OutputBuffer();
        var large = new string('a', OutputBuffer.MaxChunkLength * 3);
        string[] texts = ["a", large, "b", large, "c"];

        // Each read reuses the buffers of the one before the last.
        foreach (var text in texts)
        {
            output.Write(Out, text);
            Assert.AreEqual(text, string.Concat(output.Read().Chunks.Select(static chunk => chunk.Text)));
        }

        AssertChunks([], output.Read().Chunks);
    }

    [TestMethod]
    public void ReadTakesProgressOutOfStandardOutputOnly()
    {
        var output = new OutputBuffer();
        output.Write(Out, "a\e]9;4;1;50\a");
        output.Write(Error, "\e]9;4;1;60\a");

        var batch = output.Read();

        AssertChunks([new(Out, "a"), new(Error, "\e]9;4;1;60\a")], batch.Chunks);
        Assert.AreEqual(new ProgressReport(ProgressState.Normal, 50), batch.Progress);
    }

    [TestMethod]
    public void ReadKeepsOrderAroundSplitSequence()
    {
        var output = new OutputBuffer();
        output.Write(Out, "a\e]9;4");
        output.Write(Error, "b");
        output.Write(Out, ";1;5\ac");

        var batch = output.Read();

        AssertChunks([new(Out, "a"), new(Error, "b"), new(Out, "c")], batch.Chunks);
        Assert.AreEqual(new ProgressReport(ProgressState.Normal, 5), batch.Progress);
    }

    [TestMethod]
    public void ReadSplitsLongTextIntoChunks()
    {
        var output = new OutputBuffer();
        output.Write(Out, new string('a', (OutputBuffer.MaxChunkLength * 2) + 100));

        var chunks = output.Read().Chunks;

        int[] expected = [OutputBuffer.MaxChunkLength, OutputBuffer.MaxChunkLength, 100];
        Assert.AreSequenceEqual(expected, chunks.Select(static chunk => chunk.Text.Length));
    }

    [TestMethod]
    public void ReadDoesNotSplitSurrogatePairs()
    {
        var output = new OutputBuffer();
        var text = new string('a', OutputBuffer.MaxChunkLength - 1) + "😀b";
        output.Write(Out, text);

        var chunks = output.Read().Chunks;

        AssertChunks([new(Out, text[..^3]), new(Out, "😀b")], chunks);
    }

    [TestMethod]
    public async Task WriteOfProgressAloneWakesReader()
    {
        var output = new OutputBuffer();

        output.Write(Out, "\e]9;4;1;50\a");

        var waiting = output.WaitToReadAsync(testContext.CancellationToken).AsTask();
        Assert.IsTrue(waiting.IsCompleted);
        Assert.IsTrue(await waiting);
        var batch = output.Read();
        AssertChunks([], batch.Chunks);
        Assert.AreEqual(new ProgressReport(ProgressState.Normal, 50), batch.Progress);
    }

    [TestMethod]
    public async Task CompleteWakesReaderWithHeldBackText()
    {
        var output = new OutputBuffer();
        output.Write(Out, "a");
        output.Read();
        output.Write(Out, "\e]9;4;1");
        var waiting = output.WaitToReadAsync(testContext.CancellationToken).AsTask();
        Assert.IsFalse(waiting.IsCompleted);

        output.Complete();

        Assert.IsTrue(output.Completion.IsCompleted);
        Assert.IsTrue(await waiting);
        AssertChunks([new(Out, "\e]9;4;1")], output.Read().Chunks);
        Assert.IsFalse(await output.WaitToReadAsync(testContext.CancellationToken));
    }

    [TestMethod]
    public async Task WriteAfterCompleteIsIgnored()
    {
        var output = new OutputBuffer();
        output.Write(Out, "a");
        output.Complete();

        output.Write(Out, "b");

        AssertChunks([new(Out, "a")], await ReadToEndAsync(output));
    }

    [TestMethod]
    public async Task ReadGetsAllWrittenFromOtherThreads()
    {
        const int LineCount = 10_000;
        var output = new OutputBuffer();
        var cancellationToken = testContext.CancellationToken;

        var writing = Task.Run(
            async () =>
            {
                try
                {
                    await Task.WhenAll(
                        Task.Run(() => WriteLines(output, Out, LineCount), cancellationToken),
                        Task.Run(() => WriteLines(output, Error, LineCount), cancellationToken));
                }
                finally
                {
                    output.Complete();
                }
            },
            cancellationToken);

        var text = new StringBuilder();
        var error = new StringBuilder();
        var progress = default(ProgressReport);
        while (await output.WaitToReadAsync(cancellationToken))
        {
            var batch = output.Read();
            foreach (var chunk in batch.Chunks)
            {
                (chunk.Stream is Out ? text : error).Append(chunk.Text);
            }

            progress = batch.Progress;
        }

        await writing;
        var expected = string.Concat(Enumerable.Range(0, LineCount).Select(static index => $"line {index}\n"));
        Assert.AreEqual(expected, text.ToString());
        Assert.AreEqual(expected, error.ToString());
        Assert.AreEqual(new ProgressReport(ProgressState.Normal, 100), progress);
    }

    // Writes numbered lines in two pieces, split at a different place in each, with a progress sequence before each on the standard output.
    private static void WriteLines(OutputBuffer output, OutputStream stream, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var line = stream is Out ? $"\e]9;4;1;{(index % 100) + 1}\aline {index}\n" : $"line {index}\n";
            var split = index % line.Length;
            output.Write(stream, line.AsSpan(0, split));
            output.Write(stream, line.AsSpan(split));
        }
    }

    private async Task<List<OutputChunk>> ReadToEndAsync(OutputBuffer output)
    {
        var chunks = new List<OutputChunk>();
        while (await output.WaitToReadAsync(testContext.CancellationToken))
        {
            chunks.AddRange(output.Read().Chunks);
        }

        return chunks;
    }

    private static void AssertChunks(OutputChunk[] expected, IEnumerable<OutputChunk> actual) =>
        Assert.AreSequenceEqual(expected, actual);
}
