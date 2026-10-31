using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;

namespace App.Client.WinUI3.Services;

/// <summary>What a frame sent between the app and the elevated host is about.</summary>
public enum FrameType : byte
{
    /// <summary>From the app: runs a command. The payload is the length of the project, the project as JSON, and the command, both in UTF-8.</summary>
    Run = 1,

    /// <summary>From the app: stops the command, and all the processes it started. No payload.</summary>
    Cancel = 2,

    /// <summary>From the host: text the command wrote to its standard output, in UTF-8, made up of whole characters.</summary>
    StandardOutput = 3,

    /// <summary>From the host: text the command wrote to its standard error, in UTF-8, made up of whole characters.</summary>
    StandardError = 4,

    /// <summary>From the host: the command ended, after all its output was sent. The payload is its exit code.</summary>
    Exited = 5,

    /// <summary>From the host: the command couldn't be run. The payload is the reason, in UTF-8.</summary>
    Failed = 6,
}

/// <summary>A frame sent between the app and the elevated host, about the command with <paramref name="Id"/>.</summary>
public readonly record struct Frame(FrameType Type, int Id, ReadOnlySequence<byte> Payload);

/// <summary>
/// How frames are laid out: the type in a byte, then the id of the command and the length of the payload, each in 4 bytes, little endian, then the payload.
/// </summary>
public static class ElevationProtocol
{
    public const int HeaderLength = 9;

    /// <summary>The longest payload accepted, so a corrupt length doesn't make the reader wait for, and buffer, gigabytes.</summary>
    public const int MaxPayloadLength = 16 * 1024 * 1024;

    /// <summary>
    /// Takes the frame at the start of <paramref name="buffer"/>, if all of it was read. The payload refers to the buffer, so it is only valid until the reader is advanced.
    /// </summary>
    /// <exception cref="InvalidDataException">The header is corrupt.</exception>
    public static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out Frame frame)
    {
        frame = default;
        if (buffer.Length < HeaderLength)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[HeaderLength];
        buffer.Slice(0, HeaderLength).CopyTo(header);

        var type = (FrameType)header[0];
        var id = BinaryPrimitives.ReadInt32LittleEndian(header[1..]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header[5..]);
        if (type is < FrameType.Run or > FrameType.Failed || length is < 0 or > MaxPayloadLength)
        {
            throw new InvalidDataException("A corrupt frame was received from the other end of the connection.");
        }

        if (buffer.Length < HeaderLength + length)
        {
            return false;
        }

        frame = new Frame(type, id, buffer.Slice(HeaderLength, length));
        buffer = buffer.Slice(HeaderLength + length);
        return true;
    }

    /// <summary>Reads the 4 byte integer a payload starts with.</summary>
    /// <exception cref="InvalidDataException">The payload is shorter.</exception>
    public static int ReadInt32(ReadOnlySequence<byte> payload)
    {
        var reader = new SequenceReader<byte>(payload);
        return reader.TryReadLittleEndian(out int value) ? value : throw new InvalidDataException("A frame was received without the number it should have held.");
    }
}

/// <summary>
/// Writes frames, each flushed right away, so the other end gets the output as it is written. Can be called from any thread, as the writes are serialized.
/// </summary>
public sealed class FrameWriter(PipeWriter writer)
{
    // The most text in a frame, so a frame of text is never larger than a segment of the pipe by much.
    private const int MaxTextLength = 16 * 1024;

    private readonly Lock _lock = new();
    private bool _isCompleted;

