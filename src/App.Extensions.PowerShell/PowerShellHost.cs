using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace App.Extensions.PowerShell;

/// <summary>
/// A pwsh that keeps running <paramref name="script"/> in <paramref name="workingFolder"/>, which reads a request from each line it is given,
/// and writes the answer on a line of its own; it answers one request at a time.
/// </summary>
internal sealed partial class PowerShellHost(string script, string? workingFolder) : IDisposable
{
    // How long an answer is waited for, after which the pwsh is stopped, and started again for the next one.
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private bool _isDisposed;

    public bool HasExited => _isDisposed || _process is { HasExited: true };

    /// <summary>Sends <paramref name="request"/>, and returns the answer, or <see langword="null"/> when pwsh couldn't be started, or stopped answering.</summary>
    public async Task<string?> SendAsync(string request)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_isDisposed)
            {
                return null;
            }

            _process ??= Start(script, workingFolder);
            await _process.StandardInput.WriteLineAsync(request).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);

            // the pipe can't be canceled, so it is closed by stopping the pwsh, once the answer took too long
            return await _process.StandardOutput.ReadLineAsync(CancellationToken.None).AsTask().WaitAsync(ResponseTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or TimeoutException or ObjectDisposedException)
        {
            // pwsh couldn't be started, or stopped answering; the next request starts it again
            Stop();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Process Start(string script, string? workingFolder)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script },
            WorkingDirectory = workingFolder ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
        };

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"'{startInfo.FileName}' couldn't be started.");
    }

    private void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or Win32Exception)
        {
            // it has already exited
        }

        _process.Dispose();
        _process = null;
    }

    // When the app exits without this, its end of the input is closed, which ends the loop, so the pwsh exits too.
    public void Dispose()
    {
        _isDisposed = true;
        Stop();
    }
}
