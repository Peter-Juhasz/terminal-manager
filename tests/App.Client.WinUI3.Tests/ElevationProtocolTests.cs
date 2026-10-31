using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Tests;

[TestClass]
public sealed class ElevationProtocolTests(TestContext testContext)
{
    [TestMethod]
    public void TryReadFrameWaitsForWholeHeader()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[ElevationProtocol.HeaderLength - 1]);

        Assert.IsFalse(ElevationProtocol.TryReadFrame(ref buffer, out _));
        Assert.AreEqual(ElevationProtocol.HeaderLength - 1, buffer.Length);
    }

    [TestMethod]
    public async Task TryReadFrameWaitsForWholePayload()
    {
        var bytes = await WriteAsync(static writer => writer.WriteInt32(FrameType.Exited, 1, 42));
        var buffer = new ReadOnlySequence<byte>(bytes.AsMemory(0, bytes.Length - 1));

        Assert.IsFalse(ElevationProtocol.TryReadFrame(ref buffer, out _));
    }

    [TestMethod]
    public async Task TryReadFrameReadsFrameSplitAcrossSegments()
    {
        var bytes = await WriteAsync(static writer => writer.WriteText(FrameType.StandardOutput, 7, "hello"));

        // Split inside the header, and again inside the payload.
        var buffer = Segments(bytes[..3], bytes[3..11], bytes[11..]);

        Assert.IsTrue(ElevationProtocol.TryReadFrame(ref buffer, out var frame));
        Assert.AreEqual(FrameType.StandardOutput, frame.Type);
        Assert.AreEqual(7, frame.Id);
        Assert.AreEqual("hello", Encoding.UTF8.GetString(frame.Payload));
        Assert.AreEqual(0, buffer.Length);
    }

    [TestMethod]
    public async Task TryReadFrameReadsFramesInOrder()
    {
        var bytes = await WriteAsync(static writer =>
        {
            writer.WriteText(FrameType.StandardError, 1, "oops");
            writer.WriteEmpty(FrameType.Cancel, 2);
            writer.WriteInt32(FrameType.Exited, 1, -1);
        });
        var buffer = new ReadOnlySequence<byte>(bytes);

        Assert.IsTrue(ElevationProtocol.TryReadFrame(ref buffer, out var first));
        Assert.IsTrue(ElevationProtocol.TryReadFrame(ref buffer, out var second));
        Assert.IsTrue(ElevationProtocol.TryReadFrame(ref buffer, out var third));
        Assert.IsFalse(ElevationProtocol.TryReadFrame(ref buffer, out _));

        Assert.AreEqual((FrameType.StandardError, 1, "oops"), (first.Type, first.Id, Encoding.UTF8.GetString(first.Payload)));
        Assert.AreEqual((FrameType.Cancel, 2, 0L), (second.Type, second.Id, second.Payload.Length));
        Assert.AreEqual((FrameType.Exited, 1, -1), (third.Type, third.Id, ElevationProtocol.ReadInt32(third.Payload)));
    }

    [TestMethod]
    public void TryReadFrameRejectsUnknownType()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 0xFF, 1, 0, 0, 0, 0, 0, 0, 0 });

        Assert.ThrowsExactly<InvalidDataException>(() => ElevationProtocol.TryReadFrame(ref buffer, out _));
    }

    [TestMethod]
    public void TryReadFrameRejectsPayloadTooLong()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { (byte)FrameType.StandardOutput, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F });

        Assert.ThrowsExactly<InvalidDataException>(() => ElevationProtocol.TryReadFrame(ref buffer, out _));
    }

    // Long text is sent in several frames, none of which may split a surrogate pair, so each can be decoded on its own.
    [TestMethod]
    public async Task WriteTextSplitsLongTextBetweenCharacters()
    {
        var text = "a" + string.Concat(Enumerable.Repeat("😀", 20_000));
        var bytes = await WriteAsync(writer => writer.WriteText(FrameType.StandardOutput, 1, text));
        var buffer = new ReadOnlySequence<byte>(bytes);

        var decoded = new StringBuilder();
        var frames = 0;
        while (ElevationProtocol.TryReadFrame(ref buffer, out var frame))
        {
            var piece = Encoding.UTF8.GetString(frame.Payload);
            Assert.DoesNotContain("�", piece);
            decoded.Append(piece);
            frames++;
        }

        Assert.IsGreaterThan(1, frames);
        Assert.AreEqual(text, decoded.ToString());
    }

    [TestMethod]
    public async Task WriteRunWritesProjectAndCommand()
    {
        var bytes = await WriteAsync(static writer => writer.WriteRun(3, "{}"u8, "echo é"));
        var buffer = new ReadOnlySequence<byte>(bytes);

        Assert.IsTrue(ElevationProtocol.TryReadFrame(ref buffer, out var frame));
        Assert.AreEqual(FrameType.Run, frame.Type);
        Assert.AreEqual(3, frame.Id);
        Assert.AreEqual(2, ElevationProtocol.ReadInt32(frame.Payload));
        Assert.AreEqual("{}", Encoding.UTF8.GetString(frame.Payload.Slice(4, 2)));
        Assert.AreEqual("echo é", Encoding.UTF8.GetString(frame.Payload.Slice(6)));
    }

    [TestMethod]
    public void WriteAfterCompleteThrowsIOException()
    {
        var writer = new FrameWriter(new Pipe().Writer);
        writer.Complete();

        Assert.ThrowsExactly<IOException>(() => writer.WriteEmpty(FrameType.Cancel, 1));
    }

    private async Task<byte[]> WriteAsync(Action<FrameWriter> write)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var writer = new FrameWriter(pipe.Writer);
        write(writer);
        writer.Complete();

        using var bytes = new MemoryStream();
        await pipe.Reader.CopyToAsync(bytes, testContext.CancellationToken);
        return bytes.ToArray();
    }

    private static ReadOnlySequence<byte> Segments(params byte[][] parts)
    {
        var first = new Segment(parts[0], 0);
        var last = first;
        foreach (var part in parts[1..])
        {
            last = last.Append(part);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