    /// <summary>Writes <paramref name="text"/>, in as many frames as needed, none of which splits a character.</summary>
    /// <exception cref="IOException">The connection was closed.</exception>
    public void WriteText(FrameType type, int id, ReadOnlySpan<char> text)
    {
        lock (_lock)
        {
            ThrowIfCompleted();
            while (!text.IsEmpty)
            {
                var length = Math.Min(text.Length, MaxTextLength);
                if (length < text.Length && char.IsHighSurrogate(text[length - 1]))
                {
                    length--;
                }

                var piece = text[..length];
                var span = writer.GetSpan(ElevationProtocol.HeaderLength + Encoding.UTF8.GetMaxByteCount(piece.Length));
                var written = Encoding.UTF8.GetBytes(piece, span[ElevationProtocol.HeaderLength..]);
                WriteHeader(span, type, id, written);
                writer.Advance(ElevationProtocol.HeaderLength + written);
                text = text[length..];
            }

            Flush();
        }
    }

    /// <summary>Writes a frame whose payload is <paramref name="value"/>.</summary>
    /// <exception cref="IOException">The connection was closed.</exception>
    public void WriteInt32(FrameType type, int id, int value)
    {
        lock (_lock)
        {
            ThrowIfCompleted();
            var span = writer.GetSpan(ElevationProtocol.HeaderLength + sizeof(int));
            WriteHeader(span, type, id, sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(span[ElevationProtocol.HeaderLength..], value);
            writer.Advance(ElevationProtocol.HeaderLength + sizeof(int));
            Flush();
        }
    }

    /// <summary>Writes a frame without a payload.</summary>
    /// <exception cref="IOException">The connection was closed.</exception>
    public void WriteEmpty(FrameType type, int id)
    {
        lock (_lock)
        {
            ThrowIfCompleted();
            var span = writer.GetSpan(ElevationProtocol.HeaderLength);
            WriteHeader(span, type, id, 0);
            writer.Advance(ElevationProtocol.HeaderLength);
            Flush();
        }
    }

    /// <summary>Writes a <see cref="FrameType.Run"/> frame.</summary>
    /// <exception cref="IOException">The connection was closed.</exception>
    public void WriteRun(int id, ReadOnlySpan<byte> project, ReadOnlySpan<char> command)
    {
        var length = sizeof(int) + project.Length + Encoding.UTF8.GetByteCount(command);
        if (length > ElevationProtocol.MaxPayloadLength)
        {
            throw new InvalidOperationException("The command is too long to be run as an administrator.");
        }

        lock (_lock)
        {
            ThrowIfCompleted();
            var span = writer.GetSpan(ElevationProtocol.HeaderLength + length);
            WriteHeader(span, FrameType.Run, id, length);
            var payload = span[ElevationProtocol.HeaderLength..];
            BinaryPrimitives.WriteInt32LittleEndian(payload, project.Length);
            project.CopyTo(payload[sizeof(int)..]);
            Encoding.UTF8.GetBytes(command, payload[(sizeof(int) + project.Length)..]);
            writer.Advance(ElevationProtocol.HeaderLength + length);
            Flush();
        }
    }

    /// <summary>Ends the connection from this end, once whatever was written is sent.</summary>
    public void Complete()
    {
        lock (_lock)
        {
            if (_isCompleted)
            {
                return;
            }

            _isCompleted = true;
            try
            {
                writer.Complete();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // the other end closed the connection already
            }
        }
    }

    private void ThrowIfCompleted()
    {
        if (_isCompleted)
        {
            throw new IOException("The connection was closed.");
        }
    }

    private static void WriteHeader(Span<byte> span, FrameType type, int id, int length)
    {
        span[0] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(span[1..], id);
        BinaryPrimitives.WriteInt32LittleEndian(span[5..], length);
    }

    // Waited for under the lock, so frames are sent in the order they were written; the frames are small, and sent to a local pipe.
    private void Flush()
    {
        FlushResult result;
        try
        {
            var flush = writer.FlushAsync(CancellationToken.None);
            result = flush.IsCompletedSuccessfully ? flush.Result : flush.AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            throw new IOException("The connection was closed.", ex);
        }

        if (result.IsCompleted)
        {
            throw new IOException("The connection was closed.");
        }
    }
}
