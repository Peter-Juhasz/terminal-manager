using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Runs the commands asked for at the other end of a connection by <paramref name="runner"/>, any number of them at the same time,
/// and sends their output back as it is written. Run by the elevated host, so the commands are run with its rights.
/// </summary>
/// <param name="connection">The connection to the app, which is disposed once it is closed.</param>
public sealed class ElevationServer(Stream connection, ICommandRunner runner)
{
    private readonly ConcurrentDictionary<int, RunningCommand> _running = new();

    /// <summary>
    /// Runs commands until the connection is closed, or <paramref name="cancellationToken"/> is canceled,
    /// then stops those still running, and waits for them to end.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var reader = PipeReader.Create(connection, new StreamPipeReaderOptions(leaveOpen: true));
        var writer = new FrameWriter(PipeWriter.Create(connection, new StreamPipeWriterOptions(leaveOpen: true)));
        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = result.Buffer;
                while (ElevationProtocol.TryReadFrame(ref buffer, out var frame))
                {
                    Dispatch(writer, frame);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ObjectDisposedException or OperationCanceledException)
        {
            // the app closed the connection, or sent what isn't understood, so nothing more is run for it
        }
        finally
        {
            // Nothing the commands write is waited for anymore, as there is no one to show it to.
            foreach (var command in _running.Values)
            {
                await command.Stop.CancelAsync().ConfigureAwait(false);
            }

            await Task.WhenAll(_running.Values.Select(static command => command.Task)).ConfigureAwait(false);

            await reader.CompleteAsync().ConfigureAwait(false);
            writer.Complete();
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Dispatch(FrameWriter writer, Frame frame)
    {
        switch (frame.Type)
        {
            case FrameType.Run:
                Start(writer, frame.Id, frame.Payload);
                break;

            case FrameType.Cancel:
                if (_running.TryGetValue(frame.Id, out var command))
                {
                    command.Stop.Cancel();
                }

                break;

            default:
                throw new InvalidDataException($"A frame that is only sent to the app was received: {frame.Type}.");
        }
    }

    private void Start(FrameWriter writer, int id, ReadOnlySequence<byte> payload)
    {
        var projectLength = ElevationProtocol.ReadInt32(payload);
        if (projectLength < 0 || projectLength > payload.Length - sizeof(int))
        {
            throw new InvalidDataException("A command was received with a corrupt project.");
        }

        var jsonReader = new Utf8JsonReader(payload.Slice(sizeof(int), projectLength));
        var project = JsonSerializer.Deserialize(ref jsonReader, AppJsonSerializerContext.Default.Project)
            ?? throw new InvalidDataException("A command was received without a project.");
        var command = Encoding.UTF8.GetString(payload.Slice(sizeof(int) + projectLength));

        // Added before it is started, so it can be stopped as soon as the next frame is read; it isn't, if one with the same id is running.
        var running = new RunningCommand();
        if (!_running.TryAdd(id, running))
        {
            throw new InvalidDataException("A command was received with the id of one that is running.");
        }

        running.Task = Task.Run(() => RunCommandAsync(writer, id, project, command, running), CancellationToken.None);
    }

    private async Task RunCommandAsync(FrameWriter writer, int id, Project project, string command, RunningCommand running)
    {
        try
        {
            var output = new FrameOutputSink(writer, id);
            int exitCode;
            try
            {
                exitCode = await runner.RunAsync(project, command, output, running.Stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                writer.WriteText(FrameType.Failed, id, ex.Message);
                return;
            }

            writer.WriteInt32(FrameType.Exited, id, exitCode);
        }
        catch (IOException)
        {
            // the connection was closed, so there is no one to tell how it ended
        }
        finally
        {
            _running.TryRemove(id, out _);
        }
    }

    // The source isn't disposed, as it may still be canceled by a frame read as the command ends; it holds no timer.
    private sealed class RunningCommand
    {
        public CancellationTokenSource Stop { get; } = new();

        public Task Task { get; set; } = Task.CompletedTask;
    }

    // Sends what the command writes as frames; once it is complete, nothing more is sent, so its output is sent before how it ended.
    private sealed class FrameOutputSink(FrameWriter writer, int id) : IOutputSink
    {
        private readonly Lock _lock = new();
        private bool _isCompleted;

        public void Write(OutputStream stream, ReadOnlySpan<char> text)
        {
            lock (_lock)
            {
                if (_isCompleted)
                {
                    return;
                }

                try
                {
                    writer.WriteText(stream is OutputStream.StandardOutput ? FrameType.StandardOutput : FrameType.StandardError, id, text);
                }
                catch (IOException)
                {
                    // the connection was closed, which stops the command
                }
            }
        }

        public void Complete()
        {
            lock (_lock)
            {
                _isCompleted = true;
            }
        }
    }
}
