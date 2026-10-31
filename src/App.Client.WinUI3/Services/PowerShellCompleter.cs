using System.Text.Json;
using System.Text.Json.Serialization;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Completes commands the way PowerShell does, by asking a pwsh that keeps running in the working folder, so it only starts up once.
/// </summary>
public sealed class PowerShellCompleter : IDisposable
{
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
    private readonly Dictionary<string, PowerShellHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

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

        PowerShellHost host;
        lock (_lock)
        {
            if (!_hosts.TryGetValue(workingFolder, out var existing) || existing.HasExited)
            {
                existing?.Dispose();
                existing = new PowerShellHost(Script, workingFolder);
                _hosts[workingFolder] = existing;
            }

            host = existing;
        }

        var request = JsonSerializer.Serialize(new CompletionRequest(input, cursor), CompletionJsonSerializerContext.Default.CompletionRequest);
        var line = await host.SendAsync(request).WaitAsync(cancellationToken).ConfigureAwait(false);
        var completion = Deserialize(line);

        // what isn't within the input couldn't be put in its place
        return completion is { ReplacementIndex: >= 0, ReplacementLength: >= 0 } && completion.ReplacementIndex + completion.ReplacementLength <= input.Length
            ? completion
            : null;
    }

    private static CommandCompletion? Deserialize(string? line)
    {
        if (line is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(line, CompletionJsonSerializerContext.Default.CommandCompletion);
        }
        catch (JsonException)
        {
            return null;
        }
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
}

internal sealed record CompletionRequest(string Input, int Cursor);

// Source generated, so serialization keeps working when the app is trimmed.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CompletionRequest))]
[JsonSerializable(typeof(CommandCompletion))]
internal sealed partial class CompletionJsonSerializerContext : JsonSerializerContext;
