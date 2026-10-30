using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Services;

public enum OutputStream
{
    StandardOutput,
    StandardError,
}

/// <summary>A piece of what a command wrote, in the order it was read.</summary>
public readonly record struct OutputChunk(OutputStream Stream, string Text);

/// <summary>
/// Runs commands by the terminal of their project, in its working folder.
/// </summary>
public sealed class CommandRunner
{
    // How long the output is still read after the process exits, as a process it started may keep the streams open.
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Runs <paramref name="command"/>, writing its output to <paramref name="output"/> as it is read, which is completed when the command ends.
    /// Canceling stops the process and all the processes it started.
    /// </summary>
    /// <returns>The exit code of the process.</returns>
    public async Task<int> RunAsync(Project project, string command, ChannelWriter<OutputChunk> output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            var startInfo = project.CreateStartInfo(command);
            startInfo.WorkingDirectory = project.WorkingFolder;
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

            var pumps = Task.WhenAll(
                PumpAsync(process.StandardOutput, OutputStream.StandardOutput, output),
                PumpAsync(process.StandardError, OutputStream.StandardError, output));

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
            output.TryComplete();
        }
    }

    private static async Task PumpAsync(StreamReader reader, OutputStream stream, ChannelWriter<OutputChunk> output)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            output.TryWrite(new OutputChunk(stream, new string(buffer, 0, read)));
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
