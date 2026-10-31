using System.Diagnostics;
using System.Text;
using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Runs commands by the terminal of their project, in its working folder.
/// </summary>
public sealed class CommandRunner
{
    // How long the output is still read after the process exits, as a process it started may keep the streams open.
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(1);

    // As much as the pipe of a stream holds.
    private const int ReadBufferSize = 4096;

    /// <summary>
    /// Runs <paramref name="command"/>, writing its output to <paramref name="output"/> as it is read, which is completed when the command ends.
    /// Canceling stops the process and all the processes it started.
    /// </summary>
    /// <returns>The exit code of the process.</returns>
    public async Task<int> RunAsync(Project project, string command, OutputBuffer output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            var startInfo = project.CreateStartInfo(command);
            startInfo.WorkingDirectory = project.ExpandedWorkingFolder;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"'{startInfo.FileName}' couldn't be started.");

            // Nothing is ever typed in, so commands that read their input don't wait for it.
            process.StandardInput.Close();

            await using var registration = cancellationToken.Register(() => Kill(process));

            // Each stream is closed once it was read to the end, not with the process, so a process the command started, which keeps it open,
            // can still write to it after the command ended.
            var pumps = Task.WhenAll(
                StartPump(process.StandardOutput.BaseStream, OutputStream.StandardOutput, output),
                StartPump(process.StandardError.BaseStream, OutputStream.StandardError, output));

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            try
            {
                await pumps.WaitAsync(OutputDrainTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }

            return process.ExitCode;
        }
        finally
        {
            output.Complete();
        }
    }

    // On a thread of its own, as the pipe can only be read by blocking one, for as long as any process keeps it open,
    // which would hold up everything else run by the thread pool.
    private static Task StartPump(Stream source, OutputStream stream, OutputBuffer output) =>
        Task.Factory.StartNew(() => Pump(source, stream, output), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Decodes what was read itself, as a StreamReader holds back what it decoded until it reads again whenever a read filled its buffer,
    // which waits for the command to write more.
    private static void Pump(Stream source, OutputStream stream, OutputBuffer output)
    {
        using (source)
        {
            var decoder = Encoding.UTF8.GetDecoder();
            Span<byte> bytes = stackalloc byte[ReadBufferSize];
            Span<char> chars = stackalloc char[Encoding.UTF8.GetMaxCharCount(ReadBufferSize)];
            var isStart = true;
            int read;
            do
            {
                read = source.Read(bytes);
                ReadOnlySpan<char> text = chars[..decoder.GetChars(bytes[..read], chars, flush: read == 0)];

                // A byte order mark isn't text, so it isn't shown.
                if (isStart && !text.IsEmpty)
                {
                    isStart = false;
                    if (text[0] == '\uFEFF')
                    {
                        text = text[1..];
                    }
                }

                if (!text.IsEmpty)
                {
                    output.Write(stream, text);
                }
            }
            while (read > 0);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception)
        {
            // It has already exited, or some of the processes it started can't be stopped.
        }
    }
}
