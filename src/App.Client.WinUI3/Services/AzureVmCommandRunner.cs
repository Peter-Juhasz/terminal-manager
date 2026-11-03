using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using App.Client.WinUI3.Models;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Runs the commands of an <see cref="AzureVmProject"/> on its machine, by <c>az vm run-command invoke</c>, which is expected to be on the path and signed in.
/// </summary>
/// <remarks>
/// What the Azure CLI writes to its standard error, such as why the command couldn't be run, is shown as it is read; the output of the command only once it ended,
/// as that is when the run command reports it.
/// </remarks>
public static class AzureVmCommandRunner
{
    /// <inheritdoc cref="ICommandRunner.RunAsync"/>
    /// <remarks>The output isn't completed, so more can be written to it after.</remarks>
    public static async Task<int> RunAsync(AzureVmProject project, string command, IOutputSink output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(output);

        // unique to the run, so output that happens to look like it isn't taken for the exit code
        var exitCodeMarker = $"__terminal_manager_exit_code_{Guid.NewGuid():N}=";

        // passed by a file, so the script doesn't need to be quoted for the command line, which cmd would expand variables and special characters of
        var scriptPath = Path.Combine(Path.GetTempPath(), $"terminal-manager-{Guid.NewGuid():N}{project.ScriptFileExtension}");
        await File.WriteAllTextAsync(scriptPath, project.CreateScript(command, exitCodeMarker), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        try
        {
            var result = new CapturingSink(output);
            var exitCode = await ProcessRunner.RunAsync(CreateStartInfo(project, scriptPath), input: null, result, cancellationToken).ConfigureAwait(false);
            var standardOutput = result.StandardOutput;

            // it failed before the command was run, and told why on its standard error
            if (exitCode != 0)
            {
                Write(output, OutputStream.StandardOutput, standardOutput);
                return exitCode;
            }

            var parsed = ParseResult(standardOutput, exitCodeMarker);
            Write(output, OutputStream.StandardOutput, parsed.StandardOutput);
            Write(output, OutputStream.StandardError, parsed.StandardError);
            return parsed.ExitCode;
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Describes the process of the Azure CLI that runs the script in <paramref name="scriptPath"/> on the machine of <paramref name="project"/>.</summary>
    internal static ProcessStartInfo CreateStartInfo(AzureVmProject project, string scriptPath)
    {
        // The Azure CLI is a batch file, so it is run by cmd, which finds it on the path; with /s, only the outermost quotes are stripped.
        var arguments = new StringBuilder("az vm run-command invoke");
        arguments.Append(" --command-id ").Append(project.CommandId);
        if (project.Subscription is { Length: > 0 } subscription)
        {
            arguments.Append(" --subscription ").Append(Quote(subscription));
        }

        arguments.Append(" --resource-group ").Append(Quote(project.ResourceGroup));
        arguments.Append(" --name ").Append(Quote(project.VmName));
        arguments.Append(" --scripts ").Append(Quote("@" + scriptPath));
        arguments.Append(" --output json --only-show-errors");

        // The working folder is on the machine, so the CLI is run in one that is sure to exist here.
        // Its output is written as UTF-8 rather than by the code page of the system, and without colors, as it isn't written to a console.
        return new ProcessStartInfo("cmd")
        {
            Arguments = $"/d /s /c \"{arguments}\"",
            WorkingDirectory = Path.GetTempPath(),
            Environment =
            {
                ["PYTHONIOENCODING"] = "utf-8",
                ["AZURE_CORE_NO_COLOR"] = "true",
                ["AZURE_CORE_COLLECT_TELEMETRY"] = "false",
            },
        };
    }

    // names in Azure can't contain quotes, so they are only put in them, for those with spaces
    private static string Quote(string value) => $"\"{value.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";

    /// <summary>
    /// Reads what the command wrote, and its exit code, from <paramref name="json"/>, which is what <c>az vm run-command invoke</c> wrote;
    /// a Linux machine reports both streams in a single message, a Windows machine each in a status of its own.
    /// </summary>
    /// <remarks>
    /// The exit code is that written after <paramref name="exitCodeMarker"/>, which is taken out of the output; when it isn't there, as when the output was cut short,
    /// it is 1 if the run command failed, 0 otherwise.
    /// </remarks>
    internal static RunCommandResult ParseResult(string json, string exitCodeMarker)
    {
        string standardOutput;
        var standardError = string.Empty;
        var failed = false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var statuses = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.Array
                ? value.EnumerateArray().Select(static status => (Code: GetString(status, "code"), Message: GetString(status, "message"))).ToList()
                : [];

            if (statuses.Any(static status => status.Code.Contains("/StdOut/", StringComparison.OrdinalIgnoreCase) || status.Code.Contains("/StdErr/", StringComparison.OrdinalIgnoreCase)))
            {
                standardOutput = string.Concat(statuses.Where(static status => status.Code.Contains("/StdOut/", StringComparison.OrdinalIgnoreCase)).Select(static status => status.Message));
                standardError = string.Concat(statuses.Where(static status => status.Code.Contains("/StdErr/", StringComparison.OrdinalIgnoreCase)).Select(static status => status.Message));
                failed = statuses.Any(static status => status.Code.EndsWith("/failed", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                var message = string.Join("\n", statuses.Select(static status => status.Message));
                (standardOutput, standardError, failed) = ParseLinuxMessage(message);
                failed |= statuses.Any(static status => status.Code.EndsWith("/failed", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (JsonException)
        {
            // shown as it is, so whatever it says can still be read
            return new(StandardOutput: json, StandardError: string.Empty, ExitCode: 0);
        }

        if (TryTakeExitCode(ref standardOutput, exitCodeMarker, out var exitCode))
        {
            return new(StandardOutput: standardOutput, StandardError: standardError, ExitCode: exitCode);
        }

        return new(StandardOutput: standardOutput, StandardError: standardError, ExitCode: failed ? 1 : 0);
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.String ? property.GetString() ?? string.Empty : string.Empty;

    // As in "Enable succeeded: \n[stdout]\n...\n[stderr]\n...", where a line is put after the standard output; what comes before the streams is only shown when it failed.
    private static (string StandardOutput, string StandardError, bool Failed) ParseLinuxMessage(string message)
    {
        const string StandardOutputHeader = "[stdout]\n";
        const string StandardErrorHeader = "\n[stderr]\n";

        var outputStart = message.IndexOf(StandardOutputHeader, StringComparison.Ordinal);
        if (outputStart < 0)
        {
            return (message, string.Empty, Failed: false);
        }

        var status = message[..outputStart].Trim();
        var failed = status.StartsWith("Enable failed", StringComparison.OrdinalIgnoreCase);
        var rest = message[(outputStart + StandardOutputHeader.Length)..];
        var errorStart = rest.LastIndexOf(StandardErrorHeader, StringComparison.Ordinal);
        var (standardOutput, standardError) = errorStart switch
        {
            < 0 => (rest, string.Empty),
            _ => (rest[..errorStart], rest[(errorStart + StandardErrorHeader.Length)..]),
        };

        if (failed && status.Length > 0)
        {
            standardError = standardError.Length > 0 ? $"{status}\n{standardError}" : status + "\n";
        }

        return (standardOutput, standardError, failed);
    }

    // Takes the line with the exit code out, with the line break put before it, so the output is shown as the command wrote it.
    private static bool TryTakeExitCode(ref string standardOutput, string exitCodeMarker, out int exitCode)
    {
        exitCode = 0;
        var markerStart = standardOutput.LastIndexOf(exitCodeMarker, StringComparison.Ordinal);
        if (markerStart < 0)
        {
            return false;
        }

        var codeStart = markerStart + exitCodeMarker.Length;
        var lineEnd = standardOutput.IndexOf('\n', codeStart);
        var codeEnd = lineEnd < 0 ? standardOutput.Length : lineEnd;
        if (!int.TryParse(standardOutput.AsSpan(codeStart, codeEnd - codeStart).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exitCode))
        {
            return false;
        }

        var removeStart = markerStart switch
        {
            >= 2 when standardOutput[markerStart - 1] == '\n' && standardOutput[markerStart - 2] == '\r' => markerStart - 2,
            >= 1 when standardOutput[markerStart - 1] == '\n' => markerStart - 1,
            _ => markerStart,
        };
        var removeEnd = lineEnd < 0 ? standardOutput.Length : lineEnd + 1;
        standardOutput = standardOutput.Remove(removeStart, removeEnd - removeStart);
        return true;
    }

    private static void Write(IOutputSink output, OutputStream stream, string text)
    {
        if (text.Length > 0)
        {
            output.Write(stream, text);
        }
    }

    /// <summary>Keeps what is written to the standard output, which is the result of the run command, and passes what is written to the standard error on to <paramref name="output"/>.</summary>
    private sealed class CapturingSink(IOutputSink output) : IOutputSink
    {
        private readonly Lock _lock = new();
        private readonly StringBuilder _standardOutput = new();

        public string StandardOutput
        {
            get
            {
                lock (_lock)
                {
                    return _standardOutput.ToString();
                }
            }
        }

        public void Write(OutputStream stream, ReadOnlySpan<char> text)
        {
            if (stream is OutputStream.StandardError)
            {
                output.Write(stream, text);
                return;
            }

            lock (_lock)
            {
                _standardOutput.Append(text);
            }
        }

        public void Complete()
        {
        }
    }
}

/// <summary>What a command run on an Azure virtual machine wrote, and the code it exited with.</summary>
internal readonly record struct RunCommandResult(string StandardOutput, string StandardError, int ExitCode);
