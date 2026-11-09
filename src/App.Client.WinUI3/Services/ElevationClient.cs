using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using App.Domain;
using App.Services;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Runs commands by the elevated host at the other end of a connection, any number of them at the same time, with their output sent back as it is written.
/// When the connection is closed, the commands that were running fail, and no more can be run.
/// </summary>
public sealed class ElevationClient : ICommandRunner, IAsyncDisposable
{
    private readonly Stream _connection;
    private readonly PipeReader _reader;
    private readonly FrameWriter _writer;
    private readonly ConcurrentDictionary<int, PendingCommand> _pending = new();
    private readonly Task _reading;
    private int _lastId;
    private volatile bool _isClosed;

    /// <param name="connection">The connection to the host, which is disposed once it is closed.</param>
    public ElevationClient(Stream connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _connection = connection;
        _reader = PipeReader.Create(connection, new StreamPipeReaderOptions(leaveOpen: true));
        _writer = new FrameWriter(PipeWriter.Create(connection, new StreamPipeWriterOptions(leaveOpen: true)));
        _reading = Task.Run(ReadAsync, CancellationToken.None);
    }

    /// <summary>Whether commands can still be run, which they can't once the connection was closed.</summary>
    public bool IsConnected => !_isClosed;

    /// <summary>Completed when the connection was closed.</summary>
    public Task Completion => _reading;

    /// <inheritdoc/>
    /// <exception cref="IOException">The connection was closed, before the command ended.</exception>
    /// <exception cref="InvalidOperationException">The host couldn't start the command.</exception>
    public async Task<int> RunAsync(Project project, string command, IOutputSink output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        var id = Interlocked.Increment(ref _lastId);
        var pending = new PendingCommand(output);
        _pending[id] = pending;
        try
        {
            // Checked once it is added, so it is failed either here or by the reader as it stops, if the connection was closed in between.
            if (_isClosed)
            {
                throw new IOException("The connection to the elevated host was closed.");
            }

            _writer.WriteRun(id, JsonSerializer.SerializeToUtf8Bytes(project, AppJsonSerializerContext.Default.Project), command);

            // Sent from the thread pool, as the token may be canceled on the UI thread, which isn't held up by the write.
            await using var registration = cancellationToken.Register(() => ThreadPool.QueueUserWorkItem(static state =>
            {
                var (writer, id) = state;
                try
                {
                    writer.WriteEmpty(FrameType.Cancel, id);
                }
                catch (IOException)
                {
                    // the connection was closed, which stops the command anyway
                }
            }, (_writer, id), preferLocal: false));

            return await pending.Exit.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
            output.Complete();
        }
    }

    private async Task ReadAsync()
    {
        Exception? error = null;
        try
        {
            while (true)
            {
                var result = await _reader.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                var buffer = result.Buffer;
                while (ElevationProtocol.TryReadFrame(ref buffer, out var frame))
                {
                    Dispatch(frame);
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
        {
            error = ex;
        }
        finally
        {
            _isClosed = true;
            await _reader.CompleteAsync().ConfigureAwait(false);
            _writer.Complete();
            await _connection.DisposeAsync().ConfigureAwait(false);

            foreach (var pending in _pending.Values)
            {
                pending.Exit.TrySetException(new IOException("The connection to the elevated host was closed.", error));
            }
        }
    }

    // Frames of commands that are no longer waited for, such as the output written after one was stopped, are ignored.
    private void Dispatch(Frame frame)
    {
        if (!_pending.TryGetValue(frame.Id, out var pending))
        {
            return;
        }

        switch (frame.Type)
        {
            case FrameType.StandardOutput:
                WriteText(pending.Output, OutputStream.StandardOutput, frame.Payload);
                break;

            case FrameType.StandardError:
                WriteText(pending.Output, OutputStream.StandardError, frame.Payload);
                break;

            case FrameType.Exited:
                pending.Exit.TrySetResult(ElevationProtocol.ReadInt32(frame.Payload));
                break;

            case FrameType.Failed:
                pending.Exit.TrySetException(new InvalidOperationException(Encoding.UTF8.GetString(frame.Payload)));
                break;

            default:
                throw new InvalidDataException($"A frame that is only sent to the host was received: {frame.Type}.");
        }
    }

    // Each frame is made up of whole characters, so it is decoded on its own.
    private static void WriteText(IOutputSink output, OutputStream stream, ReadOnlySequence<byte> payload)
    {
        var chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount((int)payload.Length));
        try
        {
            var length = Encoding.UTF8.GetChars(payload, chars);
            output.Write(stream, chars.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>Closes the connection, which fails the commands that are running, and stops them at the host.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _reader.CancelPendingRead();
        }
        catch (InvalidOperationException)
        {
            // the reader was completed, as the connection was closed already
        }

        await _reading.ConfigureAwait(false);
    }

    private sealed class PendingCommand(IOutputSink output)
    {
        public IOutputSink Output { get; } = output;

        public TaskCompletionSource<int> Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
