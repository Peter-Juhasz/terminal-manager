using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Completes commands the way PowerShell does, by asking a pwsh that keeps running in the working folder, so it only starts up once.
/// </summary>
public sealed class PowerShellCompleter : IDisposable
{
    // How long an answer is waited for, after which the pwsh is stopped, and started again for the next one.
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);

    // Reads a request from each line it is given, and writes the answer on a line of its own.
    // The variables are private, so they aren't offered by the completion of variables.
    private const string Script = """
        [Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $ProgressPreference = 'SilentlyContinue'
        while ($null -ne ($private:line = [Console]::In.ReadLine())) {
            $private:response = try {
                $private:request = ConvertFrom-Json -InputObject $line
                $private:completion = TabExpansion2 -inputScript $request.input -cursorColumn $request.cursor
                @{
                    replacementIndex = $completion.ReplacementIndex
                    replacementLength = $completion.ReplacementLength
                    matches = @($completion.CompletionMatches | ForEach-Object {
                        @{
                            completionText = $_.CompletionText
                            listItemText = $_.ListItemText
                            resultType = $_.ResultType.ToString()
                            toolTip = $_.ToolTip
                        }
                    })
                }
            }
            catch {
                @{ replacementIndex = 0; replacementLength = 0; matches = @() }
            }
            [Console]::Out.WriteLine((ConvertTo-Json -InputObject $response -Depth 4 -Compress))
        }
        """;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Host> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts the pwsh for <paramref name="workingFolder"/>, and has it load what completion needs, so the first completion is quick too.</summary>
    public void Prepare(string workingFolder) =>
        _ = CompleteAsync(workingFolder, "Get-", 4, CancellationToken.None);

    /// <summary>
    /// Completes the text before <paramref name="cursor"/> in <paramref name="input"/>, or returns <see langword="null"/> when it can't be completed, such as when pwsh isn't installed.
    /// The pwsh is left to answer when <paramref name="cancellationToken"/> is canceled, so it doesn't have to be started again.
    /// </summary>
    public async Task<CommandCompletion?> CompleteAsync(string workingFolder, string input, int cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(input);

        Host host;
        lock (_lock)
        {
            if (!_hosts.TryGetValue(workingFolder, out var existing) || existing.HasExited)
            {
                existing?.Dispose();
                existing = new Host(workingFolder);
                _hosts[workingFolder] = existing;
            }

            host = existing;
        }

        var completion = await host.CompleteAsync(input, cursor).WaitAsync(cancellationToken).ConfigureAwait(false);

        // what isn't within the input couldn't be put in its place
        return completion is { ReplacementIndex: >= 0, ReplacementLength: >= 0 } && completion.ReplacementIndex + completion.ReplacementLength <= input.Length
            ? completion
            : null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var host in _hosts.Values)
            {
                host.Dispose();
            }

            _hosts.Clear();
        }
    }

    // A pwsh that answers one request at a time.
    private sealed class Host(string workingFolder) : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Process? _process;
        private bool _isDisposed;

        public bool HasExited => _isDisposed || _process is { HasExited: true };

        public async Task<CommandCompletion?> CompleteAsync(string input, int cursor)
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_isDisposed)
                {
                    return null;
                }

                _process ??= Start(workingFolder);
                await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new CompletionRequest(input, cursor), CompletionJsonSerializerContext.Default.CompletionRequest)).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                // the pipe can't be canceled, so it is closed by stopping the pwsh, once the answer took too long
                var line = await _process.StandardOutput.ReadLineAsync(CancellationToken.None).AsTask().WaitAsync(ResponseTimeout, CancellationToken.None).ConfigureAwait(false);
                return line is null ? null : JsonSerializer.Deserialize(line, CompletionJsonSerializerContext.Default.CommandCompletion);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or TimeoutException or JsonException or ObjectDisposedException)
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

        private static Process Start(string workingFolder)
        {
            var startInfo = new ProcessStartInfo("pwsh")
            {
                ArgumentList = { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", Script },
                WorkingDirectory = workingFolder,
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
}

internal sealed record CompletionRequest(string Input, int Cursor);

// Source generated, so serialization keeps working when the app is trimmed.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CompletionRequest))]
[JsonSerializable(typeof(CommandCompletion))]
internal sealed partial class CompletionJsonSerializerContext : JsonSerializerContext;
